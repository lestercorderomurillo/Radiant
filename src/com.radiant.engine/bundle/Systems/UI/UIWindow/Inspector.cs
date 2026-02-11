using System;
using System.Collections.Generic;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

[RunAfter(typeof(Geometry))]
[RunBefore(typeof(GizmosRenderer))]
public class Inspector : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.UI;
    private static Inspector Instance;

    public Inspector()
    {
        Instance = this;
    }

    private SpriteFont Font;
    private Dictionary<string, WindowData> Windows = new();
    private List<WindowData> RenderOrder = new();
    private int NextZOrder;
    private int NextCreationIndex;
    private bool LayoutDone;

    // Input state
    private MouseState PrevMouse;
    private KeyboardState PrevKeyState;
    private bool Dragging;
    private string DragWindowId;
    private Vector2 DragOffset;
    private bool DraggingSlider;
    private string SliderWindowId;
    private string SliderWidgetId;
    private bool MouseOverUI;
    private bool GlobalVisible = false;

    public static event Action WindowsRestored;

    // Layout constants
    private const int DefaultWindowWidth = 340;
    private const int TitleBarHeight = 40;
    private const int WidgetHeight = 36;
    private const int WidgetSpacing = 6;
    private const int Padding = 12;
    private const int CloseButtonSize = 28;
    private const int AutoLayoutGap = 20;
    private const int AutoLayoutMaxY = 1900;
    private const int SliderTrackHeight = 8;
    private const int SliderHandleSize = 16;
    private const int ToggleBoxSize = 22;

    // Colors
    private static readonly Color WindowBg = new(20, 20, 25, 220);
    private static readonly Color TitleBarColor = new(45, 45, 55, 240);
    private static readonly Color TitleBarHover = new(55, 55, 70, 240);
    private static readonly Color ButtonColor = new(60, 60, 80, 220);
    private static readonly Color ButtonHover = new(80, 80, 110, 240);
    private static readonly Color SliderTrack = new(50, 50, 65, 200);
    private static readonly Color SliderFill = new(100, 140, 220, 255);
    private static readonly Color SliderHandle = new(180, 180, 200, 255);
    private static readonly Color ToggleOn = new(100, 100, 200, 255);
    private static readonly Color ToggleOff = new(80, 80, 80, 200);
    private static readonly Color CloseColor = new(180, 60, 60, 255);
    private static readonly Color CloseHover = new(220, 80, 80, 255);
    private static readonly Color TextColor = new(220, 220, 230, 255);
    private static readonly Color LabelDim = new(160, 160, 170, 255);

    // --- Static API ---

    public static void CreateWindow(string Id, string Title)
        => Instance?.CreateWindowInternal(Id, Title);

    public static void DestroyWindow(string Id)
        => Instance?.DestroyWindowInternal(Id);

    public static void ShowWindow(string Id)
        => Instance?.SetWindowVisible(Id, true);

    public static void HideWindow(string Id)
        => Instance?.SetWindowVisible(Id, false);

    public static void ToggleWindow(string Id)
        => Instance?.ToggleWindowInternal(Id);

    public static bool IsWindowVisible(string Id)
        => Instance?.IsWindowVisibleInternal(Id) ?? false;

    public static void AddLabel(string WindowId, string WidgetId, string Text)
        => Instance?.AddWidgetInternal(WindowId, WidgetId, WidgetType.Label, Text);

    public static void AddButton(string WindowId, string WidgetId, string Text, Action Callback)
        => Instance?.AddButtonInternal(WindowId, WidgetId, Text, Callback);

    public static void AddToggle(string WindowId, string WidgetId, string Text, bool Initial, Action<bool> Callback)
        => Instance?.AddToggleInternal(WindowId, WidgetId, Text, Initial, Callback);

    public static void AddSlider(string WindowId, string WidgetId, string Text, float Min, float Max, float Initial, Action<float> Callback)
        => Instance?.AddSliderInternal(WindowId, WidgetId, Text, Min, Max, Initial, Callback);

    public static void RemoveWidget(string WindowId, string WidgetId)
        => Instance?.RemoveWidgetInternal(WindowId, WidgetId);

    public static void SetLabel(string WindowId, string WidgetId, string Text)
        => Instance?.SetLabelInternal(WindowId, WidgetId, Text);

    public static void SetSliderValue(string WindowId, string WidgetId, float Value)
        => Instance?.SetSliderValueInternal(WindowId, WidgetId, Value);

    public static void SetToggleValue(string WindowId, string WidgetId, bool Value)
        => Instance?.SetToggleValueInternal(WindowId, WidgetId, Value);

    public static bool IsMouseOverUI()
        => Instance?.MouseOverUI ?? false;

    // --- Internal API ---

    private void CreateWindowInternal(string Id, string Title)
    {
        if (Windows.ContainsKey(Id)) return;
        var Window = new WindowData
        {
            Id = Id,
            Title = Title,
            Position = Vector2.Zero,
            Size = new Vector2(DefaultWindowWidth, 0),
            Visible = true,
            ZOrder = NextZOrder++,
            CreationIndex = NextCreationIndex++
        };
        Windows[Id] = Window;
        RenderOrder.Add(Window);
        SortRenderOrder();
    }

    private void DestroyWindowInternal(string Id)
    {
        if (!Windows.TryGetValue(Id, out var Window)) return;
        Windows.Remove(Id);
        RenderOrder.Remove(Window);
    }

    private void SetWindowVisible(string Id, bool Visible)
    {
        if (Windows.TryGetValue(Id, out var Window))
            Window.Visible = Visible;
    }

    private void ToggleWindowInternal(string Id)
    {
        if (Windows.TryGetValue(Id, out var Window))
            Window.Visible = !Window.Visible;
    }

    private bool IsWindowVisibleInternal(string Id)
        => Windows.TryGetValue(Id, out var Window) && Window.Visible;

    private void AddWidgetInternal(string WindowId, string WidgetId, WidgetType Type, string Text)
    {
        if (!Windows.TryGetValue(WindowId, out var Window)) return;
        if (Window.WidgetIndex.ContainsKey(WidgetId)) return;
        Window.WidgetIndex[WidgetId] = Window.Widgets.Count;
        Window.Widgets.Add(new Widget { Id = WidgetId, Type = Type, Text = Text, Visible = true });
    }

    private void AddButtonInternal(string WindowId, string WidgetId, string Text, Action Callback)
    {
        if (!Windows.TryGetValue(WindowId, out var Window)) return;
        if (Window.WidgetIndex.ContainsKey(WidgetId)) return;
        Window.WidgetIndex[WidgetId] = Window.Widgets.Count;
        Window.Widgets.Add(new Widget
        {
            Id = WidgetId, Type = WidgetType.Button, Text = Text, Visible = true,
            ButtonCallback = Callback
        });
    }

    private void AddToggleInternal(string WindowId, string WidgetId, string Text, bool Initial, Action<bool> Callback)
    {
        if (!Windows.TryGetValue(WindowId, out var Window)) return;
        if (Window.WidgetIndex.ContainsKey(WidgetId)) return;
        Window.WidgetIndex[WidgetId] = Window.Widgets.Count;
        Window.Widgets.Add(new Widget
        {
            Id = WidgetId, Type = WidgetType.Toggle, Text = Text, Visible = true,
            ToggleValue = Initial, ToggleCallback = Callback
        });
    }

    private void AddSliderInternal(string WindowId, string WidgetId, string Text, float Min, float Max, float Initial, Action<float> Callback)
    {
        if (!Windows.TryGetValue(WindowId, out var Window)) return;
        if (Window.WidgetIndex.ContainsKey(WidgetId)) return;
        Window.WidgetIndex[WidgetId] = Window.Widgets.Count;
        Window.Widgets.Add(new Widget
        {
            Id = WidgetId, Type = WidgetType.Slider, Text = Text, Visible = true,
            SliderValue = Initial, SliderMin = Min, SliderMax = Max, SliderCallback = Callback
        });
    }

    private void RemoveWidgetInternal(string WindowId, string WidgetId)
    {
        if (!Windows.TryGetValue(WindowId, out var Window)) return;
        if (!Window.WidgetIndex.TryGetValue(WidgetId, out int Index)) return;
        Window.Widgets.RemoveAt(Index);
        Window.WidgetIndex.Remove(WidgetId);
        // Rebuild index
        for (int I = 0; I < Window.Widgets.Count; I++)
            Window.WidgetIndex[Window.Widgets[I].Id] = I;
    }

    private void SetLabelInternal(string WindowId, string WidgetId, string Text)
    {
        if (!Windows.TryGetValue(WindowId, out var Window)) return;
        if (!Window.WidgetIndex.TryGetValue(WidgetId, out int Index)) return;
        var W = Window.Widgets[Index];
        W.Text = Text;
        Window.Widgets[Index] = W;
    }

    private void SetSliderValueInternal(string WindowId, string WidgetId, float Value)
    {
        if (!Windows.TryGetValue(WindowId, out var Window)) return;
        if (!Window.WidgetIndex.TryGetValue(WidgetId, out int Index)) return;
        var W = Window.Widgets[Index];
        W.SliderValue = MathHelper.Clamp(Value, W.SliderMin, W.SliderMax);
        Window.Widgets[Index] = W;
    }

    private void SetToggleValueInternal(string WindowId, string WidgetId, bool Value)
    {
        if (!Windows.TryGetValue(WindowId, out var Window)) return;
        if (!Window.WidgetIndex.TryGetValue(WidgetId, out int Index)) return;
        var W = Window.Widgets[Index];
        W.ToggleValue = Value;
        Window.Widgets[Index] = W;
    }

    private void SortRenderOrder()
    {
        RenderOrder.Sort((A, B) => A.ZOrder.CompareTo(B.ZOrder));
    }

    private void BringToFront(WindowData Window)
    {
        Window.ZOrder = NextZOrder++;
        SortRenderOrder();
    }

    // --- Auto Layout ---

    private void AutoPositionAll()
    {
        var Ordered = new List<WindowData>(Windows.Values);
        Ordered.Sort((A, B) => A.CreationIndex.CompareTo(B.CreationIndex));

        float X = AutoLayoutGap;
        float Y = AutoLayoutGap;
        var PendingHidden = new List<WindowData>();

        foreach (var Window in Ordered)
        {
            if (!Window.Visible)
            {
                PendingHidden.Add(Window);
                continue;
            }

            int GroupHeight = ComputeWindowHeight(Window);
            foreach (var Hidden in PendingHidden)
                GroupHeight = Math.Max(GroupHeight, ComputeWindowHeight(Hidden));

            if (Y + GroupHeight > AutoLayoutMaxY && Y > AutoLayoutGap)
            {
                X += DefaultWindowWidth + AutoLayoutGap;
                Y = AutoLayoutGap;
            }

            Window.Position = new Vector2(X, Y);
            foreach (var Hidden in PendingHidden)
                Hidden.Position = new Vector2(X, Y);

            PendingHidden.Clear();
            Y += GroupHeight + AutoLayoutGap;
        }

        foreach (var Hidden in PendingHidden)
            Hidden.Position = new Vector2(X, Y);
    }

    private int ComputeWindowHeight(WindowData Window)
    {
        int ContentWidth = (int)Window.Size.X - Padding * 2;
        int Height = TitleBarHeight + WidgetSpacing;
        for (int I = 0; I < Window.Widgets.Count; I++)
        {
            var W = Window.Widgets[I];
            if (!W.Visible) continue;
            int WidgetH = W.Type switch
            {
                WidgetType.Slider => WidgetHeight + 16,
                WidgetType.Label => MeasureWrappedHeight(W.Text, ContentWidth),
                _ => WidgetHeight
            };
            Height += WidgetH + WidgetSpacing;
        }
        return Height + Padding;
    }

    // --- System Lifecycle ---

    public override void Initialize()
    {
        Font = Renderer.GetFont("fonts/BaseFont");
        PrevMouse = Mouse.GetState();
        PrevKeyState = Keyboard.GetState();
    }

    public override void Update()
    {
        if (!LayoutDone)
        {
            AutoPositionAll();
            LayoutDone = true;
        }

        // F1 toggles all windows
        var Keyboard = Microsoft.Xna.Framework.Input.Keyboard.GetState();
        if (Keyboard.IsKeyDown(Keys.F1) && PrevKeyState.IsKeyUp(Keys.F1))
        {
            GlobalVisible = !GlobalVisible;
            if (GlobalVisible)
            {
                foreach (var Window in Windows.Values)
                    Window.Visible = true;
                WindowsRestored?.Invoke();
            }
        }
        PrevKeyState = Keyboard;

        if (!GlobalVisible)
            return;

        var CurrentMouse = Mouse.GetState();
        var VirtualMouse = ScreenToVirtual(new Vector2(CurrentMouse.X, CurrentMouse.Y));
        var PrevVirtual = ScreenToVirtual(new Vector2(PrevMouse.X, PrevMouse.Y));

        bool LeftPressed = CurrentMouse.LeftButton == ButtonState.Pressed && PrevMouse.LeftButton == ButtonState.Released;
        bool LeftHeld = CurrentMouse.LeftButton == ButtonState.Pressed;
        bool LeftReleased = CurrentMouse.LeftButton == ButtonState.Released && PrevMouse.LeftButton == ButtonState.Pressed;

        MouseOverUI = false;

        // Handle slider dragging (continues even outside window)
        if (DraggingSlider && LeftHeld)
        {
            HandleSliderDrag(VirtualMouse);
            MouseOverUI = true;
        }
        else if (DraggingSlider && LeftReleased)
        {
            DraggingSlider = false;
        }

        // Handle window dragging
        if (Dragging && LeftHeld)
        {
            if (Windows.TryGetValue(DragWindowId, out var DragWin))
                DragWin.Position = VirtualMouse - DragOffset;
            MouseOverUI = true;
        }
        else if (Dragging && LeftReleased)
        {
            Dragging = false;
        }

        // Compute layout for hit testing
        ComputeAllLayouts();

        // Hit test windows back-to-front (reverse order = highest Z first)
        if (!Dragging && !DraggingSlider)
        {
            for (int I = RenderOrder.Count - 1; I >= 0; I--)
            {
                var Window = RenderOrder[I];
                if (!Window.Visible) continue;

                if (!Window.WindowBounds.Contains((int)VirtualMouse.X, (int)VirtualMouse.Y))
                    continue;

                MouseOverUI = true;

                if (LeftPressed)
                {
                    // Close button
                    if (Window.CloseBounds.Contains((int)VirtualMouse.X, (int)VirtualMouse.Y))
                    {
                        Window.Visible = false;
                        break;
                    }

                    // Title bar drag
                    if (Window.TitleBarBounds.Contains((int)VirtualMouse.X, (int)VirtualMouse.Y))
                    {
                        Dragging = true;
                        DragWindowId = Window.Id;
                        DragOffset = VirtualMouse - Window.Position;
                        BringToFront(Window);
                        break;
                    }

                    // Widget hit testing
                    BringToFront(Window);
                    HandleWidgetPress(Window, VirtualMouse);
                    break;
                }

                if (LeftReleased)
                {
                    HandleWidgetRelease(Window, VirtualMouse);
                    break;
                }

                break;
            }
        }

        PrevMouse = CurrentMouse;
    }

    private void HandleWidgetPress(WindowData Window, Vector2 Mouse)
    {
        for (int I = 0; I < Window.Widgets.Count; I++)
        {
            var W = Window.Widgets[I];
            if (!W.Visible) continue;
            if (!W.Bounds.Contains((int)Mouse.X, (int)Mouse.Y)) continue;

            switch (W.Type)
            {
                case WidgetType.Slider:
                    DraggingSlider = true;
                    SliderWindowId = Window.Id;
                    SliderWidgetId = W.Id;
                    HandleSliderDrag(Mouse);
                    return;

                case WidgetType.Toggle:
                    W.ToggleValue = !W.ToggleValue;
                    Window.Widgets[I] = W;
                    W.ToggleCallback?.Invoke(W.ToggleValue);
                    return;

                case WidgetType.Button:
                    W.ButtonCallback?.Invoke();
                    return;
            }
        }
    }

    private void HandleWidgetRelease(WindowData Window, Vector2 Mouse)
    {
        // Currently all widget actions fire on press — nothing needed on release
    }

    private void HandleSliderDrag(Vector2 Mouse)
    {
        if (!Windows.TryGetValue(SliderWindowId, out var Window)) return;
        if (!Window.WidgetIndex.TryGetValue(SliderWidgetId, out int Index)) return;

        var W = Window.Widgets[Index];
        int TrackLeft = W.Bounds.X + Padding;
        int TrackRight = W.Bounds.Right - Padding;
        int TrackWidth = TrackRight - TrackLeft;

        if (TrackWidth <= 0) return;

        float T = MathHelper.Clamp((Mouse.X - TrackLeft) / TrackWidth, 0f, 1f);
        W.SliderValue = W.SliderMin + T * (W.SliderMax - W.SliderMin);
        Window.Widgets[Index] = W;
        W.SliderCallback?.Invoke(W.SliderValue);
    }

    private Vector2 ScreenToVirtual(Vector2 ScreenPos)
    {
        return new Vector2(
            ScreenPos.X * (Renderer.VirtualWidth / Renderer.ScreenWidth),
            ScreenPos.Y * (Renderer.VirtualHeight / Renderer.ScreenHeight));
    }

    // --- Layout ---

    private void ComputeAllLayouts()
    {
        foreach (var Window in RenderOrder)
        {
            if (!Window.Visible) continue;
            ComputeLayout(Window);
        }
    }

    private void ComputeLayout(WindowData Window)
    {
        int X = (int)Window.Position.X;
        int Y = (int)Window.Position.Y;
        int W = (int)Window.Size.X;

        // Title bar
        Window.TitleBarBounds = new Rectangle(X, Y, W, TitleBarHeight);
        Window.CloseBounds = new Rectangle(X + W - CloseButtonSize - 6, Y + 6, CloseButtonSize, CloseButtonSize);

        // Widgets
        int ContentWidth = W - Padding * 2;
        int WidgetY = Y + TitleBarHeight + WidgetSpacing;
        for (int I = 0; I < Window.Widgets.Count; I++)
        {
            var Widget = Window.Widgets[I];
            if (!Widget.Visible) continue;

            int WidgetH = Widget.Type switch
            {
                WidgetType.Slider => WidgetHeight + 16,
                WidgetType.Label => MeasureWrappedHeight(Widget.Text, ContentWidth),
                _ => WidgetHeight
            };
            Widget.Bounds = new Rectangle(X + Padding, WidgetY, ContentWidth, WidgetH);
            Window.Widgets[I] = Widget;
            WidgetY += WidgetH + WidgetSpacing;
        }

        // Auto-size height
        int TotalHeight = WidgetY - Y + Padding;
        Window.WindowBounds = new Rectangle(X, Y, W, TotalHeight);
    }

    private int MeasureWrappedHeight(string Text, int AvailableWidth)
    {
        int MaxWidth = AvailableWidth - 8;
        if (Font.MeasureString(Text).X <= MaxWidth)
            return WidgetHeight;

        string[] Words = Text.Split(' ');
        float SpaceWidth = Font.MeasureString(" ").X;
        int Lines = 1;
        float LineWidth = 0;

        for (int I = 0; I < Words.Length; I++)
        {
            float WordWidth = Font.MeasureString(Words[I]).X;
            float AddWidth = LineWidth == 0 ? WordWidth : SpaceWidth + WordWidth;

            if (LineWidth + AddWidth > MaxWidth && LineWidth > 0)
            {
                Lines++;
                LineWidth = WordWidth;
            }
            else
            {
                LineWidth += AddWidth;
            }
        }

        return Lines * Font.LineSpacing + 8;
    }

    // --- Rendering ---

    public override void LateRender()
    {
        if (RenderOrder.Count == 0 || !GlobalVisible) return;

        ComputeAllLayouts();

        var Scale = Matrix.CreateScale(
            Renderer.ScreenWidth / Renderer.VirtualWidth,
            Renderer.ScreenHeight / Renderer.VirtualHeight,
            1f);

        var CurrentMouse = Mouse.GetState();
        var VirtualMouse = ScreenToVirtual(new Vector2(CurrentMouse.X, CurrentMouse.Y));

        Renderer.BeginDraw(SpriteSortMode.Deferred, BlendState.AlphaBlend, transform: Scale);

        foreach (var Window in RenderOrder)
        {
            if (!Window.Visible) continue;
            DrawWindow(Window, VirtualMouse);
        }

        Renderer.EndDraw();
    }

    private void DrawWindow(WindowData Window, Vector2 Mouse)
    {
        var Solid = Renderer.GetSolidTexture(Color.White);

        // Window background
        Renderer.DrawSprite(Solid, Window.WindowBounds, WindowBg);

        // Title bar
        bool TitleHovered = Window.TitleBarBounds.Contains((int)Mouse.X, (int)Mouse.Y);
        Renderer.DrawSprite(Solid, Window.TitleBarBounds, TitleHovered ? TitleBarHover : TitleBarColor);

        // Title text
        var TitlePos = new Vector2(Window.TitleBarBounds.X + Padding, Window.TitleBarBounds.Y + 8);
        Renderer.DrawString(Font, Window.Title, TitlePos, TextColor);

        // Close button
        bool CloseHovered = Window.CloseBounds.Contains((int)Mouse.X, (int)Mouse.Y);
        Renderer.DrawSprite(Solid, Window.CloseBounds, CloseHovered ? CloseHover : CloseColor);
        var CloseTextSize = Font.MeasureString("X");
        var CloseTextPos = new Vector2(
            Window.CloseBounds.X + (Window.CloseBounds.Width - CloseTextSize.X) / 2,
            Window.CloseBounds.Y + (Window.CloseBounds.Height - CloseTextSize.Y) / 2);
        Renderer.DrawString(Font, "X", CloseTextPos, TextColor);

        // Widgets
        for (int I = 0; I < Window.Widgets.Count; I++)
        {
            var W = Window.Widgets[I];
            if (!W.Visible) continue;

            switch (W.Type)
            {
                case WidgetType.Label: DrawLabel(W, Mouse); break;
                case WidgetType.Button: DrawButton(W, Mouse); break;
                case WidgetType.Toggle: DrawToggle(W, Mouse); break;
                case WidgetType.Slider: DrawSlider(W, Mouse); break;
            }
        }
    }

    private void DrawLabel(Widget W, Vector2 Mouse)
    {
        int MaxWidth = W.Bounds.Width - 8;
        if (Font.MeasureString(W.Text).X <= MaxWidth)
        {
            var TextPos = new Vector2(W.Bounds.X + 4, W.Bounds.Y + (W.Bounds.Height - Font.LineSpacing) / 2);
            Renderer.DrawString(Font, W.Text, TextPos, LabelDim);
            return;
        }

        string[] Words = W.Text.Split(' ');
        float SpaceWidth = Font.MeasureString(" ").X;
        float Y = W.Bounds.Y + 4;
        string CurrentLine = "";

        for (int I = 0; I < Words.Length; I++)
        {
            string TestLine = CurrentLine.Length == 0 ? Words[I] : CurrentLine + " " + Words[I];
            if (Font.MeasureString(TestLine).X > MaxWidth && CurrentLine.Length > 0)
            {
                Renderer.DrawString(Font, CurrentLine, new Vector2(W.Bounds.X + 4, Y), LabelDim);
                Y += Font.LineSpacing;
                CurrentLine = Words[I];
            }
            else
            {
                CurrentLine = TestLine;
            }
        }

        if (CurrentLine.Length > 0)
            Renderer.DrawString(Font, CurrentLine, new Vector2(W.Bounds.X + 4, Y), LabelDim);
    }

    private void DrawButton(Widget W, Vector2 Mouse)
    {
        var Solid = Renderer.GetSolidTexture(Color.White);
        bool Hovered = W.Bounds.Contains((int)Mouse.X, (int)Mouse.Y);
        Renderer.DrawSprite(Solid, W.Bounds, Hovered ? ButtonHover : ButtonColor);

        var TextSize = Font.MeasureString(W.Text);
        var TextPos = new Vector2(
            W.Bounds.X + (W.Bounds.Width - TextSize.X) / 2,
            W.Bounds.Y + (W.Bounds.Height - TextSize.Y) / 2);
        Renderer.DrawString(Font, W.Text, TextPos, TextColor);
    }

    private void DrawToggle(Widget W, Vector2 Mouse)
    {
        var Solid = Renderer.GetSolidTexture(Color.White);

        // Toggle box
        int BoxY = W.Bounds.Y + (W.Bounds.Height - ToggleBoxSize) / 2;
        var BoxRect = new Rectangle(W.Bounds.X, BoxY, ToggleBoxSize, ToggleBoxSize);
        Renderer.DrawSprite(Solid, BoxRect, W.ToggleValue ? ToggleOn : ToggleOff);

        // Inner fill
        if (W.ToggleValue)
        {
            int Inset = 5;
            var InnerRect = new Rectangle(
                BoxRect.X + Inset, BoxRect.Y + Inset,
                BoxRect.Width - Inset * 2, BoxRect.Height - Inset * 2);
            Renderer.DrawSprite(Solid, InnerRect, TextColor);
        }

        // Label text
        var TextPos = new Vector2(W.Bounds.X + ToggleBoxSize + 8, W.Bounds.Y + (W.Bounds.Height - Font.LineSpacing) / 2);
        Renderer.DrawString(Font, W.Text, TextPos, TextColor);
    }

    private void DrawSlider(Widget W, Vector2 Mouse)
    {
        var Solid = Renderer.GetSolidTexture(Color.White);

        // Label + value text
        string ValueText = $"{W.Text}: {W.SliderValue:F2}";
        var TextPos = new Vector2(W.Bounds.X + 4, W.Bounds.Y + 2);
        Renderer.DrawString(Font, ValueText, TextPos, TextColor);

        // Track
        int TrackY = W.Bounds.Y + Font.LineSpacing + 8;
        int TrackLeft = W.Bounds.X + Padding;
        int TrackWidth = W.Bounds.Width - Padding * 2;
        var TrackRect = new Rectangle(TrackLeft, TrackY, TrackWidth, SliderTrackHeight);
        Renderer.DrawSprite(Solid, TrackRect, SliderTrack);

        // Fill
        float Range = W.SliderMax - W.SliderMin;
        float T = Range > 0 ? (W.SliderValue - W.SliderMin) / Range : 0;
        int FillWidth = (int)(TrackWidth * T);
        if (FillWidth > 0)
        {
            var FillRect = new Rectangle(TrackLeft, TrackY, FillWidth, SliderTrackHeight);
            Renderer.DrawSprite(Solid, FillRect, SliderFill);
        }

        // Handle
        int HandleX = TrackLeft + FillWidth - SliderHandleSize / 2;
        int HandleY = TrackY + SliderTrackHeight / 2 - SliderHandleSize / 2;
        var HandleRect = new Rectangle(HandleX, HandleY, SliderHandleSize, SliderHandleSize);
        Renderer.DrawSprite(Solid, HandleRect, SliderHandle);
    }

    public override void Dispose()
    {
        if (Instance == this)
            Instance = null;
    }
}
