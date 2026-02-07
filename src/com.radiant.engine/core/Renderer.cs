using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    #endregion

    #region Render Scale (Dynamic Resolution)

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

    private readonly RenderTargetBinding[] TwoTargetBindings = new RenderTargetBinding[2];
    private readonly RenderTargetBinding[] ThreeTargetBindings = new RenderTargetBinding[3];
    private readonly RenderTargetBinding[] FourTargetBindings = new RenderTargetBinding[4];
    private readonly Stack<RenderTargetBinding[]> RenderTargetStack = new();
    private RenderTargetBinding[] CurrentTargets = null;

    // Pooled arrays for render target bindings (avoids Clone() allocations)
    private const int BindingPoolSize = 16;
    private readonly RenderTargetBinding[][] BindingPool2 = new RenderTargetBinding[BindingPoolSize][];
    private readonly RenderTargetBinding[][] BindingPool3 = new RenderTargetBinding[BindingPoolSize][];
    private readonly RenderTargetBinding[][] BindingPool4 = new RenderTargetBinding[BindingPoolSize][];
    private int BindingPool2Index = 0;
    private int BindingPool3Index = 0;
    private int BindingPool4Index = 0;

    // Instanced shape rendering
    private const int DefaultShapeCapacity = 65536;
    private const int MaxShapeCapacity = int.MaxValue;
    private VertexBuffer ShapeQuadBuffer;
    private IndexBuffer ShapeIndexBuffer;
    private DynamicVertexBuffer ShapeInstanceBuffer;
    private Effect ShapeShader;
    private Shape[] Shapes;
    private int ShapeCount;
    private int ShapeCapacity;

    // Parallel shape collection
    private int ParallelThreadCount;
    private Shape[][] ParallelShapeBuffers;
    private float[][] ParallelZBuffers;
    private int[][] ParallelSortIndices;
    private int[] ParallelShapeCounts;
    private int[] ParallelMergeIndices;
    private int ParallelCapacityPerThread;

    // Min-heap for O(N log T) k-way merge (replaces O(N*T) linear scan)
    private (float z, int thread)[] MergeHeap;
    private int MergeHeapSize;

    // GPU upload timing stats
    public float LastSetDataMs { get; private set; }
    public float LastDrawMs { get; private set; }

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

        // Pre-allocate binding pools
        for (int i = 0; i < BindingPoolSize; i++)
        {
            BindingPool2[i] = new RenderTargetBinding[2];
            BindingPool3[i] = new RenderTargetBinding[3];
            BindingPool4[i] = new RenderTargetBinding[4];
        }

        InitializeQuad();
        InitializeShapes();
        UpdateScreenInfo();
        UpdateScaledScreenInfo();

        // Fixed virtual resolution — the world coordinate space.
        // All entity positions, sizes, and scene layout use these units.
        VirtualWidth = 3840;
        VirtualHeight = 2160;
        VirtualSize = new Vector2(VirtualWidth, VirtualHeight);

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
            worldPos.X * ((float)ScreenWidth / VirtualWidth),
            worldPos.Y * ((float)ScreenHeight / VirtualHeight));
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

    #region Shape Rendering

    private void InitializeShapes()
    {
        var vertices = new ShapeQuadVertex[]
        {
            new(new Vector2(0, 0), new Vector2(0, 0)),
            new(new Vector2(1, 0), new Vector2(1, 0)),
            new(new Vector2(0, 1), new Vector2(0, 1)),
            new(new Vector2(1, 1), new Vector2(1, 1))
        };

        ShapeQuadBuffer = new VertexBuffer(Device, ShapeQuadVertex.Declaration, 4, BufferUsage.WriteOnly);
        ShapeQuadBuffer.SetData(vertices);

        var indices = new short[] { 0, 1, 2, 2, 1, 3 };
        ShapeIndexBuffer = new IndexBuffer(Device, IndexElementSize.SixteenBits, 6, BufferUsage.WriteOnly);
        ShapeIndexBuffer.SetData(indices);

        ShapeCapacity = DefaultShapeCapacity;
        Shapes = new Shape[ShapeCapacity];
        ShapeInstanceBuffer = new DynamicVertexBuffer(Device, Shape.Declaration, ShapeCapacity, BufferUsage.WriteOnly);
    }

    private void EnsureShapeCapacity(int required)
    {
        if (required <= ShapeCapacity) return;

        int newCapacity = ShapeCapacity;
        while (newCapacity < required && newCapacity < MaxShapeCapacity)
            newCapacity *= 2;

        if (newCapacity > MaxShapeCapacity)
            newCapacity = MaxShapeCapacity;

        Array.Resize(ref Shapes, newCapacity);
        ShapeInstanceBuffer?.Dispose();
        ShapeInstanceBuffer = new DynamicVertexBuffer(Device, Shape.Declaration, newCapacity, BufferUsage.WriteOnly);
        ShapeCapacity = newCapacity;
    }

    /// <summary>
    /// Adds a shape to the current batch. Call FlushShapes() to render.
    /// Silently drops shapes beyond MaxShapeCapacity.
    /// </summary>
    public Renderer DrawShape(Shape shape)
    {
        if (ShapeCount >= MaxShapeCapacity) return this; // At capacity limit
        EnsureShapeCapacity(ShapeCount + 1);
        Shapes[ShapeCount++] = shape;
        return this;
    }

    /// <summary>
    /// Adds a rectangle shape to the current batch.
    /// </summary>
    public Renderer DrawRect(Vector2 position, Vector2 size, Color color)
    {
        return DrawShape(Shape.Rect(position, size, color));
    }

    /// <summary>
    /// Adds a rectangle shape to the current batch.
    /// </summary>
    public Renderer DrawRect(float x, float y, float width, float height, Color color)
    {
        return DrawShape(Shape.Rect(x, y, width, height, color));
    }

    /// <summary>
    /// Adds a circle shape to the current batch.
    /// </summary>
    public Renderer DrawCircle(Vector2 center, float radius, Color color)
    {
        return DrawShape(Shape.Circle(center, radius, color));
    }

    /// <summary>
    /// Adds a circle shape to the current batch.
    /// </summary>
    public Renderer DrawCircle(float x, float y, float radius, Color color)
    {
        return DrawShape(Shape.Circle(x, y, radius, color));
    }

    /// <summary>
    /// Adds a triangle shape to the current batch.
    /// </summary>
    public Renderer DrawTriangle(Vector2 position, Vector2 size, Color color)
    {
        return DrawShape(Shape.Triangle(position, size, color));
    }

    /// <summary>
    /// Adds a bordered (unfilled) triangle shape to the current batch.
    /// </summary>
    public Renderer DrawTriangleBorder(Vector2 position, Vector2 size, Color color)
    {
        return DrawShape(Shape.TriangleBorder(position, size, color));
    }

    /// <summary>
    /// Renders all batched shapes in a single draw call and clears the batch.
    /// </summary>
    /// <param name="target">Render target (null for backbuffer).</param>
    /// <param name="clearColor">Optional clear color before rendering.</param>
    /// <param name="technique">Shader technique: "Default" (AA), "Sharp" (no AA), or "Emissive" (sharp SDF for light sources).</param>
    public Renderer FlushShapes(RenderTarget2D target = null, Color? clearColor = null, string technique = "Default")
    {
        CommitTextures();

        Device.SetRenderTarget(target);

        if (clearColor.HasValue)
            Device.Clear(clearColor.Value);

        if (ShapeCount > 0)
        {
            ShapeInstanceBuffer.SetData(Shapes, 0, ShapeCount, SetDataOptions.Discard);

            Device.BlendState = BlendState;
            Device.DepthStencilState = DepthStencilState.None;
            Device.RasterizerState = RasterizerState.CullNone;

            Device.SetVertexBuffers(
                new VertexBufferBinding(ShapeQuadBuffer, 0, 0),
                new VertexBufferBinding(ShapeInstanceBuffer, 0, 1)
            );
            Device.Indices = ShapeIndexBuffer;

            var viewProjection = Matrix.CreateOrthographicOffCenter(0, VirtualWidth, VirtualHeight, 0, 0, 1);

            ShapeShader ??= GetShaderEffect("InstancedShapes");
            ShapeShader.CurrentTechnique = ShapeShader.Techniques[technique];
            ShapeShader.Parameters["ViewProjection"]?.SetValue(viewProjection);

            foreach (var pass in ShapeShader.CurrentTechnique.Passes)
            {
                pass.Apply();
                Device.DrawInstancedPrimitives(PrimitiveType.TriangleList, 0, 0, 2, ShapeCount);
            }

            ShapeCount = 0;
        }

        return this;
    }

    /// <summary>
    /// Clears the shape batch without rendering.
    /// </summary>
    public Renderer ClearShapes()
    {
        ShapeCount = 0;
        return this;
    }

    /// <summary>
    /// Current number of shapes in the batch.
    /// </summary>
    public int ShapeBatchCount => ShapeCount;

    /// <summary>
    /// Renders shapes directly from an external buffer - ZERO COPY.
    /// Use this when shapes are pre-collected in Renderer.Shape format.
    /// </summary>
    public Renderer FlushShapesExternal(Shape[] shapes, int count, RenderTarget2D target = null, Color? clearColor = null, string technique = "Default")
    {
        CommitTextures();

        Device.SetRenderTarget(target);

        if (clearColor.HasValue)
            Device.Clear(clearColor.Value);

        if (count > 0)
        {
            // Ensure instance buffer can hold the shapes
            if (count > ShapeCapacity)
            {
                int newCapacity = ShapeCapacity;
                while (newCapacity < count && newCapacity < MaxShapeCapacity)
                    newCapacity *= 2;
                if (newCapacity > MaxShapeCapacity)
                    newCapacity = MaxShapeCapacity;

                ShapeInstanceBuffer?.Dispose();
                ShapeInstanceBuffer = new DynamicVertexBuffer(Device, Shape.Declaration, newCapacity, BufferUsage.WriteOnly);
                ShapeCapacity = newCapacity;
            }

            int uploadCount = Math.Min(count, ShapeCapacity);
            var swSetData = Stopwatch.StartNew();
            ShapeInstanceBuffer.SetData(shapes, 0, uploadCount, SetDataOptions.Discard);
            LastSetDataMs = (float)swSetData.Elapsed.TotalMilliseconds;

            Device.BlendState = BlendState;
            Device.DepthStencilState = DepthStencilState.None;
            Device.RasterizerState = RasterizerState.CullNone;

            Device.SetVertexBuffers(
                new VertexBufferBinding(ShapeQuadBuffer, 0, 0),
                new VertexBufferBinding(ShapeInstanceBuffer, 0, 1)
            );
            Device.Indices = ShapeIndexBuffer;

            var viewProjection = Matrix.CreateOrthographicOffCenter(0, VirtualWidth, VirtualHeight, 0, 0, 1);

            ShapeShader ??= GetShaderEffect("InstancedShapes");
            ShapeShader.CurrentTechnique = ShapeShader.Techniques[technique];
            ShapeShader.Parameters["ViewProjection"]?.SetValue(viewProjection);

            var swDraw = Stopwatch.StartNew();
            foreach (var pass in ShapeShader.CurrentTechnique.Passes)
            {
                pass.Apply();
                Device.DrawInstancedPrimitives(PrimitiveType.TriangleList, 0, 0, 2, uploadCount);
            }
            LastDrawMs = (float)swDraw.Elapsed.TotalMilliseconds;
        }

        return this;
    }

    #endregion

    #region Parallel Shape Collection

    /// <summary>
    /// Initializes parallel shape buffers for multi-threaded shape collection.
    /// Call once at startup or when thread count changes.
    /// </summary>
    /// <param name="threadCount">Number of worker threads.</param>
    /// <param name="capacityPerThread">Initial capacity per thread buffer.</param>
    public Renderer InitializeParallelShapes(int threadCount, int capacityPerThread = 16384)
    {
        ParallelThreadCount = threadCount;
        ParallelCapacityPerThread = capacityPerThread;
        ParallelShapeBuffers = new Shape[threadCount][];
        ParallelZBuffers = new float[threadCount][];
        ParallelSortIndices = new int[threadCount][];
        ParallelShapeCounts = new int[threadCount];
        ParallelMergeIndices = new int[threadCount];

        // Min-heap for O(N log T) k-way merge
        MergeHeap = new (float, int)[threadCount];

        for (int i = 0; i < threadCount; i++)
        {
            ParallelShapeBuffers[i] = new Shape[capacityPerThread];
            ParallelZBuffers[i] = new float[capacityPerThread];
            ParallelSortIndices[i] = new int[capacityPerThread];
        }

        return this;
    }

    /// <summary>
    /// Ensures all parallel buffers have at least the specified capacity.
    /// Call when entity count grows.
    /// </summary>
    public Renderer EnsureParallelCapacity(int capacityPerThread)
    {
        if (capacityPerThread <= ParallelCapacityPerThread) return this;

        ParallelCapacityPerThread = capacityPerThread;
        for (int i = 0; i < ParallelThreadCount; i++)
        {
            Array.Resize(ref ParallelShapeBuffers[i], capacityPerThread);
            Array.Resize(ref ParallelZBuffers[i], capacityPerThread);
            Array.Resize(ref ParallelSortIndices[i], capacityPerThread);
        }

        return this;
    }

    /// <summary>
    /// Ensures parallel buffers can hold totalEntities divided across threads.
    /// Also ensures the main Shapes array can hold the total.
    /// Call when entity count grows: EnsureParallelCapacityForEntities(entityCount)
    /// </summary>
    public Renderer EnsureParallelCapacityForEntities(int totalEntities)
    {
        EnsureShapeCapacity(totalEntities);
        int perThread = (totalEntities + ParallelThreadCount - 1) / ParallelThreadCount;
        return EnsureParallelCapacity(perThread);
    }

    /// <summary>
    /// Gets the shape buffer for a specific thread for direct indexed writes.
    /// Thread writes directly: buffer[localIndex] = shape;
    /// </summary>
    public Shape[] GetParallelBuffer(int threadIndex) => ParallelShapeBuffers[threadIndex];

    /// <summary>
    /// Gets the Z buffer for a specific thread for direct indexed writes.
    /// Thread writes directly: zBuffer[localIndex] = z;
    /// </summary>
    public float[] GetParallelZBuffer(int threadIndex) => ParallelZBuffers[threadIndex];

    /// <summary>
    /// Sets the shape count for a thread after direct indexed writes.
    /// </summary>
    public void SetParallelCount(int threadIndex, int count)
    {
        ParallelShapeCounts[threadIndex] = count;
    }

    /// <summary>
    /// Adds a shape to a thread's buffer (append style).
    /// Thread-local, no synchronization needed.
    /// </summary>
    public void DrawShapeParallel(int threadIndex, Shape shape)
    {
        var buffer = ParallelShapeBuffers[threadIndex];
        var count = ParallelShapeCounts[threadIndex];

        if (count >= buffer.Length)
        {
            int newCapacity = Math.Min(buffer.Length * 2, MaxShapeCapacity);
            Array.Resize(ref ParallelShapeBuffers[threadIndex], newCapacity);
            buffer = ParallelShapeBuffers[threadIndex];
        }

        buffer[count] = shape;
        ParallelShapeCounts[threadIndex] = count + 1;
    }

    /// <summary>
    /// Collects all parallel thread buffers into the main Shapes array in order.
    /// Call on main thread after parallel work completes, before FlushShapes.
    /// </summary>
    public Renderer CollectParallelShapes()
    {
        int totalCount = 0;
        for (int i = 0; i < ParallelThreadCount; i++)
            totalCount += ParallelShapeCounts[i];

        EnsureShapeCapacity(totalCount);

        int offset = 0;
        for (int t = 0; t < ParallelThreadCount; t++)
        {
            var buffer = ParallelShapeBuffers[t];
            var count = ParallelShapeCounts[t];

            Array.Copy(buffer, 0, Shapes, offset, count);
            offset += count;

            ParallelShapeCounts[t] = 0;
        }

        ShapeCount = totalCount;
        return this;
    }

    /// <summary>
    /// Clears all parallel shape buffers without collecting.
    /// </summary>
    public Renderer ClearParallelShapes()
    {
        for (int i = 0; i < ParallelThreadCount; i++)
            ParallelShapeCounts[i] = 0;
        return this;
    }

    /// <summary>
    /// Sorts a thread's shapes by Z. Call from within parallel work after populating.
    /// Cache-friendly: sorts locally within thread's buffer.
    /// </summary>
    public void SortParallelBufferByZ(int threadIndex)
    {
        int count = ParallelShapeCounts[threadIndex];
        if (count <= 1) return;

        var shapes = ParallelShapeBuffers[threadIndex];
        var zValues = ParallelZBuffers[threadIndex];
        var indices = ParallelSortIndices[threadIndex];

        // Initialize indices
        for (int i = 0; i < count; i++)
            indices[i] = i;

        // Sort indices by Z
        Array.Sort(indices, 0, count, Comparer<int>.Create((a, b) => zValues[a].CompareTo(zValues[b])));

        // Reorder shapes in-place using cycle sort to avoid allocation
        for (int i = 0; i < count; i++)
        {
            while (indices[i] != i)
            {
                int target = indices[i];
                (shapes[i], shapes[target]) = (shapes[target], shapes[i]);
                (zValues[i], zValues[target]) = (zValues[target], zValues[i]);
                (indices[i], indices[target]) = (indices[target], indices[i]);
            }
        }
    }

    /// <summary>
    /// Collects all parallel thread buffers with k-way merge by Z order.
    /// Each thread's buffer must be pre-sorted via SortParallelBufferByZ.
    /// Call on main thread after parallel work completes.
    /// Uses min-heap for O(N log T) instead of O(N*T).
    /// </summary>
    public Renderer CollectParallelShapesSorted()
    {
        int totalCount = 0;
        for (int i = 0; i < ParallelThreadCount; i++)
            totalCount += ParallelShapeCounts[i];

        if (totalCount == 0)
        {
            ShapeCount = 0;
            return this;
        }

        EnsureShapeCapacity(totalCount);

        // Reset merge indices
        Array.Clear(ParallelMergeIndices, 0, ParallelThreadCount);

        // Build initial min-heap with first element from each non-empty thread
        MergeHeapSize = 0;
        for (int t = 0; t < ParallelThreadCount; t++)
        {
            if (ParallelShapeCounts[t] > 0)
            {
                MergeHeap[MergeHeapSize++] = (ParallelZBuffers[t][0], t);
            }
        }
        HeapifyAll();

        int outputIndex = 0;
        while (MergeHeapSize > 0)
        {
            // Extract min (root of heap)
            var (_, bestThread) = MergeHeap[0];
            int idx = ParallelMergeIndices[bestThread]++;
            Shapes[outputIndex++] = ParallelShapeBuffers[bestThread][idx];

            // Replace root with next element from same thread, or remove if exhausted
            if (ParallelMergeIndices[bestThread] < ParallelShapeCounts[bestThread])
            {
                MergeHeap[0] = (ParallelZBuffers[bestThread][ParallelMergeIndices[bestThread]], bestThread);
                HeapSiftDown(0);
            }
            else
            {
                // Remove root by replacing with last element
                MergeHeap[0] = MergeHeap[--MergeHeapSize];
                if (MergeHeapSize > 0) HeapSiftDown(0);
            }
        }

        // Reset counts
        for (int i = 0; i < ParallelThreadCount; i++)
            ParallelShapeCounts[i] = 0;

        ShapeCount = totalCount;
        return this;
    }

    private void HeapifyAll()
    {
        for (int i = MergeHeapSize / 2 - 1; i >= 0; i--)
            HeapSiftDown(i);
    }

    private void HeapSiftDown(int i)
    {
        while (true)
        {
            int smallest = i;
            int left = 2 * i + 1;
            int right = 2 * i + 2;

            if (left < MergeHeapSize && MergeHeap[left].z < MergeHeap[smallest].z)
                smallest = left;
            if (right < MergeHeapSize && MergeHeap[right].z < MergeHeap[smallest].z)
                smallest = right;

            if (smallest == i) break;

            (MergeHeap[i], MergeHeap[smallest]) = (MergeHeap[smallest], MergeHeap[i]);
            i = smallest;
        }
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
        RenderTargetStack.Push(CurrentTargets);
        return this;
    }

    /// <summary>
    /// Pops and restores render targets from the internal stack.
    /// </summary>
    public Renderer PopTargets()
    {
        if (RenderTargetStack.Count > 0)
        {
            var targets = RenderTargetStack.Pop();
            CommitTextures();
            if (targets == null)
                Device.SetRenderTarget(null);
            else
                Device.SetRenderTargets(targets);
            CurrentTargets = targets;
        }
        return this;
    }

    /// <summary>Sets a single render target (or null for backbuffer).</summary>
    public Renderer SetTarget(RenderTarget2D target)
    {
        CommitTextures();
        Device.SetRenderTarget(target);
        CurrentTargets = target != null ? new[] { new RenderTargetBinding(target) } : null;
        return this;
    }

    /// <summary>Sets two render targets for MRT rendering.</summary>
    public Renderer SetTargets(RenderTarget2D target0, RenderTarget2D target1)
    {
        CommitTextures();
        TwoTargetBindings[0] = new RenderTargetBinding(target0);
        TwoTargetBindings[1] = new RenderTargetBinding(target1);
        Device.SetRenderTargets(TwoTargetBindings);
        // Use pooled array instead of Clone()
        var pooled = BindingPool2[BindingPool2Index];
        BindingPool2Index = (BindingPool2Index + 1) & (BindingPoolSize - 1);
        pooled[0] = TwoTargetBindings[0];
        pooled[1] = TwoTargetBindings[1];
        CurrentTargets = pooled;
        return this;
    }

    /// <summary>Sets three render targets for MRT rendering.</summary>
    public Renderer SetTargets(RenderTarget2D target0, RenderTarget2D target1, RenderTarget2D target2)
    {
        CommitTextures();
        ThreeTargetBindings[0] = new RenderTargetBinding(target0);
        ThreeTargetBindings[1] = new RenderTargetBinding(target1);
        ThreeTargetBindings[2] = new RenderTargetBinding(target2);
        Device.SetRenderTargets(ThreeTargetBindings);
        // Use pooled array instead of Clone()
        var pooled = BindingPool3[BindingPool3Index];
        BindingPool3Index = (BindingPool3Index + 1) & (BindingPoolSize - 1);
        pooled[0] = ThreeTargetBindings[0];
        pooled[1] = ThreeTargetBindings[1];
        pooled[2] = ThreeTargetBindings[2];
        CurrentTargets = pooled;
        return this;
    }

    /// <summary>Sets four render targets for MRT rendering.</summary>
    public Renderer SetTargets(RenderTarget2D target0, RenderTarget2D target1, RenderTarget2D target2, RenderTarget2D target3)
    {
        CommitTextures();
        FourTargetBindings[0] = new RenderTargetBinding(target0);
        FourTargetBindings[1] = new RenderTargetBinding(target1);
        FourTargetBindings[2] = new RenderTargetBinding(target2);
        FourTargetBindings[3] = new RenderTargetBinding(target3);
        Device.SetRenderTargets(FourTargetBindings);
        // Use pooled array instead of Clone()
        var pooled = BindingPool4[BindingPool4Index];
        BindingPool4Index = (BindingPool4Index + 1) & (BindingPoolSize - 1);
        pooled[0] = FourTargetBindings[0];
        pooled[1] = FourTargetBindings[1];
        pooled[2] = FourTargetBindings[2];
        pooled[3] = FourTargetBindings[3];
        CurrentTargets = pooled;
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
        CurrentTargets = bindings;
        return this;
    }

    /// <summary>Sets render targets from pre-built bindings array.</summary>
    public Renderer SetTargets(params RenderTargetBinding[] bindings)
    {
        CommitTextures();
        Device.SetRenderTargets(bindings);
        CurrentTargets = bindings;
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

        ShapeQuadBuffer?.Dispose();
        ShapeIndexBuffer?.Dispose();
        ShapeInstanceBuffer?.Dispose();

        foreach (var texture in SolidTextureCache.Values)
            texture?.Dispose();
        SolidTextureCache.Clear();

        foreach (var texture in CircleTextureCache.Values)
            texture?.Dispose();
        CircleTextureCache.Clear();

        foreach (var shader in ShaderCache.Values)
            shader?.Dispose();
        ShaderCache.Clear();

        ParallelShapeBuffers = null;
        ParallelZBuffers = null;
        ParallelSortIndices = null;
        ParallelShapeCounts = null;
        ParallelMergeIndices = null;
        MergeHeap = null;
    }

    #endregion
}
