using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public struct Triangle2D : Component
{
    public Vector2 Size;
    public bool Bordered;

    public Triangle2D(Vector2? size = null, bool bordered = false)
    {
        Size = size ?? new Vector2(1.0f);
        Bordered = bordered;
    }
}
