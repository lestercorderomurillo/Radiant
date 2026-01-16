using com.radiant.engine.core;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public struct Transform : Component
{
    public Vector3 Position;
    
    public Vector3 Rotation;

    public Vector3 Scale;

    public Transform()
    {
        Position = new Vector3();
        Rotation = new Vector3();
        Scale = new Vector3(1.0f);
    }
}