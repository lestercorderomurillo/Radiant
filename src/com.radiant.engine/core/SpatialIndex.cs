using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using com.radiant.engine.bundle;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.core;

public class SpatialIndex
{
    private readonly ECS Ecs;
    private readonly float CellSize;
    private readonly float InverseCellSize;

    private const int InitialCellCapacity = 64;
    private const int MaxEntitiesPerCell = 256;

    private readonly Dictionary<long, CellData> Cells;

    private readonly Dictionary<int, EntitySpatialData> EntityData;

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
        public Vector3 Position;
        public long ExactKey;
    }

    public SpatialIndex(ECS ecs, float cellSize = 64f)
    {
        Ecs = ecs;
        CellSize = cellSize;
        InverseCellSize = 1f / cellSize;

        Cells = new Dictionary<long, CellData>(InitialCellCapacity);
        EntityData = new Dictionary<int, EntitySpatialData>();
        ExactLookup = new Dictionary<long, int>();

        ResultArray = new int[1024];
        DistanceCache = new float[1024];
        SortBuffer = new int[1024];
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
        long newCellKey = ToCellKey(px, py, pz);
        long newExactKey = ToExactKey(px, py, pz);

        if (EntityData.TryGetValue(entity, out var existing))
        {
            if (existing.CellKey == newCellKey)
            {
                existing.Position = new Vector3(px, py, pz);

                if (existing.ExactKey != newExactKey)
                {
                    if (ExactLookup.TryGetValue(existing.ExactKey, out int e) && e == entity)
                        ExactLookup.Remove(existing.ExactKey);
                    existing.ExactKey = newExactKey;
                    ExactLookup[newExactKey] = entity;
                }

                EntityData[entity] = existing;
                return;
            }

            RemoveFromCell(entity, existing);
        }

        if (!Cells.TryGetValue(newCellKey, out var cell))
        {
            cell = new CellData(MaxEntitiesPerCell);
            Cells[newCellKey] = cell;
        }

        if (cell.Count >= cell.Entities.Length)
        {
            Array.Resize(ref cell.Entities, cell.Entities.Length * 2);
        }

        int indexInCell = cell.Count++;
        cell.Entities[indexInCell] = entity;
        Cells[newCellKey] = cell;

        var newData = new EntitySpatialData
        {
            CellKey = newCellKey,
            IndexInCell = indexInCell,
            Position = new Vector3(px, py, pz),
            ExactKey = newExactKey
        };
        EntityData[entity] = newData;

        if (existing.ExactKey != 0 && ExactLookup.TryGetValue(existing.ExactKey, out int old) && old == entity)
            ExactLookup.Remove(existing.ExactKey);
        ExactLookup[newExactKey] = entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveFromCell(int entity, EntitySpatialData data)
    {
        if (!Cells.TryGetValue(data.CellKey, out var cell))
            return;

        int indexInCell = data.IndexInCell;
        int lastIndex = cell.Count - 1;

        if (indexInCell < lastIndex)
        {
            int lastEntity = cell.Entities[lastIndex];
            cell.Entities[indexInCell] = lastEntity;

            if (EntityData.TryGetValue(lastEntity, out var lastData))
            {
                lastData.IndexInCell = indexInCell;
                EntityData[lastEntity] = lastData;
            }
        }

        cell.Count--;
        Cells[data.CellKey] = cell;

        if (cell.Count == 0)
            Cells.Remove(data.CellKey);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Remove(int entity)
    {
        if (!EntityData.TryGetValue(entity, out var data))
            return;

        RemoveFromCell(entity, data);

        if (ExactLookup.TryGetValue(data.ExactKey, out int e) && e == entity)
            ExactLookup.Remove(data.ExactKey);

        EntityData.Remove(entity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Update(int entity, Vector3 position)
    {
        Insert(entity, position.X, position.Y, position.Z);
    }

    public void Clear()
    {
        Cells.Clear();
        EntityData.Clear();
        ExactLookup.Clear();
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
        long key = ToExactKey(position.X, position.Y, position.Z);
        return ExactLookup.TryGetValue(key, out int entity) ? entity : null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int? AtExact(float x, float y, float z)
    {
        long key = ToExactKey(x, y, z);
        return ExactLookup.TryGetValue(key, out int entity) ? entity : null;
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
                        if (!EntityData.TryGetValue(entity, out var data)) continue;

                        float dx = data.Position.X - cx;
                        float dy = data.Position.Y - cy;
                        float dz = data.Position.Z - cz;
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
                    if (!EntityData.TryGetValue(entity, out var data)) continue;

                    float dx = data.Position.X - cx;
                    float dz = data.Position.Z - cz;

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
                        if (!EntityData.TryGetValue(entity, out var data)) continue;

                        if (data.Position.X >= minX && data.Position.X <= maxX &&
                            data.Position.Y >= minY && data.Position.Y <= maxY &&
                            data.Position.Z >= minZ && data.Position.Z <= maxZ)
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
            if (EntityData.TryGetValue(entity, out var data))
            {
                float dx = data.Position.X - cx;
                float dy = data.Position.Y - cy;
                float dz = data.Position.Z - cz;
                DistanceCache[i] = dx * dx + dy * dy + dz * dz;
            }
            else
            {
                DistanceCache[i] = float.MaxValue;
            }
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