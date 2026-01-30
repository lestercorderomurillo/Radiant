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
        set { _albedo = value; UpdateAbsorption(); }
    }

    public Color Emissive
    {
        readonly get => _emissive;
        set { _emissive = value; UpdateAbsorption(); }
    }

    public Color Absorption { get; private set; }

    public Material()
    {
        _albedo = Color.White;
        _emissive = Color.Black;
        Absorption = new Color(0, 0, 0, 255);
    }

    private void UpdateAbsorption()
    {
        bool isEmissive = _emissive.R > 0 || _emissive.G > 0 || _emissive.B > 0;
        float alpha = _albedo.A / 255f;

        if (isEmissive)
        {
            float intensity = _emissive.A / 255f;
            Absorption = new Color(
                (int)(_emissive.R * intensity),
                (int)(_emissive.G * intensity),
                (int)(_emissive.B * intensity),
                _albedo.A);
        }
        else
        {
            Absorption = new Color(
                (int)((255 - _albedo.R) * alpha),
                (int)((255 - _albedo.G) * alpha),
                (int)((255 - _albedo.B) * alpha),
                _albedo.A);
        }
    }
}