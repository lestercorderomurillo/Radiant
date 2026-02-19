using System;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public static class VectorExtensions
{
    public static Vector3 Add(this Vector3 v, float scalar)
        => new Vector3(v.X + scalar, v.Y + scalar, v.Z + scalar);

    public static Vector3 Subtract(this Vector3 v, float scalar)
        => new Vector3(v.X - scalar, v.Y - scalar, v.Z - scalar);

    public static Vector3 Apply(this Vector3 v, Func<float, float> func)
        => new Vector3(func(v.X), func(v.Y), func(v.Z));

    public static Vector3 Apply(this Vector3 v, Vector3 other, Func<float, float, float> func)
        => new Vector3(func(v.X, other.X), func(v.Y, other.Y), func(v.Z, other.Z));
}
