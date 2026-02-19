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
    private bool Resizing;
    private string ResizeWindowId;
    private Vector2 ResizeStartMouse;
    private float ResizeStartWidth;
    private float ResizeStartHeight;

    private string OpenDropdownWindowId;
    private string OpenDropdownWidgetId;
    private Rectangle OpenDropdownPopupBounds;
    private int DropdownScrollOffset;
    private int DropdownTotalOptions;

    private string FocusedTextInputWindowId;
    private string FocusedTextInputWidgetId;
    private float CursorBlinkTimer;
    private const float CursorBlinkRate = 0.53f;
    private const int InlineGap = 8;

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
    private const int SliderHandleSize = 18;
    private const int ToggleBoxSize = 28;
    private const int CornerRadius = 8;
    private const int MaxVisibleDropdownItems = 4;
    private const int ResizeHandleSize = 20;
    private const int MinWindowWidth = 280;
    private const int MinWindowHeight = 300;

    private static float UIScaleBacking = 1.0f;
    private static float UIScale
    {
        get => UIScaleBacking;
        set
        {
            UIScaleBacking = value;
            if (Instance?.Renderer != null)
                Instance.Renderer.FontRenderScale = MathF.Max(1f, value);
        }
    }

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

    /// <summary> Adds a text input field with placeholder text. </summary>
    public static void AddTextInput(string WindowId, string WidgetId, string Placeholder, Action<string> Callback, float InlineRatio = 0f)
        => Instance?.AddTextInputInternal(WindowId, WidgetId, Placeholder, Callback, InlineRatio);

    /// <summary> Updates a text input's current value. </summary>
    public static void SetTextInputValue(string WindowId, string WidgetId, string Value)
        => Instance?.SetTextInputValueInternal(WindowId, WidgetId, Value);

    /// <summary> Gets a text input's current value. </summary>
    public static string GetTextInputValue(string WindowId, string WidgetId)
        => Instance?.GetTextInputValueInternal(WindowId, WidgetId) ?? "";

    /// <summary> Adds an empty list box with a fixed pixel height. </summary>
    public static void AddListBox(string WindowId, string WidgetId, int Height, string[] Items = null)
        => Instance?.AddListBoxInternal(WindowId, WidgetId, Height, Items);

    /// <summary> Updates the items displayed in a list box. </summary>
    public static void SetListBoxItems(string WindowId, string WidgetId, string[] Items)
        => Instance?.SetListBoxItemsInternal(WindowId, WidgetId, Items);

    /// <summary> Returns the set of selected indices in a list box. </summary>
    public static HashSet<int> GetListBoxSelected(string WindowId, string WidgetId)
        => Instance?.GetListBoxSelectedInternal(WindowId, WidgetId);

    /// <summary> Clears all selections in a list box. </summary>
    public static void ClearListBoxSelection(string WindowId, string WidgetId)
        => Instance?.ClearListBoxSelectionInternal(WindowId, WidgetId);

    /// <summary> Adds a clickable button widget with inline ratio for horizontal layout. </summary>
    public static void AddButton(string WindowId, string WidgetId, string Text, Action Callback, float InlineRatio)
        => Instance?.AddButtonInternal(WindowId, WidgetId, Text, Callback, InlineRatio);

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
            LayoutOrder = LayoutOrder,
            AutoPosition = AutoPosition
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

    private void AddButtonInternal(string WindowId, string WidgetId, string Text, Action Callback, float InlineRatio = 0f)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (window.WidgetIndex.ContainsKey(WidgetId)) return;
        window.WidgetIndex[WidgetId] = window.Widgets.Count;
        window.Widgets.Add(new Widget
        {
            Id = WidgetId, Type = WidgetType.Button, Text = Text, Visible = true,
            ButtonCallback = Callback, InlineRatio = InlineRatio
        });
    }

    private void AddTextInputInternal(string WindowId, string WidgetId, string Placeholder, Action<string> Callback, float InlineRatio)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (window.WidgetIndex.ContainsKey(WidgetId)) return;
        window.WidgetIndex[WidgetId] = window.Widgets.Count;
        window.Widgets.Add(new Widget
        {
            Id = WidgetId, Type = WidgetType.TextInput, Visible = true,
            TextInputValue = "", TextInputPlaceholder = Placeholder,
            TextInputCallback = Callback, TextInputCursor = 0, InlineRatio = InlineRatio
        });
    }

    private void SetTextInputValueInternal(string WindowId, string WidgetId, string Value)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int index)) return;
        var widget = window.Widgets[index];
        widget.TextInputValue = Value ?? "";
        widget.TextInputCursor = widget.TextInputValue.Length;
        window.Widgets[index] = widget;
    }

    private string GetTextInputValueInternal(string WindowId, string WidgetId)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return "";
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int index)) return "";
        return window.Widgets[index].TextInputValue ?? "";
    }

    private void AddListBoxInternal(string WindowId, string WidgetId, int Height, string[] Items)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (window.WidgetIndex.ContainsKey(WidgetId)) return;
        window.WidgetIndex[WidgetId] = window.Widgets.Count;
        window.Widgets.Add(new Widget
        {
            Id = WidgetId, Type = WidgetType.ListBox, Visible = true,
            ListBoxHeight = Height, ListBoxItems = Items ?? Array.Empty<string>(),
            ListBoxSelected = new HashSet<int>()
        });
    }

    private void SetListBoxItemsInternal(string WindowId, string WidgetId, string[] Items)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int index)) return;
        var widget = window.Widgets[index];
        widget.ListBoxItems = Items ?? Array.Empty<string>();
        widget.ListBoxSelected ??= new HashSet<int>();
        widget.ListBoxSelected.Clear();
        widget.ListBoxScroll = 0;
        window.Widgets[index] = widget;
    }

    private HashSet<int> GetListBoxSelectedInternal(string WindowId, string WidgetId)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return null;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int index)) return null;
        return window.Widgets[index].ListBoxSelected;
    }

    private void ClearListBoxSelectionInternal(string WindowId, string WidgetId)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int index)) return;
        window.Widgets[index].ListBoxSelected?.Clear();
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
        Renderer.Window.Window.TextInput += OnTextInput;
    }

    private void OnTextInput(object Sender, TextInputEventArgs Args)
    {
        if (FocusedTextInputWindowId == null) return;
        if (!Windows.TryGetValue(FocusedTextInputWindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(FocusedTextInputWidgetId, out int index)) return;

        var widget = window.Widgets[index];
        string value = widget.TextInputValue ?? "";
        int cursor = Math.Clamp(widget.TextInputCursor, 0, value.Length);
        char character = Args.Character;

        if (character == '\b')
        {
            if (cursor > 0)
            {
                value = value.Remove(cursor - 1, 1);
                cursor--;
            }
        }
        else if (character == 127)
        {
            if (cursor < value.Length)
                value = value.Remove(cursor, 1);
        }
        else if (character == '\r' || character == '\n')
        {
            widget.TextInputCallback?.Invoke(value);
            widget.TextInputValue = value;
            widget.TextInputCursor = cursor;
            window.Widgets[index] = widget;
            return;
        }
        else if (character == '\t' || character == '\x1b')
        {
            if (character == '\x1b') ClearTextInputFocus();
            return;
        }
        else if (!char.IsControl(character))
        {
            value = value.Insert(cursor, character.ToString());
            cursor++;
        }

        widget.TextInputValue = value;
        widget.TextInputCursor = cursor;
        window.Widgets[index] = widget;
        CursorBlinkTimer = 0;
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
        if (!LayoutDone && !Dragging && !Resizing)
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

        CursorBlinkTimer += (float)(GameTime?.ElapsedGameTime.TotalSeconds ?? 0.016);
        UpdateTextInputKeys(keyboard);

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

        if (Resizing && leftHeld)
        {
            if (Windows.TryGetValue(ResizeWindowId, out var resizeWin))
            {
                float deltaX = virtualMouse.X - ResizeStartMouse.X;
                float deltaY = virtualMouse.Y - ResizeStartMouse.Y;
                resizeWin.Size = new Vector2(Math.Max(MinWindowWidth, ResizeStartWidth + deltaX), resizeWin.Size.Y);
                resizeWin.ResizedHeight = Math.Max(MinWindowHeight, ResizeStartHeight + deltaY);
            }
            MouseOverUI = true;
        }
        else if (Resizing && leftReleased)
        {
            Resizing = false;
        }

        ComputeAllLayouts();

        int scrollDelta = currentMouse.ScrollWheelValue - PrevMouse.ScrollWheelValue;

        if (OpenDropdownWindowId != null)
        {
            if (scrollDelta != 0 && OpenDropdownPopupBounds.Contains((int)virtualMouse.X, (int)virtualMouse.Y))
            {
                int maxScroll = Math.Max(0, DropdownTotalOptions - MaxVisibleDropdownItems);
                DropdownScrollOffset = Math.Clamp(DropdownScrollOffset - Math.Sign(scrollDelta), 0, maxScroll);
                MouseOverUI = true;
            }
        }

        if (scrollDelta != 0)
            HandleListBoxScroll(virtualMouse, scrollDelta);

        if (!Dragging && !DraggingSlider && !Resizing && OpenDropdownWindowId != null && leftPressed)
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

        if (!Dragging && !DraggingSlider && !Resizing)
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

                    if (window.Resizable)
                    {
                        var resizeBounds = new Rectangle(
                            window.WindowBounds.Right - ResizeHandleSize,
                            window.WindowBounds.Bottom - ResizeHandleSize,
                            ResizeHandleSize, ResizeHandleSize);
                        if (resizeBounds.Contains((int)virtualMouse.X, (int)virtualMouse.Y))
                        {
                            Resizing = true;
                            ResizeWindowId = window.Id;
                            ResizeStartMouse = virtualMouse;
                            ResizeStartWidth = window.Size.X;
                            ResizeStartHeight = window.ResizedHeight > 0 ? window.ResizedHeight : ComputeWindowHeight(window);
                            BringToFront(window);
                            break;
                        }
                    }

                    BringToFront(window);
                    ClearTextInputFocus();
                    HandleWidgetPress(window, virtualMouse);
                    break;
                }

                break;
            }

            if (leftPressed && !MouseOverUI)
                ClearTextInputFocus();
        }

        if (OpenDropdownWindowId != null && OpenDropdownPopupBounds.Contains((int)virtualMouse.X, (int)virtualMouse.Y))
            MouseOverUI = true;

        PrevMouse = currentMouse;
    }

    private void UpdateTextInputKeys(KeyboardState Keyboard)
    {
        if (FocusedTextInputWindowId == null) return;
        if (!Windows.TryGetValue(FocusedTextInputWindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(FocusedTextInputWidgetId, out int index)) return;

        var widget = window.Widgets[index];
        string value = widget.TextInputValue ?? "";
        int cursor = Math.Clamp(widget.TextInputCursor, 0, value.Length);
        bool changed = false;

        if (Keyboard.IsKeyDown(Keys.Left) && PrevKeyState.IsKeyUp(Keys.Left) && cursor > 0)
        { cursor--; changed = true; }
        if (Keyboard.IsKeyDown(Keys.Right) && PrevKeyState.IsKeyUp(Keys.Right) && cursor < value.Length)
        { cursor++; changed = true; }
        if (Keyboard.IsKeyDown(Keys.Home) && PrevKeyState.IsKeyUp(Keys.Home))
        { cursor = 0; changed = true; }
        if (Keyboard.IsKeyDown(Keys.End) && PrevKeyState.IsKeyUp(Keys.End))
        { cursor = value.Length; changed = true; }

        if (changed)
        {
            widget.TextInputCursor = cursor;
            window.Widgets[index] = widget;
            CursorBlinkTimer = 0;
        }
    }

    private void FocusTextInput(string WindowId, string WidgetId)
    {
        FocusedTextInputWindowId = WindowId;
        FocusedTextInputWidgetId = WidgetId;
        CursorBlinkTimer = 0;
    }

    private void ClearTextInputFocus()
    {
        FocusedTextInputWindowId = null;
        FocusedTextInputWidgetId = null;
    }

    private bool IsTextInputFocused(string WindowId, string WidgetId)
        => FocusedTextInputWindowId == WindowId && FocusedTextInputWidgetId == WidgetId;

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

                case WidgetType.TextInput:
                    FocusTextInput(Window.Id, widget.Id);
                    widget.TextInputCursor = (widget.TextInputValue ?? "").Length;
                    Window.Widgets[i] = widget;
                    return;

                case WidgetType.ListBox:
                    HandleListBoxClick(Window, i, Mouse);
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

    private void HandleListBoxClick(WindowData Window, int WidgetIndex, Vector2 Mouse)
    {
        var widget = Window.Widgets[WidgetIndex];
        if (widget.ListBoxItems == null || widget.ListBoxItems.Length == 0) return;

        int itemHeight = (int)(LineHeight + 4);
        int contentY = widget.Bounds.Y + 4;
        int localY = (int)Mouse.Y - contentY;
        if (localY < 0) return;

        int clickedIndex = localY / itemHeight + widget.ListBoxScroll;
        if (clickedIndex < 0 || clickedIndex >= widget.ListBoxItems.Length) return;

        widget.ListBoxSelected ??= new HashSet<int>();
        if (widget.ListBoxSelected.Contains(clickedIndex))
            widget.ListBoxSelected.Remove(clickedIndex);
        else
            widget.ListBoxSelected.Add(clickedIndex);
        Window.Widgets[WidgetIndex] = widget;
    }

    private void HandleListBoxScroll(Vector2 Mouse, int ScrollDelta)
    {
        for (int i = RenderOrder.Count - 1; i >= 0; i--)
        {
            var window = RenderOrder[i];
            if (!window.Visible) continue;
            for (int j = 0; j < window.Widgets.Count; j++)
            {
                var widget = window.Widgets[j];
                if (widget.Type != WidgetType.ListBox || !widget.Visible) continue;
                if (!widget.Bounds.Contains((int)Mouse.X, (int)Mouse.Y)) continue;

                int itemHeight = (int)(LineHeight + 4);
                int maxVisible = (widget.Bounds.Height - 8) / itemHeight;
                int itemCount = widget.ListBoxItems?.Length ?? 0;
                int maxScroll = Math.Max(0, itemCount - maxVisible);
                widget.ListBoxScroll = Math.Clamp(widget.ListBoxScroll - Math.Sign(ScrollDelta), 0, maxScroll);
                window.Widgets[j] = widget;
                MouseOverUI = true;
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
        Renderer.Window.Window.TextInput -= OnTextInput;
        BlurRT_A?.Dispose();
        BlurRT_B?.Dispose();
        if (Instance == this)
            Instance = null;
    }
}
