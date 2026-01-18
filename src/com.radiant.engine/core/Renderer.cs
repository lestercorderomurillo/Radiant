using System;
using System.Collections.Generic;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.core;

public class Renderer : IDisposable
{
    // State
    public Window Window { get; }
    private GameWindow NativeWindow => Window.Window;
    public GraphicsDevice Device { get; }
    public SpriteBatch SpriteBatch { get; }
    public bool IsDrawing { get; private set; }
    public string CurrentShaderName { get; private set; }

    // Screen Info
    public int ScreenWidth { get; private set; }
    public int ScreenHeight { get; private set; }
    public Vector2 ScreenSize { get; private set; }
    public float AspectRatio { get; private set; }
    public float InverseAspectRatio { get; private set; }
    public float ScreenDiagonal { get; private set; }
    public int ScreenArea { get; private set; }
    public int ScreenLowerPowerOfTwo { get; private set; }
    public int ScreenHigherPowerOfTwo { get; private set; }
    public Vector2 ScreenSizeLowerPowerOfTwo { get; private set; }
    public Vector2 ScreenSizeHigherPowerOfTwo { get; private set; }

    // Internal
    private Dictionary<string, Effect> ShaderCache = new();
    private Dictionary<(Color, int, int), Texture2D> SolidTextureCache = new();
    private VertexBuffer QuadVertexBuffer;
    private IndexBuffer QuadIndexBuffer;
    private Effect CurrentShader;
    private bool IsDrawingTextures;

    // Configured states
    private BlendState BlendState = BlendState.Opaque;
    private DepthStencilState DepthStencilState = DepthStencilState.None;
    private RasterizerState RasterizerState = RasterizerState.CullNone;
    private SpriteSortMode SpriteSortMode = SpriteSortMode.Immediate;
    private SamplerState[] SamplerStates = new SamplerState[8];

    // Cached MRT binding arrays (allocation-free)
    private readonly RenderTargetBinding[] _twoTargetBindings = new RenderTargetBinding[2];
    private readonly RenderTargetBinding[] _threeTargetBindings = new RenderTargetBinding[3];
    private readonly RenderTargetBinding[] _fourTargetBindings = new RenderTargetBinding[4];

    public Renderer(Window window)
    {
        Window = window;
        Device = window.GraphicsDevice;
        SpriteBatch = new SpriteBatch(Device);

        for (int i = 0; i < SamplerStates.Length; i++)
            SamplerStates[i] = SamplerState.LinearClamp;

        InitializeQuad();
        UpdateScreenInfo();

        NativeWindow.ClientSizeChanged += (_, _) => UpdateScreenInfo();
    }

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

    // Shader
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

    public Effect GetShaderEffect(string name)
    {
        if (!ShaderCache.TryGetValue(name, out var shader))
        {
            shader = Window.Content.Load<Effect>($"shaders/{name}");
            ShaderCache[name] = shader;
        }
        return shader;
    }

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

    public Renderer SetTechnique(string technique)
    {
        if (CurrentShader != null)
            CurrentShader.CurrentTechnique = CurrentShader.Techniques[technique];
        return this;
    }

    // Configure
    public Renderer Configure(BlendState state)
    {
        BlendState = state;
        return this;
    }

    public Renderer Configure(DepthStencilState state)
    {
        DepthStencilState = state;
        return this;
    }

    public Renderer Configure(RasterizerState state)
    {
        RasterizerState = state;
        return this;
    }

    public Renderer Configure(SamplerState state, int slot = 0)
    {
        if (slot >= 0 && slot < SamplerStates.Length)
            SamplerStates[slot] = state;
        return this;
    }

    public Renderer Configure(SpriteSortMode mode)
    {
        SpriteSortMode = mode;
        return this;
    }

    public Renderer Configure((int slot, SamplerState state) s0, (int slot, SamplerState state) s1)
    {
        if (s0.slot >= 0 && s0.slot < SamplerStates.Length)
            SamplerStates[s0.slot] = s0.state;
        if (s1.slot >= 0 && s1.slot < SamplerStates.Length)
            SamplerStates[s1.slot] = s1.state;
        return this;
    }

    public Renderer Configure((int slot, SamplerState state) s0, (int slot, SamplerState state) s1, (int slot, SamplerState state) s2)
    {
        if (s0.slot >= 0 && s0.slot < SamplerStates.Length)
            SamplerStates[s0.slot] = s0.state;
        if (s1.slot >= 0 && s1.slot < SamplerStates.Length)
            SamplerStates[s1.slot] = s1.state;
        if (s2.slot >= 0 && s2.slot < SamplerStates.Length)
            SamplerStates[s2.slot] = s2.state;
        return this;
    }

    public Renderer Configure((int slot, SamplerState state) s0, (int slot, SamplerState state) s1, (int slot, SamplerState state) s2, (int slot, SamplerState state) s3)
    {
        if (s0.slot >= 0 && s0.slot < SamplerStates.Length)
            SamplerStates[s0.slot] = s0.state;
        if (s1.slot >= 0 && s1.slot < SamplerStates.Length)
            SamplerStates[s1.slot] = s1.state;
        if (s2.slot >= 0 && s2.slot < SamplerStates.Length)
            SamplerStates[s2.slot] = s2.state;
        if (s3.slot >= 0 && s3.slot < SamplerStates.Length)
            SamplerStates[s3.slot] = s3.state;
        return this;
    }

    public Renderer Configure(params (int slot, SamplerState state)[] samplers)
    {
        foreach (var (slot, state) in samplers)
        {
            if (slot >= 0 && slot < SamplerStates.Length)
                SamplerStates[slot] = state;
        }
        return this;
    }

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
                case SamplerState ss: SamplerStates[0] = ss; break;
            }
        }
        return this;
    }

    // Targets

    public Renderer SetTarget(RenderTarget2D target)
    {
        CommitTextures();
        Device.SetRenderTarget(target);
        return this;
    }

    public Renderer SetTargets(RenderTarget2D target0, RenderTarget2D target1)
    {
        CommitTextures();
        _twoTargetBindings[0] = new RenderTargetBinding(target0);
        _twoTargetBindings[1] = new RenderTargetBinding(target1);
        Device.SetRenderTargets(_twoTargetBindings);
        return this;
    }

    public Renderer SetTargets(RenderTarget2D target0, RenderTarget2D target1, RenderTarget2D target2)
    {
        CommitTextures();
        _threeTargetBindings[0] = new RenderTargetBinding(target0);
        _threeTargetBindings[1] = new RenderTargetBinding(target1);
        _threeTargetBindings[2] = new RenderTargetBinding(target2);
        Device.SetRenderTargets(_threeTargetBindings);
        return this;
    }

    public Renderer SetTargets(RenderTarget2D target0, RenderTarget2D target1, RenderTarget2D target2, RenderTarget2D target3)
    {
        CommitTextures();
        _fourTargetBindings[0] = new RenderTargetBinding(target0);
        _fourTargetBindings[1] = new RenderTargetBinding(target1);
        _fourTargetBindings[2] = new RenderTargetBinding(target2);
        _fourTargetBindings[3] = new RenderTargetBinding(target3);
        Device.SetRenderTargets(_fourTargetBindings);
        return this;
    }

    public Renderer SetTargets(params RenderTarget2D[] targets)
    {
        CommitTextures();
        var bindings = new RenderTargetBinding[targets.Length];
        for (int i = 0; i < targets.Length; i++)
            bindings[i] = new RenderTargetBinding(targets[i]);
        Device.SetRenderTargets(bindings);
        return this;
    }

    public Renderer SetTargets(params RenderTargetBinding[] bindings)
    {
        CommitTextures();
        Device.SetRenderTargets(bindings);
        return this;
    }

    // Clear

    public Renderer Clear(Color? color = null)
    {
        Device.Clear(color ?? Color.Black);
        return this;
    }

    // Parameters

    public Renderer SetParameter(string name, float value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, int value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, bool value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, Vector2 value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, Vector3 value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, Vector4 value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, Matrix value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, Texture2D value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, float[] value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, Vector2[] value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, Vector3[] value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, Vector4[] value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, Matrix[] value, Effect shader = null)
    {
        (shader ?? CurrentShader)?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(Effect shader = null, params (string name, object value)[] parameters)
    {
        var target = shader ?? CurrentShader;
        if (target == null) return this;

        foreach (var (name, value) in parameters)
            SetParameter(target, name, value);

        return this;
    }

    // Static helper for setting parameters on external Effect objects
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

    // Solid Textures (cached)
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

    // Textures
    public Renderer SetTexture(int slot, Texture2D texture)
    {
        Device.Textures[slot] = texture;
        return this;
    }

    public Renderer SetTexture(int slot, Texture2D texture, SamplerState sampler)
    {
        Device.Textures[slot] = texture;
        Device.SamplerStates[slot] = sampler;
        return this;
    }

    public Renderer SetTextures(params (int slot, Texture2D texture)[] textures)
    {
        foreach (var (slot, texture) in textures)
            Device.Textures[slot] = texture;
        return this;
    }

    public Renderer SetTextures(params (int slot, Texture2D texture, SamplerState sampler)[] textures)
    {
        foreach (var (slot, texture, sampler) in textures)
        {
            Device.Textures[slot] = texture;
            Device.SamplerStates[slot] = sampler;
        }
        return this;
    }

    public Renderer ClearTextures(int count = 4)
    {
        for (int i = 0; i < count; i++)
            Device.Textures[i] = null;
        return this;
    }

    // Draw Shader
    public Renderer Draw()
    {
        CommitTextures();

        Device.BlendState = BlendState;
        Device.DepthStencilState = DepthStencilState;
        Device.RasterizerState = RasterizerState;

        for (int i = 0; i < SamplerStates.Length; i++)
            Device.SamplerStates[i] = SamplerStates[i];

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

    // Draw Textures
    public Renderer DrawTexture(Texture2D texture, Vector2 position)
    {
        return DrawTexture(texture, position, Color.White);
    }

    public Renderer DrawTexture(Texture2D texture, Vector2 position, Color color)
    {
        BeginTextures();
        SpriteBatch.Draw(texture, position, color);
        return this;
    }

    public Renderer DrawTexture(Texture2D texture, Rectangle destination)
    {
        return DrawTexture(texture, destination, Color.White);
    }

    public Renderer DrawTexture(Texture2D texture, Rectangle destination, Color color)
    {
        BeginTextures();
        SpriteBatch.Draw(texture, destination, color);
        return this;
    }

    public Renderer DrawTexture(Texture2D texture, Rectangle destination, Rectangle? source, Color color)
    {
        BeginTextures();
        SpriteBatch.Draw(texture, destination, source, color);
        return this;
    }

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

    // PingPong
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
                Device.SamplerStates[s] = SamplerStates[s];

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

    // Control flags
    public Renderer Begin()
    {
        IsDrawing = true;
        return this;
    }

    public Renderer Commit()
    {
        CommitTextures();
        IsDrawing = false;
        return this;
    }

    public Renderer Reset()
    {
        CommitTextures();

        BlendState = BlendState.Opaque;
        DepthStencilState = DepthStencilState.None;
        RasterizerState = RasterizerState.CullNone;
        SpriteSortMode = SpriteSortMode.Immediate;

        for (int i = 0; i < SamplerStates.Length; i++)
            SamplerStates[i] = SamplerState.LinearClamp;

        CurrentShader = null;
        CurrentShaderName = null;
        IsDrawing = false;

        return this;
    }

    public void Dispose()
    {
        QuadVertexBuffer?.Dispose();
        QuadIndexBuffer?.Dispose();
        SpriteBatch?.Dispose();

        foreach (var texture in SolidTextureCache.Values)
            texture?.Dispose();
        SolidTextureCache.Clear();

        foreach (var shader in ShaderCache.Values)
            shader?.Dispose();
        ShaderCache.Clear();
    }
}
