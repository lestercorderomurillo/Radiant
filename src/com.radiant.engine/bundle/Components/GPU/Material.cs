using com.radiant.engine.core;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public struct Material : Component
{
    private Color _albedo;
    private Color _emissive;

    public Color Albedo
    {
        readonly get => _albedo;
        set { _albedo = value; UpdateCached(); }
    }

    public Color Emissive
    {
        readonly get => _emissive;
        set { _emissive = value; UpdateCached(); }
    }

    /// <summary>Auto-calculated: Albedo inverted, scaled by alpha. Used by HRC.</summary>
    public Color Absorption { get; private set; }

    /// <summary>Auto-calculated: Emissive RGB scaled by intensity (A), with alpha=255 for rendering.</summary>
    public Color EmissiveScaled { get; private set; }

    public Material()
    {
        _albedo = Color.White;
        _emissive = Color.Black;
        Absorption = new Color(0, 0, 0, 255);
        EmissiveScaled = Color.Black;
    }

    private void UpdateCached()
    {
        // EmissiveScaled: RGB scaled by intensity (A), alpha=255 for rendering
        float intensity = _emissive.A / 255f;
        EmissiveScaled = new Color(
            (int)(_emissive.R * intensity),
            (int)(_emissive.G * intensity),
            (int)(_emissive.B * intensity),
            _emissive.A);

        // Absorption depends on whether object emits light
        // HRC formula: radiance = absorption * emission, so emitters need absorption = emission
        bool isEmissive = _emissive.R > 0 || _emissive.G > 0 || _emissive.B > 0;
        if (isEmissive)
        {
            // Emitters: absorption = scaled emissive (required by HRC radiance formula)
            Absorption = new Color(
                (int)(_emissive.R * intensity),
                (int)(_emissive.G * intensity),
                (int)(_emissive.B * intensity),
                _albedo.A);
        }
        else
        {
            // Non-emitters: absorption = inverted albedo
            float alpha = _albedo.A / 255f;
            Absorption = new Color(
                (int)((255 - _albedo.R) * alpha),
                (int)((255 - _albedo.G) * alpha),
                (int)((255 - _albedo.B) * alpha),
                _albedo.A);
        }
    }
}