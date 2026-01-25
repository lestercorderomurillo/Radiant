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

    private int EmissiveCount;
    private int AbsorptionCount;

    private enum DebugMode { None, Emissive, Absorption, SDF, JFADirection, JFARaw }
    private DebugMode CurrentDebug = DebugMode.None;
    private KeyboardState PrevKeyState;
    private GizmosRenderer Gizmos;

    // Parallel shape collection
    private readonly struct ShapeData : IComparable<ShapeData>
    {
        public readonly Vector2 Position;
        public readonly Vector2 Size;
        public readonly float Radius;
        public readonly Color Color;
        public readonly float Z;
        public readonly bool IsCircle;

        public ShapeData(Vector2 pos, Vector2 size, Color color, float z)
        {
            Position = pos; Size = size; Radius = 0; Color = color; Z = z; IsCircle = false;
        }
        public ShapeData(Vector2 pos, float radius, Color color, float z)
        {
            Position = pos; Size = default; Radius = radius; Color = color; Z = z; IsCircle = true;
        }

        public int CompareTo(ShapeData other) => Z.CompareTo(other.Z);
    }
    private List<ShapeData>[] EmissiveShapesByThread;
    private List<ShapeData>[] AbsorptionShapesByThread;
    private int[] MergeIndices;
    private int ThreadCount;

    // Min-heap for O(N log K) k-way merge
    private (float z, int thread)[] MergeHeap;
    private int HeapSize;

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
        EmissiveShapesByThread = new List<ShapeData>[ThreadCount];
        AbsorptionShapesByThread = new List<ShapeData>[ThreadCount];
        MergeIndices = new int[ThreadCount];
        MergeHeap = new (float, int)[ThreadCount];
        for (int i = 0; i < ThreadCount; i++)
        {
            EmissiveShapesByThread[i] = new List<ShapeData>();
            AbsorptionShapesByThread[i] = new List<ShapeData>();
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
            .SetParameter("EmissiveTexture", EmissiveTexture)
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
            .SetParameter("EmissiveTexture", EmissiveTexture)
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

        RenderEmissiveTexture();
        RenderAbsorptionTexture();

        bool needsSDF = EnableSDF ||
            CurrentDebug == DebugMode.SDF ||
            CurrentDebug == DebugMode.JFADirection ||
            CurrentDebug == DebugMode.JFARaw;

        if (needsSDF)
            RenderSDFTexture();

        UpdateGizmos();
    }

    private void RenderEmissiveTexture()
    {
        Renderer.ClearShapes();

        // Clear thread-local lists
        for (int i = 0; i < ThreadCount; i++)
            EmissiveShapesByThread[i].Clear();

        // Parallel visibility filter for rectangles
        Scene.ECS.ForEach<Transform, Rectangle2D, Material>((int threadIdx, int entity, ref Transform transform, ref Rectangle2D rect, ref Material mat) =>
        {
            if (mat.Emissive.A == 0) return;

            Vector2 position = new Vector2(MathF.Round(transform.Position.X), MathF.Round(transform.Position.Y));

            if (position.X + rect.Size.X >= 0 && position.X < WorldBounds.X &&
                position.Y + rect.Size.Y >= 0 && position.Y < WorldBounds.Y)
            {
                EmissiveShapesByThread[threadIdx].Add(new ShapeData(position, rect.Size, mat.Emissive, transform.Position.Z));
            }
        });

        // Parallel visibility filter for circles
        Scene.ECS.ForEach<Transform, Circle2D, Material>((int threadIdx, int entity, ref Transform transform, ref Circle2D circle, ref Material mat) =>
        {
            if (mat.Emissive.A == 0) return;

            Vector2 center = new Vector2(MathF.Round(transform.Position.X), MathF.Round(transform.Position.Y));

            if (center.X + circle.Radius >= 0 && center.X - circle.Radius < WorldBounds.X &&
                center.Y + circle.Radius >= 0 && center.Y - circle.Radius < WorldBounds.Y)
            {
                EmissiveShapesByThread[threadIdx].Add(new ShapeData(center, circle.Radius, mat.Emissive, transform.Position.Z));
            }
        });

        // Parallel sort each thread's list
        Parallel.For(0, ThreadCount, t =>
        {
            EmissiveShapesByThread[t].Sort();
        });

        // K-way merge draw
        DrawShapesMerged(EmissiveShapesByThread);

        EmissiveCount = Renderer.ShapeBatchCount;
        Renderer.Configure(BlendState.AlphaBlend).FlushShapes(EmissiveTexture, Color.Transparent);
    }

    private void RenderAbsorptionTexture()
    {
        Renderer.ClearShapes();

        // Clear thread-local lists
        for (int i = 0; i < ThreadCount; i++)
            AbsorptionShapesByThread[i].Clear();

        // Parallel visibility filter for rectangles
        Scene.ECS.ForEach<Transform, Rectangle2D, Material>((int threadIdx, int entity, ref Transform transform, ref Rectangle2D rect, ref Material mat) =>
        {
            if (mat.Albedo.A == 0) return;

            Vector2 position = new Vector2(MathF.Round(transform.Position.X), MathF.Round(transform.Position.Y));

            if (position.X + rect.Size.X >= 0 && position.X < WorldBounds.X &&
                position.Y + rect.Size.Y >= 0 && position.Y < WorldBounds.Y)
            {
                AbsorptionShapesByThread[threadIdx].Add(new ShapeData(position, rect.Size, mat.Albedo, transform.Position.Z));
            }
        });

        // Parallel visibility filter for circles
        Scene.ECS.ForEach<Transform, Circle2D, Material>((int threadIdx, int entity, ref Transform transform, ref Circle2D circle, ref Material mat) =>
        {
            if (mat.Albedo.A == 0) return;

            Vector2 center = new Vector2(MathF.Round(transform.Position.X), MathF.Round(transform.Position.Y));

            if (center.X + circle.Radius >= 0 && center.X - circle.Radius < WorldBounds.X &&
                center.Y + circle.Radius >= 0 && center.Y - circle.Radius < WorldBounds.Y)
            {
                AbsorptionShapesByThread[threadIdx].Add(new ShapeData(center, circle.Radius, mat.Albedo, transform.Position.Z));
            }
        });

        // Parallel sort each thread's list
        Parallel.For(0, ThreadCount, t =>
        {
            AbsorptionShapesByThread[t].Sort();
        });

        // K-way merge draw
        DrawShapesMerged(AbsorptionShapesByThread);

        AbsorptionCount = Renderer.ShapeBatchCount;
        Renderer.Configure(BlendState.AlphaBlend).FlushShapes(AbsorptionTexture, Color.Transparent);
    }

    private void DrawShapesMerged(List<ShapeData>[] shapesByThread)
    {
        // Reset merge indices and build min-heap
        Array.Clear(MergeIndices, 0, ThreadCount);
        HeapSize = 0;

        // Initialize heap with first element from each non-empty list
        for (int t = 0; t < ThreadCount; t++)
        {
            if (shapesByThread[t].Count > 0)
            {
                MergeHeap[HeapSize++] = (shapesByThread[t][0].Z, t);
            }
        }

        // Heapify - build min-heap
        for (int i = HeapSize / 2 - 1; i >= 0; i--)
            HeapifyDown(i);

        // K-way merge using min-heap: O(N log K)
        while (HeapSize > 0)
        {
            // Extract min
            var (_, bestThread) = MergeHeap[0];
            var shape = shapesByThread[bestThread][MergeIndices[bestThread]++];

            if (shape.IsCircle)
                Renderer.DrawCircle(shape.Position, shape.Radius, shape.Color);
            else
                Renderer.DrawRect(shape.Position, shape.Size, shape.Color);

            // Replace root with next from same thread, or remove if exhausted
            if (MergeIndices[bestThread] < shapesByThread[bestThread].Count)
            {
                MergeHeap[0] = (shapesByThread[bestThread][MergeIndices[bestThread]].Z, bestThread);
                HeapifyDown(0);
            }
            else
            {
                // Remove root by replacing with last element
                MergeHeap[0] = MergeHeap[--HeapSize];
                if (HeapSize > 0)
                    HeapifyDown(0);
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
            .SetParameter("EmissiveTexture", EmissiveTexture)
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
                    .SetParameter("EmissiveTexture", EmissiveTexture)
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
                    .SetParameter("EmissiveTexture", EmissiveTexture)
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
        JFATexture1?.Dispose();
        JFATexture2?.Dispose();
        JFATextureInterior1?.Dispose();
        JFATextureInterior2?.Dispose();
    }
}
