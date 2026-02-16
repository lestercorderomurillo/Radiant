using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

/// <summary> Available widget types for Inspector windows. </summary>
public enum WidgetType { Label, Button, Toggle, Slider, Dropdown }

/// <summary> A single UI widget inside an Inspector window. Struct for value-type list storage. </summary>
public struct Widget
{
    public string Id;
    public WidgetType Type;
    public string Text;
    public bool Visible;
    public bool Disabled;
    public bool Section;
    public Rectangle Bounds;

    public bool ToggleValue;
    public Action<bool> ToggleCallback;

    public float SliderValue;
    public float SliderMin;
    public float SliderMax;
    public Action<float> SliderCallback;

    public Action ButtonCallback;

    public string[] DropdownOptions;
    public int DropdownSelected;
    public Action<int> DropdownCallback;
}

/// <summary> Color palette for an Inspector theme. All 15 slots must be set. </summary>
public struct InspectorTheme
{
    public Color WindowBg, TitleBarColor, TitleBarHover;
    public Color ButtonColor, ButtonHover;
    public Color SliderTrack, SliderFill, SliderHandle;
    public Color ToggleOn, ToggleOff;
    public Color CloseColor, CloseHover;
    public Color TextColor, CloseText, LabelDim;
}

/// <summary> Runtime state for a single Inspector window. </summary>
public class WindowData
{
    public string Id;
    public string Title;
    public Vector2 Position;
    public Vector2 Size;
    public bool Visible;
    public int ZOrder;
    public int CreationIndex;
    public int LayoutOrder;

    public List<Widget> Widgets = new();
    public Dictionary<string, int> WidgetIndex = new();

    public Rectangle TitleBarBounds;
    public Rectangle CloseBounds;
    public Rectangle WindowBounds;
}
