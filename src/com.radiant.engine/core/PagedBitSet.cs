using System;
using System.Runtime.CompilerServices;

namespace com.radiant.engine.core;

/// <summary>
/// Ultra-fast paged bitset for tracking entity active state.
/// Uses 1 bit per entity instead of ~24+ bytes for HashSet entry.
/// Power-of-2 sizes enable bit operations instead of division.
/// </summary>
public sealed class PagedBitSet
{
    // 4096 ulongs per page = 262,144 bits per page = 32KB per page (L1 cache friendly)
    private const int PageSize = 4096;
    private const int BitsPerUlong = 64;
    private const int BitShift = 6;
    private const int BitMask = BitsPerUlong - 1;
    private const int BitsPerPage = PageSize * BitsPerUlong; // 262,144 entities per page

    private ulong[][] pages;
    private int pageCount;
    private int count;

    public int Count => count;

    public PagedBitSet(int initialCapacity = 65536)
    {
        int requiredPages = Math.Max(1, (initialCapacity + BitsPerPage - 1) / BitsPerPage);
        pages = new ulong[requiredPages][];
        pageCount = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCapacity(int entityId)
    {
        int pageIndex = entityId / BitsPerPage;

        if (pageIndex >= pages.Length)
        {
            int newSize = Math.Max(pages.Length * 2, pageIndex + 1);
            Array.Resize(ref pages, newSize);
        }

        while (pageCount <= pageIndex)
        {
            pages[pageCount++] = new ulong[PageSize];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(int entityId)
    {
        if (entityId < 0) return false;

        int pageIndex = entityId / BitsPerPage;
        if (pageIndex >= pageCount) return false;

        var page = pages[pageIndex];
        if (page == null) return false;

        int localBit = entityId % BitsPerPage;
        int ulongIndex = localBit >> BitShift;
        int bitIndex = localBit & BitMask;

        return (page[ulongIndex] & (1UL << bitIndex)) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Add(int entityId)
    {
        if (entityId < 0) return false;

        EnsureCapacity(entityId);

        int pageIndex = entityId / BitsPerPage;
        int localBit = entityId % BitsPerPage;
        int ulongIndex = localBit >> BitShift;
        int bitIndex = localBit & BitMask;
        ulong mask = 1UL << bitIndex;

        ref ulong slot = ref pages[pageIndex][ulongIndex];
        if ((slot & mask) != 0) return false; // Already set

        slot |= mask;
        count++;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(int entityId)
    {
        if (entityId < 0) return false;

        int pageIndex = entityId / BitsPerPage;
        if (pageIndex >= pageCount) return false;

        var page = pages[pageIndex];
        if (page == null) return false;

        int localBit = entityId % BitsPerPage;
        int ulongIndex = localBit >> BitShift;
        int bitIndex = localBit & BitMask;
        ulong mask = 1UL << bitIndex;

        ref ulong slot = ref page[ulongIndex];
        if ((slot & mask) == 0) return false; // Not set

        slot &= ~mask;
        count--;
        return true;
    }

    public void Clear()
    {
        for (int i = 0; i < pageCount; i++)
        {
            if (pages[i] != null)
                Array.Clear(pages[i], 0, PageSize);
        }
        count = 0;
    }

    /// <summary>
    /// Iterate all set bits. Yields entity IDs.
    /// </summary>
    public EntityEnumerator GetEnumerator() => new EntityEnumerator(this);

    public ref struct EntityEnumerator
    {
        private readonly PagedBitSet bitset;
        private int pageIndex;
        private int ulongIndex;
        private ulong currentBits;
        private int currentEntity;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal EntityEnumerator(PagedBitSet bitset)
        {
            this.bitset = bitset;
            pageIndex = 0;
            ulongIndex = -1;
            currentBits = 0;
            currentEntity = -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            while (true)
            {
                // Check remaining bits in current ulong
                if (currentBits != 0)
                {
                    int bit = BitOperations.TrailingZeroCount(currentBits);
                    currentBits &= currentBits - 1; // Clear lowest bit
                    currentEntity = pageIndex * BitsPerPage + ulongIndex * BitsPerUlong + bit;
                    return true;
                }

                // Move to next ulong
                ulongIndex++;

                // Check if we need to move to next page
                while (ulongIndex >= PageSize || (pageIndex < bitset.pageCount && bitset.pages[pageIndex] == null))
                {
                    pageIndex++;
                    ulongIndex = 0;
                    if (pageIndex >= bitset.pageCount) return false;
                }

                if (pageIndex >= bitset.pageCount) return false;

                currentBits = bitset.pages[pageIndex][ulongIndex];
            }
        }

        public int Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => currentEntity;
        }
    }
}

// BitOperations polyfill for older .NET versions
file static class BitOperations
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int TrailingZeroCount(ulong value)
    {
        if (value == 0) return 64;

        int count = 0;
        if ((value & 0xFFFFFFFF) == 0) { count += 32; value >>= 32; }
        if ((value & 0xFFFF) == 0) { count += 16; value >>= 16; }
        if ((value & 0xFF) == 0) { count += 8; value >>= 8; }
        if ((value & 0xF) == 0) { count += 4; value >>= 4; }
        if ((value & 0x3) == 0) { count += 2; value >>= 2; }
        if ((value & 0x1) == 0) { count += 1; }
        return count;
    }
}
