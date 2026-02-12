using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public enum WidgetType
{
    Label,
    Button,
    Toggle,
    Slider,
    Dropdown
}

public struct Widget
{
    public string Id;
    public WidgetType Type;
    public string Text;
    public bool Visible;
    public Rectangle Bounds;
    public bool ToggleValue;
    public float SliderValue;
    public float SliderMin;
    public float SliderMax;
    public Action ButtonCallback;
    public Action<bool> ToggleCallback;
    public Action<float> SliderCallback;
    public string[] DropdownOptions;
    public int DropdownSelected;
    public Action<int> DropdownCallback;
    public bool DropdownOpen;
}

public class WindowData
{
    public string Id;
    public string Title;
    public Vector2 Position;
    public Vector2 Size;
    public bool Visible;
    public int ZOrder;
    public int CreationIndex;
    public List<Widget> Widgets = new();
    public Dictionary<string, int> WidgetIndex = new();
    public Rectangle TitleBarBounds;
    public Rectangle CloseBounds;
    public Rectangle WindowBounds;
}
