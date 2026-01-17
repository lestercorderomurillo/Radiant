using System;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.core;

public class RenderPipeline : IDisposable
{
    public Window Window { get; }
    public SpriteBatch SpriteBatch { get; }
    public GraphicsDevice Device;

    private VertexBuffer _quadVertexBuffer;
    private IndexBuffer _quadIndexBuffer;
    private RenderTargetBinding[] _savedRenderTargets;

    public RenderPipeline(Window window)
    {
        Window = window;
        SpriteBatch = window.SpriteBatch;
        Device = window.GraphicsDevice;

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

        _quadVertexBuffer = new VertexBuffer(Device, typeof(VertexPositionTexture), 4, BufferUsage.WriteOnly);
        _quadVertexBuffer.SetData(vertices);

        var indices = new short[] { 0, 1, 2, 2, 1, 3 };
        _quadIndexBuffer = new IndexBuffer(Device, IndexElementSize.SixteenBits, 6, BufferUsage.WriteOnly);
        _quadIndexBuffer.SetData(indices);
    }

    private void DrawQuad(Effect shader)
    {
        Device.SetVertexBuffer(_quadVertexBuffer);
        Device.Indices = _quadIndexBuffer;

        foreach (var pass in shader.CurrentTechnique.Passes)
        {
            pass.Apply();
            Device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 2);
        }
    }

    public void DrawShader(
        Effect shader,
        string technique = null,
        RenderTarget2D target = null,
        Color? clearColor = null,
        BlendState blendState = null,
        Action afterPass = null,
        bool resetStates = true)
    {
        if (resetStates) _savedRenderTargets = Device.GetRenderTargets();

        Device.SetRenderTarget(target);
        if (clearColor.HasValue) Device.Clear(clearColor.Value);

        Device.BlendState = blendState ?? BlendState.Opaque;
        Device.DepthStencilState = DepthStencilState.None;
        Device.RasterizerState = RasterizerState.CullNone;

        if (technique != null)
            shader.CurrentTechnique = shader.Techniques[technique];

        DrawQuad(shader);

        afterPass?.Invoke();

        if (resetStates) Device.SetRenderTargets(_savedRenderTargets);
    }

    public void DrawShader(
        Effect shader,
        string[] techniques,
        RenderTarget2D target = null,
        Color? clearColor = null,
        BlendState blendState = null,
        Action<int> afterPass = null,
        bool resetStates = true)
    {
        if (resetStates) _savedRenderTargets = Device.GetRenderTargets();

        Device.SetRenderTarget(target);
        if (clearColor.HasValue) Device.Clear(clearColor.Value);

        Device.BlendState = blendState ?? BlendState.Opaque;
        Device.DepthStencilState = DepthStencilState.None;
        Device.RasterizerState = RasterizerState.CullNone;

        for (int i = 0; i < techniques.Length; i++)
        {
            shader.CurrentTechnique = shader.Techniques[techniques[i]];
            DrawQuad(shader);
            afterPass?.Invoke(i);
        }

        if (resetStates) Device.SetRenderTargets(_savedRenderTargets);
    }

    public RenderTarget2D PingPong(
        Effect shader,
        string technique,
        RenderTarget2D a,
        RenderTarget2D b,
        int passes,
        Action<int, RenderTarget2D> beforePass = null,
        Action<int> afterPass = null,
        Color? clearColor = null)
    {
        shader.CurrentTechnique = shader.Techniques[technique];

        RenderTarget2D input = a;
        RenderTarget2D output = b;

        for (int i = 0; i < passes; i++)
        {
            beforePass?.Invoke(i, input);

            Device.SetRenderTarget(output);
            if (clearColor.HasValue) Device.Clear(clearColor.Value);

            Device.BlendState = BlendState.Opaque;
            Device.DepthStencilState = DepthStencilState.None;
            Device.RasterizerState = RasterizerState.CullNone;

            DrawQuad(shader);

            afterPass?.Invoke(i);

            (input, output) = (output, input);
        }

        Device.SetRenderTarget(null);
        return input;
    }

    public void ClearTextures(int count = 4)
    {
        for (int i = 0; i < count; i++)
            Device.Textures[i] = null;
    }

    public void Dispose()
    {
        _quadVertexBuffer?.Dispose();
        _quadIndexBuffer?.Dispose();
    }
}
