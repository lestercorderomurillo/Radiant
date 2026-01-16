using com.radiant.engine.core;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public struct RigidBody2D : Component
{
    public float Weight;
    
    public RigidBody2D()
    {
        Weight = 1.0f;
    }
}