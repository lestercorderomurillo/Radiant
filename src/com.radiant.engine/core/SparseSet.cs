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

    private int[][] sparsePages;
    private int sparsePageCount;

    private int[][] denseChunks;
    private T[][] componentChunks;
    private int denseChunkCount;

    private int count;

    public int Count => count;

    public SparseSet()
    {
        // Start with 4 sparse pages (can address ~65K entities)
        sparsePages = new int[4][];
        sparsePageCount = 0;

        // Start with 2 dense chunks
        denseChunks = new int[2][];
        componentChunks = new T[2][];
        denseChunkCount = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureSparseCapacity(int entityId)
    {
        int pageIndex = entityId >> SparsePageShift;

        if (pageIndex >= sparsePages.Length)
        {
            int newSize = Math.Max(sparsePages.Length * 2, pageIndex + 1);
            Array.Resize(ref sparsePages, newSize);
        }

        while (sparsePageCount <= pageIndex)
        {
            var page = new int[SparsePageSize];
            Array.Fill(page, InvalidIndex);
            sparsePages[sparsePageCount++] = page;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureDenseCapacity(int denseIndex)
    {
        int chunkIndex = denseIndex >> DenseChunkShift;

        if (chunkIndex >= denseChunks.Length)
        {
            int newSize = Math.Max(denseChunks.Length * 2, chunkIndex + 1);
            Array.Resize(ref denseChunks, newSize);
            Array.Resize(ref componentChunks, newSize);
        }

        while (denseChunkCount <= chunkIndex)
        {
            denseChunks[denseChunkCount] = new int[DenseChunkSize];
            componentChunks[denseChunkCount] = new T[DenseChunkSize];
            denseChunkCount++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(int entity)
    {
        if (entity < 0) return false;

        int pageIndex = entity >> SparsePageShift;
        if (pageIndex >= sparsePageCount) return false;

        var page = sparsePages[pageIndex];
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
        if (sparsePages[pageIndex][localIndex] != InvalidIndex)
            return;

        int denseIndex = count;
        EnsureDenseCapacity(denseIndex);

        int chunkIndex = denseIndex >> DenseChunkShift;
        int chunkLocal = denseIndex & DenseChunkMask;

        sparsePages[pageIndex][localIndex] = denseIndex;
        denseChunks[chunkIndex][chunkLocal] = entity;
        componentChunks[chunkIndex][chunkLocal] = component;
        count++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Remove(int entity)
    {
        if (entity < 0) return;

        int pageIndex = entity >> SparsePageShift;
        if (pageIndex >= sparsePageCount) return;

        var page = sparsePages[pageIndex];
        if (page == null) return;

        int localIndex = entity & SparsePageMask;
        int denseIndex = page[localIndex];
        if (denseIndex == InvalidIndex) return;

        int lastDenseIndex = count - 1;

        if (denseIndex != lastDenseIndex)
        {
            // Swap with last element
            int lastChunk = lastDenseIndex >> DenseChunkShift;
            int lastLocal = lastDenseIndex & DenseChunkMask;
            int lastEntity = denseChunks[lastChunk][lastLocal];
            T lastComponent = componentChunks[lastChunk][lastLocal];

            int chunk = denseIndex >> DenseChunkShift;
            int local = denseIndex & DenseChunkMask;
            denseChunks[chunk][local] = lastEntity;
            componentChunks[chunk][local] = lastComponent;

            // Update sparse for swapped entity
            int lastPageIndex = lastEntity >> SparsePageShift;
            int lastPageLocal = lastEntity & SparsePageMask;
            sparsePages[lastPageIndex][lastPageLocal] = denseIndex;
        }

        page[localIndex] = InvalidIndex;
        count--;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get(int entity)
    {
        int pageIndex = entity >> SparsePageShift;
        int localIndex = entity & SparsePageMask;
        int denseIndex = sparsePages[pageIndex][localIndex];

        int chunkIndex = denseIndex >> DenseChunkShift;
        int chunkLocal = denseIndex & DenseChunkMask;
        return ref componentChunks[chunkIndex][chunkLocal];
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
        if (pageIndex >= sparsePageCount)
        {
            denseIndex = InvalidIndex;
            return false;
        }

        var page = sparsePages[pageIndex];
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
        for (int i = 0; i < count; i++)
        {
            int chunk = i >> DenseChunkShift;
            int local = i & DenseChunkMask;
            yield return denseChunks[chunk][local];
        }
    }

    public void TrimExcess()
    {
        int requiredChunks = count > 0 ? ((count - 1) >> DenseChunkShift) + 1 : 0;
        while (denseChunkCount > requiredChunks)
        {
            denseChunkCount--;
            denseChunks[denseChunkCount] = null;
            componentChunks[denseChunkCount] = null;
        }
    }

    public void Clear()
    {
        // Reset sparse pages
        for (int i = 0; i < sparsePageCount; i++)
        {
            if (sparsePages[i] != null)
                Array.Fill(sparsePages[i], InvalidIndex);
        }

        // Don't deallocate, just reset count
        count = 0;
    }

    // Direct iteration access - avoids GetComponent lookups
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetEntityAt(int denseIndex)
    {
        int chunk = denseIndex >> DenseChunkShift;
        int local = denseIndex & DenseChunkMask;
        return denseChunks[chunk][local];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetComponentAt(int denseIndex)
    {
        int chunk = denseIndex >> DenseChunkShift;
        int local = denseIndex & DenseChunkMask;
        return ref componentChunks[chunk][local];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetDenseIndex(int entity)
    {
        if (entity < 0) return InvalidIndex;

        int pageIndex = entity >> SparsePageShift;
        if (pageIndex >= sparsePageCount) return InvalidIndex;

        var page = sparsePages[pageIndex];
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
        if (chunkIndex >= denseChunkCount)
        {
            entities = ReadOnlySpan<int>.Empty;
            components = Span<T>.Empty;
            chunkCount = 0;
            return;
        }

        int startIndex = chunkIndex << DenseChunkShift;
        int endIndex = Math.Min(startIndex + DenseChunkSize, count);
        chunkCount = endIndex - startIndex;

        entities = new ReadOnlySpan<int>(denseChunks[chunkIndex], 0, chunkCount);
        components = new Span<T>(componentChunks[chunkIndex], 0, chunkCount);
    }

    public int ChunkCount => denseChunkCount;
}
