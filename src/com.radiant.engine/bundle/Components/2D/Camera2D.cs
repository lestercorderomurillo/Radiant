using com.radiant.engine.core;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public struct Camera2D : Component
{
    public Vector2 Position;
    public float Zoom;
    public float Rotation;

    public Camera2D()
    {
        Position = Vector2.Zero;
        Zoom = 1f;
        Rotation = 0f;
    }
}