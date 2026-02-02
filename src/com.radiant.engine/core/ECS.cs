using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using com.radiant.engine.bundle;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.core;

public class ECS : IGameObject
{
    private readonly PagedBitSet ActiveEntities;
    private readonly Stack<int> RecycledIds;
    private readonly List<System> Systems;
    private readonly Dictionary<Type, IComponentSet> ComponentSets;
    private int NextEntityId;
    public int EntityCount => ActiveEntities.Count;
    public Scene Scene { get; set; }
    public Renderer Renderer { get; private set; }
    public SpatialIndex Spatial { get; private set; }

    public ECS(Scene scene, Renderer renderer)
    {
        Scene = scene;
        Renderer = renderer;
        ActiveEntities = new PagedBitSet();
        RecycledIds = new Stack<int>();
        Systems = new List<System>();
        ComponentSets = new Dictionary<Type, IComponentSet>();
        Spatial = new SpatialIndex(this, 64f);
    }

    public void Initialize()
    {
        for (int i = 0; i < Systems.Count; i++)
            if (Systems[i].Enabled)
                Systems[i].Initialize();
    }

    public void Dispose()
    {
        for (int i = 0; i < Systems.Count; i++)
            Systems[i].Dispose();
        GC.SuppressFinalize(this);
    }

    public T AddSystem<T>(bool enabled = true) where T : System, new() => AddSystem(new T(), enabled);

    public T AddSystem<T>(T system, bool enabled = true) where T : System
    {
        system.Scene = Scene;
        system.Renderer = Scene.Renderer;
        system.GameTime = Scene.GameTime;
        system.Enabled = enabled;
        Systems.Add(system);

        return system;
    }

    public T GetSystem<T>() where T : System
    {
        for (int i = 0; i < Systems.Count; i++)
            if (Systems[i] is T typedSystem)
                return typedSystem;
        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SparseSet<T> GetComponentSet<T>() where T : struct, Component
    {
        var type = typeof(T);
        if (!ComponentSets.TryGetValue(type, out var set))
        {
            set = new SparseSet<T>();
            ComponentSets[type] = set;
        }
        return (SparseSet<T>)set;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal IComponentSet GetComponentSetByType(Type type)
    {
        return ComponentSets.TryGetValue(type, out var set) ? set : null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasComponentType(int entity, Type type)
    {
        var set = GetComponentSetByType(type);
        return set != null && set.Contains(entity);
    }

    public int CreateEntity()
    {
        int id = RecycledIds.Count > 0 ? RecycledIds.Pop() : NextEntityId++;
        ActiveEntities.Add(id);
        return id;
    }

    public int CreateEntity(Vector3 position)
    {
        int id = CreateEntity();
        ref var transform = ref AddComponent<Transform>(id);
        transform.Position = position;
        Spatial.Insert(id, position);
        return id;
    }

    public bool DestroyEntity(int entity)
    {
        if (!ActiveEntities.Contains(entity))
            return false;

        Spatial.Remove(entity);

        foreach (var set in ComponentSets.Values)
            set.Remove(entity);

        ActiveEntities.Remove(entity);
        RecycledIds.Push(entity);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetEntityActive(int entity, bool active)
    {
        if (active)
            ActiveEntities.Add(entity);
        else
            ActiveEntities.Remove(entity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsEntityActive(int entity) => ActiveEntities.Contains(entity);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T AddComponent<T>(int entity) where T : struct, Component
    {
        var set = GetComponentSet<T>();
        set.Add(entity, new T());
        return ref set.Get(entity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetComponent<T>(int entity) where T : struct, Component
    {
        return ref GetComponentSet<T>().Get(entity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasComponent<T>(int entity) where T : struct, Component
    {
        return GetComponentSet<T>().Contains(entity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RemoveComponent<T>(int entity) where T : struct, Component
    {
        GetComponentSet<T>().Remove(entity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetPosition(int entity, Vector3 position)
    {
        ref var transform = ref GetComponentSet<Transform>().Get(entity);
        transform.Position = position;
        Spatial.Update(entity, position);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetPosition(int entity, float x, float y, float z)
    {
        ref var transform = ref GetComponentSet<Transform>().Get(entity);
        transform.Position.X = x;
        transform.Position.Y = y;
        transform.Position.Z = z;
        Spatial.Update(entity, new Vector3(x, y, z));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Move(int entity, Vector3 delta)
    {
        ref var transform = ref GetComponentSet<Transform>().Get(entity);
        transform.Position += delta;
        Spatial.Update(entity, transform.Position);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Move(int entity, float dx, float dy, float dz)
    {
        ref var transform = ref GetComponentSet<Transform>().Get(entity);
        transform.Position.X += dx;
        transform.Position.Y += dy;
        transform.Position.Z += dz;
        Spatial.Update(entity, transform.Position);
    }

    public void SyncSpatial() => Spatial.SyncAll();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> InRadius(Vector3 center, float radius) => Spatial.InRadius(center, radius);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> InRadius(float x, float y, float z, float radius) => Spatial.InRadius(x, y, z, radius);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> InBox(Vector3 min, Vector3 max) => Spatial.InBox(min, max);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> Nearest(Vector3 center, int count, float maxRadius = float.MaxValue) => Spatial.Nearest(center, count, maxRadius);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int? AtExact(Vector3 position) => Spatial.AtExact(position);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int? AtExact(float x, float y, float z) => Spatial.AtExact(x, y, z);

    /// <summary>Returns all active entity IDs that have the specified component. For sequential iteration.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerable<int> GetEntities<T>() where T : struct, Component
    {
        var set = GetComponentSet<T>();
        foreach (int entity in set.GetEntityIds())
            if (ActiveEntities.Contains(entity))
                yield return entity;
    }

    /// <summary>Action delegate for parallel ForEach iteration with thread index for ordered collection.</summary>
    public delegate void ForEachAction<T1>(int threadIdx, int entity, ref T1 c1) where T1 : struct;
    public delegate void ForEachAction<T1, T2>(int threadIdx, int entity, ref T1 c1, ref T2 c2) where T1 : struct where T2 : struct;
    public delegate void ForEachAction<T1, T2, T3>(int threadIdx, int entity, ref T1 c1, ref T2 c2, ref T3 c3) where T1 : struct where T2 : struct where T3 : struct;
    public delegate void ForEachAction<T1, T2, T3, T4>(int threadIdx, int entity, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4) where T1 : struct where T2 : struct where T3 : struct where T4 : struct;

    /// <summary>Returns the number of threads used for parallel iteration.</summary>
    public static int ThreadCount => Environment.ProcessorCount;

    /// <summary>Parallel ForEach - divides entities across threads by range. Thread 0: 0-N, Thread 1: N-M, etc.</summary>
    public void ForEach<T1>(ForEachAction<T1> action)
        where T1 : struct, Component
    {
        using var _ = Profiler.Section($"ECS.ForEach<{typeof(T1).Name}>");

        var set1 = GetComponentSet<T1>();
        int count = set1.EntityCount;
        if (count == 0) return;

        int threadCount = Environment.ProcessorCount;
        int chunkSize = (count + threadCount - 1) / threadCount;

        Parallel.For(0, threadCount, threadIdx =>
        {
            int start = threadIdx * chunkSize;
            int end = Math.Min(start + chunkSize, count);

            for (int i = start; i < end; i++)
            {
                int entity = set1.GetEntityAt(i);
                if (!ActiveEntities.Contains(entity)) continue;
                action(threadIdx, entity, ref set1.GetComponentAt(i));
            }
        });
    }

    /// <summary>Parallel ForEach for two components.</summary>
    public void ForEach<T1, T2>(ForEachAction<T1, T2> action)
        where T1 : struct, Component
        where T2 : struct, Component
    {
        using var _ = Profiler.Section($"ECS.ForEach<{typeof(T1).Name},{typeof(T2).Name}>");

        var set1 = GetComponentSet<T1>();
        var set2 = GetComponentSet<T2>();

        bool iterateSet1 = set1.EntityCount <= set2.EntityCount;
        int count = iterateSet1 ? set1.EntityCount : set2.EntityCount;
        if (count == 0) return;

        int threadCount = Environment.ProcessorCount;
        int chunkSize = (count + threadCount - 1) / threadCount;

        Parallel.For(0, threadCount, threadIdx =>
        {
            int start = threadIdx * chunkSize;
            int end = Math.Min(start + chunkSize, count);

            if (iterateSet1)
            {
                for (int i = start; i < end; i++)
                {
                    int entity = set1.GetEntityAt(i);
                    if (!ActiveEntities.Contains(entity)) continue;
                    int idx2 = set2.GetDenseIndex(entity);
                    if (idx2 < 0) continue;
                    action(threadIdx, entity, ref set1.GetComponentAt(i), ref set2.GetComponentAt(idx2));
                }
            }
            else
            {
                for (int i = start; i < end; i++)
                {
                    int entity = set2.GetEntityAt(i);
                    if (!ActiveEntities.Contains(entity)) continue;
                    int idx1 = set1.GetDenseIndex(entity);
                    if (idx1 < 0) continue;
                    action(threadIdx, entity, ref set1.GetComponentAt(idx1), ref set2.GetComponentAt(i));
                }
            }
        });
    }

    /// <summary>Parallel ForEach for three components.</summary>
    public void Query<T1, T2, T3>(ForEachAction<T1, T2, T3> action)
        where T1 : struct, Component
        where T2 : struct, Component
        where T3 : struct, Component
    {
        using var _ = Profiler.Section($"ECS.Query<{typeof(T1).Name},{typeof(T2).Name},{typeof(T3).Name}>");

        var set1 = GetComponentSet<T1>();
        var set2 = GetComponentSet<T2>();
        var set3 = GetComponentSet<T3>();

        int smallestIdx = 1;
        int count = set1.EntityCount;
        if (set2.EntityCount < count) { count = set2.EntityCount; smallestIdx = 2; }
        if (set3.EntityCount < count) { count = set3.EntityCount; smallestIdx = 3; }
        if (count == 0) return;

        int threadCount = Environment.ProcessorCount;
        int chunkSize = (count + threadCount - 1) / threadCount;

        Parallel.For(0, threadCount, threadIdx =>
        {
            int start = threadIdx * chunkSize;
            int end = Math.Min(start + chunkSize, count);

            for (int i = start; i < end; i++)
            {
                int entity;
                int idx1, idx2, idx3;

                if (smallestIdx == 1)
                {
                    entity = set1.GetEntityAt(i);
                    idx1 = i;
                    idx2 = set2.GetDenseIndex(entity); if (idx2 < 0) continue;
                    idx3 = set3.GetDenseIndex(entity); if (idx3 < 0) continue;
                }
                else if (smallestIdx == 2)
                {
                    entity = set2.GetEntityAt(i);
                    idx2 = i;
                    idx1 = set1.GetDenseIndex(entity); if (idx1 < 0) continue;
                    idx3 = set3.GetDenseIndex(entity); if (idx3 < 0) continue;
                }
                else
                {
                    entity = set3.GetEntityAt(i);
                    idx3 = i;
                    idx1 = set1.GetDenseIndex(entity); if (idx1 < 0) continue;
                    idx2 = set2.GetDenseIndex(entity); if (idx2 < 0) continue;
                }

                if (!ActiveEntities.Contains(entity)) continue;
                action(threadIdx, entity, ref set1.GetComponentAt(idx1), ref set2.GetComponentAt(idx2), ref set3.GetComponentAt(idx3));
            }
        });
    }

    /// <summary>Parallel ForEach for four components.</summary>
    public void ForEach<T1, T2, T3, T4>(ForEachAction<T1, T2, T3, T4> action)
        where T1 : struct, Component
        where T2 : struct, Component
        where T3 : struct, Component
        where T4 : struct, Component
    {
        using var _ = Profiler.Section($"ECS.ForEach<{typeof(T1).Name},{typeof(T2).Name},{typeof(T3).Name},{typeof(T4).Name}>");

        var set1 = GetComponentSet<T1>();
        var set2 = GetComponentSet<T2>();
        var set3 = GetComponentSet<T3>();
        var set4 = GetComponentSet<T4>();

        int smallestIdx = 1;
        int count = set1.EntityCount;
        if (set2.EntityCount < count) { count = set2.EntityCount; smallestIdx = 2; }
        if (set3.EntityCount < count) { count = set3.EntityCount; smallestIdx = 3; }
        if (set4.EntityCount < count) { count = set4.EntityCount; smallestIdx = 4; }
        if (count == 0) return;

        int threadCount = Environment.ProcessorCount;
        int chunkSize = (count + threadCount - 1) / threadCount;

        Parallel.For(0, threadCount, threadIdx =>
        {
            int start = threadIdx * chunkSize;
            int end = Math.Min(start + chunkSize, count);

            for (int i = start; i < end; i++)
            {
                int entity;
                int idx1, idx2, idx3, idx4;

                switch (smallestIdx)
                {
                    case 1:
                        entity = set1.GetEntityAt(i);
                        idx1 = i;
                        idx2 = set2.GetDenseIndex(entity); if (idx2 < 0) continue;
                        idx3 = set3.GetDenseIndex(entity); if (idx3 < 0) continue;
                        idx4 = set4.GetDenseIndex(entity); if (idx4 < 0) continue;
                        break;
                    case 2:
                        entity = set2.GetEntityAt(i);
                        idx2 = i;
                        idx1 = set1.GetDenseIndex(entity); if (idx1 < 0) continue;
                        idx3 = set3.GetDenseIndex(entity); if (idx3 < 0) continue;
                        idx4 = set4.GetDenseIndex(entity); if (idx4 < 0) continue;
                        break;
                    case 3:
                        entity = set3.GetEntityAt(i);
                        idx3 = i;
                        idx1 = set1.GetDenseIndex(entity); if (idx1 < 0) continue;
                        idx2 = set2.GetDenseIndex(entity); if (idx2 < 0) continue;
                        idx4 = set4.GetDenseIndex(entity); if (idx4 < 0) continue;
                        break;
                    default:
                        entity = set4.GetEntityAt(i);
                        idx4 = i;
                        idx1 = set1.GetDenseIndex(entity); if (idx1 < 0) continue;
                        idx2 = set2.GetDenseIndex(entity); if (idx2 < 0) continue;
                        idx3 = set3.GetDenseIndex(entity); if (idx3 < 0) continue;
                        break;
                }

                if (!ActiveEntities.Contains(entity)) continue;
                action(threadIdx, entity, ref set1.GetComponentAt(idx1), ref set2.GetComponentAt(idx2), ref set3.GetComponentAt(idx3), ref set4.GetComponentAt(idx4));
            }
        });
    }

    public void Update()
    {
        // Handle resize - each system decides if it needs to rebuild
        if (Renderer.Window.ResizePending)
        {
            Renderer.Window.ClearResizePending();
            Renderer.UpdateScreenInfo();

            for (int i = 0; i < Systems.Count; i++)
                if (Systems[i].Enabled)
                    Systems[i].OnResize();
        }

        for (int i = 0; i < Systems.Count; i++)
        {
            if (!Systems[i].Enabled) continue;
            Systems[i].GameTime = Scene.GameTime;
            using (Profiler.Section($"Update:{Systems[i].GetType().Name}"))
            {
                Systems[i].Update();
            }
        }
    }

    public void FixedUpdate()
    {
        for (int i = 0; i < Systems.Count; i++)
        {
            if (!Systems[i].Enabled) continue;
            Systems[i].GameTime = Scene.GameTime;
            using (Profiler.Section($"FixedUpdate:{Systems[i].GetType().Name}"))
            {
                Systems[i].FixedUpdate();
            }
        }
    }

    public void Render()
    {
        for (int i = 0; i < Systems.Count; i++)
        {
            if (!Systems[i].Enabled) continue;
            Systems[i].GameTime = Scene.GameTime;
            using (Profiler.Section($"Render:{Systems[i].GetType().Name}"))
            {
                Systems[i].Render();
            }
        }
    }

    public void LateRender()
    {
        for (int i = 0; i < Systems.Count; i++)
        {
            if (!Systems[i].Enabled) continue;
            Systems[i].GameTime = Scene.GameTime;
            using (Profiler.Section($"LateRender:{Systems[i].GetType().Name}"))
            {
                Systems[i].LateRender();
            }
        }
    }
}