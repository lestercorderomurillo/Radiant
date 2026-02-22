using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

[ComponentDescription("Defines a circle shape with a radius.")]
public struct Circle2D : Component
{
    public float Radius;

    public Circle2D(float radius = 1.0f)
    {
        Radius = radius;
    }
}
