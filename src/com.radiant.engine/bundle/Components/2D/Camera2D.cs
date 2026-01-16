using com.radiant.engine.core;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public struct Camera2D : Component
{
    public Vector4 Bounds;
    public Vector2 Rotation;

    public Camera2D()
    {
        Bounds = new Vector4();
        Rotation = new Vector2();
    }
}