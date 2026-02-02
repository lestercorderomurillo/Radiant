using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace com.radiant.engine.core;

/// <summary>
/// Archetype stores entities with identical component combinations.
/// Components are stored in contiguous arrays for cache-friendly iteration.
/// </summary>
public sealed class Archetype
{
    public readonly Type[] Types;
    public readonly ulong Signature;

    private readonly Dictionary<Type, Array> Components;
    private int[] Entities;
    private int Count;
    private int Capacity;

    private const int InitialCapacity = 64;

    public int EntityCount => Count;

    public Archetype(Type[] types, ulong signature)
    {
        Types = types;
        Signature = signature;
        Components = new Dictionary<Type, Array>(types.Length);
        Entities = new int[InitialCapacity];
        Capacity = InitialCapacity;

        foreach (var type in types)
            Components[type] = Array.CreateInstance(type, InitialCapacity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasComponent<T>() where T : struct => Components.ContainsKey(typeof(T));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T[] GetArray<T>() where T : struct => (T[])Components[typeof(T)];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int[] GetEntities() => Entities;

    public int Add(int entity)
    {
        if (Count >= Capacity)
            Grow();

        int index = Count++;
        Entities[index] = entity;
        return index;
    }

    public void Set<T>(int index, in T value) where T : struct
    {
        ((T[])Components[typeof(T)])[index] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get<T>(int index) where T : struct
    {
        return ref ((T[])Components[typeof(T)])[index];
    }

    public int Remove(int index)
    {
        int lastIndex = --Count;

        if (index != lastIndex)
        {
            // Swap with last
            Entities[index] = Entities[lastIndex];
            foreach (var kvp in Components)
            {
                Array.Copy(kvp.Value, lastIndex, kvp.Value, index, 1);
            }
        }

        return lastIndex != index ? Entities[index] : -1; // Return moved entity or -1
    }

    private void Grow()
    {
        int newCapacity = Capacity * 2;

        Array.Resize(ref Entities, newCapacity);

        foreach (var type in Types)
        {
            var oldArray = Components[type];
            var newArray = Array.CreateInstance(type, newCapacity);
            Array.Copy(oldArray, newArray, Count);
            Components[type] = newArray;
        }

        Capacity = newCapacity;
    }

    public void CopyComponentsFrom(Archetype source, int sourceIndex, int destIndex)
    {
        foreach (var type in Types)
        {
            if (source.Components.TryGetValue(type, out var srcArray))
            {
                Array.Copy(srcArray, sourceIndex, Components[type], destIndex, 1);
            }
        }
    }
}
