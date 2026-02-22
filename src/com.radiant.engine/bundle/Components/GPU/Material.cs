using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

[ComponentDescription("Surface color, emission and texture for rendering.")]
public struct Material : Component
{
    private Color AlbedoColor;
    private Color EmissiveColor;

    /// <summary>Optional texture that modulates the emissive color. Null = solid color (default).</summary>
    public Texture2D Texture;

    public Color Albedo
    {
        readonly get => AlbedoColor;
        set { AlbedoColor = value; UpdateCached(); }
    }

    public Color Emissive
    {
        readonly get => EmissiveColor;
        set { EmissiveColor = value; UpdateCached(); }
    }

    /// <summary>Auto-calculated: Albedo inverted, scaled by alpha. Used by HRC.</summary>
    public Color Absorption { get; private set; }

    /// <summary>Auto-calculated: Emissive RGB scaled by intensity (A), with alpha=255 for rendering.</summary>
    public Color EmissiveScaled { get; private set; }

    public Material()
    {
        AlbedoColor = Color.White;
        EmissiveColor = Color.Black;
        Absorption = new Color(0, 0, 0, 255);
        EmissiveScaled = Color.Black;
    }

    private void UpdateCached()
    {
        float intensity = EmissiveColor.A / 255f;
        EmissiveScaled = new Color(
            (int)(EmissiveColor.R * intensity),
            (int)(EmissiveColor.G * intensity),
            (int)(EmissiveColor.B * intensity),
            EmissiveColor.A);

        bool isEmissive = EmissiveColor.R > 0 || EmissiveColor.G > 0 || EmissiveColor.B > 0;
        if (isEmissive)
        {
            Absorption = new Color(
                (int)(EmissiveColor.R * intensity),
                (int)(EmissiveColor.G * intensity),
                (int)(EmissiveColor.B * intensity),
                AlbedoColor.A);
        }
        else
        {
            float alpha = AlbedoColor.A / 255f;
            Absorption = new Color(
                (int)((255 - AlbedoColor.R) * alpha),
                (int)((255 - AlbedoColor.G) * alpha),
                (int)((255 - AlbedoColor.B) * alpha),
                AlbedoColor.A);
        }
    }
}
