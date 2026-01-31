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

    // Parallel shape collection
    private enum ShapeType { Rectangle, Circle, Triangle, TriangleBorder }

    private readonly struct Shape : IComparable<Shape>
    {
        public readonly Vector2 Position;
        public readonly Vector2 Size;
        public readonly float Radius;
        public readonly Color Color;
        public readonly float Z;
        public readonly ShapeType Type;

        public Shape(Vector2 pos, Vector2 size, Color color, float z, ShapeType type = ShapeType.Rectangle)
        {
            Position = pos; Size = size; Radius = 0; Color = color; Z = z; Type = type;
        }
        public Shape(Vector2 pos, float radius, Color color, float z)
        {
            Position = pos; Size = default; Radius = radius; Color = color; Z = z; Type = ShapeType.Circle;
        }

        public int CompareTo(Shape other) => Z.CompareTo(other.Z);
    }
    private List<Shape>[] EmissiveShapesByThread;
    private List<Shape>[] AbsorptionShapesByThread;
    private int[] MergeIndices;
    private int ThreadCount;

    // Min-heap for O(N log K) k-way merge
    private (float z, int thread)[] MergeHeap;
    private int HeapSize;

    // Single-thread buffer for small counts
    private List<Shape> SingleBuffer;
    private const int ParallelThreshold = 16000;

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

        // Initialize thread-local shape lists
        ThreadCount = Environment.ProcessorCount;
        EmissiveShapesByThread = new List<Shape>[ThreadCount];
        AbsorptionShapesByThread = new List<Shape>[ThreadCount];
        MotionShapesByThread = new List<(Vector2, Vector2, Vector2, bool, float)>[ThreadCount];
        MergeIndices = new int[ThreadCount];
        MergeHeap = new (float, int)[ThreadCount];
        SingleBuffer = new List<Shape>();
        
        for (int i = 0; i < ThreadCount; i++)
        {
            EmissiveShapesByThread[i] = new List<Shape>();
            AbsorptionShapesByThread[i] = new List<Shape>();
            MotionShapesByThread[i] = new();
        }

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
        // Clear all thread-local lists once
        for (int i = 0; i < ThreadCount; i++)
        {
            EmissiveShapesByThread[i].Clear();
            AbsorptionShapesByThread[i].Clear();
            MotionShapesByThread[i].Clear();
        }

        // Single query for rectangles - collect emissive, absorption, and motion
        Scene.ECS.Query((int threadIdx, int entity, ref Transform transform, ref Rectangle2D rect, ref Material mat) =>
        {
            Vector2 position = new Vector2(MathF.Round(transform.Position.X), MathF.Round(transform.Position.Y));
            bool inBounds = position.X + rect.Size.X >= 0 && position.X < WorldBounds.X &&
                            position.Y + rect.Size.Y >= 0 && position.Y < WorldBounds.Y;

            if (inBounds)
            {
                EmissiveShapesByThread[threadIdx].Add(new Shape(position, rect.Size, mat.EmissiveScaled, transform.Position.Z));
                AbsorptionShapesByThread[threadIdx].Add(new Shape(position, rect.Size, mat.Absorption, transform.Position.Z));
            }
        });

        // Single query for circles
        Scene.ECS.Query((int threadIdx, int entity, ref Transform transform, ref Circle2D circle, ref Material mat) =>
        {
            Vector2 center = new Vector2(MathF.Round(transform.Position.X), MathF.Round(transform.Position.Y));
            bool inBounds = center.X + circle.Radius >= 0 && center.X - circle.Radius < WorldBounds.X &&
                            center.Y + circle.Radius >= 0 && center.Y - circle.Radius < WorldBounds.Y;

            if (inBounds)
            {
                EmissiveShapesByThread[threadIdx].Add(new Shape(center, circle.Radius, mat.EmissiveScaled, transform.Position.Z));
                AbsorptionShapesByThread[threadIdx].Add(new Shape(center, circle.Radius, mat.Absorption, transform.Position.Z));
            }
        });

        // Single query for triangles (no motion for triangles currently)
        Scene.ECS.Query((int threadIdx, int entity, ref Transform transform, ref Triangle2D tri, ref Material mat) =>
        {
            Vector2 position = new Vector2(MathF.Round(transform.Position.X), MathF.Round(transform.Position.Y));
            bool inBounds = position.X + tri.Size.X >= 0 && position.X < WorldBounds.X &&
                            position.Y + tri.Size.Y >= 0 && position.Y < WorldBounds.Y;

            if (!inBounds) return;

            ShapeType type = tri.Bordered ? ShapeType.TriangleBorder : ShapeType.Triangle;

            EmissiveShapesByThread[threadIdx].Add(new Shape(position, tri.Size, mat.EmissiveScaled, transform.Position.Z, type));

            AbsorptionShapesByThread[threadIdx].Add(new Shape(position, tri.Size, mat.Absorption, transform.Position.Z, type));
        });

        // Motion tracking - only for entities with MotionTrackable component
        Scene.ECS.Query((int threadIdx, int entity, ref Transform transform, ref Rectangle2D rect, ref MotionTrackable motion) =>
        {
            Vector2 position = new Vector2(MathF.Round(transform.Position.X), MathF.Round(transform.Position.Y));
            Vector2 velocity = motion.CalculateVelocity(transform.Position, MotionHistoryFrames);

            if (velocity.LengthSquared() > 0.0001f)
            {
                MotionShapesByThread[threadIdx].Add((position, rect.Size, velocity, false, 0));
            }

            motion.Push(transform.Position);
        });

        Scene.ECS.Query((int threadIdx, int entity, ref Transform transform, ref Circle2D circle, ref MotionTrackable motion) =>
        {
            Vector2 center = new Vector2(MathF.Round(transform.Position.X), MathF.Round(transform.Position.Y));
            Vector2 velocity = motion.CalculateVelocity(transform.Position, MotionHistoryFrames);

            if (velocity.LengthSquared() > 0.0001f)
            {
                MotionShapesByThread[threadIdx].Add((center, Vector2.Zero, velocity, true, circle.Radius));
            }

            motion.Push(transform.Position);
        });

        // Render emissive
        RenderEmissiveFromCollected();

        // Render absorption
        RenderAbsorptionFromCollected();

        // Render motion vectors
        RenderMotionFromCollected();
    }

    private void RenderEmissiveFromCollected()
    {
        Renderer.ClearShapes();

        int total = 0;
        for (int t = 0; t < ThreadCount; t++)
            total += EmissiveShapesByThread[t].Count;

        if (total >= ParallelThreshold)
        {
            Parallel.For(0, ThreadCount, t => EmissiveShapesByThread[t].Sort());
        }

        DrawShapesMerged(EmissiveShapesByThread);
        EmissiveCount = Renderer.ShapeBatchCount;
        Renderer.Configure(BlendState.AlphaBlend).FlushShapes(EmissiveTexture, Color.Transparent);
    }

    private void RenderAbsorptionFromCollected()
    {
        Renderer.ClearShapes();

        int total = 0;
        for (int t = 0; t < ThreadCount; t++)
            total += AbsorptionShapesByThread[t].Count;

        if (total >= ParallelThreshold)
        {
            Parallel.For(0, ThreadCount, t => AbsorptionShapesByThread[t].Sort());
        }

        DrawShapesMerged(AbsorptionShapesByThread);
        AbsorptionCount = Renderer.ShapeBatchCount;
        Renderer.Configure(BlendState.AlphaBlend).FlushShapes(AbsorptionTexture, Color.Transparent);
    }

    private void RenderMotionFromCollected()
    {
        Renderer.ClearShapes();

        for (int t = 0; t < ThreadCount; t++)
        {
            foreach (var (pos, size, velocity, isCircle, radius) in MotionShapesByThread[t])
            {
                float vx = (velocity.X / 10f) * 0.5f + 0.5f;
                float vy = (velocity.Y / 10f) * 0.5f + 0.5f;
                Color motionColor = new Color(vx, vy, 0f, 1f);

                if (isCircle)
                    Renderer.DrawCircle(pos, radius, motionColor);
                else
                    Renderer.DrawRect(pos, size, motionColor);
            }
        }

        var motionClear = new Color(0.5f, 0.5f, 0f, 1f);
        Renderer.Configure(BlendState.Opaque).FlushShapes(MotionVectorTexture, motionClear, "Sharp");
    }

    private void DrawShapesMerged(List<Shape>[] shapesByThread)
    {
        // Count total
        int total = 0;
        for (int t = 0; t < ThreadCount; t++)
            total += shapesByThread[t].Count;

        if (total == 0) return;

        if (total < ParallelThreshold)
        {
            // Small count: single buffer + sort (no parallel overhead)
            SingleBuffer.Clear();
            for (int t = 0; t < ThreadCount; t++)
            {
                var list = shapesByThread[t];
                for (int i = 0; i < list.Count; i++)
                    SingleBuffer.Add(list[i]);
            }
            SingleBuffer.Sort();

            for (int i = 0; i < SingleBuffer.Count; i++)
            {
                var shape = SingleBuffer[i];
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
        }
        else
        {
            // Large count: parallel sort already done, use heap merge
            Array.Clear(MergeIndices, 0, ThreadCount);
            HeapSize = 0;

            // Initialize heap with first element from each non-empty list
            for (int t = 0; t < ThreadCount; t++)
            {
                if (shapesByThread[t].Count > 0)
                    MergeHeap[HeapSize++] = (shapesByThread[t][0].Z, t);
            }

            // Heapify - build min-heap
            for (int i = HeapSize / 2 - 1; i >= 0; i--)
                HeapifyDown(i);

            // K-way merge using min-heap: O(N log K)
            while (HeapSize > 0)
            {
                var (_, bestThread) = MergeHeap[0];
                var shape = shapesByThread[bestThread][MergeIndices[bestThread]++];

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

                if (MergeIndices[bestThread] < shapesByThread[bestThread].Count)
                {
                    MergeHeap[0] = (shapesByThread[bestThread][MergeIndices[bestThread]].Z, bestThread);
                    HeapifyDown(0);
                }
                else
                {
                    MergeHeap[0] = MergeHeap[--HeapSize];
                    if (HeapSize > 0)
                        HeapifyDown(0);
                }
            }
        }
    }

    private void HeapifyDown(int i)
    {
        while (true)
        {
            int left = 2 * i + 1;
            int right = 2 * i + 2;
            int smallest = i;

            if (left < HeapSize && MergeHeap[left].z < MergeHeap[smallest].z)
                smallest = left;
            if (right < HeapSize && MergeHeap[right].z < MergeHeap[smallest].z)
                smallest = right;

            if (smallest == i) break;

            (MergeHeap[i], MergeHeap[smallest]) = (MergeHeap[smallest], MergeHeap[i]);
            i = smallest;
        }
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
