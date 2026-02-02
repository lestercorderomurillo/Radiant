using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using com.radiant.engine.bundle;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.core;

public class SpatialIndex
{
    private readonly ECS Ecs;
    private readonly float CellSize;
    private readonly float InverseCellSize;

    private const int InitialEntityCapacity = 1024;
    private const int InitialCellCapacity = 64;
    private const int MaxEntitiesPerCell = 256;

    // Cells still use dictionary (sparse cell keys)
    private readonly Dictionary<long, CellData> Cells;

    // Array-based entity data (entity ID as direct index)
    private EntitySpatialData[] EntityDataArray;
    private bool[] EntityExists;
    private int EntityCapacity;

    // Exact position lookup (position key -> entity)
    private readonly Dictionary<long, int> ExactLookup;

    private int[] ResultArray;
    private int ResultCount;

    private float[] DistanceCache;
    private int[] SortBuffer;

    private struct CellData
    {
        public int[] Entities;
        public int Count;

        public CellData(int capacity)
        {
            Entities = new int[capacity];
            Count = 0;
        }
    }

    private struct EntitySpatialData
    {
        public long CellKey;
        public int IndexInCell;
        public float PosX, PosY, PosZ;  // Inline instead of Vector3 to avoid struct copy
        public long ExactKey;
    }

    public SpatialIndex(ECS ecs, float cellSize = 64f)
    {
        Ecs = ecs;
        CellSize = cellSize;
        InverseCellSize = 1f / cellSize;

        Cells = new Dictionary<long, CellData>(InitialCellCapacity);

        EntityCapacity = InitialEntityCapacity;
        EntityDataArray = new EntitySpatialData[EntityCapacity];
        EntityExists = new bool[EntityCapacity];
        ExactLookup = new Dictionary<long, int>();

        ResultArray = new int[1024];
        DistanceCache = new float[1024];
        SortBuffer = new int[1024];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureEntityCapacity(int entityId)
    {
        if (entityId < EntityCapacity) return;

        int newCapacity = EntityCapacity;
        while (newCapacity <= entityId)
            newCapacity *= 2;

        Array.Resize(ref EntityDataArray, newCapacity);
        Array.Resize(ref EntityExists, newCapacity);
        EntityCapacity = newCapacity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long PackCell(int x, int y, int z)
    {
        unchecked
        {
            return ((long)(x + 524288) << 40) | ((long)(y + 524288) << 20) | (long)(z + 524288);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ToCell(float px, float py, float pz, out int cx, out int cy, out int cz)
    {
        cx = (int)MathF.Floor(px * InverseCellSize);
        cy = (int)MathF.Floor(py * InverseCellSize);
        cz = (int)MathF.Floor(pz * InverseCellSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long ToCellKey(float px, float py, float pz)
    {
        ToCell(px, py, pz, out int cx, out int cy, out int cz);
        return PackCell(cx, cy, cz);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long ToExactKey(float px, float py, float pz)
    {
        unchecked
        {
            int x = (int)(px * 100f) + 524288;
            int y = (int)(py * 100f) + 524288;
            int z = (int)(pz * 100f) + 524288;
            return ((long)x << 40) | ((long)y << 20) | (long)z;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureResultCapacity(int required)
    {
        if (ResultArray.Length < required)
        {
            int newSize = Math.Max(ResultArray.Length * 2, required);
            Array.Resize(ref ResultArray, newSize);
            Array.Resize(ref DistanceCache, newSize);
            Array.Resize(ref SortBuffer, newSize);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Insert(int entity, Vector3 position)
    {
        Insert(entity, position.X, position.Y, position.Z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Insert(int entity, float px, float py, float pz)
    {
        EnsureEntityCapacity(entity);

        long newCellKey = ToCellKey(px, py, pz);

        if (EntityExists[entity])
        {
            ref var existing = ref EntityDataArray[entity];
            long newExactKey = ToExactKey(px, py, pz);

            // Same cell - just update position in-place
            if (existing.CellKey == newCellKey)
            {
                // Update exact lookup if position changed
                if (existing.ExactKey != newExactKey)
                {
                    if (ExactLookup.TryGetValue(existing.ExactKey, out int e) && e == entity)
                        ExactLookup.Remove(existing.ExactKey);
                    ExactLookup[newExactKey] = entity;
                }
                existing.PosX = px;
                existing.PosY = py;
                existing.PosZ = pz;
                existing.ExactKey = newExactKey;
                return;
            }

            // Different cell - remove from old cell and exact lookup
            if (ExactLookup.TryGetValue(existing.ExactKey, out int old) && old == entity)
                ExactLookup.Remove(existing.ExactKey);
            RemoveFromCell(entity, ref existing);
        }

        // Add to new cell
        if (!Cells.TryGetValue(newCellKey, out var cell))
        {
            cell = new CellData(MaxEntitiesPerCell);
            Cells[newCellKey] = cell;
        }

        if (cell.Count >= cell.Entities.Length)
            Array.Resize(ref cell.Entities, cell.Entities.Length * 2);

        int indexInCell = cell.Count++;
        cell.Entities[indexInCell] = entity;
        Cells[newCellKey] = cell;

        // Update entity data in-place
        long exactKey = ToExactKey(px, py, pz);
        ref var data = ref EntityDataArray[entity];
        data.CellKey = newCellKey;
        data.IndexInCell = indexInCell;
        data.PosX = px;
        data.PosY = py;
        data.PosZ = pz;
        data.ExactKey = exactKey;
        EntityExists[entity] = true;
        ExactLookup[exactKey] = entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveFromCell(int entity, ref EntitySpatialData data)
    {
        if (!Cells.TryGetValue(data.CellKey, out var cell))
            return;

        int indexInCell = data.IndexInCell;
        int lastIndex = cell.Count - 1;

        if (indexInCell < lastIndex)
        {
            int lastEntity = cell.Entities[lastIndex];
            cell.Entities[indexInCell] = lastEntity;
            EntityDataArray[lastEntity].IndexInCell = indexInCell;
        }

        cell.Count--;
        Cells[data.CellKey] = cell;

        if (cell.Count == 0)
            Cells.Remove(data.CellKey);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Remove(int entity)
    {
        if (entity >= EntityCapacity || !EntityExists[entity])
            return;

        ref var data = ref EntityDataArray[entity];
        RemoveFromCell(entity, ref data);
        if (ExactLookup.TryGetValue(data.ExactKey, out int e) && e == entity)
            ExactLookup.Remove(data.ExactKey);
        EntityExists[entity] = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Update(int entity, Vector3 position)
    {
        Insert(entity, position.X, position.Y, position.Z);
    }

    public void Clear()
    {
        Cells.Clear();
        ExactLookup.Clear();
        Array.Clear(EntityExists, 0, EntityCapacity);
    }

    public void SyncAll()
    {
        Clear();
        foreach (int entity in Ecs.GetEntities<Transform>())
        {
            ref var t = ref Ecs.GetComponent<Transform>(entity);
            Insert(entity, t.Position.X, t.Position.Y, t.Position.Z);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int? AtExact(Vector3 position)
    {
        return AtExact(position.X, position.Y, position.Z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int? AtExact(float x, float y, float z)
    {
        long exactKey = ToExactKey(x, y, z);
        return ExactLookup.TryGetValue(exactKey, out int entity) ? entity : null;
    }

    public ReadOnlySpan<int> InCell(int cx, int cy, int cz)
    {
        long key = PackCell(cx, cy, cz);
        if (!Cells.TryGetValue(key, out var cell))
            return ReadOnlySpan<int>.Empty;

        return new ReadOnlySpan<int>(cell.Entities, 0, cell.Count);
    }

    public ReadOnlySpan<int> InCell(Vector3 position)
    {
        ToCell(position.X, position.Y, position.Z, out int cx, out int cy, out int cz);
        return InCell(cx, cy, cz);
    }

    public ReadOnlySpan<int> InRadius(Vector3 center, float radius)
    {
        return InRadius(center.X, center.Y, center.Z, radius);
    }

    public ReadOnlySpan<int> InRadius(float cx, float cy, float cz, float radius)
    {
        ResultCount = 0;
        float radiusSq = radius * radius;

        int minX = (int)MathF.Floor((cx - radius) * InverseCellSize);
        int maxX = (int)MathF.Floor((cx + radius) * InverseCellSize);
        int minY = (int)MathF.Floor((cy - radius) * InverseCellSize);
        int maxY = (int)MathF.Floor((cy + radius) * InverseCellSize);
        int minZ = (int)MathF.Floor((cz - radius) * InverseCellSize);
        int maxZ = (int)MathF.Floor((cz + radius) * InverseCellSize);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    long key = PackCell(x, y, z);
                    if (!Cells.TryGetValue(key, out var cell)) continue;

                    EnsureResultCapacity(ResultCount + cell.Count);

                    for (int i = 0; i < cell.Count; i++)
                    {
                        int entity = cell.Entities[i];
                        ref var data = ref EntityDataArray[entity];

                        float dx = data.PosX - cx;
                        float dy = data.PosY - cy;
                        float dz = data.PosZ - cz;
                        float distSq = dx * dx + dy * dy + dz * dz;

                        if (distSq <= radiusSq)
                            ResultArray[ResultCount++] = entity;
                    }
                }
            }
        }

        return new ReadOnlySpan<int>(ResultArray, 0, ResultCount);
    }

    public ReadOnlySpan<int> InRadius2D(float cx, float cz, float radius, float y = 0)
    {
        ResultCount = 0;
        float radiusSq = radius * radius;

        int minX = (int)MathF.Floor((cx - radius) * InverseCellSize);
        int maxX = (int)MathF.Floor((cx + radius) * InverseCellSize);
        int minZ = (int)MathF.Floor((cz - radius) * InverseCellSize);
        int maxZ = (int)MathF.Floor((cz + radius) * InverseCellSize);
        int cellY = (int)MathF.Floor(y * InverseCellSize);

        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                long key = PackCell(x, cellY, z);
                if (!Cells.TryGetValue(key, out var cell)) continue;

                EnsureResultCapacity(ResultCount + cell.Count);

                for (int i = 0; i < cell.Count; i++)
                {
                    int entity = cell.Entities[i];
                    ref var data = ref EntityDataArray[entity];

                    float dx = data.PosX - cx;
                    float dz = data.PosZ - cz;

                    if (dx * dx + dz * dz <= radiusSq)
                        ResultArray[ResultCount++] = entity;
                }
            }
        }

        return new ReadOnlySpan<int>(ResultArray, 0, ResultCount);
    }

    public ReadOnlySpan<int> InBox(Vector3 min, Vector3 max)
    {
        return InBox(min.X, min.Y, min.Z, max.X, max.Y, max.Z);
    }

    public ReadOnlySpan<int> InBox(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
    {
        ResultCount = 0;

        int minCX = (int)MathF.Floor(minX * InverseCellSize);
        int maxCX = (int)MathF.Floor(maxX * InverseCellSize);
        int minCY = (int)MathF.Floor(minY * InverseCellSize);
        int maxCY = (int)MathF.Floor(maxY * InverseCellSize);
        int minCZ = (int)MathF.Floor(minZ * InverseCellSize);
        int maxCZ = (int)MathF.Floor(maxZ * InverseCellSize);

        for (int x = minCX; x <= maxCX; x++)
        {
            for (int y = minCY; y <= maxCY; y++)
            {
                for (int z = minCZ; z <= maxCZ; z++)
                {
                    long key = PackCell(x, y, z);
                    if (!Cells.TryGetValue(key, out var cell)) continue;

                    EnsureResultCapacity(ResultCount + cell.Count);

                    for (int i = 0; i < cell.Count; i++)
                    {
                        int entity = cell.Entities[i];
                        ref var data = ref EntityDataArray[entity];

                        if (data.PosX >= minX && data.PosX <= maxX &&
                            data.PosY >= minY && data.PosY <= maxY &&
                            data.PosZ >= minZ && data.PosZ <= maxZ)
                        {
                            ResultArray[ResultCount++] = entity;
                        }
                    }
                }
            }
        }

        return new ReadOnlySpan<int>(ResultArray, 0, ResultCount);
    }

    public ReadOnlySpan<int> Nearest(Vector3 center, int count, float maxRadius = float.MaxValue)
    {
        return Nearest(center.X, center.Y, center.Z, count, maxRadius);
    }

    public ReadOnlySpan<int> Nearest(float cx, float cy, float cz, int count, float maxRadius = float.MaxValue)
    {
        var candidates = InRadius(cx, cy, cz, maxRadius);
        if (candidates.Length <= count)
            return candidates;

        EnsureResultCapacity(candidates.Length);

        for (int i = 0; i < candidates.Length; i++)
        {
            int entity = candidates[i];
            ref var data = ref EntityDataArray[entity];
            float dx = data.PosX - cx;
            float dy = data.PosY - cy;
            float dz = data.PosZ - cz;
            DistanceCache[i] = dx * dx + dy * dy + dz * dz;
            SortBuffer[i] = i;
        }

        PartialSort(candidates.Length, count);

        ResultCount = 0;
        for (int i = 0; i < count; i++)
            ResultArray[ResultCount++] = candidates[SortBuffer[i]];

        return new ReadOnlySpan<int>(ResultArray, 0, ResultCount);
    }

    private void PartialSort(int length, int k)
    {
        int left = 0;
        int right = length - 1;

        while (left < right)
        {
            int pivotIndex = Partition(left, right);

            if (pivotIndex == k - 1) return;
            if (pivotIndex < k - 1)
                left = pivotIndex + 1;
            else
                right = pivotIndex - 1;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Partition(int left, int right)
    {
        float pivot = DistanceCache[SortBuffer[right]];
        int i = left - 1;

        for (int j = left; j < right; j++)
        {
            if (DistanceCache[SortBuffer[j]] <= pivot)
            {
                i++;
                (SortBuffer[i], SortBuffer[j]) = (SortBuffer[j], SortBuffer[i]);
            }
        }

        (SortBuffer[i + 1], SortBuffer[right]) = (SortBuffer[right], SortBuffer[i + 1]);
        return i + 1;
    }
}
