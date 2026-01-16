using com.radiant.engine.core;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public struct Collision2D : Component
{
    public Vector2 Bounds;
    
    public Collision2D()
    {
        Bounds = new Vector2(1.0f);
    }
}