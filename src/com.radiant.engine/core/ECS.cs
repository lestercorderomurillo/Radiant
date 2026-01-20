using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using com.radiant.engine.bundle;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.core;

public class ECS : IGameObject
{
    private readonly HashSet<int> ActiveEntities;
    private readonly Stack<int> RecycledIds;
    private readonly List<System> Systems;
    private readonly Dictionary<Type, IComponentSet> ComponentSets;
    private readonly List<int> QueryResult;
    private int NextEntityId;

    private Vector2 ViewportCenter;
    private Vector2 ViewportSize;


    public int EntityCount => ActiveEntities.Count;
    public Scene Scene { get; set; }
    public Renderer Renderer { get; private set; }
    public SpatialIndex Spatial { get; private set; }

    public ECS(Scene scene, Renderer renderer)
    {
        Scene = scene;
        Renderer = renderer;
        ActiveEntities = new HashSet<int>();
        RecycledIds = new Stack<int>();
        Systems = new List<System>();
        ComponentSets = new Dictionary<Type, IComponentSet>();
        QueryResult = new List<int>();
        Spatial = new SpatialIndex(this, 64f);

        var window = Renderer.Window;
        ViewportSize = window.GetScreenSize();
        ViewportCenter = window.GetScreenCenter();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetViewport(Vector2 center, Vector2 size)
    {
        ViewportCenter = center;
        ViewportSize = size;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetViewport(Vector2 center)
    {
        ViewportCenter = center;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void GetCullBounds(Vector2? viewport, float padding, out Vector3 min, out Vector3 max)
    {
        var center = viewport ?? ViewportCenter;
        var halfSize = ViewportSize * 0.5f;

        min = new Vector3(
            center.X - halfSize.X - padding,
            center.Y - halfSize.Y - padding,
            float.MinValue
        );
        max = new Vector3(
            center.X + halfSize.X + padding,
            center.Y + halfSize.Y + padding,
            float.MaxValue
        );
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

    public List<int> Query<T1>(bool culling = false, float padding = 100f, Vector2? viewport = null)
    where T1 : struct, Component
    {
        QueryResult.Clear();
        var set1 = GetComponentSet<T1>();

        if (culling)
        {
            GetCullBounds(viewport, padding, out var min, out var max);
            var visible = Spatial.InBox(min, max);

            for (int i = 0; i < visible.Length; i++)
            {
                int entityId = visible[i];
                if (ActiveEntities.Contains(entityId) && set1.Contains(entityId))
                    QueryResult.Add(entityId);
            }
        }
        else
        {
            foreach (int entityId in set1.GetEntityIds())
                if (ActiveEntities.Contains(entityId))
                    QueryResult.Add(entityId);
        }

        return QueryResult;
    }

    public List<int> Query<T1, T2>(bool culling = false, float padding = 100f, Vector2? viewport = null)
        where T1 : struct, Component
        where T2 : struct, Component
    {
        QueryResult.Clear();
        var set1 = GetComponentSet<T1>();
        var set2 = GetComponentSet<T2>();

        if (culling)
        {
            GetCullBounds(viewport, padding, out var min, out var max);
            var visible = Spatial.InBox(min, max);

            for (int i = 0; i < visible.Length; i++)
            {
                int entityId = visible[i];
                if (ActiveEntities.Contains(entityId) && set1.Contains(entityId) && set2.Contains(entityId))
                    QueryResult.Add(entityId);
            }
        }
        else
        {
            var smallest = set1.Count <= set2.Count ? set1 : (IComponentSet)set2;

            foreach (int entityId in smallest.GetEntityIds())
            {
                if (!ActiveEntities.Contains(entityId))
                    continue;
                if (set1.Contains(entityId) && set2.Contains(entityId))
                    QueryResult.Add(entityId);
            }
        }

        return QueryResult;
    }

    public List<int> Query<T1, T2, T3>(bool culling = false, float padding = 100f, Vector2? viewport = null)
        where T1 : struct, Component
        where T2 : struct, Component
        where T3 : struct, Component
    {
        QueryResult.Clear();
        var set1 = GetComponentSet<T1>();
        var set2 = GetComponentSet<T2>();
        var set3 = GetComponentSet<T3>();

        if (culling)
        {
            GetCullBounds(viewport, padding, out var min, out var max);
            var visible = Spatial.InBox(min, max);

            for (int i = 0; i < visible.Length; i++)
            {
                int entityId = visible[i];
                if (ActiveEntities.Contains(entityId) &&
                    set1.Contains(entityId) &&
                    set2.Contains(entityId) &&
                    set3.Contains(entityId))
                    QueryResult.Add(entityId);
            }
        }
        else
        {
            IComponentSet smallest = set1;
            if (set2.Count < smallest.Count) smallest = set2;
            if (set3.Count < smallest.Count) smallest = set3;

            foreach (int entityId in smallest.GetEntityIds())
            {
                if (!ActiveEntities.Contains(entityId))
                    continue;
                if (set1.Contains(entityId) && set2.Contains(entityId) && set3.Contains(entityId))
                    QueryResult.Add(entityId);
            }
        }

        return QueryResult;
    }

    public List<int> Query<T1, T2, T3, T4>(bool culling = false, float padding = 100f, Vector2? viewport = null)
        where T1 : struct, Component
        where T2 : struct, Component
        where T3 : struct, Component
        where T4 : struct, Component
    {
        QueryResult.Clear();
        var set1 = GetComponentSet<T1>();
        var set2 = GetComponentSet<T2>();
        var set3 = GetComponentSet<T3>();
        var set4 = GetComponentSet<T4>();

        if (culling)
        {
            GetCullBounds(viewport, padding, out var min, out var max);
            var visible = Spatial.InBox(min, max);

            for (int i = 0; i < visible.Length; i++)
            {
                int entityId = visible[i];
                if (ActiveEntities.Contains(entityId) &&
                    set1.Contains(entityId) &&
                    set2.Contains(entityId) &&
                    set3.Contains(entityId) &&
                    set4.Contains(entityId))
                    QueryResult.Add(entityId);
            }
        }
        else
        {
            IComponentSet smallest = set1;
            if (set2.Count < smallest.Count) smallest = set2;
            if (set3.Count < smallest.Count) smallest = set3;
            if (set4.Count < smallest.Count) smallest = set4;

            foreach (int entityId in smallest.GetEntityIds())
            {
                if (!ActiveEntities.Contains(entityId))
                    continue;
                if (set1.Contains(entityId) &&
                    set2.Contains(entityId) &&
                    set3.Contains(entityId) &&
                    set4.Contains(entityId))
                    QueryResult.Add(entityId);
            }
        }

        return QueryResult;
    }
    public void Update()
    {
        // Handle resize - each system decides if it needs to rebuild
        if (Renderer.Window.ResizePending)
        {
            Renderer.Window.ClearResizePending();
            Renderer.UpdateScreenInfo();
            ViewportSize = Renderer.Window.GetScreenSize();
            ViewportCenter = Renderer.Window.GetScreenCenter();

            for (int i = 0; i < Systems.Count; i++)
                if (Systems[i].Enabled)
                    Systems[i].OnResize();
        }

        for (int i = 0; i < Systems.Count; i++)
        {
            if (!Systems[i].Enabled) continue;
            Systems[i].GameTime = Scene.GameTime;
            Systems[i].Update();
        }
    }

    public void FixedUpdate()
    {
        for (int i = 0; i < Systems.Count; i++)
        {
            if (!Systems[i].Enabled) continue;
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