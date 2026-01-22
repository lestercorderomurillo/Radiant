using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace com.radiant.engine.core;

public ref struct View<T1, T2>
    where T1 : struct, Component
    where T2 : struct, Component
{
    private readonly SparseSet<T1> set1;
    private readonly SparseSet<T2> set2;
    private readonly HashSet<int> activeEntities;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal View(SparseSet<T1> set1, SparseSet<T2> set2, HashSet<int> activeEntities)
    {
        this.set1 = set1;
        this.set2 = set2;
        this.activeEntities = activeEntities;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Enumerator GetEnumerator() => new Enumerator(set1, set2, activeEntities);

    public ref struct Enumerator
    {
        private readonly SparseSet<T1> set1;
        private readonly SparseSet<T2> set2;
        private readonly HashSet<int> activeEntities;
        private readonly int count;
        private int index;
        private int currentEntity;
        private int idx1, idx2;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Enumerator(SparseSet<T1> set1, SparseSet<T2> set2, HashSet<int> activeEntities)
        {
            this.set1 = set1;
            this.set2 = set2;
            this.activeEntities = activeEntities;
            // Iterate smaller set
            count = set1.Count <= set2.Count ? set1.Count : set2.Count;
            index = -1;
            currentEntity = -1;
            idx1 = idx2 = -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            while (++index < count)
            {
                if (set1.Count <= set2.Count)
                {
                    currentEntity = set1.GetEntityAt(index);
                    idx1 = index;
                    idx2 = set2.GetDenseIndex(currentEntity);
                    if (idx2 < 0) continue;
                }
                else
                {
                    currentEntity = set2.GetEntityAt(index);
                    idx2 = index;
                    idx1 = set1.GetDenseIndex(currentEntity);
                    if (idx1 < 0) continue;
                }

                if (!activeEntities.Contains(currentEntity)) continue;
                return true;
            }
            return false;
        }

        public int Entity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => currentEntity;
        }

        public ref T1 Component1
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref set1.GetComponentAt(idx1);
        }

        public ref T2 Component2
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref set2.GetComponentAt(idx2);
        }

        public Entry Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new Entry(currentEntity, ref set1.GetComponentAt(idx1), ref set2.GetComponentAt(idx2));
        }
    }

    public readonly ref struct Entry
    {
        public readonly int Entity;
        public readonly ref T1 C1;
        public readonly ref T2 C2;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entry(int entity, ref T1 c1, ref T2 c2)
        {
            Entity = entity;
            C1 = ref c1;
            C2 = ref c2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Deconstruct(out int entity, out Refs refs)
        {
            entity = Entity;
            refs = new Refs(ref C1, ref C2);
        }
    }

    public readonly ref struct Refs
    {
        public readonly ref T1 C1;
        public readonly ref T2 C2;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Refs(ref T1 c1, ref T2 c2)
        {
            C1 = ref c1;
            C2 = ref c2;
        }
    }
}

public ref struct View<T1, T2, T3>
    where T1 : struct, Component
    where T2 : struct, Component
    where T3 : struct, Component
{
    private readonly SparseSet<T1> set1;
    private readonly SparseSet<T2> set2;
    private readonly SparseSet<T3> set3;
    private readonly HashSet<int> activeEntities;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal View(SparseSet<T1> set1, SparseSet<T2> set2, SparseSet<T3> set3, HashSet<int> activeEntities)
    {
        this.set1 = set1;
        this.set2 = set2;
        this.set3 = set3;
        this.activeEntities = activeEntities;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Enumerator GetEnumerator() => new Enumerator(set1, set2, set3, activeEntities);

    public ref struct Enumerator
    {
        private readonly SparseSet<T1> set1;
        private readonly SparseSet<T2> set2;
        private readonly SparseSet<T3> set3;
        private readonly HashSet<int> activeEntities;
        private readonly int smallestIdx;
        private readonly int count;
        private int index;
        private int currentEntity;
        private int idx1, idx2, idx3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Enumerator(SparseSet<T1> set1, SparseSet<T2> set2, SparseSet<T3> set3, HashSet<int> activeEntities)
        {
            this.set1 = set1;
            this.set2 = set2;
            this.set3 = set3;
            this.activeEntities = activeEntities;

            // Find smallest
            smallestIdx = 1;
            count = set1.Count;
            if (set2.Count < count) { count = set2.Count; smallestIdx = 2; }
            if (set3.Count < count) { count = set3.Count; smallestIdx = 3; }

            index = -1;
            currentEntity = -1;
            idx1 = idx2 = idx3 = -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            while (++index < count)
            {
                switch (smallestIdx)
                {
                    case 1:
                        currentEntity = set1.GetEntityAt(index);
                        idx1 = index;
                        idx2 = set2.GetDenseIndex(currentEntity); if (idx2 < 0) continue;
                        idx3 = set3.GetDenseIndex(currentEntity); if (idx3 < 0) continue;
                        break;
                    case 2:
                        currentEntity = set2.GetEntityAt(index);
                        idx2 = index;
                        idx1 = set1.GetDenseIndex(currentEntity); if (idx1 < 0) continue;
                        idx3 = set3.GetDenseIndex(currentEntity); if (idx3 < 0) continue;
                        break;
                    default:
                        currentEntity = set3.GetEntityAt(index);
                        idx3 = index;
                        idx1 = set1.GetDenseIndex(currentEntity); if (idx1 < 0) continue;
                        idx2 = set2.GetDenseIndex(currentEntity); if (idx2 < 0) continue;
                        break;
                }

                if (!activeEntities.Contains(currentEntity)) continue;
                return true;
            }
            return false;
        }

        public int Entity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => currentEntity;
        }

        public ref T1 Component1
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref set1.GetComponentAt(idx1);
        }

        public ref T2 Component2
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref set2.GetComponentAt(idx2);
        }

        public ref T3 Component3
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref set3.GetComponentAt(idx3);
        }

        public Entry Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new Entry(currentEntity, ref set1.GetComponentAt(idx1), ref set2.GetComponentAt(idx2), ref set3.GetComponentAt(idx3));
        }
    }

    public readonly ref struct Entry
    {
        public readonly int Entity;
        public readonly ref T1 C1;
        public readonly ref T2 C2;
        public readonly ref T3 C3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entry(int entity, ref T1 c1, ref T2 c2, ref T3 c3)
        {
            Entity = entity;
            C1 = ref c1;
            C2 = ref c2;
            C3 = ref c3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Deconstruct(out int entity, out Refs refs)
        {
            entity = Entity;
            refs = new Refs(ref C1, ref C2, ref C3);
        }
    }

    public readonly ref struct Refs
    {
        public readonly ref T1 C1;
        public readonly ref T2 C2;
        public readonly ref T3 C3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Refs(ref T1 c1, ref T2 c2, ref T3 c3)
        {
            C1 = ref c1;
            C2 = ref c2;
            C3 = ref c3;
        }
    }
}
