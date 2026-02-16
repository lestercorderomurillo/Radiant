using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public partial class Inspector
{
    private static readonly Dictionary<string, InspectorTheme> Themes = new();
    private static readonly List<string> ThemeNameList = new();

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

    /// <summary> Returns the names of all registered themes. </summary>
    public static string[] GetThemeNames() => ThemeNameList.ToArray();

    /// <summary> Registers a custom theme. Overwrites if the name already exists. </summary>
    public static void RegisterTheme(string Name, InspectorTheme Theme)
    {
        if (!Themes.ContainsKey(Name))
            ThemeNameList.Add(Name);
        Themes[Name] = Theme;
    }

    /// <summary> Applies a registered theme by name. </summary>
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

    /// <summary> Applies a registered theme by index in the registration order. </summary>
    public static void ApplyTheme(int Index)
    {
        if (Index >= 0 && Index < ThemeNameList.Count)
            ApplyTheme(ThemeNameList[Index]);
    }

    private static void RegisterBuiltInThemes()
    {
        if (Themes.Count > 0) return;

        RegisterTheme("Solaris", new InspectorTheme
        {
            WindowBg = new(16, 15, 14, 235), TitleBarColor = new(32, 30, 26, 250), TitleBarHover = new(48, 44, 36, 250),
            ButtonColor = new(34, 32, 28, 230), ButtonHover = new(52, 48, 40, 250),
            SliderTrack = new(24, 22, 18, 220), SliderFill = new(255, 184, 108, 255), SliderHandle = new(255, 210, 150, 255),
            ToggleOn = new(220, 155, 80, 255), ToggleOff = new(42, 40, 34, 235),
            CloseColor = new(220, 155, 80, 255), CloseHover = new(255, 184, 108, 255),
            TextColor = new(252, 250, 245, 255), CloseText = new(252, 250, 245, 255), LabelDim = new(155, 148, 130, 255)
        });

        RegisterTheme("Carbon", new InspectorTheme
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

        RegisterTheme("Sentinel", new InspectorTheme
        {
            WindowBg = new(225, 225, 222, 230), TitleBarColor = new(205, 205, 200, 240), TitleBarHover = new(195, 195, 188, 240),
            ButtonColor = new(178, 178, 174, 235), ButtonHover = new(162, 162, 156, 245),
            SliderTrack = new(178, 178, 174, 220), SliderFill = new(110, 110, 105, 255), SliderHandle = new(80, 80, 76, 255),
            ToggleOn = new(100, 100, 95, 255), ToggleOff = new(178, 178, 174, 235),
            CloseColor = new(150, 70, 70, 255), CloseHover = new(180, 85, 85, 255),
            TextColor = new(38, 38, 36, 255), CloseText = new(238, 238, 235, 255), LabelDim = new(105, 105, 100, 255)
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

        RegisterTheme("Nord", new InspectorTheme
        {
            WindowBg = new(46, 52, 64, 225), TitleBarColor = new(59, 66, 82, 245), TitleBarHover = new(67, 76, 94, 245),
            ButtonColor = new(59, 66, 82, 225), ButtonHover = new(76, 86, 106, 245),
            SliderTrack = new(46, 52, 64, 210), SliderFill = new(136, 192, 208, 255), SliderHandle = new(143, 188, 187, 255),
            ToggleOn = new(163, 190, 140, 255), ToggleOff = new(59, 66, 82, 220),
            CloseColor = new(191, 97, 106, 255), CloseHover = new(210, 120, 130, 255),
            TextColor = new(236, 239, 244, 255), CloseText = new(236, 239, 244, 255), LabelDim = new(216, 222, 233, 180)
        });
    }
}
