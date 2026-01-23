using System;
using System.Collections.Generic;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.core;

/// <summary>
/// Fluent rendering API for MonoGame/XNA providing shader management, render targets,
/// and fullscreen quad drawing for post-processing effects.
///
/// <example>
/// Basic fullscreen shader pass:
/// <code>
/// Renderer
///     .Reset()
///     .SetShader("Effects/Blur")
///     .Configure((0, SamplerState.LinearClamp))
///     .SetTarget(outputTexture)
///     .Clear(Color.Black)
///     .SetParameter("InputTexture", inputTexture)
///     .SetParameter("BlurRadius", 5.0f)
///     .Draw()
///     .Commit();
/// </code>
/// </example>
///
/// <example>
/// Multiple render target (MRT) rendering:
/// <code>
/// Renderer
///     .Reset()
///     .SetShader("GBuffer/Generate")
///     .SetTargets(albedoRT, normalRT, depthRT)
///     .Clear(Color.Black)
///     .Draw()
///     .Commit();
/// </code>
/// </example>
///
/// <example>
/// SpriteBatch-based texture drawing with shader:
/// <code>
/// Renderer
///     .Reset()
///     .SetShader("Effects/ColorGrade")
///     .Configure(BlendState.AlphaBlend)
///     .SetTarget(outputTexture)
///     .DrawTexture(inputTexture, Vector2.Zero)
///     .Commit();
/// </code>
/// </example>
/// </summary>
public class Renderer : IDisposable
{
    #region Public Properties

    /// <summary>The parent window containing the graphics device.</summary>
    public Window Window { get; }

    /// <summary>The underlying MonoGame graphics device.</summary>
    public GraphicsDevice Device { get; }

    /// <summary>Shared SpriteBatch for texture drawing operations.</summary>
    public SpriteBatch SpriteBatch { get; }

    /// <summary>True if currently between Begin/Draw and Commit calls.</summary>
    public bool IsDrawing { get; private set; }

    /// <summary>Name of the currently active shader (null if none).</summary>
    public string CurrentShaderName { get; private set; }

    #endregion

    #region Screen Information

    /// <summary>Current viewport width in pixels.</summary>
    public int ScreenWidth { get; private set; }

    /// <summary>Current viewport height in pixels.</summary>
    public int ScreenHeight { get; private set; }

    /// <summary>Current viewport size as Vector2.</summary>
    public Vector2 ScreenSize { get; private set; }

    /// <summary>Width / Height ratio.</summary>
    public float AspectRatio { get; private set; }

    /// <summary>Height / Width ratio.</summary>
    public float InverseAspectRatio { get; private set; }

    /// <summary>Diagonal length of the screen in pixels.</summary>
    public float ScreenDiagonal { get; private set; }

    /// <summary>Total pixel count (Width * Height).</summary>
    public int ScreenArea { get; private set; }

    /// <summary>Largest power of 2 that fits within max(Width, Height).</summary>
    public int ScreenLowerPowerOfTwo { get; private set; }

    /// <summary>Smallest power of 2 that contains max(Width, Height).</summary>
    public int ScreenHigherPowerOfTwo { get; private set; }

    /// <summary>Square Vector2 using ScreenLowerPowerOfTwo.</summary>
    public Vector2 ScreenSizeLowerPowerOfTwo { get; private set; }

    /// <summary>Square Vector2 using ScreenHigherPowerOfTwo.</summary>
    public Vector2 ScreenSizeHigherPowerOfTwo { get; private set; }

    #endregion

    #region Render Scale (Dynamic Resolution)

    private float _renderScale = 1.0f;

    /// <summary>
    /// Render scale factor for dynamic resolution (0.25 to 1.0).
    /// Systems can subscribe to RenderScaleChanged to resize their render targets.
    /// </summary>
    public float RenderScale
    {
        get => _renderScale;
        set
        {
            if (Math.Abs(_renderScale - value) > 0.001f)
            {
                _renderScale = Math.Clamp(value, 0.25f, 1.0f);
                UpdateScaledScreenInfo();
                RenderScaleChanged?.Invoke(_renderScale);
            }
        }
    }

    /// <summary>Fired when RenderScale changes. Parameter is the new scale value.</summary>
    public event Action<float> RenderScaleChanged;

    /// <summary>Scaled viewport width (ScreenWidth * RenderScale).</summary>
    public int ScaledWidth { get; private set; }

    /// <summary>Scaled viewport height (ScreenHeight * RenderScale).</summary>
    public int ScaledHeight { get; private set; }

    /// <summary>Scaled viewport size as Vector2.</summary>
    public Vector2 ScaledSize { get; private set; }

    /// <summary>Smallest power of 2 containing max(ScaledWidth, ScaledHeight).</summary>
    public int ScaledHigherPowerOfTwo { get; private set; }

    #endregion

    #region Private State

    private GameWindow NativeWindow => Window.Window;
    private Dictionary<string, Effect> ShaderCache = new();
    private Dictionary<(Color, int, int), Texture2D> SolidTextureCache = new();
    private Dictionary<int, Texture2D> CircleTextureCache = new();
    private VertexBuffer QuadVertexBuffer;
    private IndexBuffer QuadIndexBuffer;
    private Effect CurrentShader;
    private bool IsDrawingTextures;

    private BlendState BlendState = BlendState.Opaque;
    private DepthStencilState DepthStencilState = DepthStencilState.None;
    private RasterizerState RasterizerState = RasterizerState.CullNone;
    private SpriteSortMode SpriteSortMode = SpriteSortMode.Immediate;
    private SamplerState[] SamplerStates = new SamplerState[8];
    private int SamplerDirtyMask = 0;

    private readonly RenderTargetBinding[] _twoTargetBindings = new RenderTargetBinding[2];
    private readonly RenderTargetBinding[] _threeTargetBindings = new RenderTargetBinding[3];
    private readonly RenderTargetBinding[] _fourTargetBindings = new RenderTargetBinding[4];
    private readonly Stack<RenderTargetBinding[]> _renderTargetStack = new();
    private RenderTargetBinding[] _currentTargets = null;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new Renderer bound to the specified window.
    /// </summary>
    /// <param name="window">The window containing the graphics device.</param>
    public Renderer(Window window)
    {
        Window = window;
        Device = window.GraphicsDevice;
        SpriteBatch = new SpriteBatch(Device);

        for (int i = 0; i < SamplerStates.Length; i++)
            SamplerStates[i] = SamplerState.LinearClamp;

        InitializeQuad();
        UpdateScreenInfo();
        UpdateScaledScreenInfo();

        NativeWindow.ClientSizeChanged += (_, _) =>
        {
            UpdateScreenInfo();
            UpdateScaledScreenInfo();
        };
    }

    #endregion

    #region Screen Info Updates

    /// <summary>Updates all screen-related properties from the current viewport.</summary>
    public void UpdateScreenInfo()
    {
        var viewport = Device.Viewport;
        ScreenWidth = viewport.Width;
        ScreenHeight = viewport.Height;
        ScreenSize = new Vector2(ScreenWidth, ScreenHeight);
        AspectRatio = (float)ScreenWidth / ScreenHeight;
        InverseAspectRatio = (float)ScreenHeight / ScreenWidth;
        ScreenDiagonal = MathF.Sqrt(ScreenWidth * ScreenWidth + ScreenHeight * ScreenHeight);
        ScreenArea = ScreenWidth * ScreenHeight;

        int maxDimension = Math.Max(ScreenWidth, ScreenHeight);
        ScreenLowerPowerOfTwo = GetLowerPowerOfTwo(maxDimension);
        ScreenHigherPowerOfTwo = GetHigherPowerOfTwo(maxDimension);
        ScreenSizeLowerPowerOfTwo = new Vector2(ScreenLowerPowerOfTwo, ScreenLowerPowerOfTwo);
        ScreenSizeHigherPowerOfTwo = new Vector2(ScreenHigherPowerOfTwo, ScreenHigherPowerOfTwo);
    }

    private void UpdateScaledScreenInfo()
    {
        ScaledWidth = Math.Max(1, (int)(ScreenWidth * _renderScale));
        ScaledHeight = Math.Max(1, (int)(ScreenHeight * _renderScale));
        ScaledSize = new Vector2(ScaledWidth, ScaledHeight);

        int scaledMax = Math.Max(ScaledWidth, ScaledHeight);
        ScaledHigherPowerOfTwo = GetHigherPowerOfTwo(scaledMax);
    }

    private static int GetLowerPowerOfTwo(int value)
    {
        if (value <= 0) return 1;
        int power = 1;
        while (power * 2 <= value)
            power *= 2;
        return power;
    }

    private static int GetHigherPowerOfTwo(int value)
    {
        if (value <= 0) return 1;
        int power = 1;
        while (power < value)
            power *= 2;
        return power;
    }

    #endregion

    #region Quad Initialization

    private void InitializeQuad()
    {
        var vertices = new VertexPositionTexture[]
        {
            new(new Vector3(-1,  1, 0), new Vector2(0, 0)),
            new(new Vector3( 1,  1, 0), new Vector2(1, 0)),
            new(new Vector3(-1, -1, 0), new Vector2(0, 1)),
            new(new Vector3( 1, -1, 0), new Vector2(1, 1))
        };

        QuadVertexBuffer = new VertexBuffer(Device, typeof(VertexPositionTexture), 4, BufferUsage.WriteOnly);
        QuadVertexBuffer.SetData(vertices);

        var indices = new short[] { 0, 1, 2, 2, 1, 3 };
        QuadIndexBuffer = new IndexBuffer(Device, IndexElementSize.SixteenBits, 6, BufferUsage.WriteOnly);
        QuadIndexBuffer.SetData(indices);
    }

    #endregion

    #region Shader Management

    /// <summary>
    /// Loads and sets the active shader by name. Shaders are cached after first load.
    /// </summary>
    /// <param name="name">Shader path relative to Content/shaders/ (e.g., "Effects/Blur").</param>
    /// <returns>This renderer for method chaining.</returns>
    public Renderer SetShader(string name)
    {
        if (!ShaderCache.TryGetValue(name, out var shader))
        {
            shader = Window.Content.Load<Effect>($"shaders/{name}");
            ShaderCache[name] = shader;
        }

        CurrentShader = shader;
        CurrentShaderName = name;
        return this;
    }

    /// <summary>
    /// Gets a shader Effect by name without setting it as active. Useful for external parameter setting.
    /// </summary>
    /// <param name="name">Shader path relative to Content/shaders/.</param>
    /// <returns>The loaded Effect object.</returns>
    public Effect GetShaderEffect(string name)
    {
        if (!ShaderCache.TryGetValue(name, out var shader))
        {
            shader = Window.Content.Load<Effect>($"shaders/{name}");
            ShaderCache[name] = shader;
        }
        return shader;
    }

    /// <summary>
    /// Disposes and removes a shader from the cache.
    /// </summary>
    /// <param name="name">Shader path to release.</param>
    /// <returns>This renderer for method chaining.</returns>
    public Renderer ReleaseShader(string name)
    {
        if (ShaderCache.TryGetValue(name, out var shader))
        {
            shader.Dispose();
            ShaderCache.Remove(name);

            if (CurrentShaderName == name)
            {
                CurrentShader = null;
                CurrentShaderName = null;
            }
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

    #endregion

    #region State Configuration

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

    #endregion

    #region Render Targets

    /// <summary>
    /// Pushes current render targets onto an internal stack. Use with PopTargets to
    /// restore state after nested rendering operations without GPU synchronization.
    /// </summary>
    public Renderer PushTargets()
    {
        _renderTargetStack.Push(_currentTargets);
        return this;
    }

    /// <summary>
    /// Pops and restores render targets from the internal stack.
    /// </summary>
    public Renderer PopTargets()
    {
        if (_renderTargetStack.Count > 0)
        {
            var targets = _renderTargetStack.Pop();
            CommitTextures();
            if (targets == null)
                Device.SetRenderTarget(null);
            else
                Device.SetRenderTargets(targets);
            _currentTargets = targets;
        }
        return this;
    }

    /// <summary>Sets a single render target (or null for backbuffer).</summary>
    public Renderer SetTarget(RenderTarget2D target)
    {
        CommitTextures();
        Device.SetRenderTarget(target);
        _currentTargets = target != null ? new[] { new RenderTargetBinding(target) } : null;
        return this;
    }

    /// <summary>Sets two render targets for MRT rendering.</summary>
    public Renderer SetTargets(RenderTarget2D target0, RenderTarget2D target1)
    {
        CommitTextures();
        _twoTargetBindings[0] = new RenderTargetBinding(target0);
        _twoTargetBindings[1] = new RenderTargetBinding(target1);
        Device.SetRenderTargets(_twoTargetBindings);
        _currentTargets = (RenderTargetBinding[])_twoTargetBindings.Clone();
        return this;
    }

    /// <summary>Sets three render targets for MRT rendering.</summary>
    public Renderer SetTargets(RenderTarget2D target0, RenderTarget2D target1, RenderTarget2D target2)
    {
        CommitTextures();
        _threeTargetBindings[0] = new RenderTargetBinding(target0);
        _threeTargetBindings[1] = new RenderTargetBinding(target1);
        _threeTargetBindings[2] = new RenderTargetBinding(target2);
        Device.SetRenderTargets(_threeTargetBindings);
        _currentTargets = (RenderTargetBinding[])_threeTargetBindings.Clone();
        return this;
    }

    /// <summary>Sets four render targets for MRT rendering.</summary>
    public Renderer SetTargets(RenderTarget2D target0, RenderTarget2D target1, RenderTarget2D target2, RenderTarget2D target3)
    {
        CommitTextures();
        _fourTargetBindings[0] = new RenderTargetBinding(target0);
        _fourTargetBindings[1] = new RenderTargetBinding(target1);
        _fourTargetBindings[2] = new RenderTargetBinding(target2);
        _fourTargetBindings[3] = new RenderTargetBinding(target3);
        Device.SetRenderTargets(_fourTargetBindings);
        _currentTargets = (RenderTargetBinding[])_fourTargetBindings.Clone();
        return this;
    }

    /// <summary>Sets multiple render targets for MRT rendering.</summary>
    public Renderer SetTargets(params RenderTarget2D[] targets)
    {
        CommitTextures();
        var bindings = new RenderTargetBinding[targets.Length];
        for (int i = 0; i < targets.Length; i++)
            bindings[i] = new RenderTargetBinding(targets[i]);
        Device.SetRenderTargets(bindings);
        _currentTargets = bindings;
        return this;
    }

    /// <summary>Sets render targets from pre-built bindings array.</summary>
    public Renderer SetTargets(params RenderTargetBinding[] bindings)
    {
        CommitTextures();
        Device.SetRenderTargets(bindings);
        _currentTargets = bindings;
        return this;
    }

    #endregion

    #region Clear

    /// <summary>
    /// Clears the current render target(s) to the specified color.
    /// </summary>
    /// <param name="color">Clear color (defaults to Black).</param>
    public Renderer Clear(Color? color = null)
    {
        Device.Clear(color ?? Color.Black);
        return this;
    }

    #endregion

    #region Shader Parameters

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

    #endregion

    #region Texture Utilities

    /// <summary>
    /// Gets or creates a cached solid color texture.
    /// </summary>
    /// <param name="color">Fill color for the texture.</param>
    /// <param name="width">Texture width (default 1).</param>
    /// <param name="height">Texture height (default 1).</param>
    /// <returns>Cached texture with the specified color.</returns>
    public Texture2D GetSolidTexture(Color color, int width = 1, int height = 1)
    {
        var key = (color, width, height);
        if (!SolidTextureCache.TryGetValue(key, out var texture))
        {
            texture = new Texture2D(Device, width, height);
            var data = new Color[width * height];
            Array.Fill(data, color);
            texture.SetData(data);
            SolidTextureCache[key] = texture;
        }
        return texture;
    }

    /// <summary>
    /// Gets or creates a cached anti-aliased circle texture.
    /// </summary>
    /// <param name="diameter">Circle diameter in pixels.</param>
    /// <returns>Cached white circle texture with premultiplied alpha.</returns>
    public Texture2D GetCircleTexture(int diameter)
    {
        if (diameter < 1) diameter = 1;

        if (!CircleTextureCache.TryGetValue(diameter, out var texture))
        {
            texture = new Texture2D(Device, diameter, diameter);
            var data = new Color[diameter * diameter];

            float radius = diameter / 2f;
            float centerX = radius - 0.5f;
            float centerY = radius - 0.5f;

            const float aaWidth = 1.0f;
            float innerRadius = radius - aaWidth * 0.5f;

            for (int y = 0; y < diameter; y++)
            {
                for (int x = 0; x < diameter; x++)
                {
                    float dx = x - centerX;
                    float dy = y - centerY;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);

                    float alpha = 1.0f - MathHelper.Clamp((dist - innerRadius) / aaWidth, 0f, 1f);
                    byte a = (byte)(alpha * 255f + 0.5f);

                    data[y * diameter + x] = new Color(a, a, a, alpha);
                }
            }

            texture.SetData(data);
            CircleTextureCache[diameter] = texture;
        }
        return texture;
    }

    /// <summary>
    /// Uploads raw Color array data to a render target. Use for efficient bulk updates.
    /// The array should match the texture dimensions (width * height elements).
    /// </summary>
    /// <param name="target">The render target to update.</param>
    /// <param name="data">Color array to upload (must be width * height in length).</param>
    /// <param name="count">Number of elements to upload (0 = all).</param>
    public void UploadToTexture(RenderTarget2D target, Color[] data, int count = 0)
    {
        if (count <= 0)
            count = data.Length;
        target.SetData(data, 0, count);
    }

    /// <summary>
    /// Uploads raw Color array data to a texture region.
    /// </summary>
    /// <param name="target">The render target to update.</param>
    /// <param name="data">Color array to upload.</param>
    /// <param name="region">Destination rectangle within the texture.</param>
    public void UploadToTexture(RenderTarget2D target, Color[] data, Rectangle region)
    {
        target.SetData(0, region, data, 0, region.Width * region.Height);
    }

    /// <summary>
    /// Binds a texture directly to a device slot (for register-bound shader textures).
    /// Prefer SetParameter for named texture parameters.
    /// </summary>
    public Renderer SetTexture(int slot, Texture2D texture)
    {
        Device.Textures[slot] = texture;
        return this;
    }

    /// <summary>Binds a texture and sampler directly to a device slot.</summary>
    public Renderer SetTexture(int slot, Texture2D texture, SamplerState sampler)
    {
        Device.Textures[slot] = texture;
        Device.SamplerStates[slot] = sampler;
        return this;
    }

    /// <summary>Binds multiple textures directly to device slots.</summary>
    public Renderer SetTextures(params (int slot, Texture2D texture)[] textures)
    {
        foreach (var (slot, texture) in textures)
            Device.Textures[slot] = texture;
        return this;
    }

    /// <summary>Binds multiple textures and samplers directly to device slots.</summary>
    public Renderer SetTextures(params (int slot, Texture2D texture, SamplerState sampler)[] textures)
    {
        foreach (var (slot, texture, sampler) in textures)
        {
            Device.Textures[slot] = texture;
            Device.SamplerStates[slot] = sampler;
        }
        return this;
    }

    /// <summary>Clears texture bindings on the first N slots.</summary>
    public Renderer ClearTextures(int count = 4)
    {
        for (int i = 0; i < count; i++)
            Device.Textures[i] = null;
        return this;
    }

    #endregion

    #region Drawing

    /// <summary>
    /// Draws a fullscreen quad using the current shader. The shader must have a vertex
    /// shader that accepts POSITION0 and TEXCOORD0 semantics.
    /// </summary>
    public Renderer Draw()
    {
        CommitTextures();

        Device.BlendState = BlendState;
        Device.DepthStencilState = DepthStencilState;
        Device.RasterizerState = RasterizerState;

        for (int i = 0; i < SamplerStates.Length; i++)
        {
            if ((SamplerDirtyMask & (1 << i)) != 0)
                Device.SamplerStates[i] = SamplerStates[i];
        }

        Device.SetVertexBuffer(QuadVertexBuffer);
        Device.Indices = QuadIndexBuffer;

        if (CurrentShader != null)
        {
            foreach (var pass in CurrentShader.CurrentTechnique.Passes)
            {
                pass.Apply();
                Device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 2);
            }
        }

        IsDrawing = true;
        return this;
    }

    /// <summary>Draws a texture at the specified position using SpriteBatch.</summary>
    public Renderer DrawTexture(Texture2D texture, Vector2 position)
    {
        return DrawTexture(texture, position, Color.White);
    }

    /// <summary>Draws a texture at the specified position with tint using SpriteBatch.</summary>
    public Renderer DrawTexture(Texture2D texture, Vector2 position, Color color)
    {
        BeginTextures();
        SpriteBatch.Draw(texture, position, color);
        return this;
    }

    /// <summary>Draws a texture stretched to the destination rectangle using SpriteBatch.</summary>
    public Renderer DrawTexture(Texture2D texture, Rectangle destination)
    {
        return DrawTexture(texture, destination, Color.White);
    }

    /// <summary>Draws a texture stretched to the destination rectangle with tint using SpriteBatch.</summary>
    public Renderer DrawTexture(Texture2D texture, Rectangle destination, Color color)
    {
        BeginTextures();
        SpriteBatch.Draw(texture, destination, color);
        return this;
    }

    /// <summary>Draws a texture with source rectangle using SpriteBatch.</summary>
    public Renderer DrawTexture(Texture2D texture, Rectangle destination, Rectangle? source, Color color)
    {
        BeginTextures();
        SpriteBatch.Draw(texture, destination, source, color);
        return this;
    }

    /// <summary>Draws a texture with full transform parameters using SpriteBatch.</summary>
    public Renderer DrawTexture(Texture2D texture, Vector2 position, Rectangle? source, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float depth)
    {
        BeginTextures();
        SpriteBatch.Draw(texture, position, source, color, rotation, origin, scale, effects, depth);
        return this;
    }

    private void BeginTextures()
    {
        if (IsDrawingTextures) return;

        SpriteBatch.Begin(
            SpriteSortMode,
            BlendState,
            SamplerStates[0],
            DepthStencilState,
            RasterizerState,
            CurrentShader
        );

        IsDrawingTextures = true;
        IsDrawing = true;
    }

    private void CommitTextures()
    {
        if (!IsDrawingTextures) return;

        SpriteBatch.End();
        IsDrawingTextures = false;
    }

    #endregion

    #region Ping-Pong Rendering

    /// <summary>
    /// Performs ping-pong rendering between two render targets for multi-pass effects.
    /// </summary>
    /// <param name="a">First render target.</param>
    /// <param name="b">Second render target.</param>
    /// <param name="passes">Number of passes to perform.</param>
    /// <param name="beforePass">Callback before each pass. Receives pass index and current input texture.</param>
    /// <param name="afterPass">Callback after each pass. Receives pass index.</param>
    /// <param name="clearColor">Color to clear output target each pass (default Black).</param>
    /// <returns>The final output render target (may be a or b depending on pass count).</returns>
    public RenderTarget2D PingPong(
        RenderTarget2D a,
        RenderTarget2D b,
        int passes,
        Action<int, RenderTarget2D> beforePass = null,
        Action<int> afterPass = null,
        Color? clearColor = null)
    {
        RenderTarget2D input = a;
        RenderTarget2D output = b;
        Color clear = clearColor ?? Color.Black;

        for (int i = 0; i < passes; i++)
        {
            beforePass?.Invoke(i, input);

            Device.SetRenderTarget(output);
            Device.Clear(clear);

            Device.BlendState = BlendState;
            Device.DepthStencilState = DepthStencilState;
            Device.RasterizerState = RasterizerState;

            for (int s = 0; s < SamplerStates.Length; s++)
            {
                if ((SamplerDirtyMask & (1 << s)) != 0)
                    Device.SamplerStates[s] = SamplerStates[s];
            }

            Device.SetVertexBuffer(QuadVertexBuffer);
            Device.Indices = QuadIndexBuffer;

            if (CurrentShader != null)
            {
                foreach (var pass in CurrentShader.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    Device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 2);
                }
            }

            afterPass?.Invoke(i);

            (input, output) = (output, input);
        }

        Device.SetRenderTarget(null);
        return input;
    }

    #endregion

    #region Flow Control

    /// <summary>Marks the renderer as actively drawing (rarely needed directly).</summary>
    public Renderer Begin()
    {
        IsDrawing = true;
        return this;
    }

    /// <summary>
    /// Commits any pending SpriteBatch operations and marks drawing complete.
    /// Call at the end of each render pass.
    /// </summary>
    public Renderer Commit()
    {
        CommitTextures();
        IsDrawing = false;
        return this;
    }

    /// <summary>
    /// Resets all render state to defaults and clears the current shader.
    /// Call at the start of each render pass for clean state.
    /// </summary>
    public Renderer Reset()
    {
        CommitTextures();

        BlendState = BlendState.Opaque;
        DepthStencilState = DepthStencilState.None;
        RasterizerState = RasterizerState.CullNone;
        SpriteSortMode = SpriteSortMode.Immediate;
        SamplerDirtyMask = 0;

        CurrentShader = null;
        CurrentShaderName = null;
        IsDrawing = false;

        return this;
    }

    #endregion

    #region IDisposable

    /// <summary>Disposes all cached resources (shaders, textures, buffers).</summary>
    public void Dispose()
    {
        QuadVertexBuffer?.Dispose();
        QuadIndexBuffer?.Dispose();
        SpriteBatch?.Dispose();

        foreach (var texture in SolidTextureCache.Values)
            texture?.Dispose();
        SolidTextureCache.Clear();

        foreach (var texture in CircleTextureCache.Values)
            texture?.Dispose();
        CircleTextureCache.Clear();

        foreach (var shader in ShaderCache.Values)
            shader?.Dispose();
        ShaderCache.Clear();
    }

    #endregion
}
