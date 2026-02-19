using System;
using com.radiant.engine.runtime;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

#pragma warning disable CS0618
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
public partial class Renderer : IDisposable
{
    /// <summary>The parent window containing the graphics device.</summary>
    public Window Window { get; }

    /// <summary>The underlying MonoGame graphics device.</summary>
    [Obsolete("Use Renderer API (CreateRenderTarget, CreateTexture, SetTarget, Clear, ViewportBounds, etc.) instead of accessing Device directly.")]
    public GraphicsDevice Device { get; }

    /// <summary>Shared SpriteBatch for texture drawing operations.</summary>
    [Obsolete("Use Renderer API (Blit, BeginDraw/EndDraw, DrawSprite, DrawString, etc.) instead of accessing SpriteBatch directly.")]
    public SpriteBatch SpriteBatch { get; }

    /// <summary>Scene render target that replaces the backbuffer as the main render surface.</summary>
    public RenderTarget2D SceneRT { get; private set; }

    /// <summary>True if currently between Begin/Draw and Commit calls.</summary>
    public bool IsDrawing { get; private set; }

    /// <summary>Name of the currently active shader (null if none).</summary>
    public string CurrentShaderName { get; private set; }

    private GameWindow NativeWindow => Window.Window;
    private readonly ShaderRegistry Shaders;
    private readonly TextureManager Textures;
    private readonly FontRegistry Fonts;
    private readonly ShapeBatcher ShapeBatch;

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

    /// <summary>Virtual resolution width (fixed world coordinate space).</summary>
    public float VirtualWidth { get; private set; }

    /// <summary>Virtual resolution height (fixed world coordinate space).</summary>
    public float VirtualHeight { get; private set; }

    /// <summary>Virtual resolution as Vector2 (fixed world coordinate space).</summary>
    public Vector2 VirtualSize { get; private set; }

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

    /// <summary>Scale factor from virtual coordinates to screen pixels (ScreenSize / VirtualSize).</summary>
    public Vector2 VirtualToScreenScale { get; private set; }

    private float RenderScaleValue = 1.0f;

    /// <summary>
    /// Render scale factor for dynamic resolution (0.25 to 1.0).
    /// Systems can subscribe to RenderScaleChanged to resize their render targets.
    /// </summary>
    public float RenderScale
    {
        get => RenderScaleValue;
        set
        {
            if (Math.Abs(RenderScaleValue - value) > 0.001f)
            {
                RenderScaleValue = Math.Clamp(value, 0.25f, 1.0f);
                UpdateScaledScreenInfo();
                RenderScaleChanged?.Invoke(RenderScaleValue);
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

    /// <summary>
    /// Creates a new Renderer bound to the specified window.
    /// </summary>
    /// <param name="window">The window containing the graphics device.</param>
    public Renderer(Window window)
    {
        Window = window;
        Device = window.GraphicsDevice;
        SpriteBatch = new SpriteBatch(Device);

        // Fixed virtual resolution — the world coordinate space.
        VirtualWidth = 3840;
        VirtualHeight = 2160;
        VirtualSize = new Vector2(VirtualWidth, VirtualHeight);

        Shaders = new ShaderRegistry(Window.Content);
        Textures = new TextureManager(Device, Window.Content);
        Fonts = new FontRegistry(Window.Content.RootDirectory);
        ShapeBatch = new ShapeBatcher(Device, VirtualWidth, VirtualHeight);

        for (int i = 0; i < SamplerStates.Length; i++)
            SamplerStates[i] = SamplerState.LinearClamp;

        InitializeBindingPools();
        InitializeQuad();
        InitializeFonts();
        UpdateScreenInfo();
        UpdateScaledScreenInfo();

        NativeWindow.ClientSizeChanged += (_, _) =>
        {
            UpdateScreenInfo();
            UpdateScaledScreenInfo();
        };
    }

    private void InitializeFonts()
    {
        Fonts.Load("Inter", "fonts/Inter-Regular.ttf");
        Fonts.Load("Inter-Bold", "fonts/Inter-Bold.ttf");
        Fonts.Load("PressStart2P", "fonts/PressStart2P.ttf");
    }

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
        VirtualToScreenScale = new Vector2(ScreenWidth / VirtualWidth, ScreenHeight / VirtualHeight);
    }

    private void UpdateScaledScreenInfo()
    {
        ScaledWidth = Math.Max(1, (int)(ScreenWidth * RenderScaleValue));
        ScaledHeight = Math.Max(1, (int)(ScreenHeight * RenderScaleValue));
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

    /// <summary>
    /// Converts screen-space coordinates (e.g. mouse position) to virtual world coordinates.
    /// </summary>
    public Vector2 ScreenToWorld(Vector2 screenPos)
    {
        return new Vector2(
            screenPos.X * (VirtualWidth / ScreenWidth),
            screenPos.Y * (VirtualHeight / ScreenHeight));
    }

    /// <summary>
    /// Converts virtual world coordinates to screen-space coordinates.
    /// </summary>
    public Vector2 WorldToScreen(Vector2 worldPos)
    {
        return new Vector2(
            worldPos.X * VirtualToScreenScale.X,
            worldPos.Y * VirtualToScreenScale.Y);
    }

    /// <summary>
    /// Converts a virtual-coordinate rectangle to a screen-pixel Rectangle with edge-snapping.
    /// Each edge is rounded independently so adjacent rectangles sharing a virtual edge
    /// map to the exact same screen pixel — no sub-pixel gaps.
    /// </summary>
    public Rectangle VirtualToScreenRect(float x, float y, float width, float height)
    {
        float sx = VirtualToScreenScale.X;
        float sy = VirtualToScreenScale.Y;
        int px = (int)MathF.Round(x * sx);
        int py = (int)MathF.Round(y * sy);
        int pr = (int)MathF.Round((x + width) * sx);
        int pb = (int)MathF.Round((y + height) * sy);
        return new Rectangle(px, py, pr - px, pb - py);
    }

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

    /// <summary>Whether the game window is active and focused.</summary>
    public bool IsActive => Window.IsActive;

    /// <summary>The game loop for timing info (FPS, etc.).</summary>
    public runtime.GameLoop GameLoop => Window.GameLoop;

    /// <summary>Current viewport bounds rectangle.</summary>
    public Rectangle ViewportBounds => Device.Viewport.Bounds;

    /// <summary>Whether the window has a pending resize that needs handling.</summary>
    public bool HasPendingResize => Window.ResizePending;

    /// <summary>Handles a pending window resize: clears the flag and updates screen info.</summary>
    public void HandleResize()
    {
        Window.ClearResizePending();
        UpdateScreenInfo();
    }

    /// <summary>Clears the backbuffer with the specified color. Routes rendering to SceneRT.</summary>
    public void ClearBackBuffer(Color color)
    {
        if (SceneRT == null || SceneRT.Width != ScreenWidth || SceneRT.Height != ScreenHeight)
        {
            SceneRT?.Dispose();
            SceneRT = new RenderTarget2D(Device, ScreenWidth, ScreenHeight, false,
                SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        }
        Device.SetRenderTarget(SceneRT);
        Device.Clear(color);
    }

    /// <summary>Copies SceneRT to the actual backbuffer for presentation.</summary>
    public void PresentToBackBuffer()
    {
        if (SceneRT == null) return;
        Device.SetRenderTarget(null);
        SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp);
        SpriteBatch.Draw(SceneRT, Device.Viewport.Bounds, Color.White);
        SpriteBatch.End();
    }

    /// <summary>
    /// Creates a new RenderTarget2D. Systems should use this instead of new RenderTarget2D(Device, ...).
    /// </summary>
    public RenderTarget2D CreateRenderTarget(int width, int height,
        SurfaceFormat format = SurfaceFormat.Color, DepthFormat depth = DepthFormat.None,
        RenderTargetUsage usage = RenderTargetUsage.DiscardContents)
    {
        return new RenderTarget2D(Device, width, height, false, format, depth, 0, usage);
    }

    /// <summary>
    /// Creates a new Texture2D. Systems should use this instead of new Texture2D(Device, ...).
    /// </summary>
    public Texture2D CreateTexture(int width, int height, SurfaceFormat format = SurfaceFormat.Color)
    {
        return new Texture2D(Device, width, height, false, format);
    }

    /// <summary>
    /// Loads and caches a content texture by name. Returns the same instance for repeated calls.
    /// </summary>
    /// <param name="name">Asset name relative to Content root (e.g., "Ghost", "sprites/Player").</param>
    /// <returns>The cached Texture2D.</returns>
    public Texture2D GetTexture(string name) => Textures.Get(name);

    /// <summary>
    /// Gets or creates a cached solid color texture.
    /// </summary>
    /// <param name="color">Fill color for the texture.</param>
    /// <param name="width">Texture width (default 1).</param>
    /// <param name="height">Texture height (default 1).</param>
    /// <returns>Cached texture with the specified color.</returns>
    public Texture2D GetSolidTexture(Color color, int width = 1, int height = 1) => Textures.GetSolid(color, width, height);

    /// <summary>
    /// Gets or creates a cached anti-aliased circle texture.
    /// </summary>
    /// <param name="diameter">Circle diameter in pixels.</param>
    /// <returns>Cached white circle texture with premultiplied alpha.</returns>
    public Texture2D GetCircleTexture(int diameter) => Textures.GetShape("Circle", diameter);

    /// <summary>
    /// Gets or creates a cached anti-aliased downward-pointing triangle texture.
    /// </summary>
    /// <param name="size">Texture size in pixels (square).</param>
    /// <returns>Cached white triangle texture with premultiplied alpha.</returns>
    public Texture2D GetTriangleTexture(int size) => Textures.GetShape("Triangle", size);

    /// <summary>
    /// Gets the cached checkmark icon texture (loaded from content pipeline).
    /// </summary>
    /// <param name="size">Ignored — retained for backward compatibility. Icon is a fixed-size PNG.</param>
    public Texture2D GetCheckmarkTexture(int size) => GetTexture("presets/icons/Checkmark");

    /// <summary>
    /// Gets the cached search (magnifying glass) icon texture (loaded from content pipeline).
    /// </summary>
    /// <param name="size">Ignored — retained for backward compatibility. Icon is a fixed-size PNG.</param>
    public Texture2D GetSearchTexture(int size) => GetTexture("presets/icons/Search");

    /// <summary>
    /// Gets the cached trash can icon texture (loaded from content pipeline).
    /// </summary>
    /// <param name="size">Ignored — retained for backward compatibility. Icon is a fixed-size PNG.</param>
    public Texture2D GetTrashTexture(int size) => GetTexture("presets/icons/Trash");

    /// <summary>
    /// Gets or creates a cached anti-aliased rounded rectangle texture for 9-slice rendering.
    /// The texture is (radius*2+2) pixels square with premultiplied alpha SDF corners.
    /// </summary>
    /// <param name="radius">Corner radius in pixels.</param>
    /// <returns>Cached white rounded rect texture with premultiplied alpha.</returns>
    public Texture2D GetRoundedRectTexture(int radius) => Textures.GetShape("RoundedRect", radius);

    /// <summary>
    /// Registers a procedural shape texture generator. The generator receives the size parameter
    /// and must return pixel data (width, height, pixels). The Renderer creates and caches the
    /// GPU texture internally.
    /// </summary>
    /// <param name="name">Unique shape name for later retrieval.</param>
    /// <param name="generator">Function that produces pixel data for a given size.</param>
    /// <param name="minSize">Minimum size clamp (requests below this are clamped up).</param>
    public void RegisterShapeTexture(string name, Func<int, (int Width, int Height, Color[] Pixels)> generator, int minSize = 1)
    {
        Textures.RegisterShape(name, (device, size) =>
        {
            var (width, height, pixels) = generator(size);
            var texture = new Texture2D(device, width, height);
            texture.SetData(pixels);
            return texture;
        }, minSize);
    }

    /// <summary>
    /// Gets or creates a cached procedural shape texture by name and size.
    /// </summary>
    /// <param name="name">Shape name as registered via RegisterShapeTexture.</param>
    /// <param name="size">Requested size (clamped to the shape's minimum).</param>
    public Texture2D GetShapeTexture(string name, int size) => Textures.GetShape(name, size);


    /// <summary>
    /// Supersample multiplier for font rendering. Fonts rasterize at size * FontRenderScale
    /// and draw scaled down for sharp, anti-aliased text.
    /// </summary>
    public float FontRenderScale
    {
        get => Fonts.FontRenderScale;
        set => Fonts.FontRenderScale = value;
    }

    /// <summary>
    /// Loads a TTF font family into the font system. Path is relative to Content root.
    /// </summary>
    public void LoadFont(string name, string path) => Fonts.Load(name, path);

    /// <summary>
    /// Gets a dynamic font at a specific pixel size (raw, no supersampling applied).
    /// </summary>
    public SpriteFontBase GetFont(string name, float size) => Fonts.GetFont(name, size);

    /// <summary>
    /// Measures text dimensions using a dynamic font at a specific pixel size.
    /// Accounts for FontRenderScale internally — returns dimensions at the requested size.
    /// </summary>
    public Vector2 MeasureString(string fontName, float size, string text) => Fonts.Measure(fontName, size, text);

    /// <summary>
    /// Gets the line height for a font at a specific size, accounting for FontRenderScale.
    /// </summary>
    public float GetLineHeight(string fontName, float size) => Fonts.GetLineHeight(fontName, size);

    /// <summary>
    /// Draws text using a dynamic font at a specific size. Call between BeginDraw/EndDraw.
    /// Supersampled via FontRenderScale for sharp rendering.
    /// </summary>
    public void DrawString(string fontName, float size, string text, Vector2 position, Color color, bool bold = false)
    {
        float scale = 1f / Fonts.FontRenderScale;
        var font = Fonts.GetFont(fontName, size * Fonts.FontRenderScale);
        SpriteBatch.DrawString(font, text, position, color, scale: new Vector2(scale));
        if (bold) SpriteBatch.DrawString(font, text, position + Vector2.UnitX, color, scale: new Vector2(scale));
    }

    /// <summary>Disposes all cached resources (shaders, textures, buffers).</summary>
    public void Dispose()
    {
        QuadVertexBuffer?.Dispose();
        QuadIndexBuffer?.Dispose();
        SpriteBatch?.Dispose();
        SceneRT?.Dispose();

        Shaders?.Dispose();
        Textures?.Dispose();
        Fonts?.Dispose();
        ShapeBatch?.Dispose();
    }
}
