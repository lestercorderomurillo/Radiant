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
    private int EntityCountValue;

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

    // Tags — lightweight string-based entity grouping
    private readonly Dictionary<string, PagedBitSet> TagSets = new();

    // Disabled entities — skipped by Query, still alive and restorable
    private readonly PagedBitSet DisabledEntities = new();

    // Deferred destruction — queued during frame, flushed at start of next Update
    private readonly HashSet<int> DeferredDestroyQueue = new();

    // Systems
    private readonly List<System> Systems;
    private List<System> RenderSystems;
    private readonly Dictionary<Type, System> SystemCache;
    private readonly List<SystemGroup> SystemGroups = new();
    private readonly Dictionary<System, SystemGroup> SystemToGroup = new();

    // Thread pool
    private static readonly int CachedThreadCount = Environment.ProcessorCount;

    public int EntityCount => EntityCountValue;
    public bool GameplayPaused { get; set; }
    public bool AnimationPaused { get; set; }
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
        BuildRenderOrder();

        for (int i = 0; i < Systems.Count; i++)
            if (Systems[i].Enabled)
                Systems[i].Initialize();
    }

    private void BuildRenderOrder()
    {
        RenderSystems = new List<System>(Systems);
        var systemIndex = new Dictionary<System, int>(Systems.Count);
        for (int i = 0; i < Systems.Count; i++)
            systemIndex[Systems[i]] = i;

        RenderSystems.Sort((a, b) =>
        {
            int layerCmp = a.RenderLayer.CompareTo(b.RenderLayer);
            if (layerCmp != 0) return layerCmp;
            return systemIndex[a].CompareTo(systemIndex[b]);
        });
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
                foreach (var dep in runAfter.SystemTypes)
                    if (typeToIndex.TryGetValue(dep, out int beforeIdx))
                    {
                        outgoing[beforeIdx].Add(i);
                        inDegree[i]++;
                    }
            }
            foreach (var attr in type.GetCustomAttributes(typeof(RunBeforeAttribute), true))
            {
                var runBefore = (RunBeforeAttribute)attr;
                foreach (var dep in runBefore.SystemTypes)
                    if (typeToIndex.TryGetValue(dep, out int afterIdx))
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
        var pauseAttr = (PausableAttribute)Attribute.GetCustomAttribute(typeof(T), typeof(PausableAttribute));
        system.PauseGroups = pauseAttr?.Groups ?? PauseGroup.None;
        Systems.Add(system);
        SystemCache[typeof(T)] = system;
        return system;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetSystem<T>() where T : System
    {
        return SystemCache.TryGetValue(typeof(T), out var system) ? (T)system : null;
    }

    public IReadOnlyList<System> GetAllSystems() => Systems;

    public void RegisterSystemGroup(SystemGroup group)
    {
        SystemGroups.Add(group);
        group.ForEach(system => SystemToGroup[system] = group);
    }

    public SystemGroup GetSystemGroup(System system) =>
        SystemToGroup.TryGetValue(system, out var group) ? group : null;

    public string GetGroupName(System system) =>
        SystemToGroup.TryGetValue(system, out var group) ? group.Name : null;

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
        foreach (var type in types)
            sig |= 1UL << GetTypeId(type);
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
        EntityCountValue++;
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

    /// <summary> Returns true if the entity ID refers to a living entity. </summary>
    public bool IsAlive(int entity) => entity >= 0 && entity < EntityRecords.Length && EntityRecords[entity].Arch != null;

    /// <summary> Schedules an entity for destruction at the start of the next Update. Safe to call mid-frame. </summary>
    public void ScheduleDestroy(int entity) => DeferredDestroyQueue.Add(entity);

    /// <summary> Processes all deferred entity destructions. Called automatically at the start of Update. </summary>
    public void FlushDeferred()
    {
        if (DeferredDestroyQueue.Count == 0) return;
        foreach (int entity in DeferredDestroyQueue)
            DestroyEntity(entity);
        DeferredDestroyQueue.Clear();
    }

    /// <summary> Collects all living entity IDs into the provided list (cleared first). </summary>
    public void GetAllEntityIds(List<int> result)
    {
        result.Clear();
        foreach (var arch in Archetypes)
        {
            var entities = arch.GetEntities();
            for (int i = 0; i < arch.EntityCount; i++)
                result.Add(entities[i]);
        }
    }

    public void DestroyAllEntities()
    {
        foreach (var arch in Archetypes)
        {
            var entities = arch.GetEntities();
            for (int i = arch.EntityCount - 1; i >= 0; i--)
            {
                int entity = entities[i];
                Spatial.Remove(entity);
                arch.Remove(i);
                EntityRecords[entity] = default;
                RecycledIds.Push(entity);
                EntityCountValue--;
            }
        }

        foreach (var bitset in TagSets.Values)
            bitset.Clear();
    }

    public bool DestroyEntity(int entity)
    {
        ref var record = ref EntityRecords[entity];
        if (record.Arch == null) return false;

        Spatial.Remove(entity);

        foreach (var bitset in TagSets.Values)
            bitset.Remove(entity);
        DisabledEntities.Remove(entity);

        int movedEntity = record.Arch.Remove(record.Index);
        if (movedEntity >= 0)
            EntityRecords[movedEntity].Index = record.Index;

        record.Arch = null;
        record.Index = -1;
        RecycledIds.Push(entity);
        EntityCountValue--;
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

        // Only build type array if archetype doesn't exist yet (avoids allocation in common case)
        if (!ArchetypesBySignature.TryGetValue(newSig, out var newArch))
        {
            var types = new List<Type>();
            if (oldArch != null)
                types.AddRange(oldArch.Types);
            types.Add(type);
            newArch = new Archetype(types.ToArray(), newSig);
            Archetypes.Add(newArch);
            ArchetypesBySignature[newSig] = newArch;
        }
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

    public Type[] GetComponentTypes(int entity)
    {
        ref var record = ref EntityRecords[entity];
        return record.Arch?.Types ?? Array.Empty<Type>();
    }

    public IReadOnlyCollection<Type> GetRegisteredComponentTypes() => TypeToId.Keys;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> Nearest(Vector3 center, int count, float maxRadius = float.MaxValue) => Spatial.Nearest(center, count, maxRadius);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> Nearest(float cx, float cy, float cz, int count, float maxRadius = float.MaxValue) => Spatial.Nearest(cx, cy, cz, count, maxRadius);

    public void AddTag(int entity, string tag)
    {
        if (!TagSets.TryGetValue(tag, out var bitset))
        {
            bitset = new PagedBitSet();
            TagSets[tag] = bitset;
        }
        bitset.Add(entity);
    }

    public void RemoveTag(int entity, string tag)
    {
        if (TagSets.TryGetValue(tag, out var bitset))
            bitset.Remove(entity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasTag(int entity, string tag) => TagSets.TryGetValue(tag, out var bitset) && bitset.Contains(entity);

    /// <summary>
    /// Returns the PagedBitSet for a tag. Caller iterates directly via foreach (zero-copy).
    /// Returns null if the tag has never been used.
    /// </summary>
    public PagedBitSet WithTag(string tag) => TagSets.TryGetValue(tag, out var bitset) ? bitset : null;

    public void DestroyEntitiesWithTag(string tag)
    {
        if (!TagSets.TryGetValue(tag, out var bitset)) return;
        foreach (int entity in bitset)
            DestroyEntity(entity);
        bitset.Clear();
    }

    public void ClearTag(string tag)
    {
        if (TagSets.TryGetValue(tag, out var bitset))
            bitset.Clear();
    }

    /// <summary> Disables an entity. Disabled entities are skipped by Query but remain alive and restorable. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DisableEntity(int entity) => DisabledEntities.Add(entity);

    /// <summary> Re-enables a previously disabled entity so it participates in queries again. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnableEntity(int entity) => DisabledEntities.Remove(entity);

    /// <summary> Returns true if the entity is currently disabled. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsDisabled(int entity) => DisabledEntities.Contains(entity);

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
    public delegate void QueryAction<T1>(int threadIndex, int entity, ref T1 c1) where T1 : struct;
    public delegate void QueryAction<T1, T2>(int threadIndex, int entity, ref T1 c1, ref T2 c2) where T1 : struct where T2 : struct;
    public delegate void QueryAction<T1, T2, T3>(int threadIndex, int entity, ref T1 c1, ref T2 c2, ref T3 c3) where T1 : struct where T2 : struct where T3 : struct;

    public void Query<T1>(QueryAction<T1> action) where T1 : struct, Component
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

        var disabled = DisabledEntities;

        if (QueryMatchCache.Count == 1)
        {
            var arch = QueryMatchCache[0];
            var entities = arch.GetEntities();
            var data1 = arch.GetArray<T1>();
            RunParallel(arch.EntityCount, (threadIdx, start, end) =>
            {
                for (int i = start; i < end; i++)
                {
                    if (disabled.Contains(entities[i])) continue;
                    action(threadIdx, entities[i], ref data1[i]);
                }
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
                {
                    if (disabled.Contains(entities[i])) continue;
                    action(threadIdx, entities[i], ref data1[i]);
                }

                globalIdx = archEnd;
            }
        });
    }

    public void Query<T1, T2>(QueryAction<T1, T2> action)
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

        var disabled = DisabledEntities;

        if (QueryMatchCache.Count == 1)
        {
            var arch = QueryMatchCache[0];
            var entities = arch.GetEntities();
            var data1 = arch.GetArray<T1>();
            var data2 = arch.GetArray<T2>();
            RunParallel(arch.EntityCount, (threadIdx, start, end) =>
            {
                for (int i = start; i < end; i++)
                {
                    if (disabled.Contains(entities[i])) continue;
                    action(threadIdx, entities[i], ref data1[i], ref data2[i]);
                }
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
                {
                    if (disabled.Contains(entities[i])) continue;
                    action(threadIdx, entities[i], ref data1[i], ref data2[i]);
                }

                globalIdx = archEnd;
            }
        });
    }

    // Cached archetype query results
    private readonly List<Archetype> QueryMatchCache = new();

    public void Query<T1, T2, T3>(QueryAction<T1, T2, T3> action)
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

        var disabled = DisabledEntities;

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
                {
                    if (disabled.Contains(entities[i])) continue;
                    action(threadIdx, entities[i], ref data1[i], ref data2[i], ref data3[i]);
                }
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
                {
                    if (disabled.Contains(entities[i])) continue;
                    action(threadIdx, entities[i], ref data1[i], ref data2[i], ref data3[i]);
                }

                globalIdx = archEnd;
            }
        });
    }

    public void Update()
    {
        FlushDeferred();

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
            if (IsSystemPaused(Systems[i])) continue;
            Systems[i].GameTime = Scene.GameTime;
            Systems[i].Update();
        }
    }

    public void FixedUpdate()
    {
        for (int i = 0; i < Systems.Count; i++)
        {
            if (!Systems[i].Enabled) continue;
            if (IsSystemPaused(Systems[i])) continue;
            Systems[i].GameTime = Scene.GameTime;
            Systems[i].FixedUpdate();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsSystemPaused(System system)
    {
        var groups = system.PauseGroups;
        if (groups == PauseGroup.None) return false;
        if (GameplayPaused && (groups & PauseGroup.Gameplay) != 0) return true;
        if (AnimationPaused && (groups & PauseGroup.Animation) != 0) return true;
        return false;
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
        for (int i = 0; i < RenderSystems.Count; i++)
        {
            if (!RenderSystems[i].Enabled) continue;
            RenderSystems[i].GameTime = Scene.GameTime;
            RenderSystems[i].LateRender();
        }
    }
}
