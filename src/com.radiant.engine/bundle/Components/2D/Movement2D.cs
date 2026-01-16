using com.radiant.engine.core;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public struct Movement2D : Component
{
    public Vector2 Speed;
    public Vector2 Acceleration;
    
    public Movement2D()
    {
        Speed = new Vector2();
        Acceleration = new Vector2();
    }
}