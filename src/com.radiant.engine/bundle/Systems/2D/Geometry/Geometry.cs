using System;
using System.Collections.Concurrent;
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
    public int MotionHistoryFrames = 4;
    private ConcurrentDictionary<int, Queue<Vector3>> PositionHistory = new();
    
    private List<(Vector2 pos, Vector2 size, Vector2 velocity, bool isCircle, float radius)>[] MotionShapesByThread;

    private enum DebugMode { None, Emissive, Absorption, SDF, JFADirection, JFARaw, MotionVectors }
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

    // Single-thread buffer for small counts
    private List<ShapeData> SingleBuffer;
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
        EmissiveShapesByThread = new List<ShapeData>[ThreadCount];
        AbsorptionShapesByThread = new List<ShapeData>[ThreadCount];
        MotionShapesByThread = new List<(Vector2, Vector2, Vector2, bool, float)>[ThreadCount];
        MergeIndices = new int[ThreadCount];
        MergeHeap = new (float, int)[ThreadCount];
        SingleBuffer = new List<ShapeData>();
        for (int i = 0; i < ThreadCount; i++)
        {
            EmissiveShapesByThread[i] = new List<ShapeData>();
            AbsorptionShapesByThread[i] = new List<ShapeData>();
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

        RenderEmissiveTexture();
        RenderAbsorptionTexture();
        RenderMotionVectorTexture();

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
                // Scale emissive color by alpha (intensity)
                float intensity = mat.Emissive.A / 255f;
                Color emissive = new Color(
                    (int)(mat.Emissive.R * intensity),
                    (int)(mat.Emissive.G * intensity),
                    (int)(mat.Emissive.B * intensity),
                    mat.Albedo.A);
                EmissiveShapesByThread[threadIdx].Add(new ShapeData(position, rect.Size, emissive, transform.Position.Z));
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
                // Scale emissive color by alpha (intensity)
                float intensity = mat.Emissive.A / 255f;
                Color emissive = new Color(
                    (int)(mat.Emissive.R * intensity),
                    (int)(mat.Emissive.G * intensity),
                    (int)(mat.Emissive.B * intensity),
                    mat.Emissive.A);
                    
                EmissiveShapesByThread[threadIdx].Add(new ShapeData(center, circle.Radius, emissive, transform.Position.Z));
            }
        });

        // Count total for threshold check
        int total = 0;
        for (int t = 0; t < ThreadCount; t++)
            total += EmissiveShapesByThread[t].Count;

        // Only parallel sort if above threshold (heap merge needs sorted lists)
        if (total >= ParallelThreshold)
        {
            Parallel.For(0, ThreadCount, t =>
            {
                EmissiveShapesByThread[t].Sort();
            });
        }

        // Merge and draw
        DrawShapesMerged(EmissiveShapesByThread);

        EmissiveCount = Renderer.ShapeBatchCount;
        Renderer.Configure(BlendState.AlphaBlend).FlushShapes(EmissiveTexture, Color.Transparent);
    }

    /// <summary>
    /// Converts intuitive material properties to HRC absorption format.
    /// - Emissive objects: absorption = emissive color (required for emission)
    /// - Non-emissive objects: base opacity + color filtering
    /// Alpha controls opacity, color controls tint.
    /// </summary>
    private static Color CalculateAbsorption(ref Material mat)
    {
        bool isEmissive = mat.Emissive.R > 0 || mat.Emissive.G > 0 || mat.Emissive.B > 0;
        float alpha = mat.Albedo.A / 255f;

        if (isEmissive)
        {
            // Lights: absorption = emissive color scaled by intensity
            float intensity = mat.Emissive.A / 255f;
            return new Color(
                (int)(mat.Emissive.R * intensity),
                (int)(mat.Emissive.G * intensity),
                (int)(mat.Emissive.B * intensity),
                mat.Albedo.A);  // Use alpha for proper blending, shaders use RGB
        }
        else
        {
            // Non-emissive: color filtering based on albedo
            // Albedo 255 = 0 absorption (full pass), Albedo 0 = 255 absorption (full block)
            // Cyan (0,255,255) → blocks red, passes green/blue
            return new Color(
                (int)((255 - mat.Albedo.R) * alpha),
                (int)((255 - mat.Albedo.G) * alpha),
                (int)((255 - mat.Albedo.B) * alpha),
                mat.Albedo.A);
        }
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
                Color absorption = CalculateAbsorption(ref mat);
                AbsorptionShapesByThread[threadIdx].Add(new ShapeData(position, rect.Size, absorption, transform.Position.Z));
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
                Color absorption = CalculateAbsorption(ref mat);
                AbsorptionShapesByThread[threadIdx].Add(new ShapeData(center, circle.Radius, absorption, transform.Position.Z));
            }
        });

        // Count total for threshold check
        int total = 0;
        for (int t = 0; t < ThreadCount; t++)
            total += AbsorptionShapesByThread[t].Count;

        // Only parallel sort if above threshold (heap merge needs sorted lists)
        if (total >= ParallelThreshold)
        {
            Parallel.For(0, ThreadCount, t =>
            {
                AbsorptionShapesByThread[t].Sort();
            });
        }

        // Merge and draw
        DrawShapesMerged(AbsorptionShapesByThread);

        AbsorptionCount = Renderer.ShapeBatchCount;
        Renderer.Configure(BlendState.AlphaBlend).FlushShapes(AbsorptionTexture, Color.Transparent);
    }

    private Vector2 CalculateVelocityFromHistory(int entity, Vector3 currentPos)
    {
        if (!PositionHistory.TryGetValue(entity, out var history) || history.Count == 0)
            return Vector2.Zero;

        // Convert queue to array for indexed access (oldest first)
        var positions = history.ToArray();
        int count = positions.Length;

        Vector2 weightedVelocity = Vector2.Zero;
        float totalWeight = 0;

        // Calculate weighted velocity for each frame transition
        // More recent transitions get higher weight (1, 2, 3, ...)
        for (int i = 0; i < count; i++)
        {
            Vector3 from = positions[i];
            Vector3 to = (i < count - 1) ? positions[i + 1] : currentPos;

            float weight = i + 1;
            weightedVelocity.X += (to.X - from.X) * weight;
            weightedVelocity.Y += (to.Y - from.Y) * weight;
            totalWeight += weight;
        }

        return totalWeight > 0 ? weightedVelocity / totalWeight : Vector2.Zero;
    }

    private void UpdatePositionHistory(int entity, Vector3 currentPos)
    {
        if (!PositionHistory.TryGetValue(entity, out var history))
        {
            history = new Queue<Vector3>();
            PositionHistory[entity] = history;
        }

        history.Enqueue(currentPos);

        // Keep only N frames of history
        while (history.Count > MotionHistoryFrames)
            history.Dequeue();
    }

    private void RenderMotionVectorTexture()
    {
        // Clear thread-local lists
        for (int i = 0; i < ThreadCount; i++)
            MotionShapesByThread[i].Clear();

        var activeEntities = new ConcurrentDictionary<int, bool>();

        // Collect motion for rectangles
        Scene.ECS.ForEach<Transform, Rectangle2D, Material>((int threadIdx, int entity, ref Transform transform, ref Rectangle2D rect, ref Material mat) =>
        {
            Vector2 position = new Vector2(MathF.Round(transform.Position.X), MathF.Round(transform.Position.Y));
            activeEntities[entity] = true;

            // Calculate velocity from position history
            Vector2 velocity = CalculateVelocityFromHistory(entity, transform.Position);

            // Only add if there's motion (very low threshold to catch slow movement)
            if (velocity.LengthSquared() > 0.0001f)
            {
                MotionShapesByThread[threadIdx].Add((position, rect.Size, velocity, false, 0));
            }

            // Update history (must be done after velocity calculation)
            UpdatePositionHistory(entity, transform.Position);
        });

        // Collect motion for circles
        Scene.ECS.ForEach<Transform, Circle2D, Material>((int threadIdx, int entity, ref Transform transform, ref Circle2D circle, ref Material mat) =>
        {
            Vector2 center = new Vector2(MathF.Round(transform.Position.X), MathF.Round(transform.Position.Y));
            activeEntities[entity] = true;

            // Calculate velocity from position history
            Vector2 velocity = CalculateVelocityFromHistory(entity, transform.Position);

            // Only add if there's motion (very low threshold to catch slow movement)
            if (velocity.LengthSquared() > 0.0001f)
            {
                MotionShapesByThread[threadIdx].Add((center, Vector2.Zero, velocity, true, circle.Radius));
            }

            // Update history (must be done after velocity calculation)
            UpdatePositionHistory(entity, transform.Position);
        });

        // Clean up history for entities that no longer exist
        var toRemove = new List<int>();
        foreach (var kvp in PositionHistory)
        {
            if (!activeEntities.ContainsKey(kvp.Key))
                toRemove.Add(kvp.Key);
        }
        foreach (var key in toRemove)
            PositionHistory.TryRemove(key, out _);

        // Render motion vectors
        Renderer.ClearShapes();

        for (int t = 0; t < ThreadCount; t++)
        {
            foreach (var (pos, size, velocity, isCircle, radius) in MotionShapesByThread[t])
            {
                // Encode velocity as color: RG = velocity XY, normalized to screen
                // Scale velocity to 0-1 range (max ±10 pixels/frame for better precision at slow speeds)
                float vx = (velocity.X / 10f) * 0.5f + 0.5f;  // -10..+10 -> 0..1
                float vy = (velocity.Y / 10f) * 0.5f + 0.5f;
                Color motionColor = new Color(vx, vy, 0f, 1f);

                if (isCircle)
                    Renderer.DrawCircle(pos, radius, motionColor);
                else
                    Renderer.DrawRect(pos, size, motionColor);
            }
        }

        // Clear with Color(0.5f) which produces byte 127 -> 127/255 when read
        // Shader decodes with 127/255, so they match perfectly
        // Use "Sharp" technique to avoid edge fading which corrupts motion encoding
        var motionClear = new Color(0.5f, 0.5f, 0f, 1f);
        Renderer.Configure(BlendState.Opaque).FlushShapes(MotionVectorTexture, motionClear, "Sharp");
    }

    private void DrawShapesMerged(List<ShapeData>[] shapesByThread)
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
                if (shape.IsCircle)
                    Renderer.DrawCircle(shape.Position, shape.Radius, shape.Color);
                else
                    Renderer.DrawRect(shape.Position, shape.Size, shape.Color);
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

                if (shape.IsCircle)
                    Renderer.DrawCircle(shape.Position, shape.Radius, shape.Color);
                else
                    Renderer.DrawRect(shape.Position, shape.Size, shape.Color);

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
