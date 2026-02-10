using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using com.radiant.engine.bundle;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.core;

public class ECS : IGameObject
{
    // Entity tracking
    private readonly Stack<int> RecycledIds;
    private int NextEntityId;
    private int EntityCount_;

    // Archetype storage - components live here
    private readonly List<Archetype> Archetypes;
    private readonly Dictionary<ulong, Archetype> ArchetypesBySignature;
    private readonly Dictionary<Type, int> TypeToId;
    private int NextTypeId;

    // Entity → location mapping
    private EntityRecord[] EntityRecords;
    private const int InitialEntityCapacity = 1024;

    private struct EntityRecord
    {
        public Archetype Arch;
        public int Index;
    }

    // Systems
    private readonly List<System> Systems;
    private readonly Dictionary<Type, System> SystemCache;

    // Thread pool
    private static readonly int CachedThreadCount = Environment.ProcessorCount;

    public int EntityCount => EntityCount_;
    public bool Paused { get; set; }
    public Scene Scene { get; set; }
    public Renderer Renderer { get; private set; }
    public SpatialIndex Spatial { get; private set; }

    // Job system
    private readonly Thread[] Workers;
    private readonly ManualResetEventSlim[] JobReadyEvents;
    private readonly ManualResetEventSlim[] JobDoneEvents;
    private volatile bool ShuttingDown;
    private struct JobData { public int ThreadIdx, Start, End; }
    private readonly JobData[] JobDataArray;
    private Action<int, int, int> CurrentWork;

    public ECS(Scene scene, Renderer renderer)
    {
        Scene = scene;
        Renderer = renderer;
        RecycledIds = new Stack<int>();
        Systems = new List<System>();
        SystemCache = new Dictionary<Type, System>();
        Archetypes = new List<Archetype>();
        ArchetypesBySignature = new Dictionary<ulong, Archetype>();
        TypeToId = new Dictionary<Type, int>();
        EntityRecords = new EntityRecord[InitialEntityCapacity];
        Spatial = new SpatialIndex(this, 64f);

        Workers = new Thread[CachedThreadCount];
        JobDataArray = new JobData[CachedThreadCount];
        JobReadyEvents = new ManualResetEventSlim[CachedThreadCount];
        JobDoneEvents = new ManualResetEventSlim[CachedThreadCount];

        InitializeJobSystem();
    }

    public void Initialize()
    {
        SortSystemsByDependencies();
        
        for (int i = 0; i < Systems.Count; i++)
            Systems[i].Initialize();
    }

    private void SortSystemsByDependencies()
    {
        if (Systems.Count <= 1) return;
        var typeToIndex = new Dictionary<Type, int>();
        for (int i = 0; i < Systems.Count; i++)
            typeToIndex[Systems[i].GetType()] = i;

        var outgoing = new List<int>[Systems.Count];
        var inDegree = new int[Systems.Count];
        for (int i = 0; i < Systems.Count; i++)
            outgoing[i] = new List<int>();

        for (int i = 0; i < Systems.Count; i++)
        {
            var type = Systems[i].GetType();
            foreach (var attr in type.GetCustomAttributes(typeof(RunAfterAttribute), true))
            {
                var runAfter = (RunAfterAttribute)attr;
                if (typeToIndex.TryGetValue(runAfter.SystemType, out int beforeIdx))
                {
                    outgoing[beforeIdx].Add(i);
                    inDegree[i]++;
                }
            }
            foreach (var attr in type.GetCustomAttributes(typeof(RunBeforeAttribute), true))
            {
                var runBefore = (RunBeforeAttribute)attr;
                if (typeToIndex.TryGetValue(runBefore.SystemType, out int afterIdx))
                {
                    outgoing[i].Add(afterIdx);
                    inDegree[afterIdx]++;
                }
            }
        }

        var queue = new Queue<int>();
        for (int i = 0; i < Systems.Count; i++)
            if (inDegree[i] == 0)
                queue.Enqueue(i);

        var sorted = new List<System>(Systems.Count);
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            sorted.Add(Systems[current]);
            foreach (int next in outgoing[current])
                if (--inDegree[next] == 0)
                    queue.Enqueue(next);
        }

        if (sorted.Count == Systems.Count)
        {
            Systems.Clear();
            Systems.AddRange(sorted);
        }
    }

    public void Dispose()
    {
        ShutdownJobSystem();
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
        system.IsPausable = Attribute.IsDefined(typeof(T), typeof(PausableAttribute));
        Systems.Add(system);
        SystemCache[typeof(T)] = system;
        return system;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetSystem<T>() where T : System
    {
        return SystemCache.TryGetValue(typeof(T), out var system) ? (T)system : null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetTypeId(Type type)
    {
        if (!TypeToId.TryGetValue(type, out int id))
        {
            id = NextTypeId++;
            TypeToId[type] = id;
        }
        return id;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ulong GetSignature(params Type[] types)
    {
        ulong sig = 0;
        foreach (var t in types)
            sig |= 1UL << GetTypeId(t);
        return sig;
    }

    private Archetype GetOrCreateArchetype(ulong signature, Type[] types)
    {
        if (!ArchetypesBySignature.TryGetValue(signature, out var arch))
        {
            arch = new Archetype(types, signature);
            Archetypes.Add(arch);
            ArchetypesBySignature[signature] = arch;
        }
        return arch;
    }

    private void EnsureEntityCapacity(int entityId)
    {
        if (entityId >= EntityRecords.Length)
        {
            int newSize = EntityRecords.Length;
            while (newSize <= entityId) newSize *= 2;
            Array.Resize(ref EntityRecords, newSize);
        }
    }

    public int CreateEntity()
    {
        int id = RecycledIds.Count > 0 ? RecycledIds.Pop() : NextEntityId++;
        EnsureEntityCapacity(id);
        EntityRecords[id] = default;
        EntityCount_++;
        return id;
    }

    public int CreateEntity(Vector3 position)
    {
        int id = CreateEntity();
        AddComponent<Transform>(id);
        ref var transform = ref GetComponent<Transform>(id);
        transform.Position = position;
        Spatial.Insert(id, position);
        return id;
    }

    public bool DestroyEntity(int entity)
    {
        ref var record = ref EntityRecords[entity];
        if (record.Arch == null) return false;

        Spatial.Remove(entity);

        int movedEntity = record.Arch.Remove(record.Index);
        if (movedEntity >= 0)
            EntityRecords[movedEntity].Index = record.Index;

        record.Arch = null;
        record.Index = -1;
        RecycledIds.Push(entity);
        EntityCount_--;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T AddComponent<T>(int entity) where T : struct, Component
    {
        ref var record = ref EntityRecords[entity];
        var oldArch = record.Arch;
        var type = typeof(T);

        // Calculate new signature
        ulong newSig = oldArch != null ? oldArch.Signature | (1UL << GetTypeId(type)) : (1UL << GetTypeId(type));

        if (oldArch != null && oldArch.Signature == newSig)
            return ref oldArch.Get<T>(record.Index);

        // Build new type array - signature check above guarantees type not in oldArch
        var types = new List<Type>();
        if (oldArch != null)
            types.AddRange(oldArch.Types);
        types.Add(type);

        var newArch = GetOrCreateArchetype(newSig, types.ToArray());
        int newIndex = newArch.Add(entity);

        if (oldArch != null)
        {
            newArch.CopyComponentsFrom(oldArch, record.Index, newIndex);
            int movedEntity = oldArch.Remove(record.Index);
            if (movedEntity >= 0)
                EntityRecords[movedEntity].Index = record.Index;
        }

        record.Arch = newArch;
        record.Index = newIndex;

        return ref newArch.Get<T>(newIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetComponent<T>(int entity) where T : struct, Component
    {
        ref var record = ref EntityRecords[entity];
        return ref record.Arch.Get<T>(record.Index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasComponent<T>(int entity) where T : struct, Component
    {
        ref var record = ref EntityRecords[entity];
        return record.Arch != null && record.Arch.HasComponent<T>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetPosition(int entity, Vector3 position)
    {
        GetComponent<Transform>(entity).Position = position;
        Spatial.Update(entity, position);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> InRadius(Vector3 center, float radius) => Spatial.InRadius(center, radius);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> InRadius(float x, float y, float z, float radius) => Spatial.InRadius(x, y, z, radius);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> InBox(Vector3 min, Vector3 max) => Spatial.InBox(min, max);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int? AtExact(Vector3 position) => Spatial.AtExact(position);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int? AtExact(float x, float y, float z) => Spatial.AtExact(x, y, z);


    public static int ThreadCount => CachedThreadCount;

    // Job system
    private void InitializeJobSystem()
    {
        for (int i = 0; i < CachedThreadCount; i++)
        {
            int idx = i;
            JobReadyEvents[i] = new ManualResetEventSlim(false);
            JobDoneEvents[i] = new ManualResetEventSlim(true);
            Workers[i] = new Thread(() => WorkerLoop(idx)) { IsBackground = true, Name = $"ECS Worker {idx}" };
            Workers[i].Start();
        }
    }

    private void WorkerLoop(int idx)
    {
        while (!ShuttingDown)
        {
            JobReadyEvents[idx].Wait();
            if (ShuttingDown) return;
            JobReadyEvents[idx].Reset();
            ref var data = ref JobDataArray[idx];
            CurrentWork?.Invoke(data.ThreadIdx, data.Start, data.End);
            JobDoneEvents[idx].Set();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RunParallel(int count, Action<int, int, int> work)
    {
        if (count == 0) return;
        CurrentWork = work;
        int chunkSize = (count + CachedThreadCount - 1) / CachedThreadCount;

        for (int i = 0; i < CachedThreadCount; i++)
        {
            int start = i * chunkSize;
            int end = Math.Min(start + chunkSize, count);
            if (start >= count) { JobDoneEvents[i].Set(); continue; }
            JobDataArray[i] = new JobData { ThreadIdx = i, Start = start, End = end };
            JobDoneEvents[i].Reset();
            JobReadyEvents[i].Set();
        }

        for (int i = 0; i < CachedThreadCount; i++)
            JobDoneEvents[i].Wait();
    }

    private void ShutdownJobSystem()
    {
        ShuttingDown = true;
        for (int i = 0; i < CachedThreadCount; i++)
        {
            JobReadyEvents[i]?.Set();
            Workers[i]?.Join(100);
            JobReadyEvents[i]?.Dispose();
            JobDoneEvents[i]?.Dispose();
        }
    }

    // Query delegates
    public delegate void ForEachAction<T1>(int threadIndex, int entity, ref T1 c1) where T1 : struct;
    public delegate void ForEachAction<T1, T2>(int threadIndex, int entity, ref T1 c1, ref T2 c2) where T1 : struct where T2 : struct;
    public delegate void ForEachAction<T1, T2, T3>(int threadIndex, int entity, ref T1 c1, ref T2 c2, ref T3 c3) where T1 : struct where T2 : struct where T3 : struct;

    public void ForEach<T1>(ForEachAction<T1> action) where T1 : struct, Component
    {
        ulong sig = 1UL << GetTypeId(typeof(T1));

        QueryMatchCache.Clear();
        int totalCount = 0;
        foreach (var arch in Archetypes)
        {
            if ((arch.Signature & sig) != sig) continue;
            if (arch.EntityCount == 0) continue;
            QueryMatchCache.Add(arch);
            totalCount += arch.EntityCount;
        }

        if (totalCount == 0) return;

        if (QueryMatchCache.Count == 1)
        {
            var arch = QueryMatchCache[0];
            var entities = arch.GetEntities();
            var data1 = arch.GetArray<T1>();
            RunParallel(arch.EntityCount, (threadIdx, start, end) =>
            {
                for (int i = start; i < end; i++)
                    action(threadIdx, entities[i], ref data1[i]);
            });
            return;
        }

        RunParallel(totalCount, (threadIdx, start, end) =>
        {
            int globalIdx = 0;
            foreach (var arch in QueryMatchCache)
            {
                int archCount = arch.EntityCount;
                int archEnd = globalIdx + archCount;
                if (start >= archEnd) { globalIdx = archEnd; continue; }
                if (end <= globalIdx) break;

                int localStart = Math.Max(0, start - globalIdx);
                int localEnd = Math.Min(archCount, end - globalIdx);
                var entities = arch.GetEntities();
                var data1 = arch.GetArray<T1>();

                for (int i = localStart; i < localEnd; i++)
                    action(threadIdx, entities[i], ref data1[i]);

                globalIdx = archEnd;
            }
        });
    }

    public void ForEach<T1, T2>(ForEachAction<T1, T2> action)
        where T1 : struct, Component
        where T2 : struct, Component
    {
        ulong sig = (1UL << GetTypeId(typeof(T1))) | (1UL << GetTypeId(typeof(T2)));

        QueryMatchCache.Clear();
        int totalCount = 0;
        foreach (var arch in Archetypes)
        {
            if ((arch.Signature & sig) != sig) continue;
            if (arch.EntityCount == 0) continue;
            QueryMatchCache.Add(arch);
            totalCount += arch.EntityCount;
        }

        if (totalCount == 0) return;

        if (QueryMatchCache.Count == 1)
        {
            var arch = QueryMatchCache[0];
            var entities = arch.GetEntities();
            var data1 = arch.GetArray<T1>();
            var data2 = arch.GetArray<T2>();
            RunParallel(arch.EntityCount, (threadIdx, start, end) =>
            {
                for (int i = start; i < end; i++)
                    action(threadIdx, entities[i], ref data1[i], ref data2[i]);
            });
            return;
        }

        RunParallel(totalCount, (threadIdx, start, end) =>
        {
            int globalIdx = 0;
            foreach (var arch in QueryMatchCache)
            {
                int archCount = arch.EntityCount;
                int archEnd = globalIdx + archCount;
                if (start >= archEnd) { globalIdx = archEnd; continue; }
                if (end <= globalIdx) break;

                int localStart = Math.Max(0, start - globalIdx);
                int localEnd = Math.Min(archCount, end - globalIdx);
                var entities = arch.GetEntities();
                var data1 = arch.GetArray<T1>();
                var data2 = arch.GetArray<T2>();

                for (int i = localStart; i < localEnd; i++)
                    action(threadIdx, entities[i], ref data1[i], ref data2[i]);

                globalIdx = archEnd;
            }
        });
    }

    // Cached archetype query results
    private readonly List<Archetype> QueryMatchCache = new();

    public void Query<T1, T2, T3>(ForEachAction<T1, T2, T3> action)
        where T1 : struct, Component
        where T2 : struct, Component
        where T3 : struct, Component
    {
        ulong sig = (1UL << GetTypeId(typeof(T1))) | (1UL << GetTypeId(typeof(T2))) | (1UL << GetTypeId(typeof(T3)));

        // Find all matching archetypes and total count
        QueryMatchCache.Clear();
        int totalCount = 0;
        foreach (var arch in Archetypes)
        {
            if ((arch.Signature & sig) != sig) continue;
            if (arch.EntityCount == 0) continue;
            QueryMatchCache.Add(arch);
            totalCount += arch.EntityCount;
        }

        if (totalCount == 0) return;

        // Single archetype - fast path
        if (QueryMatchCache.Count == 1)
        {
            var arch = QueryMatchCache[0];
            var entities = arch.GetEntities();
            var data1 = arch.GetArray<T1>();
            var data2 = arch.GetArray<T2>();
            var data3 = arch.GetArray<T3>();
            int count = arch.EntityCount;

            RunParallel(count, (threadIdx, start, end) =>
            {
                for (int i = start; i < end; i++)
                    action(threadIdx, entities[i], ref data1[i], ref data2[i], ref data3[i]);
            });
            return;
        }

        // Multiple archetypes - iterate with global index
        RunParallel(totalCount, (threadIdx, start, end) =>
        {
            int globalIdx = 0;
            foreach (var arch in QueryMatchCache)
            {
                int archCount = arch.EntityCount;
                int archEnd = globalIdx + archCount;

                if (start >= archEnd) { globalIdx = archEnd; continue; }
                if (end <= globalIdx) break;

                int localStart = Math.Max(0, start - globalIdx);
                int localEnd = Math.Min(archCount, end - globalIdx);

                var entities = arch.GetEntities();
                var data1 = arch.GetArray<T1>();
                var data2 = arch.GetArray<T2>();
                var data3 = arch.GetArray<T3>();

                for (int i = localStart; i < localEnd; i++)
                    action(threadIdx, entities[i], ref data1[i], ref data2[i], ref data3[i]);

                globalIdx = archEnd;
            }
        });
    }

    public void Update()
    {
        if (Renderer.HasPendingResize)
        {
            Renderer.HandleResize();
            for (int i = 0; i < Systems.Count; i++)
                if (Systems[i].Enabled)
                    Systems[i].OnResize();
        }

        for (int i = 0; i < Systems.Count; i++)
        {
            if (!Systems[i].Enabled) continue;
            if (Paused && Systems[i].IsPausable) continue;
            Systems[i].GameTime = Scene.GameTime;
            Systems[i].Update();
        }
    }

    public void FixedUpdate()
    {
        for (int i = 0; i < Systems.Count; i++)
        {
            if (!Systems[i].Enabled) continue;
            if (Paused && Systems[i].IsPausable) continue;
            Systems[i].GameTime = Scene.GameTime;
            Systems[i].FixedUpdate();
        }
    }

    public void Render()
    {
        for (int i = 0; i < Systems.Count; i++)
        {
            if (!Systems[i].Enabled) continue;
            Systems[i].GameTime = Scene.GameTime;
            Systems[i].Render();
        }
    }

    public void LateRender()
    {
        for (int i = 0; i < Systems.Count; i++)
        {
            if (!Systems[i].Enabled) continue;
            Systems[i].GameTime = Scene.GameTime;
            Systems[i].LateRender();
        }
    }
}
