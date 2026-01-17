using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

public static class EffectExtensions
{
    public static Effect Set(this Effect effect, string name, float value)
    {
        effect.Parameters[name]?.SetValue(value);
        return effect;
    }

    public static Effect Set(this Effect effect, string name, int value)
    {
        effect.Parameters[name]?.SetValue(value);
        return effect;
    }

    public static Effect Set(this Effect effect, string name, bool value)
    {
        effect.Parameters[name]?.SetValue(value);
        return effect;
    }

    public static Effect Set(this Effect effect, string name, Vector2 value)
    {
        effect.Parameters[name]?.SetValue(value);
        return effect;
    }

    public static Effect Set(this Effect effect, string name, Vector3 value)
    {
        effect.Parameters[name]?.SetValue(value);
        return effect;
    }

    public static Effect Set(this Effect effect, string name, Vector4 value)
    {
        effect.Parameters[name]?.SetValue(value);
        return effect;
    }

    public static Effect Set(this Effect effect, string name, Color value)
    {
        effect.Parameters[name]?.SetValue(value.ToVector4());
        return effect;
    }

    public static Effect Set(this Effect effect, string name, Matrix value)
    {
        effect.Parameters[name]?.SetValue(value);
        return effect;
    }

    public static Effect Set(this Effect effect, string name, Matrix[] value)
    {
        effect.Parameters[name]?.SetValue(value);
        return effect;
    }

    public static Effect Set(this Effect effect, string name, float[] value)
    {
        effect.Parameters[name]?.SetValue(value);
        return effect;
    }

    public static Effect Set(this Effect effect, string name, Vector2[] value)
    {
        effect.Parameters[name]?.SetValue(value);
        return effect;
    }

    public static Effect Set(this Effect effect, string name, Vector3[] value)
    {
        effect.Parameters[name]?.SetValue(value);
        return effect;
    }

    public static Effect Set(this Effect effect, string name, Vector4[] value)
    {
        effect.Parameters[name]?.SetValue(value);
        return effect;
    }

    public static Effect Set(this Effect effect, string name, Texture2D value)
    {
        effect.Parameters[name]?.SetValue(value);
        return effect;
    }

    public static Effect Set(this Effect effect, string name, TextureCube value)
    {
        effect.Parameters[name]?.SetValue(value);
        return effect;
    }

    public static Effect Technique(this Effect effect, string name)
    {
        effect.CurrentTechnique = effect.Techniques[name];
        return effect;
    }
}
