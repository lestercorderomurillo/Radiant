using System;
using System.Collections.Generic;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

/// <summary>
/// Retained-mode UI system with draggable windows, themes, and blurry glass.
/// All public methods are static and null-safe (no-op if Inspector is not registered).
/// </summary>
[RunAfter(typeof(Geometry))]
[RunBefore(typeof(GizmosRenderer))]
public partial class Inspector : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.UI;
    private static Inspector Instance;

    public Inspector() => Instance = this;

    private const float FontSize = 24f;
    private float LineHeight;
    private Dictionary<string, WindowData> Windows = new();
    private List<WindowData> RenderOrder = new();
    private int NextZOrder;
    private int NextCreationIndex;
    private bool LayoutDone;

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

    private string OpenDropdownWindowId;
    private string OpenDropdownWidgetId;
    private Rectangle OpenDropdownPopupBounds;
    private int DropdownScrollOffset;
    private int DropdownTotalOptions;

    /// <summary> Raised when "Restore Defaults" is clicked in the Workspace menu. </summary>
    public static event Action WindowsRestored;

    private const int DefaultWindowWidth = 480;
    private const int TitleBarHeight = 52;
    private const int WidgetHeight = 48;
    private const int WidgetSpacing = 14;
    private const int LabelSpacing = 4;
    private const int Padding = 16;
    private const int CloseButtonSize = 28;
    private const int CloseButtonWidth = 48;
    private const int AutoLayoutGap = 26;
    private const int SliderTrackHeight = 6;
    private const int SliderHandleSize = 20;
    private const int ToggleBoxSize = 28;
    private const int CornerRadius = 8;
    private const int MaxVisibleDropdownItems = 4;

    private static float UIScale = 1.0f;

    /// <summary> Creates a new window. LayoutOrder controls auto-position column order. </summary>
    public static void CreateWindow(string Id, string Title, int LayoutOrder = 100, bool AutoPosition = true)
        => Instance?.CreateWindowInternal(Id, Title, LayoutOrder, AutoPosition);

    /// <summary> Destroys a window and all its widgets. </summary>
    public static void DestroyWindow(string Id)
        => Instance?.DestroyWindowInternal(Id);

    /// <summary> Shows a hidden window. </summary>
    public static void ShowWindow(string Id) => Instance?.SetWindowVisible(Id, true);

    /// <summary> Hides a visible window. </summary>
    public static void HideWindow(string Id) => Instance?.SetWindowVisible(Id, false);

    /// <summary> Toggles a window's visibility. </summary>
    public static void ToggleWindow(string Id) => Instance?.ToggleWindowInternal(Id);

    /// <summary> Returns true if the window exists and is visible. </summary>
    public static bool IsWindowVisible(string Id) => Instance?.IsWindowVisibleInternal(Id) ?? false;

    /// <summary> Adds a text label widget. </summary>
    public static void AddLabel(string WindowId, string WidgetId, string Text)
        => Instance?.AddWidgetInternal(WindowId, WidgetId, WidgetType.Label, Text);

    /// <summary> Adds a bold section header label with a horizontal rule. </summary>
    public static void AddSectionLabel(string WindowId, string WidgetId, string Text)
        => Instance?.AddSectionLabelInternal(WindowId, WidgetId, Text);

    /// <summary> Adds a clickable button widget. </summary>
    public static void AddButton(string WindowId, string WidgetId, string Text, Action Callback)
        => Instance?.AddButtonInternal(WindowId, WidgetId, Text, Callback);

    /// <summary> Adds a boolean toggle widget. </summary>
    public static void AddToggle(string WindowId, string WidgetId, string Text, bool Initial, Action<bool> Callback)
        => Instance?.AddToggleInternal(WindowId, WidgetId, Text, Initial, Callback);

    /// <summary> Adds a float slider widget with min/max range. </summary>
    public static void AddSlider(string WindowId, string WidgetId, string Text, float Min, float Max, float Initial, Action<float> Callback)
        => Instance?.AddSliderInternal(WindowId, WidgetId, Text, Min, Max, Initial, Callback);

    /// <summary> Adds a dropdown selection widget. </summary>
    public static void AddDropdown(string WindowId, string WidgetId, string Text, string[] Options, int Initial, Action<int> Callback)
        => Instance?.AddDropdownInternal(WindowId, WidgetId, Text, Options, Initial, Callback);

    /// <summary> Removes a widget from a window. </summary>
    public static void RemoveWidget(string WindowId, string WidgetId)
        => Instance?.RemoveWidgetInternal(WindowId, WidgetId);

    /// <summary> Enables or disables a widget. Disabled widgets are greyed out and non-interactive. </summary>
    public static void SetWidgetEnabled(string WindowId, string WidgetId, bool Enabled)
        => Instance?.SetWidgetEnabledInternal(WindowId, WidgetId, Enabled);

    /// <summary> Updates the text of a label widget. </summary>
    public static void SetLabel(string WindowId, string WidgetId, string Text)
        => Instance?.SetLabelInternal(WindowId, WidgetId, Text);

    /// <summary> Updates a slider's current value (clamped to its range). </summary>
    public static void SetSliderValue(string WindowId, string WidgetId, float Value)
        => Instance?.SetSliderValueInternal(WindowId, WidgetId, Value);

    /// <summary> Updates a toggle's current value. </summary>
    public static void SetToggleValue(string WindowId, string WidgetId, bool Value)
        => Instance?.SetToggleValueInternal(WindowId, WidgetId, Value);

    /// <summary> Updates a dropdown's selected index. </summary>
    public static void SetDropdownValue(string WindowId, string WidgetId, int Index)
        => Instance?.SetDropdownValueInternal(WindowId, WidgetId, Index);

    /// <summary> Replaces a dropdown's option list. Resets selection if out of range. </summary>
    public static void SetDropdownOptions(string WindowId, string WidgetId, string[] Options)
        => Instance?.SetDropdownOptionsInternal(WindowId, WidgetId, Options);

    /// <summary> Returns true if the mouse is over any Inspector window or popup. </summary>
    public static bool IsMouseOverUI() => Instance?.MouseOverUI ?? false;

    private void CreateWindowInternal(string Id, string Title, int LayoutOrder, bool AutoPosition = true)
    {
        if (Windows.ContainsKey(Id)) return;
        var window = new WindowData
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
        Windows[Id] = window;
        RenderOrder.Add(window);
        SortRenderOrder();
        RebuildWorkspaceMenu();
        if (AutoPosition) AutoPositionAll();
    }

    private void DestroyWindowInternal(string Id)
    {
        if (!Windows.TryGetValue(Id, out var window)) return;
        Windows.Remove(Id);
        RenderOrder.Remove(window);
        RebuildWorkspaceMenu();
    }

    private void SetWindowVisible(string Id, bool Visible)
    {
        if (Windows.TryGetValue(Id, out var window))
        {
            window.Visible = Visible;
            if (Visible) AutoPositionAll();
        }
    }

    private void ToggleWindowInternal(string Id)
    {
        if (Windows.TryGetValue(Id, out var window))
        {
            window.Visible = !window.Visible;
            if (window.Visible) AutoPositionAll();
        }
    }

    private bool IsWindowVisibleInternal(string Id)
        => Windows.TryGetValue(Id, out var window) && window.Visible;

    private void AddWidgetInternal(string WindowId, string WidgetId, WidgetType Type, string Text)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (window.WidgetIndex.ContainsKey(WidgetId)) return;
        window.WidgetIndex[WidgetId] = window.Widgets.Count;
        window.Widgets.Add(new Widget { Id = WidgetId, Type = Type, Text = Text, Visible = true });
    }

    private void AddSectionLabelInternal(string WindowId, string WidgetId, string Text)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (window.WidgetIndex.ContainsKey(WidgetId)) return;
        window.WidgetIndex[WidgetId] = window.Widgets.Count;
        window.Widgets.Add(new Widget { Id = WidgetId, Type = WidgetType.Label, Text = Text, Visible = true, Section = true });
    }

    private void AddButtonInternal(string WindowId, string WidgetId, string Text, Action Callback)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (window.WidgetIndex.ContainsKey(WidgetId)) return;
        window.WidgetIndex[WidgetId] = window.Widgets.Count;
        window.Widgets.Add(new Widget
        {
            Id = WidgetId, Type = WidgetType.Button, Text = Text, Visible = true,
            ButtonCallback = Callback
        });
    }

    private void AddToggleInternal(string WindowId, string WidgetId, string Text, bool Initial, Action<bool> Callback)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (window.WidgetIndex.ContainsKey(WidgetId)) return;
        window.WidgetIndex[WidgetId] = window.Widgets.Count;
        window.Widgets.Add(new Widget
        {
            Id = WidgetId, Type = WidgetType.Toggle, Text = Text, Visible = true,
            ToggleValue = Initial, ToggleCallback = Callback
        });
    }

    private void AddSliderInternal(string WindowId, string WidgetId, string Text, float Min, float Max, float Initial, Action<float> Callback)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (window.WidgetIndex.ContainsKey(WidgetId)) return;
        window.WidgetIndex[WidgetId] = window.Widgets.Count;
        window.Widgets.Add(new Widget
        {
            Id = WidgetId, Type = WidgetType.Slider, Text = Text, Visible = true,
            SliderValue = Initial, SliderMin = Min, SliderMax = Max, SliderCallback = Callback
        });
    }

    private void AddDropdownInternal(string WindowId, string WidgetId, string Text, string[] Options, int Initial, Action<int> Callback)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (window.WidgetIndex.ContainsKey(WidgetId)) return;
        window.WidgetIndex[WidgetId] = window.Widgets.Count;
        window.Widgets.Add(new Widget
        {
            Id = WidgetId, Type = WidgetType.Dropdown, Text = Text, Visible = true,
            DropdownOptions = Options, DropdownSelected = Initial, DropdownCallback = Callback
        });
    }

    private void RemoveWidgetInternal(string WindowId, string WidgetId)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int index)) return;
        window.Widgets.RemoveAt(index);
        window.WidgetIndex.Remove(WidgetId);
        for (int i = 0; i < window.Widgets.Count; i++)
            window.WidgetIndex[window.Widgets[i].Id] = i;
    }

    private void SetLabelInternal(string WindowId, string WidgetId, string Text)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int index)) return;
        var widget = window.Widgets[index];
        widget.Text = Text;
        window.Widgets[index] = widget;
    }

    private void SetSliderValueInternal(string WindowId, string WidgetId, float Value)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int index)) return;
        var widget = window.Widgets[index];
        widget.SliderValue = MathHelper.Clamp(Value, widget.SliderMin, widget.SliderMax);
        window.Widgets[index] = widget;
    }

    private void SetToggleValueInternal(string WindowId, string WidgetId, bool Value)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int index)) return;
        var widget = window.Widgets[index];
        widget.ToggleValue = Value;
        window.Widgets[index] = widget;
    }

    private void SetDropdownValueInternal(string WindowId, string WidgetId, int Index)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int widgetIdx)) return;
        var widget = window.Widgets[widgetIdx];
        if (widget.DropdownOptions != null && Index >= 0 && Index < widget.DropdownOptions.Length)
        {
            widget.DropdownSelected = Index;
            window.Widgets[widgetIdx] = widget;
        }
    }

    private void SetDropdownOptionsInternal(string WindowId, string WidgetId, string[] Options)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int widgetIdx)) return;
        var widget = window.Widgets[widgetIdx];
        widget.DropdownOptions = Options;
        if (widget.DropdownSelected >= Options.Length)
            widget.DropdownSelected = 0;
        window.Widgets[widgetIdx] = widget;
    }

    private void SetWidgetEnabledInternal(string WindowId, string WidgetId, bool Enabled)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int index)) return;
        var widget = window.Widgets[index];
        widget.Disabled = !Enabled;
        window.Widgets[index] = widget;
    }

    private void SortRenderOrder() => RenderOrder.Sort((a, b) => a.ZOrder.CompareTo(b.ZOrder));

    private void BringToFront(WindowData Window)
    {
        Window.ZOrder = NextZOrder++;
        SortRenderOrder();
    }

    /// <summary> Registers built-in themes, creates the Inspector window, and sets up default widgets. </summary>
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
        AddDropdown("inspector", "theme", "Theme", GetThemeNames(), 0, ApplyTheme);

        InitializeMenuBar();
    }

    /// <summary> Recomputes UI scale and triggers layout recalculation on window resize. </summary>
    public override void OnResize()
    {
        UIScale = ComputeAutoScale();
        SetSliderValue("inspector", "uiScale", UIScale);
        LayoutDone = false;
    }

    /// <summary> Processes F1 toggle, mouse input, window dragging, slider dragging, and dropdown interaction. </summary>
    public override void Update()
    {
        if (!LayoutDone && !Dragging)
        {
            AutoPositionAll();
            LayoutDone = true;
        }

        var keyboard = Microsoft.Xna.Framework.Input.Keyboard.GetState();
        if (keyboard.IsKeyDown(Keys.F1) && PrevKeyState.IsKeyUp(Keys.F1))
        {
            GlobalVisible = !GlobalVisible;
            if (!GlobalVisible) CloseMenuDropdown();
        }
        PrevKeyState = keyboard;

        if (!GlobalVisible)
            return;

        var currentMouse = Mouse.GetState();
        var virtualMouse = ScreenToVirtual(new Vector2(currentMouse.X, currentMouse.Y));

        bool leftPressed = currentMouse.LeftButton == ButtonState.Pressed && PrevMouse.LeftButton == ButtonState.Released;
        bool leftHeld = currentMouse.LeftButton == ButtonState.Pressed;
        bool leftReleased = currentMouse.LeftButton == ButtonState.Released && PrevMouse.LeftButton == ButtonState.Pressed;

        MouseOverUI = false;

        if (UpdateMenuBar(virtualMouse, leftPressed, keyboard))
        {
            PrevMouse = currentMouse;
            return;
        }

        if (DraggingSlider && leftHeld)
        {
            float prevScale = UIScale;
            HandleSliderDrag(virtualMouse);
            if (UIScale != prevScale) LayoutDone = false;
            MouseOverUI = true;
        }
        else if (DraggingSlider && leftReleased)
        {
            DraggingSlider = false;
        }

        if (Dragging && leftHeld)
        {
            if (Windows.TryGetValue(DragWindowId, out var dragWin))
                dragWin.Position = virtualMouse - DragOffset;
            MouseOverUI = true;
        }
        else if (Dragging && leftReleased)
        {
            Dragging = false;
        }

        ComputeAllLayouts();

        if (OpenDropdownWindowId != null)
        {
            int scrollDelta = currentMouse.ScrollWheelValue - PrevMouse.ScrollWheelValue;
            if (scrollDelta != 0 && OpenDropdownPopupBounds.Contains((int)virtualMouse.X, (int)virtualMouse.Y))
            {
                int maxScroll = Math.Max(0, DropdownTotalOptions - MaxVisibleDropdownItems);
                DropdownScrollOffset = Math.Clamp(DropdownScrollOffset - Math.Sign(scrollDelta), 0, maxScroll);
                MouseOverUI = true;
            }
        }

        if (!Dragging && !DraggingSlider && OpenDropdownWindowId != null && leftPressed)
        {
            if (OpenDropdownPopupBounds.Contains((int)virtualMouse.X, (int)virtualMouse.Y))
            {
                MouseOverUI = true;
                HandleDropdownPopupClick(virtualMouse);
                PrevMouse = currentMouse;
                return;
            }
            CloseDropdown();
            MouseOverUI = true;
            PrevMouse = currentMouse;
            return;
        }

        if (!Dragging && !DraggingSlider)
        {
            for (int i = RenderOrder.Count - 1; i >= 0; i--)
            {
                var window = RenderOrder[i];
                if (!window.Visible) continue;
                if (!window.WindowBounds.Contains((int)virtualMouse.X, (int)virtualMouse.Y)) continue;

                MouseOverUI = true;

                if (leftPressed)
                {
                    if (window.CloseBounds.Contains((int)virtualMouse.X, (int)virtualMouse.Y))
                    {
                        window.Visible = false;
                        break;
                    }

                    if (window.TitleBarBounds.Contains((int)virtualMouse.X, (int)virtualMouse.Y))
                    {
                        Dragging = true;
                        DragWindowId = window.Id;
                        DragOffset = virtualMouse - window.Position;
                        BringToFront(window);
                        break;
                    }

                    BringToFront(window);
                    HandleWidgetPress(window, virtualMouse);
                    break;
                }

                break;
            }
        }

        if (OpenDropdownWindowId != null && OpenDropdownPopupBounds.Contains((int)virtualMouse.X, (int)virtualMouse.Y))
            MouseOverUI = true;

        PrevMouse = currentMouse;
    }

    private void HandleWidgetPress(WindowData Window, Vector2 Mouse)
    {
        for (int i = 0; i < Window.Widgets.Count; i++)
        {
            var widget = Window.Widgets[i];
            if (!widget.Visible || widget.Disabled) continue;
            if (!widget.Bounds.Contains((int)Mouse.X, (int)Mouse.Y)) continue;

            switch (widget.Type)
            {
                case WidgetType.Slider:
                    int trackLeft = widget.Bounds.X + 4;
                    int trackWidth = widget.Bounds.Width - 8;
                    float range = widget.SliderMax - widget.SliderMin;
                    float normalizedValue = range > 0 ? (widget.SliderValue - widget.SliderMin) / range : 0;
                    int fillWidth = (int)(trackWidth * normalizedValue);
                    int handleCX = trackLeft + fillWidth;
                    int handleCY = (int)(widget.Bounds.Y + LineHeight + 8) + SliderTrackHeight / 2;
                    float dx = Mouse.X - handleCX;
                    float dy = Mouse.Y - handleCY;
                    if (dx * dx + dy * dy > SliderHandleSize * SliderHandleSize) return;
                    DraggingSlider = true;
                    SliderWindowId = Window.Id;
                    SliderWidgetId = widget.Id;
                    return;

                case WidgetType.Toggle:
                    widget.ToggleValue = !widget.ToggleValue;
                    Window.Widgets[i] = widget;
                    widget.ToggleCallback?.Invoke(widget.ToggleValue);
                    return;

                case WidgetType.Button:
                    widget.ButtonCallback?.Invoke();
                    return;

                case WidgetType.Dropdown:
                    if (OpenDropdownWindowId == Window.Id && OpenDropdownWidgetId == widget.Id)
                        CloseDropdown();
                    else
                        OpenDropdown(Window.Id, widget.Id, widget);
                    return;
            }
        }
    }

    private void HandleSliderDrag(Vector2 Mouse)
    {
        if (!Windows.TryGetValue(SliderWindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(SliderWidgetId, out int index)) return;

        var widget = window.Widgets[index];
        int trackLeft = widget.Bounds.X + 4;
        int trackRight = widget.Bounds.Right - 4;
        int trackWidth = trackRight - trackLeft;
        if (trackWidth <= 0) return;

        float normalizedValue = MathHelper.Clamp((Mouse.X - trackLeft) / trackWidth, 0f, 1f);
        widget.SliderValue = widget.SliderMin + normalizedValue * (widget.SliderMax - widget.SliderMin);
        window.Widgets[index] = widget;
        widget.SliderCallback?.Invoke(widget.SliderValue);
    }

    private void OpenDropdown(string WindowId, string WidgetId, Widget Widget)
    {
        OpenDropdownWindowId = WindowId;
        OpenDropdownWidgetId = WidgetId;
        DropdownScrollOffset = 0;
        int optionCount = Widget.DropdownOptions?.Length ?? 0;
        DropdownTotalOptions = optionCount;
        int visibleCount = Math.Min(optionCount, MaxVisibleDropdownItems);
        OpenDropdownPopupBounds = new Rectangle(Widget.Bounds.X, Widget.Bounds.Bottom, Widget.Bounds.Width, visibleCount * WidgetHeight);
    }

    private void CloseDropdown()
    {
        OpenDropdownWindowId = null;
        OpenDropdownWidgetId = null;
        DropdownScrollOffset = 0;
    }

    private void HandleDropdownPopupClick(Vector2 Mouse)
    {
        if (!Windows.TryGetValue(OpenDropdownWindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(OpenDropdownWidgetId, out int index)) return;

        var widget = window.Widgets[index];
        int localIndex = ((int)Mouse.Y - OpenDropdownPopupBounds.Y) / WidgetHeight;
        int optionIndex = localIndex + DropdownScrollOffset;
        if (widget.DropdownOptions != null && optionIndex >= 0 && optionIndex < widget.DropdownOptions.Length)
        {
            widget.DropdownSelected = optionIndex;
            window.Widgets[index] = widget;
            widget.DropdownCallback?.Invoke(optionIndex);
        }
        CloseDropdown();
    }

    /// <summary> Disposes blur render targets and clears the singleton instance. </summary>
    public override void Dispose()
    {
        BlurRT_A?.Dispose();
        BlurRT_B?.Dispose();
        if (Instance == this)
            Instance = null;
    }
}
