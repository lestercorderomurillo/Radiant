using System;
using System.Collections.Generic;
using System.IO;
using FontStashSharp;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.core;

/// <summary>
/// Manages TTF font families loaded at runtime via FontStashSharp.
/// Fonts are rasterized dynamically at any requested size.
/// </summary>
internal class FontRegistry : IDisposable
{
    private readonly Dictionary<string, FontSystem> Systems = new();
    private readonly string ContentRootDirectory;

    /// <summary>
    /// Supersample multiplier for font rendering. Fonts rasterize at size * FontRenderScale
    /// and draw scaled down for sharp text at high DPI.
    /// </summary>
    public float FontRenderScale { get; set; } = 1f;

    public FontRegistry(string contentRootDirectory)
    {
        ContentRootDirectory = contentRootDirectory;
    }

    /// <summary>
    /// Loads a TTF font file and registers it under the given name.
    /// </summary>
    /// <param name="name">Font family name for later retrieval.</param>
    /// <param name="path">Path to TTF file relative to content root.</param>
    public void Load(string name, string path)
    {
        if (Systems.ContainsKey(name)) return;
        #pragma warning disable CS0618
        var settings = new FontSystemSettings { PremultiplyAlpha = true };
        #pragma warning restore CS0618
        var system = new FontSystem(settings);
        system.AddFont(File.ReadAllBytes(Path.Combine(ContentRootDirectory, path)));
        Systems[name] = system;
    }

    /// <summary>
    /// Gets a SpriteFontBase at the exact requested pixel size (no supersampling).
    /// </summary>
    public SpriteFontBase GetFont(string name, float size) => Systems[name].GetFont(size);

    /// <summary>
    /// Measures text dimensions accounting for FontRenderScale.
    /// </summary>
    public Vector2 Measure(string fontName, float size, string text)
    {
        var font = Systems[fontName].GetFont(size * FontRenderScale);
        return font.MeasureString(text) / FontRenderScale;
    }

    /// <summary>
    /// Gets the line height for a font at a specific size, accounting for FontRenderScale.
    /// </summary>
    public float GetLineHeight(string fontName, float size) =>
        Systems[fontName].GetFont(size * FontRenderScale).LineHeight / FontRenderScale;

    /// <summary>Disposes all font systems.</summary>
    public void Dispose()
    {
        foreach (var fontSystem in Systems.Values)
            fontSystem.Dispose();
        Systems.Clear();
    }
}
