using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public struct Rectangle2D : Component
{
    public Vector2 Size;

    public Rectangle2D(Vector2? size)
    {
        Size = size ?? new Vector2(1.0f);
    }
}