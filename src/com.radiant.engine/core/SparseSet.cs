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

/// <summary>
/// High-performance sparse set optimized for millions of entities.
/// Uses paged arrays instead of Dictionary for O(1) direct indexing.
/// Power-of-2 page sizes enable bit operations instead of division.
/// </summary>
public sealed class SparseSet<T> : IComponentSet where T : struct
{
    // Sparse array: entity ID -> dense index (paged)
    // 16384 entries per page = 64KB per page (good for L2/L3 cache)
    private const int SparsePageShift = 14;
    private const int SparsePageSize = 1 << SparsePageShift;   // 16384
    private const int SparsePageMask = SparsePageSize - 1;
    private const int InvalidIndex = -1;

    // Dense arrays: chunked for component storage
    // 8192 entries per chunk for components (may be larger structs)
    private const int DenseChunkShift = 13;
    private const int DenseChunkSize = 1 << DenseChunkShift;   // 8192
    private const int DenseChunkMask = DenseChunkSize - 1;

    private int[][] SparsePages;
    private int SparsePageCount;

    private int[][] DenseChunks;
    private T[][] ComponentChunks;
    private int DenseChunkCount;

    private int Count;

    public int EntityCount => Count;
    int IComponentSet.Count => Count;

    public SparseSet()
    {
        // Start with 4 sparse pages (can address ~65K entities)
        SparsePages = new int[4][];
        SparsePageCount = 0;

        // Start with 2 dense chunks
        DenseChunks = new int[2][];
        ComponentChunks = new T[2][];
        DenseChunkCount = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureSparseCapacity(int entityId)
    {
        int pageIndex = entityId >> SparsePageShift;

        if (pageIndex >= SparsePages.Length)
        {
            int newSize = Math.Max(SparsePages.Length * 2, pageIndex + 1);
            Array.Resize(ref SparsePages, newSize);
        }

        while (SparsePageCount <= pageIndex)
        {
            var page = new int[SparsePageSize];
            Array.Fill(page, InvalidIndex);
            SparsePages[SparsePageCount++] = page;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureDenseCapacity(int denseIndex)
    {
        int chunkIndex = denseIndex >> DenseChunkShift;

        if (chunkIndex >= DenseChunks.Length)
        {
            int newSize = Math.Max(DenseChunks.Length * 2, chunkIndex + 1);
            Array.Resize(ref DenseChunks, newSize);
            Array.Resize(ref ComponentChunks, newSize);
        }

        while (DenseChunkCount <= chunkIndex)
        {
            DenseChunks[DenseChunkCount] = new int[DenseChunkSize];
            ComponentChunks[DenseChunkCount] = new T[DenseChunkSize];
            DenseChunkCount++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(int entity)
    {
        if (entity < 0) return false;

        int pageIndex = entity >> SparsePageShift;
        if (pageIndex >= SparsePageCount) return false;

        var page = SparsePages[pageIndex];
        if (page == null) return false;

        int localIndex = entity & SparsePageMask;
        return page[localIndex] != InvalidIndex;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(int entity, in T component)
    {
        if (entity < 0) return;

        EnsureSparseCapacity(entity);

        int pageIndex = entity >> SparsePageShift;
        int localIndex = entity & SparsePageMask;

        // Already exists?
        if (SparsePages[pageIndex][localIndex] != InvalidIndex)
            return;

        int denseIndex = Count;
        EnsureDenseCapacity(denseIndex);

        int chunkIndex = denseIndex >> DenseChunkShift;
        int chunkLocal = denseIndex & DenseChunkMask;

        SparsePages[pageIndex][localIndex] = denseIndex;
        DenseChunks[chunkIndex][chunkLocal] = entity;
        ComponentChunks[chunkIndex][chunkLocal] = component;
        Count++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Remove(int entity)
    {
        if (entity < 0) return;

        int pageIndex = entity >> SparsePageShift;
        if (pageIndex >= SparsePageCount) return;

        var page = SparsePages[pageIndex];
        if (page == null) return;

        int localIndex = entity & SparsePageMask;
        int denseIndex = page[localIndex];
        if (denseIndex == InvalidIndex) return;

        int lastDenseIndex = Count - 1;

        if (denseIndex != lastDenseIndex)
        {
            // Swap with last element
            int lastChunk = lastDenseIndex >> DenseChunkShift;
            int lastLocal = lastDenseIndex & DenseChunkMask;
            int lastEntity = DenseChunks[lastChunk][lastLocal];
            T lastComponent = ComponentChunks[lastChunk][lastLocal];

            int chunk = denseIndex >> DenseChunkShift;
            int local = denseIndex & DenseChunkMask;
            DenseChunks[chunk][local] = lastEntity;
            ComponentChunks[chunk][local] = lastComponent;

            // Update sparse for swapped entity
            int lastPageIndex = lastEntity >> SparsePageShift;
            int lastPageLocal = lastEntity & SparsePageMask;
            SparsePages[lastPageIndex][lastPageLocal] = denseIndex;
        }

        page[localIndex] = InvalidIndex;
        Count--;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get(int entity)
    {
        int pageIndex = entity >> SparsePageShift;
        int localIndex = entity & SparsePageMask;
        int denseIndex = SparsePages[pageIndex][localIndex];

        int chunkIndex = denseIndex >> DenseChunkShift;
        int chunkLocal = denseIndex & DenseChunkMask;
        return ref ComponentChunks[chunkIndex][chunkLocal];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(int entity, out int denseIndex)
    {
        if (entity < 0)
        {
            denseIndex = InvalidIndex;
            return false;
        }

        int pageIndex = entity >> SparsePageShift;
        if (pageIndex >= SparsePageCount)
        {
            denseIndex = InvalidIndex;
            return false;
        }

        var page = SparsePages[pageIndex];
        if (page == null)
        {
            denseIndex = InvalidIndex;
            return false;
        }

        int localIndex = entity & SparsePageMask;
        denseIndex = page[localIndex];
        return denseIndex != InvalidIndex;
    }

    public IEnumerable<int> GetEntityIds()
    {
        for (int i = 0; i < Count; i++)
        {
            int chunk = i >> DenseChunkShift;
            int local = i & DenseChunkMask;
            yield return DenseChunks[chunk][local];
        }
    }

    public void TrimExcess()
    {
        int requiredChunks = Count > 0 ? ((Count - 1) >> DenseChunkShift) + 1 : 0;
        while (DenseChunkCount > requiredChunks)
        {
            DenseChunkCount--;
            DenseChunks[DenseChunkCount] = null;
            ComponentChunks[DenseChunkCount] = null;
        }
    }

    public void Clear()
    {
        // Reset sparse pages
        for (int i = 0; i < SparsePageCount; i++)
        {
            if (SparsePages[i] != null)
                Array.Fill(SparsePages[i], InvalidIndex);
        }

        // Don't deallocate, just reset count
        Count = 0;
    }

    // Direct iteration access - avoids GetComponent lookups
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetEntityAt(int denseIndex)
    {
        int chunk = denseIndex >> DenseChunkShift;
        int local = denseIndex & DenseChunkMask;
        return DenseChunks[chunk][local];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetComponentAt(int denseIndex)
    {
        int chunk = denseIndex >> DenseChunkShift;
        int local = denseIndex & DenseChunkMask;
        return ref ComponentChunks[chunk][local];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetDenseIndex(int entity)
    {
        if (entity < 0) return InvalidIndex;

        int pageIndex = entity >> SparsePageShift;
        if (pageIndex >= SparsePageCount) return InvalidIndex;

        var page = SparsePages[pageIndex];
        if (page == null) return InvalidIndex;

        int localIndex = entity & SparsePageMask;
        return page[localIndex];
    }

    /// <summary>
    /// Get raw access to dense component chunks for batch processing.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void GetChunkData(int chunkIndex, out ReadOnlySpan<int> entities, out Span<T> components, out int chunkCount)
    {
        if (chunkIndex >= DenseChunkCount)
        {
            entities = ReadOnlySpan<int>.Empty;
            components = Span<T>.Empty;
            chunkCount = 0;
            return;
        }

        int startIndex = chunkIndex << DenseChunkShift;
        int endIndex = Math.Min(startIndex + DenseChunkSize, Count);
        chunkCount = endIndex - startIndex;

        entities = new ReadOnlySpan<int>(DenseChunks[chunkIndex], 0, chunkCount);
        components = new Span<T>(ComponentChunks[chunkIndex], 0, chunkCount);
    }

    public int ChunkCount => DenseChunkCount;
}
