using com.radiant.engine.core;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public struct Material : Component
{
    public Color Albedo;

    public Color Emissive;

    public Material()
    {
        Albedo = Color.White;
        Emissive = Color.Black;
    }
}