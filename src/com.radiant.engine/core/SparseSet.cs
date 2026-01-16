using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace com.radiant.engine.core;

public interface IComponentSet
{
    void Remove(int entity);
    bool Contains(int entity);
    int Count { get; }
    IEnumerable<int> GetEntityIds();
}

public sealed class SparseSet<T> : IComponentSet where T : struct
{
    private const int DefaultChunkSize = 10_000;

    private readonly Dictionary<int, int> sparse;
    private readonly List<int[]> denseChunks;
    private readonly List<T[]> componentChunks;
    private readonly int chunkSize;
    private int count;

    public int Count => count;

    public SparseSet(int chunkSize = DefaultChunkSize)
    {
        this.chunkSize = chunkSize;
        sparse = new Dictionary<int, int>();
        denseChunks = new List<int[]>();
        componentChunks = new List<T[]>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void GetChunkIndices(int denseIndex, out int chunkIndex, out int localIndex)
    {
        chunkIndex = denseIndex / chunkSize;
        localIndex = denseIndex % chunkSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(int entity) => sparse.ContainsKey(entity);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(int entity, in T component)
    {
        if (sparse.ContainsKey(entity))
            return;

        int denseIndex = count;
        GetChunkIndices(denseIndex, out int chunkIndex, out int localIndex);

        while (chunkIndex >= denseChunks.Count)
        {
            denseChunks.Add(new int[chunkSize]);
            componentChunks.Add(new T[chunkSize]);
        }

        sparse[entity] = denseIndex;
        denseChunks[chunkIndex][localIndex] = entity;
        componentChunks[chunkIndex][localIndex] = component;
        count++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Remove(int entity)
    {
        if (!sparse.TryGetValue(entity, out int index))
            return;

        int lastIndex = count - 1;

        if (index != lastIndex)
        {
            GetChunkIndices(lastIndex, out int lastChunk, out int lastLocal);
            int lastEntity = denseChunks[lastChunk][lastLocal];
            T lastComponent = componentChunks[lastChunk][lastLocal];

            GetChunkIndices(index, out int chunk, out int local);
            denseChunks[chunk][local] = lastEntity;
            componentChunks[chunk][local] = lastComponent;

            sparse[lastEntity] = index;
        }

        sparse.Remove(entity);
        count--;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get(int entity)
    {
        int denseIndex = sparse[entity];
        GetChunkIndices(denseIndex, out int chunkIndex, out int localIndex);
        return ref componentChunks[chunkIndex][localIndex];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(int entity, out int denseIndex)
    {
        return sparse.TryGetValue(entity, out denseIndex);
    }

    public IEnumerable<int> GetEntityIds()
    {
        for (int i = 0; i < count; i++)
        {
            GetChunkIndices(i, out int chunk, out int local);
            yield return denseChunks[chunk][local];
        }
    }

    public void TrimExcess()
    {
        int requiredChunks = count > 0 ? (count + chunkSize - 1) / chunkSize : 0;
        while (denseChunks.Count > requiredChunks)
        {
            denseChunks.RemoveAt(denseChunks.Count - 1);
            componentChunks.RemoveAt(componentChunks.Count - 1);
        }
        sparse.TrimExcess();
    }

    public void Clear()
    {
        sparse.Clear();
        denseChunks.Clear();
        componentChunks.Clear();
        count = 0;
    }
}