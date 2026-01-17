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
    public GraphicsDevice Device { get; }
    public SpriteBatch SpriteBatch { get; }
    public bool IsDrawing { get; private set; }
    public string CurrentShaderName { get; private set; }
    public Texture2D PixelTexture { get; private set; }

    // Internal
    private Dictionary<string, Effect> ShaderCache = new();
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

    public Renderer(Window window)
    {
        Window = window;
        Device = window.GraphicsDevice;
        SpriteBatch = new SpriteBatch(Device);

        PixelTexture = new Texture2D(Device, 1, 1);
        PixelTexture.SetData([Color.White]);

        for (int i = 0; i < SamplerStates.Length; i++)
            SamplerStates[i] = SamplerState.LinearClamp;

        InitializeQuad();
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

    public Renderer SetParameter(string name, float value)
    {
        CurrentShader?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, int value)
    {
        CurrentShader?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, bool value)
    {
        CurrentShader?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, Vector2 value)
    {
        CurrentShader?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, Vector3 value)
    {
        CurrentShader?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, Vector4 value)
    {
        CurrentShader?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, Matrix value)
    {
        CurrentShader?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, Texture2D value)
    {
        CurrentShader?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, float[] value)
    {
        CurrentShader?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, Vector2[] value)
    {
        CurrentShader?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, Vector3[] value)
    {
        CurrentShader?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, Vector4[] value)
    {
        CurrentShader?.Parameters[name]?.SetValue(value);
        return this;
    }

    public Renderer SetParameter(string name, Matrix[] value)
    {
        CurrentShader?.Parameters[name]?.SetValue(value);
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

    // Control

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
        PixelTexture?.Dispose();

        foreach (var shader in ShaderCache.Values)
            shader?.Dispose();

        ShaderCache.Clear();
    }
}
