using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using com.radiant.engine.bundle;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.core;

public class SpatialIndex
{
    private readonly ECS ecs;
    private readonly float cellSize;
    private readonly float inverseCellSize;

    private const int InitialCellCapacity = 64;
    private const int MaxEntitiesPerCell = 256;

    // Cell storage - grows on demand
    private readonly Dictionary<long, CellData> cells;

    // Entity tracking - dictionary-based, zero memory when empty
    private readonly Dictionary<int, EntitySpatialData> entityData;

    // Exact position lookup
    private readonly Dictionary<long, int> exactLookup;

    // Result buffer - grows as needed
    private int[] resultArray;
    private int resultCount;

    // Sorting buffers - grows as needed
    private float[] distanceCache;
    private int[] sortBuffer;

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
        public Vector3 Position;
        public long ExactKey;
    }

    public SpatialIndex(ECS ecs, float cellSize = 64f)
    {
        this.ecs = ecs;
        this.cellSize = cellSize;
        this.inverseCellSize = 1f / cellSize;

        cells = new Dictionary<long, CellData>(InitialCellCapacity);
        entityData = new Dictionary<int, EntitySpatialData>();
        exactLookup = new Dictionary<long, int>();

        resultArray = new int[1024];
        distanceCache = new float[1024];
        sortBuffer = new int[1024];
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
    private void UnpackCell(long key, out int x, out int y, out int z)
    {
        unchecked
        {
            z = (int)(key & 0xFFFFF) - 524288;
            y = (int)((key >> 20) & 0xFFFFF) - 524288;
            x = (int)((key >> 40) & 0xFFFFF) - 524288;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ToCell(float px, float py, float pz, out int cx, out int cy, out int cz)
    {
        cx = (int)MathF.Floor(px * inverseCellSize);
        cy = (int)MathF.Floor(py * inverseCellSize);
        cz = (int)MathF.Floor(pz * inverseCellSize);
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
        if (resultArray.Length < required)
        {
            int newSize = Math.Max(resultArray.Length * 2, required);
            Array.Resize(ref resultArray, newSize);
            Array.Resize(ref distanceCache, newSize);
            Array.Resize(ref sortBuffer, newSize);
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
        long newCellKey = ToCellKey(px, py, pz);
        long newExactKey = ToExactKey(px, py, pz);

        if (entityData.TryGetValue(entity, out var existing))
        {
            // Already tracked - check if cell changed
            if (existing.CellKey == newCellKey)
            {
                // Same cell, just update position
                existing.Position = new Vector3(px, py, pz);

                // Update exact lookup
                if (existing.ExactKey != newExactKey)
                {
                    if (exactLookup.TryGetValue(existing.ExactKey, out int e) && e == entity)
                        exactLookup.Remove(existing.ExactKey);
                    existing.ExactKey = newExactKey;
                    exactLookup[newExactKey] = entity;
                }

                entityData[entity] = existing;
                return;
            }

            // Cell changed - remove from old cell
            RemoveFromCell(entity, existing);
        }

        // Add to new cell
        if (!cells.TryGetValue(newCellKey, out var cell))
        {
            cell = new CellData(MaxEntitiesPerCell);
            cells[newCellKey] = cell;
        }

        // Grow cell if needed
        if (cell.Count >= cell.Entities.Length)
        {
            Array.Resize(ref cell.Entities, cell.Entities.Length * 2);
        }

        int indexInCell = cell.Count++;
        cell.Entities[indexInCell] = entity;
        cells[newCellKey] = cell;

        // Update entity data
        var newData = new EntitySpatialData
        {
            CellKey = newCellKey,
            IndexInCell = indexInCell,
            Position = new Vector3(px, py, pz),
            ExactKey = newExactKey
        };
        entityData[entity] = newData;

        // Update exact lookup (remove old first if exists)
        if (existing.ExactKey != 0 && exactLookup.TryGetValue(existing.ExactKey, out int old) && old == entity)
            exactLookup.Remove(existing.ExactKey);
        exactLookup[newExactKey] = entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveFromCell(int entity, EntitySpatialData data)
    {
        if (!cells.TryGetValue(data.CellKey, out var cell))
            return;

        int indexInCell = data.IndexInCell;
        int lastIndex = cell.Count - 1;

        if (indexInCell < lastIndex)
        {
            // Swap with last entity
            int lastEntity = cell.Entities[lastIndex];
            cell.Entities[indexInCell] = lastEntity;

            // Update the swapped entity's index
            if (entityData.TryGetValue(lastEntity, out var lastData))
            {
                lastData.IndexInCell = indexInCell;
                entityData[lastEntity] = lastData;
            }
        }

        cell.Count--;
        cells[data.CellKey] = cell;

        // Remove empty cells
        if (cell.Count == 0)
            cells.Remove(data.CellKey);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Remove(int entity)
    {
        if (!entityData.TryGetValue(entity, out var data))
            return;

        RemoveFromCell(entity, data);

        // Remove from exact lookup
        if (exactLookup.TryGetValue(data.ExactKey, out int e) && e == entity)
            exactLookup.Remove(data.ExactKey);

        entityData.Remove(entity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Update(int entity, Vector3 position)
    {
        Insert(entity, position.X, position.Y, position.Z);
    }

    public void Clear()
    {
        cells.Clear();
        entityData.Clear();
        exactLookup.Clear();
    }

    public void SyncAll()
    {
        Clear();
        foreach (int entity in ecs.Query<Transform>())
        {
            ref var t = ref ecs.GetComponent<Transform>(entity);
            Insert(entity, t.Position.X, t.Position.Y, t.Position.Z);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int? AtExact(Vector3 position)
    {
        long key = ToExactKey(position.X, position.Y, position.Z);
        return exactLookup.TryGetValue(key, out int entity) ? entity : null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int? AtExact(float x, float y, float z)
    {
        long key = ToExactKey(x, y, z);
        return exactLookup.TryGetValue(key, out int entity) ? entity : null;
    }

    public ReadOnlySpan<int> InCell(int cx, int cy, int cz)
    {
        long key = PackCell(cx, cy, cz);
        if (!cells.TryGetValue(key, out var cell))
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
        resultCount = 0;
        float radiusSq = radius * radius;

        int minX = (int)MathF.Floor((cx - radius) * inverseCellSize);
        int maxX = (int)MathF.Floor((cx + radius) * inverseCellSize);
        int minY = (int)MathF.Floor((cy - radius) * inverseCellSize);
        int maxY = (int)MathF.Floor((cy + radius) * inverseCellSize);
        int minZ = (int)MathF.Floor((cz - radius) * inverseCellSize);
        int maxZ = (int)MathF.Floor((cz + radius) * inverseCellSize);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    long key = PackCell(x, y, z);
                    if (!cells.TryGetValue(key, out var cell)) continue;

                    EnsureResultCapacity(resultCount + cell.Count);

                    for (int i = 0; i < cell.Count; i++)
                    {
                        int entity = cell.Entities[i];
                        if (!entityData.TryGetValue(entity, out var data)) continue;

                        float dx = data.Position.X - cx;
                        float dy = data.Position.Y - cy;
                        float dz = data.Position.Z - cz;
                        float distSq = dx * dx + dy * dy + dz * dz;

                        if (distSq <= radiusSq)
                            resultArray[resultCount++] = entity;
                    }
                }
            }
        }

        return new ReadOnlySpan<int>(resultArray, 0, resultCount);
    }

    public ReadOnlySpan<int> InRadius2D(float cx, float cz, float radius, float y = 0)
    {
        resultCount = 0;
        float radiusSq = radius * radius;

        int minX = (int)MathF.Floor((cx - radius) * inverseCellSize);
        int maxX = (int)MathF.Floor((cx + radius) * inverseCellSize);
        int minZ = (int)MathF.Floor((cz - radius) * inverseCellSize);
        int maxZ = (int)MathF.Floor((cz + radius) * inverseCellSize);
        int cellY = (int)MathF.Floor(y * inverseCellSize);

        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                long key = PackCell(x, cellY, z);
                if (!cells.TryGetValue(key, out var cell)) continue;

                EnsureResultCapacity(resultCount + cell.Count);

                for (int i = 0; i < cell.Count; i++)
                {
                    int entity = cell.Entities[i];
                    if (!entityData.TryGetValue(entity, out var data)) continue;

                    float dx = data.Position.X - cx;
                    float dz = data.Position.Z - cz;

                    if (dx * dx + dz * dz <= radiusSq)
                        resultArray[resultCount++] = entity;
                }
            }
        }

        return new ReadOnlySpan<int>(resultArray, 0, resultCount);
    }

    public ReadOnlySpan<int> InBox(Vector3 min, Vector3 max)
    {
        return InBox(min.X, min.Y, min.Z, max.X, max.Y, max.Z);
    }

    public ReadOnlySpan<int> InBox(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
    {
        resultCount = 0;

        int minCX = (int)MathF.Floor(minX * inverseCellSize);
        int maxCX = (int)MathF.Floor(maxX * inverseCellSize);
        int minCY = (int)MathF.Floor(minY * inverseCellSize);
        int maxCY = (int)MathF.Floor(maxY * inverseCellSize);
        int minCZ = (int)MathF.Floor(minZ * inverseCellSize);
        int maxCZ = (int)MathF.Floor(maxZ * inverseCellSize);

        for (int x = minCX; x <= maxCX; x++)
        {
            for (int y = minCY; y <= maxCY; y++)
            {
                for (int z = minCZ; z <= maxCZ; z++)
                {
                    long key = PackCell(x, y, z);
                    if (!cells.TryGetValue(key, out var cell)) continue;

                    EnsureResultCapacity(resultCount + cell.Count);

                    for (int i = 0; i < cell.Count; i++)
                    {
                        int entity = cell.Entities[i];
                        if (!entityData.TryGetValue(entity, out var data)) continue;

                        if (data.Position.X >= minX && data.Position.X <= maxX &&
                            data.Position.Y >= minY && data.Position.Y <= maxY &&
                            data.Position.Z >= minZ && data.Position.Z <= maxZ)
                        {
                            resultArray[resultCount++] = entity;
                        }
                    }
                }
            }
        }

        return new ReadOnlySpan<int>(resultArray, 0, resultCount);
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
            if (entityData.TryGetValue(entity, out var data))
            {
                float dx = data.Position.X - cx;
                float dy = data.Position.Y - cy;
                float dz = data.Position.Z - cz;
                distanceCache[i] = dx * dx + dy * dy + dz * dz;
            }
            else
            {
                distanceCache[i] = float.MaxValue;
            }
            sortBuffer[i] = i;
        }

        PartialSort(candidates.Length, count);

        resultCount = 0;
        for (int i = 0; i < count; i++)
            resultArray[resultCount++] = candidates[sortBuffer[i]];

        return new ReadOnlySpan<int>(resultArray, 0, resultCount);
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
        float pivot = distanceCache[sortBuffer[right]];
        int i = left - 1;

        for (int j = left; j < right; j++)
        {
            if (distanceCache[sortBuffer[j]] <= pivot)
            {
                i++;
                (sortBuffer[i], sortBuffer[j]) = (sortBuffer[j], sortBuffer[i]);
            }
        }

        (sortBuffer[i + 1], sortBuffer[right]) = (sortBuffer[right], sortBuffer[i + 1]);
        return i + 1;
    }
}