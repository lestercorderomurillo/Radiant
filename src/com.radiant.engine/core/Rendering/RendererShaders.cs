using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.core;

public partial class Renderer
{
    private Effect CurrentShader;

    private BlendState BlendState = BlendState.Opaque;
    private DepthStencilState DepthStencilState = DepthStencilState.None;
    private RasterizerState RasterizerState = RasterizerState.CullNone;
    private SpriteSortMode SpriteSortMode = SpriteSortMode.Immediate;
    private SamplerState[] SamplerStates = new SamplerState[8];
    private int SamplerDirtyMask = 0;

    /// <summary>
    /// Loads and sets the active shader by name. Shaders are cached after first load.
    /// </summary>
    /// <param name="name">Shader path relative to Content/shaders/ (e.g., "Effects/Blur").</param>
    /// <returns>This renderer for method chaining.</returns>
    public Renderer SetShader(string name)
    {
        CurrentShader = Shaders.Load(name);
        CurrentShaderName = name;
        return this;
    }

    /// <summary>
    /// Gets a shader Effect by name without setting it as active. Useful for external parameter setting.
    /// </summary>
    /// <param name="name">Shader path relative to Content/shaders/.</param>
    /// <returns>The loaded Effect object.</returns>
    public Effect GetShaderEffect(string name) => Shaders.Get(name);

    /// <summary>
    /// Disposes and removes a shader from the cache.
    /// </summary>
    /// <param name="name">Shader path to release.</param>
    /// <returns>This renderer for method chaining.</returns>
    public Renderer ReleaseShader(string name)
    {
        if (Shaders.Release(name) && CurrentShaderName == name)
        {
            CurrentShader = null;
            CurrentShaderName = null;
        }
        return this;
    }

    /// <summary>
    /// Sets the active technique on the current shader.
    /// </summary>
    /// <param name="technique">Name of the technique to activate.</param>
    /// <returns>This renderer for method chaining.</returns>
    public Renderer SetTechnique(string technique)
    {
        if (CurrentShader != null)
            CurrentShader.CurrentTechnique = CurrentShader.Techniques[technique];
        return this;
    }

    /// <summary>Sets the blend state for subsequent draw calls.</summary>
    public Renderer Configure(BlendState state)
    {
        BlendState = state;
        return this;
    }

    /// <summary>Sets the depth stencil state for subsequent draw calls.</summary>
    public Renderer Configure(DepthStencilState state)
    {
        DepthStencilState = state;
        return this;
    }

    /// <summary>Sets the rasterizer state for subsequent draw calls.</summary>
    public Renderer Configure(RasterizerState state)
    {
        RasterizerState = state;
        return this;
    }

    /// <summary>Sets a sampler state at the specified slot.</summary>
    /// <param name="state">The sampler state to set.</param>
    /// <param name="slot">Sampler slot index (0-7).</param>
    public Renderer Configure(SamplerState state, int slot = 0)
    {
        if (slot >= 0 && slot < SamplerStates.Length)
        {
            SamplerStates[slot] = state;
            SamplerDirtyMask |= 1 << slot;
        }
        return this;
    }

    /// <summary>Sets the sprite sort mode for DrawTexture operations.</summary>
    public Renderer Configure(SpriteSortMode mode)
    {
        SpriteSortMode = mode;
        return this;
    }

    /// <summary>Sets two sampler states at specified slots.</summary>
    public Renderer Configure((int slot, SamplerState state) s0, (int slot, SamplerState state) s1)
    {
        if (s0.slot >= 0 && s0.slot < SamplerStates.Length)
        {
            SamplerStates[s0.slot] = s0.state;
            SamplerDirtyMask |= 1 << s0.slot;
        }
        if (s1.slot >= 0 && s1.slot < SamplerStates.Length)
        {
            SamplerStates[s1.slot] = s1.state;
            SamplerDirtyMask |= 1 << s1.slot;
        }
        return this;
    }

    /// <summary>Sets three sampler states at specified slots.</summary>
    public Renderer Configure((int slot, SamplerState state) s0, (int slot, SamplerState state) s1, (int slot, SamplerState state) s2)
    {
        if (s0.slot >= 0 && s0.slot < SamplerStates.Length)
        {
            SamplerStates[s0.slot] = s0.state;
            SamplerDirtyMask |= 1 << s0.slot;
        }
        if (s1.slot >= 0 && s1.slot < SamplerStates.Length)
        {
            SamplerStates[s1.slot] = s1.state;
            SamplerDirtyMask |= 1 << s1.slot;
        }
        if (s2.slot >= 0 && s2.slot < SamplerStates.Length)
        {
            SamplerStates[s2.slot] = s2.state;
            SamplerDirtyMask |= 1 << s2.slot;
        }
        return this;
    }

    /// <summary>Sets four sampler states at specified slots.</summary>
    public Renderer Configure((int slot, SamplerState state) s0, (int slot, SamplerState state) s1, (int slot, SamplerState state) s2, (int slot, SamplerState state) s3)
    {
        if (s0.slot >= 0 && s0.slot < SamplerStates.Length)
        {
            SamplerStates[s0.slot] = s0.state;
            SamplerDirtyMask |= 1 << s0.slot;
        }
        if (s1.slot >= 0 && s1.slot < SamplerStates.Length)
        {
            SamplerStates[s1.slot] = s1.state;
            SamplerDirtyMask |= 1 << s1.slot;
        }
        if (s2.slot >= 0 && s2.slot < SamplerStates.Length)
        {
            SamplerStates[s2.slot] = s2.state;
            SamplerDirtyMask |= 1 << s2.slot;
        }
        if (s3.slot >= 0 && s3.slot < SamplerStates.Length)
        {
            SamplerStates[s3.slot] = s3.state;
            SamplerDirtyMask |= 1 << s3.slot;
        }
        return this;
    }

    /// <summary>Sets multiple sampler states at specified slots.</summary>
    public Renderer Configure(params (int slot, SamplerState state)[] samplers)
    {
        foreach (var (slot, state) in samplers)
        {
            if (slot >= 0 && slot < SamplerStates.Length)
            {
                SamplerStates[slot] = state;
                SamplerDirtyMask |= 1 << slot;
            }
        }
        return this;
    }

    /// <summary>Sets multiple render states by type detection.</summary>
    public Renderer Configure(params object[] states)
    {
        foreach (var state in states)
        {
            switch (state)
            {
                case BlendState bs: BlendState = bs; break;
                case DepthStencilState ds: DepthStencilState = ds; break;
                case RasterizerState rs: RasterizerState = rs; break;
                case SpriteSortMode sm: SpriteSortMode = sm; break;
                case SamplerState ss:
                    SamplerStates[0] = ss;
                    SamplerDirtyMask |= 1;
                    break;
            }
        }
        return this;
    }

    /// <summary>Sets a float parameter on the current (or specified) shader.</summary>
    public Renderer SetParameter(string name, float value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    /// <summary>Sets an int parameter on the current (or specified) shader.</summary>
    public Renderer SetParameter(string name, int value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    /// <summary>Sets a bool parameter on the current (or specified) shader.</summary>
    public Renderer SetParameter(string name, bool value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    /// <summary>Sets a Vector2 parameter on the current (or specified) shader.</summary>
    public Renderer SetParameter(string name, Vector2 value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    /// <summary>Sets a Vector3 parameter on the current (or specified) shader.</summary>
    public Renderer SetParameter(string name, Vector3 value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    /// <summary>Sets a Vector4 parameter on the current (or specified) shader.</summary>
    public Renderer SetParameter(string name, Vector4 value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    /// <summary>Sets a Matrix parameter on the current (or specified) shader.</summary>
    public Renderer SetParameter(string name, Matrix value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    /// <summary>
    /// Sets a Texture2D parameter on the current (or specified) shader.
    /// The shader must have a named texture parameter (not just a register binding).
    /// </summary>
    public Renderer SetParameter(string name, Texture2D value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    /// <summary>Sets a float array parameter on the current (or specified) shader.</summary>
    public Renderer SetParameter(string name, float[] value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    /// <summary>Sets a Vector2 array parameter on the current (or specified) shader.</summary>
    public Renderer SetParameter(string name, Vector2[] value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    /// <summary>Sets a Vector3 array parameter on the current (or specified) shader.</summary>
    public Renderer SetParameter(string name, Vector3[] value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    /// <summary>Sets a Vector4 array parameter on the current (or specified) shader.</summary>
    public Renderer SetParameter(string name, Vector4[] value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    /// <summary>Sets a Matrix array parameter on the current (or specified) shader.</summary>
    public Renderer SetParameter(string name, Matrix[] value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    /// <summary>Sets multiple parameters using tuples with type detection.</summary>
    public Renderer SetParameter(Effect shader = null, params (string name, object value)[] parameters)
    {
        var target = shader ?? CurrentShader;
        if (target == null) return this;

        foreach (var (name, value) in parameters)
            SetParameter(target, name, value);

        return this;
    }

    /// <summary>
    /// Static helper for setting parameters on external Effect objects with automatic type detection.
    /// </summary>
    public static void SetParameter(Effect shader, string key, object value)
    {
        var parameter = shader?.Parameters[key];
        if (parameter == null) return;

        switch (value)
        {
            case float f: parameter.SetValue(f); break;
            case int i: parameter.SetValue(i); break;
            case bool b: parameter.SetValue(b); break;
            case Vector2 v2: parameter.SetValue(v2); break;
            case Vector3 v3: parameter.SetValue(v3); break;
            case Vector4 v4: parameter.SetValue(v4); break;
            case Matrix m: parameter.SetValue(m); break;
            case Texture2D t: parameter.SetValue(t); break;
            case float[] fa: parameter.SetValue(fa); break;
            case Vector2[] v2a: parameter.SetValue(v2a); break;
            case Vector3[] v3a: parameter.SetValue(v3a); break;
            case Vector4[] v4a: parameter.SetValue(v4a); break;
            case Matrix[] ma: parameter.SetValue(ma); break;
        }
    }
}
