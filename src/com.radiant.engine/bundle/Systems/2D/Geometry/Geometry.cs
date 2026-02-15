using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RendererShape = com.radiant.engine.core.Shape;

namespace com.radiant.engine.bundle;

public class Geometry : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.World;
    public float SDFScale = 0.5f;
    public bool EnableSDF = false;

    private RenderTarget2D JFATexture1;
    private RenderTarget2D JFATexture2;
    private RenderTarget2D JFATextureInterior1;
    private RenderTarget2D JFATextureInterior2;

    private Vector2 WorldBounds;
    private Vector2 SDFBounds;
    private float ScreenDiagonal;
    private int JFAPassCount;

    private RenderTarget2D JFAResult;
    private RenderTarget2D JFAResultInterior;
    public RenderTarget2D EmissiveTexture { get; private set; }
    public RenderTarget2D AbsorptionTexture { get; private set; }
    public RenderTarget2D SDFTexture { get; private set; }
    public RenderTarget2D MotionVectorTexture { get; private set; }

    public RenderTarget2D BackgroundEmissive { get; set; }
    public RenderTarget2D BackgroundAbsorption { get; set; }

    private int EmissiveCount;
    private int AbsorptionCount;

    // Motion vector tracking with N-frame history (weighted: recent frames have more influence)
    public int MotionHistoryFrames = 2;

    private enum DebugMode { None, Emissive, Absorption, SDF, JFADirection, JFARaw, MotionVectors }
    private DebugMode CurrentDebug = DebugMode.None;
    public bool IsDebugging => CurrentDebug != DebugMode.None;
    public bool IsDebugHidingGameplay => CurrentDebug != DebugMode.None
        && CurrentDebug != DebugMode.Emissive
        && CurrentDebug != DebugMode.Absorption;

    // Z-layer config: Z values map directly to layers (Z=0 -> layer 0, etc.)
    // 65536 layers supports millions of entities with unique Z ordering
    // Reserve top layers: 65530 for rotating, 65535 for mouse
    private const int MaxZLayers = 65536;
    private const int ZLayerOffset = 0;  // No offset, Z maps directly to layer
    private const int InitialBucketCapacity = 64;  // Smaller since we have many more buckets now

    // Shape types for GPU (must match InstancedShapes.fx)
    private const float SHAPE_RECT = 0f;
    private const float SHAPE_CIRCLE = 1f;
    private const float SHAPE_TRIANGLE = 2f;
    private const float SHAPE_TRIANGLE_BORDER = 3f;

    private int ThreadCount;
    private int BucketCount;

    // Double-buffered shape storage in GPU-ready format
    private struct BufferSet
    {
        // Shapes in GPU-ready format (no conversion needed at render time)
        public RendererShape[][] EmissiveBuffers;  // [bucketIndex][]
        public int[] EmissiveCounts;
        public RendererShape[][] AbsorptionBuffers;
        public int[] AbsorptionCounts;
        public int EmissiveMinLayer, EmissiveMaxLayer;
        public int AbsorptionMinLayer, AbsorptionMaxLayer;

        // Per-thread layer tracking
        public int[] EmissiveMinLayerByThread;
        public int[] EmissiveMaxLayerByThread;
        public int[] AbsorptionMinLayerByThread;
        public int[] AbsorptionMaxLayerByThread;

        // Motion shapes (kept separate - small count)
        public List<(Vector2 pos, Vector2 size, Vector2 velocity, bool isCircle, float radius)>[] MotionShapesByThread;

        // Textured shapes drawn via SpriteBatch on emissive+absorption (kept separate from instanced shapes)
        public List<(Vector2 pos, Vector2 size, Texture2D texture, Color emissive, Color absorption)>[] TextureDrawsByThread;

        // Flat render array - populated from buckets, passed directly to GPU
        public RendererShape[] EmissiveRenderArray;
        public RendererShape[] AbsorptionRenderArray;
        public int EmissiveRenderCount;
        public int AbsorptionRenderCount;

        // Sparse tracking — only layers that actually received shapes
        public int[][] OccupiedLayers;     // [threadIndex][] layers with shapes
        public int[] OccupiedLayerCount;   // [threadIndex] count of occupied layers
    }

    private BufferSet[] Buffers; // [0] and [1]
    private int WriteBuffer;     // Buffer being written to (collection)
    private int ReadBuffer;      // Buffer being read from (rendering)

    // Background collection task
    private Task CollectionTask;
    private bool FirstFrame = true;

    // Timing for profiling
    private float CollectMs, FlattenMs, RenderMs;

    // Scratch buffer for sparse flatten (reused each frame)
    private int[] FlattenScratch = new int[1024];

    // Initial render array capacity
    private const int InitialRenderCapacity = 65536;

    public override void Initialize()
    {
        WorldBounds = Renderer.VirtualSize;

        SDFBounds = new Vector2(
            (int)(WorldBounds.X * SDFScale),
            (int)(WorldBounds.Y * SDFScale));

        ScreenDiagonal = MathF.Sqrt(WorldBounds.X * WorldBounds.X + WorldBounds.Y * WorldBounds.Y);

        float sdfDiagonal = SDFBounds.Length();
        JFAPassCount = (int)Math.Ceiling(Math.Log(sdfDiagonal, 2));

        InitializeGeometryBuffers();

        JFAResult = JFATexture1;
        JFAResultInterior = JFATextureInterior1;

        // Initialize Z-layer bucketed shape storage
        ThreadCount = Environment.ProcessorCount;
        BucketCount = MaxZLayers * ThreadCount;

        // Initialize double buffers
        Buffers = new BufferSet[2];
        for (int b = 0; b < 2; b++)
        {
            Buffers[b].EmissiveBuffers = new RendererShape[BucketCount][];
            Buffers[b].EmissiveCounts = new int[BucketCount];
            Buffers[b].AbsorptionBuffers = new RendererShape[BucketCount][];
            Buffers[b].AbsorptionCounts = new int[BucketCount];

            // Buckets are lazily allocated on first use (most layers are empty)

            Buffers[b].MotionShapesByThread = new List<(Vector2, Vector2, Vector2, bool, float)>[ThreadCount];
            Buffers[b].TextureDrawsByThread = new List<(Vector2, Vector2, Texture2D, Color, Color)>[ThreadCount];
            for (int t = 0; t < ThreadCount; t++)
            {
                Buffers[b].MotionShapesByThread[t] = new();
                Buffers[b].TextureDrawsByThread[t] = new();
            }

            Buffers[b].EmissiveMinLayerByThread = new int[ThreadCount];
            Buffers[b].EmissiveMaxLayerByThread = new int[ThreadCount];
            Buffers[b].AbsorptionMinLayerByThread = new int[ThreadCount];
            Buffers[b].AbsorptionMaxLayerByThread = new int[ThreadCount];

            Buffers[b].OccupiedLayers = new int[ThreadCount][];
            Buffers[b].OccupiedLayerCount = new int[ThreadCount];
            for (int t = 0; t < ThreadCount; t++)
                Buffers[b].OccupiedLayers[t] = new int[256];

            // Flat render arrays
            Buffers[b].EmissiveRenderArray = new RendererShape[InitialRenderCapacity];
            Buffers[b].AbsorptionRenderArray = new RendererShape[InitialRenderCapacity];
        }

        WriteBuffer = 0;
        ReadBuffer = 1;

        Inspector.CreateWindow("pipeline", "Pipeline Controller");

        Inspector.AddLabel("pipeline", "sceneHeader", "Scene");
        Inspector.AddLabel("pipeline", "emissive", "Emissive: 0");
        Inspector.AddLabel("pipeline", "absorption", "Absorption: 0");
        Inspector.AddLabel("pipeline", "buffers", "World: -");

        Inspector.AddLabel("pipeline", "perfHeader", "Performance");
        Inspector.AddLabel("pipeline", "timing", "Collect: - | Flatten: - | Render: -");
        Inspector.AddLabel("pipeline", "gpu", "GPU: -");
        Inspector.AddLabel("pipeline", "sdf", "SDF: -");
        Inspector.AddLabel("pipeline", "jfa", "JFA: -");

        Inspector.AddLabel("pipeline", "controlsHeader", "Controls");
        var debugModeNames = Enum.GetNames<DebugMode>();
        Inspector.AddDropdown("pipeline", "debugMode", "Debug", debugModeNames, 0, (index) => CurrentDebug = (DebugMode)index);
    }

    private void InitializeJFA()
    {
        Renderer
            .Reset()
            .SetShader("Geometry")
            .SetTechnique("InitializeJFA")
            .SetTarget(JFATexture1)
            .Clear(Color.Black)
            .SetParameter("EmissiveTexture", AbsorptionTexture)
            .SetParameter("WorldsBounds", SDFBounds)
            .SetParameter("ScreenDiagonal", ScreenDiagonal)
            .Draw()
            .Commit()
            .SetTarget(null);

        Renderer
            .Reset()
            .SetShader("Geometry")
            .SetTechnique("InitializeJFAInterior")
            .SetTarget(JFATextureInterior1)
            .Clear(Color.Black)
            .SetParameter("EmissiveTexture", AbsorptionTexture)
            .SetParameter("WorldsBounds", SDFBounds)
            .SetParameter("ScreenDiagonal", ScreenDiagonal)
            .Draw()
            .Commit()
            .SetTarget(null);
    }

    private void InitializeGeometryBuffers()
    {
        // Render targets use actual screen size for pixel quality matching the display.
        // The orthographic projection (in Renderer) maps virtual coordinates to these targets.
        int rtWidth = Renderer.ScreenWidth;
        int rtHeight = Renderer.ScreenHeight;

        EmissiveTexture = Renderer.CreateRenderTarget(rtWidth, rtHeight);

        AbsorptionTexture = Renderer.CreateRenderTarget(rtWidth, rtHeight,
            usage: RenderTargetUsage.PreserveContents);

        SDFTexture = Renderer.CreateRenderTarget(rtWidth, rtHeight, SurfaceFormat.HalfVector2);

        MotionVectorTexture = Renderer.CreateRenderTarget(rtWidth, rtHeight, SurfaceFormat.HalfVector2);

        JFATexture1 = Renderer.CreateRenderTarget(
            (int)SDFBounds.X, (int)SDFBounds.Y, SurfaceFormat.Vector4);

        JFATexture2 = Renderer.CreateRenderTarget(
            (int)SDFBounds.X, (int)SDFBounds.Y, SurfaceFormat.Vector4);

        JFATextureInterior1 = Renderer.CreateRenderTarget(
            (int)SDFBounds.X, (int)SDFBounds.Y, SurfaceFormat.Vector4);

        JFATextureInterior2 = Renderer.CreateRenderTarget(
            (int)SDFBounds.X, (int)SDFBounds.Y, SurfaceFormat.Vector4);
    }

    public override void Update()
    {
        int collectBuffer = WriteBuffer;
        CollectionTask = Task.Run(() => CollectShapes(collectBuffer));

        if (!FirstFrame)
        {
            RenderFromBuffer(ReadBuffer);
        }
        else
        {
            CollectionTask.Wait();
            RenderFromBuffer(collectBuffer);
            FirstFrame = false;
        }

        CollectionTask.Wait();

        WriteBuffer = ReadBuffer;
        ReadBuffer = collectBuffer;

        bool needsSDF = EnableSDF ||
            CurrentDebug == DebugMode.SDF ||
            CurrentDebug == DebugMode.JFADirection ||
            CurrentDebug == DebugMode.JFARaw;

        if (needsSDF)
            RenderSDFTexture();

        UpdateGizmos();
    }

    private void CollectShapes(int bufferIdx)
    {
        var swCollect = Stopwatch.StartNew();
        ref var buf = ref Buffers[bufferIdx];

        // Sparse clear — only reset buckets that were occupied last frame
        for (int t = 0; t < ThreadCount; t++)
        {
            for (int i = 0; i < buf.OccupiedLayerCount[t]; i++)
            {
                int bucketIndex = t * MaxZLayers + buf.OccupiedLayers[t][i];
                buf.EmissiveCounts[bucketIndex] = 0;
                buf.AbsorptionCounts[bucketIndex] = 0;
            }
            buf.OccupiedLayerCount[t] = 0;

            buf.EmissiveMinLayerByThread[t] = MaxZLayers;
            buf.EmissiveMaxLayerByThread[t] = -1;
            buf.AbsorptionMinLayerByThread[t] = MaxZLayers;
            buf.AbsorptionMaxLayerByThread[t] = -1;
            buf.MotionShapesByThread[t].Clear();
            buf.TextureDrawsByThread[t].Clear();
        }

        var emissiveBuffers = buf.EmissiveBuffers;
        var emissiveCounts = buf.EmissiveCounts;
        var absorptionBuffers = buf.AbsorptionBuffers;
        var absorptionCounts = buf.AbsorptionCounts;
        var emissiveMinByThread = buf.EmissiveMinLayerByThread;
        var emissiveMaxByThread = buf.EmissiveMaxLayerByThread;
        var absorptionMinByThread = buf.AbsorptionMinLayerByThread;
        var absorptionMaxByThread = buf.AbsorptionMaxLayerByThread;
        var motionByThread = buf.MotionShapesByThread;
        var textureByThread = buf.TextureDrawsByThread;
        var occupiedLayers = buf.OccupiedLayers;
        var occupiedLayerCount = buf.OccupiedLayerCount;

        Scene.ECS.Query((int threadIndex, int entity, ref Rectangle2D rectangle, ref Transform transform, ref Material material) =>
        {
            Vector2 position = new Vector2(MathF.Round(transform.Position.X), MathF.Round(transform.Position.Y));
            if (position.X + rectangle.Size.X < 0 || position.X >= WorldBounds.X ||
                position.Y + rectangle.Size.Y < 0 || position.Y >= WorldBounds.Y) return;

            int layer = Math.Clamp((int)transform.Position.Z + ZLayerOffset, 0, MaxZLayers - 1);
            int bucketIndex = threadIndex * MaxZLayers + layer;

            if (layer < emissiveMinByThread[threadIndex]) emissiveMinByThread[threadIndex] = layer;
            if (layer > emissiveMaxByThread[threadIndex]) emissiveMaxByThread[threadIndex] = layer;
            if (layer < absorptionMinByThread[threadIndex]) absorptionMinByThread[threadIndex] = layer;
            if (layer > absorptionMaxByThread[threadIndex]) absorptionMaxByThread[threadIndex] = layer;

            if (material.Texture != null)
            {
                textureByThread[threadIndex].Add((position, rectangle.Size, material.Texture, material.EmissiveScaled, material.Absorption));
                return;
            }

            int emissiveIndex = emissiveCounts[bucketIndex];

            if (emissiveIndex == 0)
            {
                int occIdx = occupiedLayerCount[threadIndex]++;
                if (occIdx >= occupiedLayers[threadIndex].Length)
                    Array.Resize(ref occupiedLayers[threadIndex], occIdx * 2);
                occupiedLayers[threadIndex][occIdx] = layer;
            }

            if (emissiveBuffers[bucketIndex] == null || emissiveIndex >= emissiveBuffers[bucketIndex].Length)
                EnsureBuffer(ref emissiveBuffers[bucketIndex], emissiveIndex);

            ref var emissiveShape = ref emissiveBuffers[bucketIndex][emissiveIndex];
            emissiveShape.Position = position;
            emissiveShape.Size = rectangle.Size;
            emissiveShape.Color = material.EmissiveScaled;
            emissiveShape.Type = SHAPE_RECT;
            emissiveCounts[bucketIndex] = emissiveIndex + 1;

            int absorptionIndex = absorptionCounts[bucketIndex];
            if (absorptionBuffers[bucketIndex] == null || absorptionIndex >= absorptionBuffers[bucketIndex].Length)
                EnsureBuffer(ref absorptionBuffers[bucketIndex], absorptionIndex);

            ref var absorptionShape = ref absorptionBuffers[bucketIndex][absorptionIndex];
            absorptionShape.Position = position;
            absorptionShape.Size = rectangle.Size;
            absorptionShape.Color = material.Absorption;
            absorptionShape.Type = SHAPE_RECT;
            absorptionCounts[bucketIndex] = absorptionIndex + 1;
        });

        Scene.ECS.Query((int threadIndex, int entity, ref Circle2D circle, ref Transform transform, ref Material material) =>
        {
            Vector2 center = new Vector2(MathF.Round(transform.Position.X), MathF.Round(transform.Position.Y));
            if (center.X + circle.Radius < 0 || center.X - circle.Radius >= WorldBounds.X ||
                center.Y + circle.Radius < 0 || center.Y - circle.Radius >= WorldBounds.Y) return;

            int layer = Math.Clamp((int)transform.Position.Z + ZLayerOffset, 0, MaxZLayers - 1);
            int bucketIndex = threadIndex * MaxZLayers + layer;

            if (layer < emissiveMinByThread[threadIndex]) emissiveMinByThread[threadIndex] = layer;
            if (layer > emissiveMaxByThread[threadIndex]) emissiveMaxByThread[threadIndex] = layer;
            if (layer < absorptionMinByThread[threadIndex]) absorptionMinByThread[threadIndex] = layer;
            if (layer > absorptionMaxByThread[threadIndex]) absorptionMaxByThread[threadIndex] = layer;

            // GPU format for circles: position is top-left corner, size is diameter
            Vector2 cornerPos = new Vector2(center.X - circle.Radius, center.Y - circle.Radius);
            Vector2 diameter = new Vector2(circle.Radius * 2f, circle.Radius * 2f);

            if (material.Texture != null)
            {
                textureByThread[threadIndex].Add((cornerPos, diameter, material.Texture, material.EmissiveScaled, material.Absorption));
                return;
            }

            int emissiveIndex = emissiveCounts[bucketIndex];

            if (emissiveIndex == 0)
            {
                int occIdx = occupiedLayerCount[threadIndex]++;
                if (occIdx >= occupiedLayers[threadIndex].Length)
                    Array.Resize(ref occupiedLayers[threadIndex], occIdx * 2);
                occupiedLayers[threadIndex][occIdx] = layer;
            }

            if (emissiveBuffers[bucketIndex] == null || emissiveIndex >= emissiveBuffers[bucketIndex].Length)
                EnsureBuffer(ref emissiveBuffers[bucketIndex], emissiveIndex);
            ref var emissiveShape = ref emissiveBuffers[bucketIndex][emissiveIndex];
            emissiveShape.Position = cornerPos;
            emissiveShape.Size = diameter;
            emissiveShape.Color = material.EmissiveScaled;
            emissiveShape.Type = SHAPE_CIRCLE;
            emissiveCounts[bucketIndex] = emissiveIndex + 1;

            int absorptionIndex = absorptionCounts[bucketIndex];
            if (absorptionBuffers[bucketIndex] == null || absorptionIndex >= absorptionBuffers[bucketIndex].Length)
                EnsureBuffer(ref absorptionBuffers[bucketIndex], absorptionIndex);
            ref var absorptionShape = ref absorptionBuffers[bucketIndex][absorptionIndex];
            absorptionShape.Position = cornerPos;
            absorptionShape.Size = diameter;
            absorptionShape.Color = material.Absorption;
            absorptionShape.Type = SHAPE_CIRCLE;
            absorptionCounts[bucketIndex] = absorptionIndex + 1;
        });

        Scene.ECS.Query((int threadIndex, int entity, ref Triangle2D triangle, ref Transform transform, ref Material material) =>
        {
            Vector2 position = new Vector2(MathF.Round(transform.Position.X), MathF.Round(transform.Position.Y));
            if (position.X + triangle.Size.X < 0 || position.X >= WorldBounds.X ||
                position.Y + triangle.Size.Y < 0 || position.Y >= WorldBounds.Y) return;

            int layer = Math.Clamp((int)transform.Position.Z + ZLayerOffset, 0, MaxZLayers - 1);
            int bucketIndex = threadIndex * MaxZLayers + layer;
            float shapeType = triangle.Bordered ? SHAPE_TRIANGLE_BORDER : SHAPE_TRIANGLE;

            if (layer < emissiveMinByThread[threadIndex]) emissiveMinByThread[threadIndex] = layer;
            if (layer > emissiveMaxByThread[threadIndex]) emissiveMaxByThread[threadIndex] = layer;
            if (layer < absorptionMinByThread[threadIndex]) absorptionMinByThread[threadIndex] = layer;
            if (layer > absorptionMaxByThread[threadIndex]) absorptionMaxByThread[threadIndex] = layer;

            if (material.Texture != null)
            {
                textureByThread[threadIndex].Add((position, triangle.Size, material.Texture, material.EmissiveScaled, material.Absorption));
                return;
            }

            int emissiveIndex = emissiveCounts[bucketIndex];

            if (emissiveIndex == 0)
            {
                int occIdx = occupiedLayerCount[threadIndex]++;
                if (occIdx >= occupiedLayers[threadIndex].Length)
                    Array.Resize(ref occupiedLayers[threadIndex], occIdx * 2);
                occupiedLayers[threadIndex][occIdx] = layer;
            }

            if (emissiveBuffers[bucketIndex] == null || emissiveIndex >= emissiveBuffers[bucketIndex].Length)
                EnsureBuffer(ref emissiveBuffers[bucketIndex], emissiveIndex);
            ref var emissiveShape = ref emissiveBuffers[bucketIndex][emissiveIndex];
            emissiveShape.Position = position;
            emissiveShape.Size = triangle.Size;
            emissiveShape.Color = material.EmissiveScaled;
            emissiveShape.Type = shapeType;
            emissiveCounts[bucketIndex] = emissiveIndex + 1;

            int absorptionIndex = absorptionCounts[bucketIndex];
            if (absorptionBuffers[bucketIndex] == null || absorptionIndex >= absorptionBuffers[bucketIndex].Length)
                EnsureBuffer(ref absorptionBuffers[bucketIndex], absorptionIndex);
            ref var absorptionShape = ref absorptionBuffers[bucketIndex][absorptionIndex];
            absorptionShape.Position = position;
            absorptionShape.Size = triangle.Size;
            absorptionShape.Color = material.Absorption;
            absorptionShape.Type = shapeType;
            absorptionCounts[bucketIndex] = absorptionIndex + 1;
        });

        // Merge per-thread layer ranges
        buf.EmissiveMinLayer = MaxZLayers;
        buf.EmissiveMaxLayer = -1;
        buf.AbsorptionMinLayer = MaxZLayers;
        buf.AbsorptionMaxLayer = -1;
        for (int t = 0; t < ThreadCount; t++)
        {
            if (emissiveMaxByThread[t] >= 0)
            {
                if (emissiveMinByThread[t] < buf.EmissiveMinLayer) buf.EmissiveMinLayer = emissiveMinByThread[t];
                if (emissiveMaxByThread[t] > buf.EmissiveMaxLayer) buf.EmissiveMaxLayer = emissiveMaxByThread[t];
            }
            if (absorptionMaxByThread[t] >= 0)
            {
                if (absorptionMinByThread[t] < buf.AbsorptionMinLayer) buf.AbsorptionMinLayer = absorptionMinByThread[t];
                if (absorptionMaxByThread[t] > buf.AbsorptionMaxLayer) buf.AbsorptionMaxLayer = absorptionMaxByThread[t];
            }
        }
        CollectMs = (float)swCollect.Elapsed.TotalMilliseconds;

        // Flatten buckets into render arrays (Z-ordered)
        var swFlatten = Stopwatch.StartNew();
        FlattenToRenderArray(ref buf, true);  // Emissive
        FlattenToRenderArray(ref buf, false); // Absorption
        FlattenMs = (float)swFlatten.Elapsed.TotalMilliseconds;

        // Motion tracking
        Scene.ECS.Query((int threadIndex, int entity, ref Transform transform, ref Rectangle2D rectangle, ref MotionTrackable motion) =>
        {
            Vector2 position = new Vector2(MathF.Round(transform.Position.X), MathF.Round(transform.Position.Y));
            Vector2 velocity = motion.CalculateVelocity(transform.Position, MotionHistoryFrames);
            if (velocity.LengthSquared() > 0.0001f)
                motionByThread[threadIndex].Add((position, rectangle.Size, velocity, false, 0));
            motion.Push(transform.Position);
        });

        Scene.ECS.Query((int threadIndex, int entity, ref Transform transform, ref Circle2D circle, ref MotionTrackable motion) =>
        {
            Vector2 center = new Vector2(MathF.Round(transform.Position.X), MathF.Round(transform.Position.Y));
            Vector2 velocity = motion.CalculateVelocity(transform.Position, MotionHistoryFrames);
            if (velocity.LengthSquared() > 0.0001f)
                motionByThread[threadIndex].Add((center, Vector2.Zero, velocity, true, circle.Radius));
            motion.Push(transform.Position);
        });
    }

    private void FlattenToRenderArray(ref BufferSet buf, bool isEmissive)
    {
        var buckets = isEmissive ? buf.EmissiveBuffers : buf.AbsorptionBuffers;
        var counts = isEmissive ? buf.EmissiveCounts : buf.AbsorptionCounts;

        // Collect all occupied layers from all threads into scratch buffer
        int scratchCount = 0;
        for (int t = 0; t < ThreadCount; t++)
        {
            int layerCount = buf.OccupiedLayerCount[t];
            if (scratchCount + layerCount > FlattenScratch.Length)
                Array.Resize(ref FlattenScratch, (scratchCount + layerCount) * 2);
            Array.Copy(buf.OccupiedLayers[t], 0, FlattenScratch, scratchCount, layerCount);
            scratchCount += layerCount;
        }

        if (scratchCount == 0)
        {
            if (isEmissive) buf.EmissiveRenderCount = 0;
            else buf.AbsorptionRenderCount = 0;
            return;
        }

        // Sort and deduplicate to get unique layers in Z-order
        Array.Sort(FlattenScratch, 0, scratchCount);
        int uniqueCount = 1;
        for (int i = 1; i < scratchCount; i++)
        {
            if (FlattenScratch[i] != FlattenScratch[i - 1])
                FlattenScratch[uniqueCount++] = FlattenScratch[i];
        }

        // Count total shapes across only occupied layers
        int totalCount = 0;
        for (int i = 0; i < uniqueCount; i++)
        {
            int layer = FlattenScratch[i];
            for (int t = 0; t < ThreadCount; t++)
                totalCount += counts[t * MaxZLayers + layer];
        }

        // Ensure render array capacity
        ref var renderArray = ref (isEmissive ? ref buf.EmissiveRenderArray : ref buf.AbsorptionRenderArray);
        if (totalCount > renderArray.Length)
        {
            int newCapacity = renderArray.Length;
            while (newCapacity < totalCount) newCapacity *= 2;
            renderArray = new RendererShape[newCapacity];
        }

        // Copy buckets to render array in Z-order (only occupied layers)
        int offset = 0;
        for (int i = 0; i < uniqueCount; i++)
        {
            int layer = FlattenScratch[i];
            for (int t = 0; t < ThreadCount; t++)
            {
                int bucketIndex = t * MaxZLayers + layer;
                int count = counts[bucketIndex];
                if (count > 0)
                {
                    Array.Copy(buckets[bucketIndex], 0, renderArray, offset, count);
                    offset += count;
                }
            }
        }

        if (isEmissive) buf.EmissiveRenderCount = totalCount;
        else buf.AbsorptionRenderCount = totalCount;
    }

    private void RenderFromBuffer(int bufferIdx)
    {
        var sw = Stopwatch.StartNew();
        ref var buf = ref Buffers[bufferIdx];

        bool hasTextures = false;
        for (int t = 0; t < ThreadCount; t++)
        {
            if (buf.TextureDrawsByThread[t].Count > 0) { hasTextures = true; break; }
        }

        // Emissive: flush shapes then immediately draw textures (before switching target)
        EmissiveCount = buf.EmissiveRenderCount;
        if (BackgroundEmissive != null)
        {
            Renderer.Blit(BackgroundEmissive, EmissiveTexture, Color.Transparent, BlendState.Opaque);
            Renderer.Configure(BlendState.AlphaBlend)
                .FlushShapesExternal(buf.EmissiveRenderArray, buf.EmissiveRenderCount, EmissiveTexture, null);
        }
        else
        {
            Renderer.Configure(BlendState.AlphaBlend)
                .FlushShapesExternal(buf.EmissiveRenderArray, buf.EmissiveRenderCount, EmissiveTexture, Color.Transparent);
        }

        if (hasTextures)
        {
            Renderer.Reset().Configure(BlendState.AlphaBlend).SetTarget(EmissiveTexture);
            for (int t = 0; t < ThreadCount; t++)
                foreach (var (pos, size, texture, emissive, _) in buf.TextureDrawsByThread[t])
                    Renderer.DrawTexture(texture,
                        Renderer.VirtualToScreenRect(pos.X, pos.Y, size.X, size.Y),
                        emissive);
            Renderer.Commit();
        }

        // Absorption: flush shapes then immediately draw textures (before switching target)
        AbsorptionCount = buf.AbsorptionRenderCount;
        if (BackgroundAbsorption != null)
        {
            Renderer.Blit(BackgroundAbsorption, AbsorptionTexture, Color.Transparent, BlendState.Opaque);
            Renderer.Configure(BlendState.AlphaBlend)
                .FlushShapesExternal(buf.AbsorptionRenderArray, buf.AbsorptionRenderCount, AbsorptionTexture, null);
        }
        else
        {
            Renderer.Configure(BlendState.AlphaBlend)
                .FlushShapesExternal(buf.AbsorptionRenderArray, buf.AbsorptionRenderCount, AbsorptionTexture, Color.Transparent);
        }

        if (hasTextures)
        {
            Renderer.Reset().Configure(BlendState.AlphaBlend).SetTarget(AbsorptionTexture);
            for (int t = 0; t < ThreadCount; t++)
                foreach (var (pos, size, texture, _, absorption) in buf.TextureDrawsByThread[t])
                    Renderer.DrawTexture(texture,
                        Renderer.VirtualToScreenRect(pos.X, pos.Y, size.X, size.Y),
                        absorption);
            Renderer.Commit();
        }

        RenderMotionFromBuffer(ref buf);
        RenderMs = (float)sw.Elapsed.TotalMilliseconds;
    }

    private static void EnsureBuffer(ref RendererShape[] buffer, int currentCount)
    {
        if (buffer == null)
        {
            buffer = new RendererShape[InitialBucketCapacity];
            return;
        }
        var newBuffer = new RendererShape[buffer.Length * 2];
        Array.Copy(buffer, newBuffer, currentCount);
        buffer = newBuffer;
    }

    private void RenderMotionFromBuffer(ref BufferSet buf)
    {
        Renderer.ClearShapes();

        for (int threadIndex = 0; threadIndex < ThreadCount; threadIndex++)
        {
            foreach (var (position, size, velocity, isCircle, radius) in buf.MotionShapesByThread[threadIndex])
            {
                float normalizedVelocityX = velocity.X / 10f * 0.5f + 0.5f;
                float normalizedVelocityY = velocity.Y / 10f * 0.5f + 0.5f;
                Color motionColor = new Color(normalizedVelocityX, normalizedVelocityY, 0f, 1f);

                if (isCircle)
                    Renderer.DrawCircle(position, radius, motionColor);
                else
                    Renderer.DrawRect(position, size, motionColor);
            }
        }

        Color motionClearColor = new Color(0.5f, 0.5f, 0f, 1f);
        Renderer.Configure(BlendState.Opaque).FlushShapes(MotionVectorTexture, motionClearColor, "Sharp");
    }

    private void RenderSDFTexture()
    {
        InitializeJFA();
        RunJFAPasses();
        RunJFAPassesInterior();
        GenerateFinalSDF();
    }

    private void RunJFAPasses()
    {
        Renderer
            .Reset()
            .SetShader("Geometry")
            .SetTechnique("JFAPass")
            .SetParameter("WorldsBounds", SDFBounds)
            .SetParameter("ScreenDiagonal", ScreenDiagonal);

        JFAResult = Renderer.PingPong(
            JFATexture1, JFATexture2,
            JFAPassCount,
            beforePass: (pass, input) =>
            {
                int jump = 1 << (JFAPassCount - pass - 1);
                Renderer
                    .SetParameter("JFATexture", input)
                    .SetParameter("JumpDistance", (float)jump);
            },
            clearColor: Color.Black
        );
    }

    private void RunJFAPassesInterior()
    {
        Renderer
            .Reset()
            .SetShader("Geometry")
            .SetTechnique("JFAPass")
            .SetParameter("WorldsBounds", SDFBounds)
            .SetParameter("ScreenDiagonal", ScreenDiagonal);

        JFAResultInterior = Renderer.PingPong(
            JFATextureInterior1, JFATextureInterior2,
            JFAPassCount,
            beforePass: (pass, input) =>
            {
                int jump = 1 << (JFAPassCount - pass - 1);
                Renderer
                    .SetParameter("JFATexture", input)
                    .SetParameter("JumpDistance", (float)jump);
            },
            clearColor: Color.Black
        );
    }

    private void GenerateFinalSDF()
    {
        Renderer
            .Reset()
            .SetShader("Geometry")
            .SetTechnique("GenerateSDFFromJFA")
            .SetTarget(SDFTexture)
            .SetParameter("EmissiveTexture", AbsorptionTexture)
            .SetParameter("JFATexture", JFAResult)
            .SetParameter("JFATextureInterior", JFAResultInterior)
            .SetParameter("WorldsBounds", WorldBounds)
            .SetParameter("JFASize", SDFBounds)
            .SetParameter("ScreenDiagonal", ScreenDiagonal)
            .Draw()
            .Commit()
            .SetTarget(null);
    }

    private void UpdateGizmos()
    {
        Inspector.SetDropdownValue("pipeline", "debugMode", (int)CurrentDebug);
        Inspector.SetLabel("pipeline", "emissive", $"Emissive Objects: {EmissiveCount}");
        Inspector.SetLabel("pipeline", "absorption", $"Absorption Objects: {AbsorptionCount}");
        Inspector.SetLabel("pipeline", "buffers", $"World Bounds: {WorldBounds.X}x{WorldBounds.Y}");
        Inspector.SetLabel("pipeline", "timing", $"Collect: {CollectMs:F2}ms | Flatten: {FlattenMs:F2}ms | Render: {RenderMs:F2}ms");
        Inspector.SetLabel("pipeline", "gpu", $"GPU: SetData: {Renderer.LastSetDataMs:F2}ms | Draw: {Renderer.LastDrawMs:F2}ms");

        if (EnableSDF)
        {
            Inspector.SetLabel("pipeline", "sdf", $"SDF: {SDFBounds.X}x{SDFBounds.Y} ({SDFScale:P0})");
            Inspector.SetLabel("pipeline", "jfa", $"JFA Passes: {JFAPassCount}");
        }
        else
        {
            Inspector.SetLabel("pipeline", "sdf", "SDF: Disabled");
            Inspector.SetLabel("pipeline", "jfa", "");
        }
    }

    public override void LateRender()
    {
        if (CurrentDebug == DebugMode.None) return;

        switch (CurrentDebug)
        {
            case DebugMode.Emissive:
                Renderer
                    .Reset()
                    .SetShader("Geometry")
                    .SetTechnique("DebugEmissive")
                    .SetTarget(null)
                    .SetParameter("EmissiveTexture", EmissiveTexture)
                    .SetParameter("WorldsBounds", SDFBounds)
                    .SetParameter("ScreenDiagonal", ScreenDiagonal)
                    .Draw()
                    .Commit();
                break;

            case DebugMode.Absorption:
                Renderer
                    .Reset()
                    .Configure(BlendState.AlphaBlend)
                    .Configure(SamplerState.LinearClamp)
                    .SetTarget(null)
                    .DrawTexture(AbsorptionTexture, new Rectangle(0, 0, Renderer.ScreenWidth, Renderer.ScreenHeight), Color.White)
                    .Commit();
                break;

            case DebugMode.SDF:
                Renderer
                    .Reset()
                    .SetShader("Geometry")
                    .SetTechnique("DebugSDFVisible")
                    .SetTarget(null)
                    .SetParameter("JFATexture", JFAResult)
                    .SetParameter("JFATextureInterior", JFAResultInterior)
                    .SetParameter("EmissiveTexture", AbsorptionTexture)
                    .SetParameter("WorldsBounds", WorldBounds)
                    .SetParameter("JFASize", SDFBounds)
                    .SetParameter("ScreenDiagonal", ScreenDiagonal)
                    .Draw()
                    .Commit();
                break;

            case DebugMode.JFADirection:
                Renderer
                    .Reset()
                    .SetShader("Geometry")
                    .SetTechnique("DebugJFA")
                    .SetTarget(null)
                    .SetParameter("JFATexture", JFAResult)
                    .SetParameter("JFATextureInterior", JFAResultInterior)
                    .SetParameter("EmissiveTexture", AbsorptionTexture)
                    .SetParameter("WorldsBounds", WorldBounds)
                    .SetParameter("JFASize", SDFBounds)
                    .SetParameter("ScreenDiagonal", ScreenDiagonal)
                    .Draw()
                    .Commit();
                break;

            case DebugMode.JFARaw:
                Renderer
                    .Reset()
                    .SetShader("Geometry")
                    .SetTechnique("DebugJFARaw")
                    .SetTarget(null)
                    .SetParameter("JFATexture", JFAResult)
                    .SetParameter("WorldsBounds", SDFBounds)
                    .SetParameter("ScreenDiagonal", ScreenDiagonal)
                    .Draw()
                    .Commit();
                break;

            case DebugMode.MotionVectors:
                Renderer
                    .Reset()
                    .SetShader("Geometry")
                    .SetTechnique("DebugMotionVectors")
                    .SetTarget(null)
                    .SetParameter("MotionVectorTexture", MotionVectorTexture)
                    .SetParameter("WorldsBounds", Renderer.ScreenSize)
                    .Draw()
                    .Commit();
                break;
        }
    }

    public override void OnResize()
    {
        // WorldBounds stays at VirtualSize (constant) — culling uses virtual coordinates.
        // Render targets are recreated at screen size for pixel quality matching the display.
        Vector2 newSize = Renderer.ScreenSize;

        // Skip if screen size hasn't actually changed
        if (EmissiveTexture != null &&
            EmissiveTexture.Width == (int)newSize.X &&
            EmissiveTexture.Height == (int)newSize.Y)
            return;

        EmissiveTexture?.Dispose();
        AbsorptionTexture?.Dispose();
        SDFTexture?.Dispose();
        MotionVectorTexture?.Dispose();
        JFATexture1?.Dispose();
        JFATexture2?.Dispose();
        JFATextureInterior1?.Dispose();
        JFATextureInterior2?.Dispose();

        InitializeGeometryBuffers();
        JFAResult = JFATexture1;
        JFAResultInterior = JFATextureInterior1;

        BackgroundEmissive = null;
        BackgroundAbsorption = null;
    }

    public override void Dispose()
    {
        CollectionTask?.Wait();

        EmissiveTexture?.Dispose();
        AbsorptionTexture?.Dispose();
        SDFTexture?.Dispose();
        MotionVectorTexture?.Dispose();
        JFATexture1?.Dispose();
        JFATexture2?.Dispose();
        JFATextureInterior1?.Dispose();
        JFATextureInterior2?.Dispose();
    }
}
