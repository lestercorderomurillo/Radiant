using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

public class Geometry : core.System
{
    public float SDFScale = 0.25f;
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

    private int EmissiveCount;
    private int AbsorptionCount;

    // Motion vector tracking with N-frame history (weighted: recent frames have more influence)
    public int MotionHistoryFrames = 2;

    private List<(Vector2 pos, Vector2 size, Vector2 velocity, bool isCircle, float radius)>[] MotionShapesByThread;

    private enum DebugMode { None, Emissive, Absorption, SDF, JFADirection, JFARaw, MotionVectors }
    private DebugMode CurrentDebug = DebugMode.None;
    private KeyboardState PrevKeyState;
    private GizmosRenderer Gizmos;

    // Parallel shape collection with Z-layer bucketing - NO SORTING NEEDED
    private enum ShapeType : byte { Rectangle, Circle, Triangle, TriangleBorder }

    private struct Shape
    {
        public Vector2 Position;
        public Vector2 Size;
        public float Radius;
        public Color Color;
        public ShapeType Type;
    }

    // Z-layer config: supports Z from -ZLayerOffset to (MaxZLayers - ZLayerOffset - 1)
    private const int MaxZLayers = 512;
    private const int ZLayerOffset = 256;
    private const int InitialBucketCapacity = 64;

    private int ThreadCount;
    private int BucketCount;

    // Pre-allocated: [layer * ThreadCount + threadIdx] = array of shapes
    private Shape[][] EmissiveBuffers;
    private int[] EmissiveCounts;
    private Shape[][] AbsorptionBuffers;
    private int[] AbsorptionCounts;

    // Track which layers have any shapes (avoid iterating empty layers)
    private int EmissiveMinLayer, EmissiveMaxLayer;
    private int AbsorptionMinLayer, AbsorptionMaxLayer;

    public override void Initialize()
    {
        WorldBounds = Renderer.ScreenSize;

        SDFBounds = new Vector2(
            (int)(WorldBounds.X * SDFScale),
            (int)(WorldBounds.Y * SDFScale));

        ScreenDiagonal = Renderer.ScreenDiagonal;

        float sdfDiagonal = SDFBounds.Length();
        JFAPassCount = (int)Math.Ceiling(Math.Log(sdfDiagonal, 2));

        InitializeGeometryBuffers();

        JFAResult = JFATexture1;
        JFAResultInterior = JFATextureInterior1;

        // Initialize Z-layer bucketed shape storage
        ThreadCount = Environment.ProcessorCount;
        BucketCount = MaxZLayers * ThreadCount;

        EmissiveBuffers = new Shape[BucketCount][];
        EmissiveCounts = new int[BucketCount];
        AbsorptionBuffers = new Shape[BucketCount][];
        AbsorptionCounts = new int[BucketCount];

        for (int bucketIndex = 0; bucketIndex < BucketCount; bucketIndex++)
        {
            EmissiveBuffers[bucketIndex] = new Shape[InitialBucketCapacity];
            AbsorptionBuffers[bucketIndex] = new Shape[InitialBucketCapacity];
        }

        MotionShapesByThread = new List<(Vector2, Vector2, Vector2, bool, float)>[ThreadCount];
        for (int threadIndex = 0; threadIndex < ThreadCount; threadIndex++)
            MotionShapesByThread[threadIndex] = new();

        Gizmos = Scene.ECS.GetSystem<GizmosRenderer>();
        PrevKeyState = Keyboard.GetState();
    }

    private void InitializeJFA()
    {
        // Initialize exterior JFA (seeds surface pixels, floods outward)
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

        // Initialize interior JFA (seeds non-surface pixels, floods inward)
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
        EmissiveTexture = new RenderTarget2D(
            Renderer.Device, (int)WorldBounds.X, (int)WorldBounds.Y,
            false, SurfaceFormat.Color, DepthFormat.None);

        AbsorptionTexture = new RenderTarget2D(
            Renderer.Device, (int)WorldBounds.X, (int)WorldBounds.Y,
            false, SurfaceFormat.Color, DepthFormat.None);

        SDFTexture = new RenderTarget2D(
            Renderer.Device, (int)WorldBounds.X, (int)WorldBounds.Y,
            false, SurfaceFormat.HalfVector2, DepthFormat.None);

        MotionVectorTexture = new RenderTarget2D(
            Renderer.Device, (int)WorldBounds.X, (int)WorldBounds.Y,
            false, SurfaceFormat.HalfVector2, DepthFormat.None);

        JFATexture1 = new RenderTarget2D(
            Renderer.Device, (int)SDFBounds.X, (int)SDFBounds.Y,
            false, SurfaceFormat.Vector4, DepthFormat.None);

        JFATexture2 = new RenderTarget2D(
            Renderer.Device, (int)SDFBounds.X, (int)SDFBounds.Y,
            false, SurfaceFormat.Vector4, DepthFormat.None);

        JFATextureInterior1 = new RenderTarget2D(
            Renderer.Device, (int)SDFBounds.X, (int)SDFBounds.Y,
            false, SurfaceFormat.Vector4, DepthFormat.None);

        JFATextureInterior2 = new RenderTarget2D(
            Renderer.Device, (int)SDFBounds.X, (int)SDFBounds.Y,
            false, SurfaceFormat.Vector4, DepthFormat.None);
    }

    public override void Update()
    {
        var key = Keyboard.GetState();

        if (key.IsKeyDown(Keys.F2) && !PrevKeyState.IsKeyDown(Keys.F2))
        {
            int count = Enum.GetValues<DebugMode>().Length;
            CurrentDebug = (DebugMode)(((int)CurrentDebug + 1) % count);
        }

        PrevKeyState = key;

        CollectAndRenderGeometry();

        bool needsSDF = EnableSDF ||
            CurrentDebug == DebugMode.SDF ||
            CurrentDebug == DebugMode.JFADirection ||
            CurrentDebug == DebugMode.JFARaw;

        if (needsSDF)
            RenderSDFTexture();

        UpdateGizmos();
    }

    private void CollectAndRenderGeometry()
    {
        // Reset counts only (arrays stay allocated)
        Array.Clear(EmissiveCounts, 0, BucketCount);
        Array.Clear(AbsorptionCounts, 0, BucketCount);
        EmissiveMinLayer = MaxZLayers;
        EmissiveMaxLayer = -1;
        AbsorptionMinLayer = MaxZLayers;
        AbsorptionMaxLayer = -1;

        for (int threadIndex = 0; threadIndex < ThreadCount; threadIndex++)
            MotionShapesByThread[threadIndex].Clear();

        // Rectangles - bucket by Z layer
        Scene.ECS.Query((int threadIndex, int entity, ref Transform transform, ref Rectangle2D rectangle, ref Material material) =>
        {
            Vector2 position = new Vector2(MathF.Round(transform.Position.X), MathF.Round(transform.Position.Y));
            if (position.X + rectangle.Size.X < 0 || position.X >= WorldBounds.X ||
                position.Y + rectangle.Size.Y < 0 || position.Y >= WorldBounds.Y) return;

            int layer = Math.Clamp((int)transform.Position.Z + ZLayerOffset, 0, MaxZLayers - 1);
            int bucketIndex = layer * ThreadCount + threadIndex;

            int emissiveIndex = EmissiveCounts[bucketIndex];
            if (emissiveIndex >= EmissiveBuffers[bucketIndex].Length)
                GrowBuffer(ref EmissiveBuffers[bucketIndex], emissiveIndex);
            ref var emissiveShape = ref EmissiveBuffers[bucketIndex][emissiveIndex];
            emissiveShape.Position = position;
            emissiveShape.Size = rectangle.Size;
            emissiveShape.Color = material.EmissiveScaled;
            emissiveShape.Type = ShapeType.Rectangle;
            EmissiveCounts[bucketIndex] = emissiveIndex + 1;

            int absorptionIndex = AbsorptionCounts[bucketIndex];
            if (absorptionIndex >= AbsorptionBuffers[bucketIndex].Length)
                GrowBuffer(ref AbsorptionBuffers[bucketIndex], absorptionIndex);
            ref var absorptionShape = ref AbsorptionBuffers[bucketIndex][absorptionIndex];
            absorptionShape.Position = position;
            absorptionShape.Size = rectangle.Size;
            absorptionShape.Color = material.Absorption;
            absorptionShape.Type = ShapeType.Rectangle;
            AbsorptionCounts[bucketIndex] = absorptionIndex + 1;
        });

        // Circles
        Scene.ECS.Query((int threadIndex, int entity, ref Transform transform, ref Circle2D circle, ref Material material) =>
        {
            Vector2 center = new Vector2(MathF.Round(transform.Position.X), MathF.Round(transform.Position.Y));
            if (center.X + circle.Radius < 0 || center.X - circle.Radius >= WorldBounds.X ||
                center.Y + circle.Radius < 0 || center.Y - circle.Radius >= WorldBounds.Y) return;

            int layer = Math.Clamp((int)transform.Position.Z + ZLayerOffset, 0, MaxZLayers - 1);
            int bucketIndex = layer * ThreadCount + threadIndex;

            int emissiveIndex = EmissiveCounts[bucketIndex];
            if (emissiveIndex >= EmissiveBuffers[bucketIndex].Length)
                GrowBuffer(ref EmissiveBuffers[bucketIndex], emissiveIndex);
            ref var emissiveShape = ref EmissiveBuffers[bucketIndex][emissiveIndex];
            emissiveShape.Position = center;
            emissiveShape.Radius = circle.Radius;
            emissiveShape.Color = material.EmissiveScaled;
            emissiveShape.Type = ShapeType.Circle;
            EmissiveCounts[bucketIndex] = emissiveIndex + 1;

            int absorptionIndex = AbsorptionCounts[bucketIndex];
            if (absorptionIndex >= AbsorptionBuffers[bucketIndex].Length)
                GrowBuffer(ref AbsorptionBuffers[bucketIndex], absorptionIndex);
            ref var absorptionShape = ref AbsorptionBuffers[bucketIndex][absorptionIndex];
            absorptionShape.Position = center;
            absorptionShape.Radius = circle.Radius;
            absorptionShape.Color = material.Absorption;
            absorptionShape.Type = ShapeType.Circle;
            AbsorptionCounts[bucketIndex] = absorptionIndex + 1;
        });

        // Triangles
        Scene.ECS.Query((int threadIndex, int entity, ref Transform transform, ref Triangle2D triangle, ref Material material) =>
        {
            Vector2 position = new Vector2(MathF.Round(transform.Position.X), MathF.Round(transform.Position.Y));
            if (position.X + triangle.Size.X < 0 || position.X >= WorldBounds.X ||
                position.Y + triangle.Size.Y < 0 || position.Y >= WorldBounds.Y) return;

            int layer = Math.Clamp((int)transform.Position.Z + ZLayerOffset, 0, MaxZLayers - 1);
            int bucketIndex = layer * ThreadCount + threadIndex;
            ShapeType shapeType = triangle.Bordered ? ShapeType.TriangleBorder : ShapeType.Triangle;

            int emissiveIndex = EmissiveCounts[bucketIndex];
            if (emissiveIndex >= EmissiveBuffers[bucketIndex].Length)
                GrowBuffer(ref EmissiveBuffers[bucketIndex], emissiveIndex);
            ref var emissiveShape = ref EmissiveBuffers[bucketIndex][emissiveIndex];
            emissiveShape.Position = position;
            emissiveShape.Size = triangle.Size;
            emissiveShape.Color = material.EmissiveScaled;
            emissiveShape.Type = shapeType;
            EmissiveCounts[bucketIndex] = emissiveIndex + 1;

            int absorptionIndex = AbsorptionCounts[bucketIndex];
            if (absorptionIndex >= AbsorptionBuffers[bucketIndex].Length)
                GrowBuffer(ref AbsorptionBuffers[bucketIndex], absorptionIndex);
            ref var absorptionShape = ref AbsorptionBuffers[bucketIndex][absorptionIndex];
            absorptionShape.Position = position;
            absorptionShape.Size = triangle.Size;
            absorptionShape.Color = material.Absorption;
            absorptionShape.Type = shapeType;
            AbsorptionCounts[bucketIndex] = absorptionIndex + 1;
        });

        // Compute actual layer ranges after parallel collection
        for (int layer = 0; layer < MaxZLayers; layer++)
        {
            for (int threadIndex = 0; threadIndex < ThreadCount; threadIndex++)
            {
                int bucketIndex = layer * ThreadCount + threadIndex;
                if (EmissiveCounts[bucketIndex] > 0)
                {
                    if (layer < EmissiveMinLayer) EmissiveMinLayer = layer;
                    if (layer > EmissiveMaxLayer) EmissiveMaxLayer = layer;
                }
                if (AbsorptionCounts[bucketIndex] > 0)
                {
                    if (layer < AbsorptionMinLayer) AbsorptionMinLayer = layer;
                    if (layer > AbsorptionMaxLayer) AbsorptionMaxLayer = layer;
                }
            }
        }

        // Motion tracking for rectangles
        Scene.ECS.Query((int threadIndex, int entity, ref Transform transform, ref Rectangle2D rectangle, ref MotionTrackable motion) =>
        {
            Vector2 position = new Vector2(MathF.Round(transform.Position.X), MathF.Round(transform.Position.Y));
            Vector2 velocity = motion.CalculateVelocity(transform.Position, MotionHistoryFrames);
            if (velocity.LengthSquared() > 0.0001f)
                MotionShapesByThread[threadIndex].Add((position, rectangle.Size, velocity, false, 0));
            motion.Push(transform.Position);
        });

        // Motion tracking for circles
        Scene.ECS.Query((int threadIndex, int entity, ref Transform transform, ref Circle2D circle, ref MotionTrackable motion) =>
        {
            Vector2 center = new Vector2(MathF.Round(transform.Position.X), MathF.Round(transform.Position.Y));
            Vector2 velocity = motion.CalculateVelocity(transform.Position, MotionHistoryFrames);
            if (velocity.LengthSquared() > 0.0001f)
                MotionShapesByThread[threadIndex].Add((center, Vector2.Zero, velocity, true, circle.Radius));
            motion.Push(transform.Position);
        });

        RenderEmissiveFromCollected();
        RenderAbsorptionFromCollected();
        RenderMotionFromCollected();
    }

    private static void GrowBuffer(ref Shape[] buffer, int currentCount)
    {
        var newBuffer = new Shape[buffer.Length * 2];
        Array.Copy(buffer, newBuffer, currentCount);
        buffer = newBuffer;
    }

    private void RenderEmissiveFromCollected()
    {
        Renderer.ClearShapes();

        if (EmissiveMaxLayer < 0)
        {
            EmissiveCount = 0;
            Renderer.Configure(BlendState.AlphaBlend).FlushShapes(EmissiveTexture, Color.Transparent);
            return;
        }

        // Iterate layers in Z-order (no sorting needed - layers are naturally ordered)
        for (int layer = EmissiveMinLayer; layer <= EmissiveMaxLayer; layer++)
        {
            for (int threadIndex = 0; threadIndex < ThreadCount; threadIndex++)
            {
                int bucketIndex = layer * ThreadCount + threadIndex;
                int shapeCount = EmissiveCounts[bucketIndex];
                var shapeBuffer = EmissiveBuffers[bucketIndex];

                for (int shapeIndex = 0; shapeIndex < shapeCount; shapeIndex++)
                    DrawShape(ref shapeBuffer[shapeIndex]);
            }
        }

        EmissiveCount = Renderer.ShapeBatchCount;
        Renderer.Configure(BlendState.AlphaBlend).FlushShapes(EmissiveTexture, Color.Transparent);
    }

    private void RenderAbsorptionFromCollected()
    {
        Renderer.ClearShapes();

        if (AbsorptionMaxLayer < 0)
        {
            AbsorptionCount = 0;
            Renderer.Configure(BlendState.AlphaBlend).FlushShapes(AbsorptionTexture, Color.Transparent);
            return;
        }

        for (int layer = AbsorptionMinLayer; layer <= AbsorptionMaxLayer; layer++)
        {
            for (int threadIndex = 0; threadIndex < ThreadCount; threadIndex++)
            {
                int bucketIndex = layer * ThreadCount + threadIndex;
                int shapeCount = AbsorptionCounts[bucketIndex];
                var shapeBuffer = AbsorptionBuffers[bucketIndex];

                for (int shapeIndex = 0; shapeIndex < shapeCount; shapeIndex++)
                    DrawShape(ref shapeBuffer[shapeIndex]);
            }
        }

        AbsorptionCount = Renderer.ShapeBatchCount;
        Renderer.Configure(BlendState.AlphaBlend).FlushShapes(AbsorptionTexture, Color.Transparent);
    }

    private void DrawShape(ref Shape shape)
    {
        switch (shape.Type)
        {
            case ShapeType.Circle:
                Renderer.DrawCircle(shape.Position, shape.Radius, shape.Color);
                break;
            case ShapeType.Triangle:
                Renderer.DrawTriangle(shape.Position, shape.Size, shape.Color);
                break;
            case ShapeType.TriangleBorder:
                Renderer.DrawTriangleBorder(shape.Position, shape.Size, shape.Color);
                break;
            default:
                Renderer.DrawRect(shape.Position, shape.Size, shape.Color);
                break;
        }
    }

    private void RenderMotionFromCollected()
    {
        Renderer.ClearShapes();

        for (int threadIndex = 0; threadIndex < ThreadCount; threadIndex++)
        {
            foreach (var (position, size, velocity, isCircle, radius) in MotionShapesByThread[threadIndex])
            {
                float normalizedVelocityX = (velocity.X / 10f) * 0.5f + 0.5f;
                float normalizedVelocityY = (velocity.Y / 10f) * 0.5f + 0.5f;
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
        Gizmos.Set("Geometry", $"Debug: {CurrentDebug} (F2)");
        Gizmos.Set("Geometry", $"Emissive Objects: {EmissiveCount}");
        Gizmos.Set("Geometry", $"Absorption Objects: {AbsorptionCount}");
        Gizmos.Set("Geometry", $"Buffers: {WorldBounds.X}x{WorldBounds.Y}");

        if (EnableSDF)
        {
            Gizmos.Set("Geometry", $"SDF: {SDFBounds.X}x{SDFBounds.Y} ({SDFScale:P0})");
            Gizmos.Set("Geometry", $"JFA Passes: {JFAPassCount}");
        }
        else
        {
            Gizmos.Set("Geometry", "SDF: Disabled");
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
                    .SetParameter("WorldsBounds", WorldBounds)
                    .Draw()
                    .Commit();
                break;
        }
    }

    public override void OnResize()
    {
        Vector2 newSize = Renderer.ScreenSize;
        if (WorldBounds == newSize)
            return;

        EmissiveTexture?.Dispose();
        AbsorptionTexture?.Dispose();
        SDFTexture?.Dispose();
        MotionVectorTexture?.Dispose();
        JFATexture1?.Dispose();
        JFATexture2?.Dispose();
        JFATextureInterior1?.Dispose();
        JFATextureInterior2?.Dispose();

        WorldBounds = newSize;
        SDFBounds = new Vector2(
            (int)(WorldBounds.X * SDFScale),
            (int)(WorldBounds.Y * SDFScale));
        ScreenDiagonal = Renderer.ScreenDiagonal;

        float sdfDiagonal = SDFBounds.Length();
        JFAPassCount = (int)Math.Ceiling(Math.Log(sdfDiagonal, 2));

        InitializeGeometryBuffers();
        JFAResult = JFATexture1;
        JFAResultInterior = JFATextureInterior1;
    }

    public override void Dispose()
    {
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
