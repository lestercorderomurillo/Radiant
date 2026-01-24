using System.Runtime.CompilerServices;

namespace com.radiant.engine.core;

public ref struct View<T1, T2>
    where T1 : struct, Component
    where T2 : struct, Component
{
    private readonly SparseSet<T1> Set1;
    private readonly SparseSet<T2> Set2;
    private readonly PagedBitSet ActiveEntities;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal View(SparseSet<T1> set1, SparseSet<T2> set2, PagedBitSet activeEntities)
    {
        Set1 = set1;
        Set2 = set2;
        ActiveEntities = activeEntities;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Enumerator GetEnumerator() => new Enumerator(Set1, Set2, ActiveEntities);

    public ref struct Enumerator
    {
        private readonly SparseSet<T1> Set1;
        private readonly SparseSet<T2> Set2;
        private readonly PagedBitSet ActiveEntities;
        private readonly int Count;
        private int Index;
        private int CurrentEntity;
        private int Idx1, Idx2;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Enumerator(SparseSet<T1> set1, SparseSet<T2> set2, PagedBitSet activeEntities)
        {
            Set1 = set1;
            Set2 = set2;
            ActiveEntities = activeEntities;
            Count = set1.EntityCount <= set2.EntityCount ? set1.EntityCount : set2.EntityCount;
            Index = -1;
            CurrentEntity = -1;
            Idx1 = Idx2 = -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            while (++Index < Count)
            {
                if (Set1.EntityCount <= Set2.EntityCount)
                {
                    CurrentEntity = Set1.GetEntityAt(Index);
                    Idx1 = Index;
                    Idx2 = Set2.GetDenseIndex(CurrentEntity);
                    if (Idx2 < 0) continue;
                }
                else
                {
                    CurrentEntity = Set2.GetEntityAt(Index);
                    Idx2 = Index;
                    Idx1 = Set1.GetDenseIndex(CurrentEntity);
                    if (Idx1 < 0) continue;
                }

                if (!ActiveEntities.Contains(CurrentEntity)) continue;
                return true;
            }
            return false;
        }

        public int Entity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => CurrentEntity;
        }

        public ref T1 Component1
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref Set1.GetComponentAt(Idx1);
        }

        public ref T2 Component2
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref Set2.GetComponentAt(Idx2);
        }

        public Entry Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new Entry(CurrentEntity, ref Set1.GetComponentAt(Idx1), ref Set2.GetComponentAt(Idx2));
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
    private readonly SparseSet<T1> Set1;
    private readonly SparseSet<T2> Set2;
    private readonly SparseSet<T3> Set3;
    private readonly PagedBitSet ActiveEntities;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal View(SparseSet<T1> set1, SparseSet<T2> set2, SparseSet<T3> set3, PagedBitSet activeEntities)
    {
        Set1 = set1;
        Set2 = set2;
        Set3 = set3;
        ActiveEntities = activeEntities;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Enumerator GetEnumerator() => new Enumerator(Set1, Set2, Set3, ActiveEntities);

    public ref struct Enumerator
    {
        private readonly SparseSet<T1> Set1;
        private readonly SparseSet<T2> Set2;
        private readonly SparseSet<T3> Set3;
        private readonly PagedBitSet ActiveEntities;
        private readonly int SmallestIdx;
        private readonly int Count;
        private int Index;
        private int CurrentEntity;
        private int Idx1, Idx2, Idx3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Enumerator(SparseSet<T1> set1, SparseSet<T2> set2, SparseSet<T3> set3, PagedBitSet activeEntities)
        {
            Set1 = set1;
            Set2 = set2;
            Set3 = set3;
            ActiveEntities = activeEntities;

            SmallestIdx = 1;
            Count = set1.EntityCount;
            if (set2.EntityCount < Count) { Count = set2.EntityCount; SmallestIdx = 2; }
            if (set3.EntityCount < Count) { Count = set3.EntityCount; SmallestIdx = 3; }

            Index = -1;
            CurrentEntity = -1;
            Idx1 = Idx2 = Idx3 = -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            while (++Index < Count)
            {
                switch (SmallestIdx)
                {
                    case 1:
                        CurrentEntity = Set1.GetEntityAt(Index);
                        Idx1 = Index;
                        Idx2 = Set2.GetDenseIndex(CurrentEntity); if (Idx2 < 0) continue;
                        Idx3 = Set3.GetDenseIndex(CurrentEntity); if (Idx3 < 0) continue;
                        break;
                    case 2:
                        CurrentEntity = Set2.GetEntityAt(Index);
                        Idx2 = Index;
                        Idx1 = Set1.GetDenseIndex(CurrentEntity); if (Idx1 < 0) continue;
                        Idx3 = Set3.GetDenseIndex(CurrentEntity); if (Idx3 < 0) continue;
                        break;
                    default:
                        CurrentEntity = Set3.GetEntityAt(Index);
                        Idx3 = Index;
                        Idx1 = Set1.GetDenseIndex(CurrentEntity); if (Idx1 < 0) continue;
                        Idx2 = Set2.GetDenseIndex(CurrentEntity); if (Idx2 < 0) continue;
                        break;
                }

                if (!ActiveEntities.Contains(CurrentEntity)) continue;
                return true;
            }
            return false;
        }

        public int Entity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => CurrentEntity;
        }

        public ref T1 Component1
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref Set1.GetComponentAt(Idx1);
        }

        public ref T2 Component2
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref Set2.GetComponentAt(Idx2);
        }

        public ref T3 Component3
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref Set3.GetComponentAt(Idx3);
        }

        public Entry Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new Entry(CurrentEntity, ref Set1.GetComponentAt(Idx1), ref Set2.GetComponentAt(Idx2), ref Set3.GetComponentAt(Idx3));
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
