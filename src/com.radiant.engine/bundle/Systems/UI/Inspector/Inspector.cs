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

    private const float FontSize = 20f;
    private float LineHeight;
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
    private float DragStartUIScale;
    private bool MouseOverUI;
    private bool GlobalVisible = false;

    // Open dropdown state (only one can be open at a time)
    private string OpenDropdownWindowId;
    private string OpenDropdownWidgetId;
    private Rectangle OpenDropdownPopupBounds;
    private int DropdownScrollOffset;
    private int DropdownTotalOptions;

    public static event Action WindowsRestored;

    // Layout constants
    private const int DefaultWindowWidth = 412;
    private const int TitleBarHeight = 44;
    private const int WidgetHeight = 40;
    private const int WidgetSpacing = 7;
    private const int Padding = 13;
    private const int CloseButtonSize = 24;
    private const int CloseButtonWidth = 40;
    private const int AutoLayoutGap = 22;
    private const int SliderTrackHeight = 9;
    private const int SliderHandleSize = 15;
    private const int ToggleBoxSize = 24;
    private const int CornerRadius = 7;
    private const int MaxVisibleDropdownItems = 4;

    private static readonly Dictionary<string, InspectorTheme> Themes = new();
    private static readonly List<string> ThemeNameList = new();
    private static float UIScale = 1.0f;

    // Colors (mutable for theme switching)
    private static Color WindowBg = new(12, 12, 16, 220);
    private static Color TitleBarColor = new(28, 28, 36, 240);
    private static Color TitleBarHover = new(38, 38, 50, 240);
    private static Color ButtonColor = new(40, 40, 55, 220);
    private static Color ButtonHover = new(55, 55, 75, 240);
    private static Color SliderTrack = new(30, 30, 42, 200);
    private static Color SliderFill = new(70, 105, 180, 255);
    private static Color SliderHandle = new(140, 140, 160, 255);
    private static Color ToggleOn = new(70, 70, 160, 255);
    private static Color ToggleOff = new(50, 50, 55, 200);
    private static Color CloseColor = new(180, 60, 60, 255);
    private static Color CloseHover = new(220, 80, 80, 255);
    private static Color TextColor = new(220, 220, 230, 255);
    private static Color CloseText = new(220, 220, 230, 255);
    private static Color LabelDim = new(160, 160, 170, 255);

    private RenderTarget2D BlurRT_A;
    private RenderTarget2D BlurRT_B;
    private RenderTarget2D BlurResult;
    private const int BlurDownscale = 4;
    private const int BlurPasses = 4;
    private const float GlassTintOpacity = 0.65f;
    private const float ShadowOffsetY = 3f;
    private const float ShadowSpreadSize = 12f;
    private const float ShadowAlpha = 0.25f;


    public static void CreateWindow(string Id, string Title, int LayoutOrder = 100)
        => Instance?.CreateWindowInternal(Id, Title, LayoutOrder);

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

    public static void AddDropdown(string WindowId, string WidgetId, string Text, string[] Options, int Initial, Action<int> Callback)
        => Instance?.AddDropdownInternal(WindowId, WidgetId, Text, Options, Initial, Callback);

    public static void RemoveWidget(string WindowId, string WidgetId)
        => Instance?.RemoveWidgetInternal(WindowId, WidgetId);

    public static void SetLabel(string WindowId, string WidgetId, string Text)
        => Instance?.SetLabelInternal(WindowId, WidgetId, Text);

    public static void SetSliderValue(string WindowId, string WidgetId, float Value)
        => Instance?.SetSliderValueInternal(WindowId, WidgetId, Value);

    public static void SetToggleValue(string WindowId, string WidgetId, bool Value)
        => Instance?.SetToggleValueInternal(WindowId, WidgetId, Value);

    public static void SetDropdownValue(string WindowId, string WidgetId, int Index)
        => Instance?.SetDropdownValueInternal(WindowId, WidgetId, Index);

    public static void SetDropdownOptions(string WindowId, string WidgetId, string[] Options)
        => Instance?.SetDropdownOptionsInternal(WindowId, WidgetId, Options);

    public static bool IsMouseOverUI()
        => Instance?.MouseOverUI ?? false;

    public static string[] GetThemeNames() => ThemeNameList.ToArray();

    public static void RegisterTheme(string Name, InspectorTheme Theme)
    {
        if (!Themes.ContainsKey(Name))
            ThemeNameList.Add(Name);
        Themes[Name] = Theme;
    }

    public static void ApplyTheme(string Name)
    {
        if (!Themes.TryGetValue(Name, out var theme)) return;
        WindowBg = theme.WindowBg;
        TitleBarColor = theme.TitleBarColor;
        TitleBarHover = theme.TitleBarHover;
        ButtonColor = theme.ButtonColor;
        ButtonHover = theme.ButtonHover;
        SliderTrack = theme.SliderTrack;
        SliderFill = theme.SliderFill;
        SliderHandle = theme.SliderHandle;
        ToggleOn = theme.ToggleOn;
        ToggleOff = theme.ToggleOff;
        CloseColor = theme.CloseColor;
        CloseHover = theme.CloseHover;
        TextColor = theme.TextColor;
        CloseText = theme.CloseText;
        LabelDim = theme.LabelDim;
    }

    public static void ApplyTheme(int Index)
    {
        if (Index >= 0 && Index < ThemeNameList.Count)
            ApplyTheme(ThemeNameList[Index]);
    }


    private void CreateWindowInternal(string Id, string Title, int LayoutOrder)
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
            CreationIndex = NextCreationIndex++,
            LayoutOrder = LayoutOrder
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
        if (Windows.TryGetValue(Id, out var Window) && Window.Visible != Visible)
        {
            Window.Visible = Visible;
            LayoutDone = false;
        }
    }

    private void ToggleWindowInternal(string Id)
    {
        if (Windows.TryGetValue(Id, out var Window))
        {
            Window.Visible = !Window.Visible;
            LayoutDone = false;
        }
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

    private void AddDropdownInternal(string WindowId, string WidgetId, string Text, string[] Options, int Initial, Action<int> Callback)
    {
        if (!Windows.TryGetValue(WindowId, out var Window)) return;
        if (Window.WidgetIndex.ContainsKey(WidgetId)) return;
        Window.WidgetIndex[WidgetId] = Window.Widgets.Count;
        Window.Widgets.Add(new Widget
        {
            Id = WidgetId, Type = WidgetType.Dropdown, Text = Text, Visible = true,
            DropdownOptions = Options, DropdownSelected = Initial, DropdownCallback = Callback
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

    private void SetDropdownValueInternal(string WindowId, string WidgetId, int Index)
    {
        if (!Windows.TryGetValue(WindowId, out var Window)) return;
        if (!Window.WidgetIndex.TryGetValue(WidgetId, out int WidgetIdx)) return;
        var W = Window.Widgets[WidgetIdx];
        if (W.DropdownOptions != null && Index >= 0 && Index < W.DropdownOptions.Length)
        {
            W.DropdownSelected = Index;
            Window.Widgets[WidgetIdx] = W;
        }
    }

    private void SetDropdownOptionsInternal(string WindowId, string WidgetId, string[] Options)
    {
        if (!Windows.TryGetValue(WindowId, out var Window)) return;
        if (!Window.WidgetIndex.TryGetValue(WidgetId, out int WidgetIdx)) return;
        var W = Window.Widgets[WidgetIdx];
        W.DropdownOptions = Options;
        if (W.DropdownSelected >= Options.Length)
            W.DropdownSelected = 0;
        Window.Widgets[WidgetIdx] = W;
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


    private void AutoPositionAll()
    {
        var Ordered = new List<WindowData>(Windows.Values);
        Ordered.Sort((A, B) =>
        {
            int order = A.LayoutOrder.CompareTo(B.LayoutOrder);
            return order != 0 ? order : A.CreationIndex.CompareTo(B.CreationIndex);
        });

        float X = AutoLayoutGap;
        float Y = AutoLayoutGap;
        Vector2 lastVisiblePos = new Vector2(X, Y);

        foreach (var Window in Ordered)
        {
            if (!Window.Visible)
            {
                Window.Position = lastVisiblePos;
                continue;
            }

            int GroupHeight = ComputeWindowHeight(Window);

            float maxY = Renderer.VirtualHeight / UIScale - 64;
            if (Y + GroupHeight > maxY && Y > AutoLayoutGap)
            {
                X += DefaultWindowWidth + AutoLayoutGap;
                Y = AutoLayoutGap;
            }

            Window.Position = new Vector2(X, Y);
            lastVisiblePos = Window.Position;
            Y += GroupHeight + AutoLayoutGap;
        }
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


    public override void Initialize()
    {
        LineHeight = Renderer.GetLineHeight("Inter", FontSize);
        PrevMouse = Mouse.GetState();
        PrevKeyState = Keyboard.GetState();

        RegisterBuiltInThemes();

        UIScale = ComputeAutoScale();
        CreateWindow("inspector", "Inspector", 0);
        AddSlider("inspector", "uiScale", "UI Scale", 0.5f, 3.0f, UIScale, (value) => UIScale = value);
        ApplyTheme(0);
        AddDropdown("inspector", "theme", "Theme", GetThemeNames(), 0, (index) => ApplyTheme(index));

        var gameLoop = Renderer.GameLoop;
        AddDropdown("inspector", "fpsCap", "FPS Cap", GameLoop.FpsOptionNames, 2, (index) => gameLoop?.SetTargetFps(GameLoop.FpsOptions[index]));
        AddToggle("inspector", "throttleUnfocused", "Throttle Unfocused", true, (enabled) => { if (gameLoop != null) gameLoop.ThrottleUnfocused = enabled; });
    }

    private static void RegisterBuiltInThemes()
    {
        if (Themes.Count > 0) return;

        RegisterTheme("Radiant", new InspectorTheme
        {
            WindowBg = new(16, 15, 14, 235), TitleBarColor = new(32, 30, 26, 250), TitleBarHover = new(48, 44, 36, 250),
            ButtonColor = new(34, 32, 28, 230), ButtonHover = new(52, 48, 40, 250),
            SliderTrack = new(24, 22, 18, 220), SliderFill = new(255, 184, 108, 255), SliderHandle = new(255, 210, 150, 255),
            ToggleOn = new(220, 155, 80, 255), ToggleOff = new(42, 40, 34, 235),
            CloseColor = new(220, 155, 80, 255), CloseHover = new(255, 184, 108, 255),
            TextColor = new(252, 250, 245, 255), CloseText = new(252, 250, 245, 255), LabelDim = new(155, 148, 130, 255)
        });
        RegisterTheme("Obsidian", new InspectorTheme
        {
            WindowBg = new(20, 20, 22, 225), TitleBarColor = new(30, 30, 33, 245), TitleBarHover = new(42, 42, 46, 245),
            ButtonColor = new(36, 36, 40, 225), ButtonHover = new(52, 52, 58, 245),
            SliderTrack = new(28, 28, 32, 210), SliderFill = new(130, 135, 145, 255), SliderHandle = new(180, 184, 192, 255),
            ToggleOn = new(120, 125, 135, 255), ToggleOff = new(44, 44, 48, 220),
            CloseColor = new(100, 104, 112, 255), CloseHover = new(140, 144, 155, 255),
            TextColor = new(210, 212, 218, 255), CloseText = new(210, 212, 218, 255), LabelDim = new(120, 122, 130, 255)
        });
        RegisterTheme("Midnight", new InspectorTheme
        {
            WindowBg = new(10, 10, 22, 225), TitleBarColor = new(22, 20, 42, 245), TitleBarHover = new(34, 30, 58, 245),
            ButtonColor = new(28, 26, 50, 225), ButtonHover = new(42, 38, 70, 245),
            SliderTrack = new(20, 18, 38, 210), SliderFill = new(100, 120, 210, 255), SliderHandle = new(155, 165, 230, 255),
            ToggleOn = new(90, 100, 195, 255), ToggleOff = new(35, 32, 55, 220),
            CloseColor = new(80, 85, 170, 255), CloseHover = new(110, 115, 210, 255),
            TextColor = new(200, 205, 235, 255), CloseText = new(200, 205, 235, 255), LabelDim = new(130, 135, 170, 255)
        });
        RegisterTheme("Frost", new InspectorTheme
        {
            WindowBg = new(232, 236, 242, 235), TitleBarColor = new(200, 210, 224, 245), TitleBarHover = new(180, 195, 215, 245),
            ButtonColor = new(195, 205, 220, 230), ButtonHover = new(175, 188, 210, 245),
            SliderTrack = new(190, 198, 210, 210), SliderFill = new(60, 130, 200, 255), SliderHandle = new(45, 100, 170, 255),
            ToggleOn = new(60, 130, 200, 255), ToggleOff = new(170, 178, 190, 235),
            CloseColor = new(190, 75, 75, 255), CloseHover = new(220, 90, 90, 255),
            TextColor = new(20, 30, 45, 255), CloseText = new(245, 248, 252, 255), LabelDim = new(80, 90, 110, 255)
        });
        RegisterTheme("Neon", new InspectorTheme
        {
            WindowBg = new(8, 8, 14, 225), TitleBarColor = new(16, 16, 28, 245), TitleBarHover = new(26, 24, 42, 245),
            ButtonColor = new(18, 18, 32, 225), ButtonHover = new(30, 28, 48, 245),
            SliderTrack = new(14, 14, 26, 210), SliderFill = new(255, 50, 130, 255), SliderHandle = new(255, 110, 170, 255),
            ToggleOn = new(0, 220, 230, 255), ToggleOff = new(28, 28, 44, 220),
            CloseColor = new(255, 50, 130, 255), CloseHover = new(255, 100, 165, 255),
            TextColor = new(235, 235, 245, 255), CloseText = new(235, 235, 245, 255), LabelDim = new(100, 100, 140, 255)
        });
        RegisterTheme("Glacier", new InspectorTheme
        {
            WindowBg = new(46, 52, 64, 225), TitleBarColor = new(59, 66, 82, 245), TitleBarHover = new(67, 76, 94, 245),
            ButtonColor = new(59, 66, 82, 225), ButtonHover = new(76, 86, 106, 245),
            SliderTrack = new(46, 52, 64, 210), SliderFill = new(136, 192, 208, 255), SliderHandle = new(143, 188, 187, 255),
            ToggleOn = new(163, 190, 140, 255), ToggleOff = new(59, 66, 82, 220),
            CloseColor = new(191, 97, 106, 255), CloseHover = new(210, 120, 130, 255),
            TextColor = new(236, 239, 244, 255), CloseText = new(236, 239, 244, 255), LabelDim = new(216, 222, 233, 180)
        });



    }

    private float ComputeAutoScale()
    {
        float scale = 2160f / Renderer.ScreenHeight;
        return MathF.Round(scale * 10f) / 10f;
    }

    public override void OnResize()
    {
        UIScale = ComputeAutoScale();
        SetSliderValue("inspector", "uiScale", UIScale);
        LayoutDone = false;
    }

    public override void Update()
    {
        if (!LayoutDone && !Dragging)
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
            if (UIScale != DragStartUIScale)
                LayoutDone = false;
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

        // Handle scroll wheel on open dropdown popup
        if (OpenDropdownWindowId != null)
        {
            int scrollDelta = CurrentMouse.ScrollWheelValue - PrevMouse.ScrollWheelValue;
            if (scrollDelta != 0 && OpenDropdownPopupBounds.Contains((int)VirtualMouse.X, (int)VirtualMouse.Y))
            {
                int maxScroll = Math.Max(0, DropdownTotalOptions - MaxVisibleDropdownItems);
                DropdownScrollOffset = Math.Clamp(DropdownScrollOffset - Math.Sign(scrollDelta), 0, maxScroll);
                MouseOverUI = true;
            }
        }

        // Hit test open dropdown popup first (renders on top of everything)
        if (!Dragging && !DraggingSlider && OpenDropdownWindowId != null && LeftPressed)
        {
            if (OpenDropdownPopupBounds.Contains((int)VirtualMouse.X, (int)VirtualMouse.Y))
            {
                MouseOverUI = true;
                HandleDropdownPopupClick(VirtualMouse);
                PrevMouse = CurrentMouse;
                return;
            }
            // Click outside popup closes it — consume the click
            CloseDropdown();
            MouseOverUI = true;
            PrevMouse = CurrentMouse;
            return;
        }

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

        // Mouse over open dropdown popup counts as over UI
        if (OpenDropdownWindowId != null && OpenDropdownPopupBounds.Contains((int)VirtualMouse.X, (int)VirtualMouse.Y))
            MouseOverUI = true;

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
                    DragStartUIScale = UIScale;
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

                case WidgetType.Dropdown:
                    if (OpenDropdownWindowId == Window.Id && OpenDropdownWidgetId == W.Id)
                        CloseDropdown();
                    else
                        OpenDropdown(Window.Id, W.Id, W);
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

    private void OpenDropdown(string WindowId, string WidgetId, Widget W)
    {
        OpenDropdownWindowId = WindowId;
        OpenDropdownWidgetId = WidgetId;
        DropdownScrollOffset = 0;
        int optionCount = W.DropdownOptions?.Length ?? 0;
        DropdownTotalOptions = optionCount;
        int visibleCount = Math.Min(optionCount, MaxVisibleDropdownItems);
        OpenDropdownPopupBounds = new Rectangle(W.Bounds.X, W.Bounds.Bottom, W.Bounds.Width, visibleCount * WidgetHeight);
    }

    private void CloseDropdown()
    {
        OpenDropdownWindowId = null;
        OpenDropdownWidgetId = null;
        DropdownScrollOffset = 0;
    }

    private void HandleDropdownPopupClick(Vector2 Mouse)
    {
        if (!Windows.TryGetValue(OpenDropdownWindowId, out var Window)) return;
        if (!Window.WidgetIndex.TryGetValue(OpenDropdownWidgetId, out int Index)) return;

        var W = Window.Widgets[Index];
        int localIndex = ((int)Mouse.Y - OpenDropdownPopupBounds.Y) / WidgetHeight;
        int optionIndex = localIndex + DropdownScrollOffset;
        if (W.DropdownOptions != null && optionIndex >= 0 && optionIndex < W.DropdownOptions.Length)
        {
            W.DropdownSelected = optionIndex;
            Window.Widgets[Index] = W;
            W.DropdownCallback?.Invoke(optionIndex);
        }
        CloseDropdown();
    }

    private Vector2 ScreenToVirtual(Vector2 ScreenPos)
    {
        float scale = DraggingSlider ? DragStartUIScale : UIScale;
        return new Vector2(
            ScreenPos.X * (Renderer.VirtualWidth / Renderer.ScreenWidth) / scale,
            ScreenPos.Y * (Renderer.VirtualHeight / Renderer.ScreenHeight) / scale);
    }


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
        Window.CloseBounds = new Rectangle(X + W - CloseButtonWidth - 6, Y + (TitleBarHeight - CloseButtonSize) / 2, CloseButtonWidth, CloseButtonSize);

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
        if (MeasureText(Text).X <= MaxWidth)
            return WidgetHeight;

        string[] Words = Text.Split(' ');
        float SpaceWidth = MeasureText(" ").X;
        int Lines = 1;
        float LineWidth = 0;

        for (int I = 0; I < Words.Length; I++)
        {
            float WordWidth = MeasureText(Words[I]).X;
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

        return (int)(Lines * LineHeight + 8);
    }


    public override void LateRender()
    {
        if (RenderOrder.Count == 0 || !GlobalVisible) return;

        ComputeAllLayouts();

        var Scale = Matrix.CreateScale(
            Renderer.ScreenWidth / Renderer.VirtualWidth * UIScale,
            Renderer.ScreenHeight / Renderer.VirtualHeight * UIScale,
            1f);

        var CurrentMouse = Mouse.GetState();
        var VirtualMouse = ScreenToVirtual(new Vector2(CurrentMouse.X, CurrentMouse.Y));

        // Only the topmost window under the mouse gets hover. Null = all blocked.
        string hoveredWindowId = null;
        if (!Dragging && !DraggingSlider && OpenDropdownWindowId == null)
        {
            for (int I = RenderOrder.Count - 1; I >= 0; I--)
            {
                var Win = RenderOrder[I];
                if (!Win.Visible) continue;
                if (Win.WindowBounds.Contains((int)VirtualMouse.X, (int)VirtualMouse.Y))
                {
                    hoveredWindowId = Win.Id;
                    break;
                }
            }
        }

        UpdateBlurPipeline();

        foreach (var Window in RenderOrder)
        {
            if (!Window.Visible) continue;

            DrawWindowShadow(Window);

            if (BlurResult != null)
                DrawWindowBlurQuad(Window);

            Renderer.BeginDraw(SpriteSortMode.Deferred, BlendState.AlphaBlend, transform: Scale);
            DrawWindow(Window, VirtualMouse, Window.Id != hoveredWindowId);
            Renderer.EndDraw();
        }

        Renderer.BeginDraw(SpriteSortMode.Deferred, BlendState.AlphaBlend, transform: Scale);
        DrawDropdownPopup(VirtualMouse);
        Renderer.EndDraw();
    }

    private Vector2 MeasureText(string text) => Renderer.MeasureString("Inter", FontSize, text);

    private string TruncateText(string text, float maxWidth)
    {
        if (MeasureText(text).X <= maxWidth) return text;
        float ellipsisWidth = MeasureText("...").X;
        for (int i = text.Length - 1; i > 0; i--)
        {
            if (MeasureText(text[..i]).X + ellipsisWidth <= maxWidth)
                return text[..i] + "...";
        }
        return "...";
    }

    private void DrawText(string text, Vector2 position, Color color)
        => Renderer.DrawString("Inter", FontSize, text, position, color);

    private void DrawTextBold(string text, Vector2 position, Color color)
        => Renderer.DrawString("Inter-Bold", FontSize, text, position, color);

    private void DrawWindow(WindowData Window, Vector2 Mouse, bool HoverBlocked)
    {
        Renderer.DrawRoundedRect(Window.WindowBounds, BlurResult != null ? GlassTint(WindowBg) : WindowBg, CornerRadius);

        bool TitleHovered = !HoverBlocked && Window.TitleBarBounds.Contains((int)Mouse.X, (int)Mouse.Y);
        Color titleBg = TitleHovered ? TitleBarHover : TitleBarColor;
        if (BlurResult != null) titleBg = GlassTint(titleBg);
        Renderer.DrawRoundedRect(Window.TitleBarBounds, titleBg, CornerRadius, RoundedCorners.Top);

        float TitleTextHeight = Renderer.MeasureString("Inter-Bold", FontSize, Window.Title).Y;
        var TitlePos = new Vector2(Window.TitleBarBounds.X + Padding, Window.TitleBarBounds.Y + (TitleBarHeight - TitleTextHeight) / 2);
        DrawTextBold(Window.Title, TitlePos, TextColor);

        bool CloseHovered = !HoverBlocked && Window.CloseBounds.Contains((int)Mouse.X, (int)Mouse.Y);
        Renderer.DrawRoundedRect(Window.CloseBounds, CloseHovered ? CloseHover : CloseColor, CornerRadius);
        const float CloseFontSize = FontSize * 0.7f;
        var CloseTextSize = Renderer.MeasureString("Inter-Bold", CloseFontSize, "X");
        var CloseTextPos = new Vector2(
            Window.CloseBounds.X + (Window.CloseBounds.Width - CloseTextSize.X) / 2,
            Window.CloseBounds.Y + (Window.CloseBounds.Height - CloseTextSize.Y) / 2);
        Renderer.DrawString("Inter-Bold", CloseFontSize, "X", CloseTextPos, CloseText);

        // Widgets
        for (int I = 0; I < Window.Widgets.Count; I++)
        {
            var W = Window.Widgets[I];
            if (!W.Visible) continue;

            switch (W.Type)
            {
                case WidgetType.Label: DrawLabel(W, Mouse); break;
                case WidgetType.Button: DrawButton(W, Mouse, HoverBlocked); break;
                case WidgetType.Toggle: DrawToggle(W, Mouse); break;
                case WidgetType.Slider: DrawSlider(W, Mouse); break;
                case WidgetType.Dropdown: DrawDropdown(W, Mouse, HoverBlocked); break;
            }
        }
    }

    private void DrawLabel(Widget W, Vector2 Mouse)
    {
        bool isHeader = W.Id.EndsWith("Header");
        Color labelColor = isHeader ? TextColor : LabelDim;

        if (isHeader)
        {
            var Solid = Renderer.GetSolidTexture(Color.White);
            var TextPos = new Vector2(W.Bounds.X + 4, W.Bounds.Y + (W.Bounds.Height - LineHeight) / 2);
            DrawTextBold(W.Text, TextPos, labelColor);

            int textRight = (int)(TextPos.X + MeasureText(W.Text).X) + 8;
            int lineY = W.Bounds.Y + W.Bounds.Height / 2;
            var LineRect = new Rectangle(textRight, lineY, W.Bounds.Right - textRight, 1);
            Renderer.DrawSprite(Solid, LineRect, TextColor, 0.20f);
            return;
        }

        int MaxWidth = W.Bounds.Width - 8;
        if (MeasureText(W.Text).X <= MaxWidth)
        {
            var TextPos = new Vector2(W.Bounds.X + 4, W.Bounds.Y + (W.Bounds.Height - LineHeight) / 2);
            DrawText(W.Text, TextPos, labelColor);
            return;
        }

        string[] Words = W.Text.Split(' ');
        float SpaceWidth = MeasureText(" ").X;
        float Y = W.Bounds.Y + 4;
        string CurrentLine = "";

        for (int I = 0; I < Words.Length; I++)
        {
            string TestLine = CurrentLine.Length == 0 ? Words[I] : CurrentLine + " " + Words[I];
            if (MeasureText(TestLine).X > MaxWidth && CurrentLine.Length > 0)
            {
                DrawText(CurrentLine, new Vector2(W.Bounds.X + 4, Y), labelColor);
                Y += LineHeight;
                CurrentLine = Words[I];
            }
            else
            {
                CurrentLine = TestLine;
            }
        }

        if (CurrentLine.Length > 0)
            DrawText(CurrentLine, new Vector2(W.Bounds.X + 4, Y), labelColor);
    }

    private void DrawButton(Widget W, Vector2 Mouse, bool HoverBlocked)
    {
        bool Hovered = !HoverBlocked && W.Bounds.Contains((int)Mouse.X, (int)Mouse.Y);
        Renderer.DrawRoundedRect(W.Bounds, Hovered ? ButtonHover : ButtonColor, CornerRadius);

        var TextSize = MeasureText(W.Text);
        var TextPos = new Vector2(
            W.Bounds.X + (W.Bounds.Width - TextSize.X) / 2,
            W.Bounds.Y + (W.Bounds.Height - TextSize.Y) / 2);
        DrawText(W.Text, TextPos, TextColor);
    }

    private void DrawToggle(Widget W, Vector2 Mouse)
    {
        int BoxY = W.Bounds.Y + (W.Bounds.Height - ToggleBoxSize) / 2;
        var BoxRect = new Rectangle(W.Bounds.X, BoxY, ToggleBoxSize, ToggleBoxSize);
        var solid = Renderer.GetSolidTexture(Color.White);
        Renderer.DrawSprite(solid, BoxRect, W.ToggleValue ? ToggleOn : ToggleOff);

        if (W.ToggleValue)
        {
            int Inset = 5;
            var InnerRect = new Rectangle(
                BoxRect.X + Inset, BoxRect.Y + Inset,
                BoxRect.Width - Inset * 2, BoxRect.Height - Inset * 2);
            Renderer.DrawSprite(solid, InnerRect, TextColor);
        }

        // Label text
        var TextPos = new Vector2(W.Bounds.X + ToggleBoxSize + 8, W.Bounds.Y + (W.Bounds.Height - LineHeight) / 2);
        DrawText(W.Text, TextPos, TextColor);
    }

    private void DrawSlider(Widget W, Vector2 Mouse)
    {
        string ValueText = $"{W.Text}: {W.SliderValue:F2}";
        var TextPos = new Vector2(W.Bounds.X + 4, W.Bounds.Y + 2);
        DrawText(ValueText, TextPos, TextColor);

        int TrackY = (int)(W.Bounds.Y + LineHeight + 8);
        int TrackLeft = W.Bounds.X + Padding;
        int TrackWidth = W.Bounds.Width - Padding * 2;
        var TrackRect = new Rectangle(TrackLeft, TrackY, TrackWidth, SliderTrackHeight);
        Renderer.DrawRoundedRect(TrackRect, SliderTrack, CornerRadius);

        float Range = W.SliderMax - W.SliderMin;
        float T = Range > 0 ? (W.SliderValue - W.SliderMin) / Range : 0;
        int FillWidth = (int)(TrackWidth * T);
        if (FillWidth > 0)
        {
            var FillRect = new Rectangle(TrackLeft, TrackY, FillWidth, SliderTrackHeight);
            Renderer.DrawRoundedRect(FillRect, SliderFill, CornerRadius);
        }

        int HandleX = TrackLeft + FillWidth - SliderHandleSize / 2;
        int HandleY = TrackY + SliderTrackHeight / 2 - SliderHandleSize / 2;
        var HandleRect = new Rectangle(HandleX, HandleY, SliderHandleSize, SliderHandleSize);
        Renderer.DrawSprite(Renderer.GetCircleTexture(SliderHandleSize * 4), HandleRect, SliderHandle);
    }

    private void DrawDropdown(Widget W, Vector2 Mouse, bool HoverBlocked)
    {
        bool Hovered = !HoverBlocked && W.Bounds.Contains((int)Mouse.X, (int)Mouse.Y);
        Renderer.DrawRoundedRect(W.Bounds, Hovered ? ButtonHover : ButtonColor, CornerRadius);

        string selectedLabel = W.DropdownOptions != null && W.DropdownSelected < W.DropdownOptions.Length
            ? W.DropdownOptions[W.DropdownSelected] : "?";
        string displayText = $"{W.Text}: {selectedLabel}";

        const int triSize = 7;
        float availableWidth = W.Bounds.Width - Padding * 2 - triSize - 4;
        displayText = TruncateText(displayText, availableWidth);

        var TextSize = MeasureText(displayText);
        var TextPos = new Vector2(
            W.Bounds.X + (W.Bounds.Width - TextSize.X - triSize - 4) / 2,
            W.Bounds.Y + (W.Bounds.Height - TextSize.Y) / 2);
        DrawText(displayText, TextPos, TextColor);

        int triX = W.Bounds.Right - Padding - triSize;
        int triY = W.Bounds.Y + (W.Bounds.Height - triSize) / 2 + 1;
        Renderer.DrawSprite(Renderer.GetTriangleTexture(triSize * 4), new Rectangle(triX, triY, triSize, triSize), LabelDim);
    }

    private void DrawDropdownPopup(Vector2 Mouse)
    {
        if (OpenDropdownWindowId == null) return;
        if (!Windows.TryGetValue(OpenDropdownWindowId, out var Window)) return;
        if (!Window.WidgetIndex.TryGetValue(OpenDropdownWidgetId, out int Index)) return;

        var W = Window.Widgets[Index];
        if (W.DropdownOptions == null) return;

        Renderer.DrawRoundedRect(OpenDropdownPopupBounds, WindowBg, CornerRadius);

        int visibleCount = Math.Min(W.DropdownOptions.Length - DropdownScrollOffset, MaxVisibleDropdownItems);
        bool scrollable = W.DropdownOptions.Length > MaxVisibleDropdownItems;
        int scrollbarReserved = scrollable ? 10 : 0;

        for (int I = 0; I < visibleCount; I++)
        {
            int optionIndex = I + DropdownScrollOffset;
            int optionWidth = OpenDropdownPopupBounds.Width - scrollbarReserved;
            var OptionRect = new Rectangle(OpenDropdownPopupBounds.X, OpenDropdownPopupBounds.Y + I * WidgetHeight, optionWidth, WidgetHeight);
            bool OptionHovered = OptionRect.Contains((int)Mouse.X, (int)Mouse.Y);
            bool IsSelected = optionIndex == W.DropdownSelected;

            Color OptionBg = IsSelected ? SliderFill : (OptionHovered ? ButtonHover : ButtonColor);
            RoundedCorners optionCorners = visibleCount == 1 ? RoundedCorners.All :
                I == 0 ? RoundedCorners.Top : I == visibleCount - 1 ? RoundedCorners.Bottom : RoundedCorners.None;
            Renderer.DrawRoundedRect(OptionRect, OptionBg, CornerRadius, optionCorners);

            string optionText = TruncateText(W.DropdownOptions[optionIndex], OptionRect.Width - Padding * 2);
            var OptionTextSize = MeasureText(optionText);
            var OptionTextPos = new Vector2(OptionRect.X + Padding, OptionRect.Y + (OptionRect.Height - OptionTextSize.Y) / 2);
            DrawText(optionText, OptionTextPos, TextColor);
        }

        if (scrollable)
        {
            int trackWidth = 4;
            int trackMargin = 3;
            int trackX = OpenDropdownPopupBounds.Right - trackMargin - trackWidth;
            int trackY = OpenDropdownPopupBounds.Y;
            int trackHeight = OpenDropdownPopupBounds.Height;
            float thumbRatio = (float)MaxVisibleDropdownItems / DropdownTotalOptions;
            int thumbHeight = Math.Max(8, (int)(trackHeight * thumbRatio));
            int scrollRange = trackHeight - thumbHeight;
            int maxScroll = Math.Max(1, DropdownTotalOptions - MaxVisibleDropdownItems);
            int thumbY = trackY + (int)(scrollRange * ((float)DropdownScrollOffset / maxScroll));
            Renderer.DrawRoundedRect(new Rectangle(trackX, trackY, trackWidth, trackHeight), ButtonColor, trackWidth / 2);
            Renderer.DrawRoundedRect(new Rectangle(trackX, thumbY, trackWidth, thumbHeight), SliderFill, trackWidth / 2);
        }
    }

    private void UpdateBlurPipeline()
    {
        if (Renderer.SceneRT == null)
        {
            BlurResult = null;
            return;
        }

        int blurW = Math.Max(1, Renderer.ScreenWidth / BlurDownscale);
        int blurH = Math.Max(1, Renderer.ScreenHeight / BlurDownscale);
        if (BlurRT_A == null || BlurRT_A.Width != blurW || BlurRT_A.Height != blurH)
        {
            BlurRT_A?.Dispose();
            BlurRT_B?.Dispose();
            BlurRT_A = Renderer.CreateRenderTarget(blurW, blurH);
            BlurRT_B = Renderer.CreateRenderTarget(blurW, blurH);
        }

        Renderer.SetTarget(BlurRT_A).Clear(Color.Black);
        Renderer.Blit(Renderer.SceneRT, BlendState.Opaque, SamplerState.LinearClamp);

        var texelSize = new Vector2(1f / blurW, 1f / blurH);
        Renderer
            .Reset()
            .SetShader("GlassBlur")
            .SetTechnique("Blur")
            .Configure(BlendState.Opaque)
            .Configure(SamplerState.LinearClamp, 0);

        BlurResult = Renderer.PingPong(BlurRT_A, BlurRT_B, BlurPasses,
            (passIndex, input) =>
            {
                Renderer.SetParameter("InputTexture", input);
                Renderer.SetParameter("TexelSize", texelSize);
                Renderer.SetParameter("BlurOffset", (float)passIndex);
            },
            clearColor: Color.Black);
    }

    private void DrawWindowBlurQuad(WindowData Window)
    {
        var wb = Window.WindowBounds;
        float scaleX = UIScale * Renderer.VirtualToScreenScale.X;
        float scaleY = UIScale * Renderer.VirtualToScreenScale.Y;
        var windowRect = new Vector4(wb.X * scaleX, wb.Y * scaleY, wb.Width * scaleX, wb.Height * scaleY);
        float windowRadius = CornerRadius * Math.Min(scaleX, scaleY);

        Renderer
            .Reset()
            .SetShader("GlassBlur")
            .SetTechnique("RoundedBlit")
            .Configure(BlendState.AlphaBlend)
            .Configure(SamplerState.LinearClamp, 0)
            .SetParameter("InputTexture", BlurResult)
            .SetParameter("ScreenSize", Renderer.ScreenSize)
            .SetParameter("WindowRect", windowRect)
            .SetParameter("WindowRadius", windowRadius)
            .Draw()
            .Commit();
    }

    private void DrawWindowShadow(WindowData Window)
    {
        var wb = Window.WindowBounds;
        float scaleX = UIScale * Renderer.VirtualToScreenScale.X;
        float scaleY = UIScale * Renderer.VirtualToScreenScale.Y;
        var windowRect = new Vector4(wb.X * scaleX, wb.Y * scaleY, wb.Width * scaleX, wb.Height * scaleY);
        float windowRadius = CornerRadius * Math.Min(scaleX, scaleY);

        Renderer
            .Reset()
            .SetShader("GlassBlur")
            .SetTechnique("Shadow")
            .Configure(BlendState.AlphaBlend)
            .SetParameter("ScreenSize", Renderer.ScreenSize)
            .SetParameter("WindowRect", windowRect)
            .SetParameter("WindowRadius", windowRadius)
            .SetParameter("ShadowOffset", new Vector2(0, ShadowOffsetY * scaleY))
            .SetParameter("ShadowSpread", ShadowSpreadSize * Math.Min(scaleX, scaleY))
            .SetParameter("ShadowOpacity", ShadowAlpha)
            .Draw()
            .Commit();
    }

    private static Color GlassTint(Color color) =>
        new(color.R, color.G, color.B, (byte)(color.A * GlassTintOpacity));

    public override void Dispose()
    {
        BlurRT_A?.Dispose();
        BlurRT_B?.Dispose();
        if (Instance == this)
            Instance = null;
    }
}
