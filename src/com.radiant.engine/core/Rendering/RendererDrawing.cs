using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

#pragma warning disable CS0618
namespace com.radiant.engine.core;

public partial class Renderer
{
    private VertexBuffer QuadVertexBuffer;
    private IndexBuffer QuadIndexBuffer;
    private bool IsDrawingTextures;

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
    public Renderer DrawTexture(Texture2D texture, Vector2 position) => DrawTexture(texture, position, Color.White);

    /// <summary>Draws a texture at the specified position with tint using SpriteBatch.</summary>
    public Renderer DrawTexture(Texture2D texture, Vector2 position, Color color)
    {
        BeginTextures();
        SpriteBatch.Draw(texture, position, color);
        return this;
    }

    /// <summary>Draws a texture stretched to the destination rectangle using SpriteBatch.</summary>
    public Renderer DrawTexture(Texture2D texture, Rectangle destination) => DrawTexture(texture, destination, Color.White);

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

    /// <summary>
    /// Blits a texture fullscreen to the current render target using SpriteBatch.
    /// Covers the common Begin/Draw(viewport)/End pattern.
    /// </summary>
    public void Blit(Texture2D source, BlendState blend = null, SamplerState sampler = null)
    {
        CommitTextures();
        SpriteBatch.Begin(SpriteSortMode.Immediate, blend ?? BlendState.Opaque, sampler ?? SamplerState.PointClamp);
        SpriteBatch.Draw(source, Device.Viewport.Bounds, Color.White);
        SpriteBatch.End();
    }

    /// <summary>
    /// Copies a texture to a render target at origin (native size, no stretching).
    /// </summary>
    public void Blit(Texture2D source, RenderTarget2D target, Color? clearColor = null,
        BlendState blend = null, SamplerState sampler = null)
    {
        CommitTextures();
        Device.SetRenderTarget(target ?? SceneRT);
        if (clearColor.HasValue)
            Device.Clear(clearColor.Value);
        SpriteBatch.Begin(SpriteSortMode.Immediate, blend ?? BlendState.Opaque, sampler ?? SamplerState.PointClamp);
        SpriteBatch.Draw(source, Vector2.Zero, Color.White);
        SpriteBatch.End();
    }

    /// <summary>
    /// Begins a SpriteBatch drawing session. Use with DrawSprite/DrawString/EndDraw.
    /// </summary>
    public void BeginDraw(SpriteSortMode sort = SpriteSortMode.Deferred,
        BlendState blend = null, SamplerState sampler = null, Matrix? transform = null)
    {
        CommitTextures();
        SpriteBatch.Begin(sort, blend ?? BlendState.AlphaBlend, sampler, null, null, null, transform);
    }

    /// <summary>Draws a texture to a destination rectangle during a BeginDraw/EndDraw session.</summary>
    public void DrawSprite(Texture2D texture, Rectangle destination, Color color)
    {
        SpriteBatch.Draw(texture, destination, color);
    }

    /// <summary>Draws a texture with opacity (premultiplied alpha) during a BeginDraw/EndDraw session.</summary>
    public void DrawSprite(Texture2D texture, Rectangle destination, Color color, float opacity)
    {
        var premul = new Color(
            (byte)(color.R * opacity),
            (byte)(color.G * opacity),
            (byte)(color.B * opacity),
            (byte)(color.A * opacity));
        SpriteBatch.Draw(texture, destination, premul);
    }

    /// <summary>Draws a texture region to a destination rectangle during a BeginDraw/EndDraw session.</summary>
    public void DrawSprite(Texture2D texture, Rectangle destination, Rectangle? source,
        Color color, float rotation = 0f, Vector2 origin = default, SpriteEffects effects = SpriteEffects.None)
    {
        SpriteBatch.Draw(texture, destination, source, color, rotation, origin, effects, 0);
    }

    /// <summary>Ends a SpriteBatch drawing session started by BeginDraw.</summary>
    public void EndDraw()
    {
        SpriteBatch.End();
    }

    /// <summary>
    /// Subtractive mask: erases pixels from the current render target where the mask has alpha.
    /// Result = dest * (1 - mask.alpha). Supports rotation around origin (source-texture coords).
    /// </summary>
    public void BlitMask(Texture2D mask, Rectangle destination, float rotation = 0f, Vector2 origin = default)
    {
        CommitTextures();
        SpriteBatch.Begin(SpriteSortMode.Immediate, MaskSubtract);
        SpriteBatch.Draw(mask, destination, null, Color.White, rotation, origin, SpriteEffects.None, 0);
        SpriteBatch.End();
    }

    private static readonly BlendState MaskSubtract = new BlendState
    {
        ColorSourceBlend = Blend.Zero,
        ColorDestinationBlend = Blend.InverseSourceAlpha,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.InverseSourceAlpha,
    };

    /// <summary>
    /// Draws a rounded rectangle using 9-slice rendering during a BeginDraw/EndDraw session.
    /// Corner radius is clamped to half the smallest dimension. Falls back to solid rect for tiny sizes.
    /// </summary>
    public void DrawRoundedRect(Rectangle bounds, Color color, int cornerRadius, RoundedCorners corners = RoundedCorners.All)
    {
        int radius = Math.Min(cornerRadius, Math.Min(bounds.Width, bounds.Height) / 2);
        if (radius <= 1 || corners == RoundedCorners.None)
        {
            SpriteBatch.Draw(GetSolidTexture(Color.White), bounds, color);
            return;
        }

        int hdRadius = radius * 4;
        var tex = GetRoundedRectTexture(hdRadius);
        int texSize = hdRadius * 2 + 2;
        int bx = bounds.X, by = bounds.Y, bw = bounds.Width, bh = bounds.Height;
        int innerW = bw - radius * 2;
        int innerH = bh - radius * 2;
        var srcSolid = new Rectangle(hdRadius, hdRadius, 1, 1);

        Rectangle srcTL = corners.HasFlag(RoundedCorners.TL) ? new Rectangle(0, 0, hdRadius, hdRadius) : srcSolid;
        SpriteBatch.Draw(tex, new Rectangle(bx, by, radius, radius), srcTL, color);

        Rectangle srcTR = corners.HasFlag(RoundedCorners.TR) ? new Rectangle(texSize - hdRadius, 0, hdRadius, hdRadius) : srcSolid;
        SpriteBatch.Draw(tex, new Rectangle(bx + bw - radius, by, radius, radius), srcTR, color);

        Rectangle srcBL = corners.HasFlag(RoundedCorners.BL) ? new Rectangle(0, texSize - hdRadius, hdRadius, hdRadius) : srcSolid;
        SpriteBatch.Draw(tex, new Rectangle(bx, by + bh - radius, radius, radius), srcBL, color);

        Rectangle srcBR = corners.HasFlag(RoundedCorners.BR) ? new Rectangle(texSize - hdRadius, texSize - hdRadius, hdRadius, hdRadius) : srcSolid;
        SpriteBatch.Draw(tex, new Rectangle(bx + bw - radius, by + bh - radius, radius, radius), srcBR, color);

        if (innerW > 0)
        {
            SpriteBatch.Draw(tex, new Rectangle(bx + radius, by, innerW, radius), new Rectangle(hdRadius, 0, 2, hdRadius), color);
            SpriteBatch.Draw(tex, new Rectangle(bx + radius, by + bh - radius, innerW, radius), new Rectangle(hdRadius, texSize - hdRadius, 2, hdRadius), color);
        }

        if (innerH > 0)
        {
            SpriteBatch.Draw(tex, new Rectangle(bx, by + radius, radius, innerH), new Rectangle(0, hdRadius, hdRadius, 2), color);
            SpriteBatch.Draw(tex, new Rectangle(bx + bw - radius, by + radius, radius, innerH), new Rectangle(texSize - hdRadius, hdRadius, hdRadius, 2), color);
        }

        if (innerW > 0 && innerH > 0)
            SpriteBatch.Draw(tex, new Rectangle(bx + radius, by + radius, innerW, innerH), srcSolid, color);
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

        Device.SetRenderTarget(SceneRT);
        return input;
    }
}

/// <summary>
/// Flags for which corners to round in DrawRoundedRect. Combinable for partial rounding.
/// </summary>
[Flags]
public enum RoundedCorners : byte
{
    None = 0, TL = 1, TR = 2, BL = 4, BR = 8,
    Top = TL | TR, Bottom = BL | BR, All = Top | Bottom
}
