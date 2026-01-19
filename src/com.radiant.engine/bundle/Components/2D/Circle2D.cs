using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public struct Circle2D : Component
{
    public float Radius;

    public Circle2D(float radius = 1.0f)
    {
        Radius = radius;
    }
}
