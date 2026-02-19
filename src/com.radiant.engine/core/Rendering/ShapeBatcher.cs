using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.core;

/// <summary>
/// GPU-instanced shape renderer. Batches shapes into a single draw call using
/// hardware instancing. Supports parallel shape collection with per-thread buffers
/// and k-way merge by Z order.
/// </summary>
internal class ShapeBatcher : IDisposable
{
    private const int DefaultShapeCapacity = 65536;
    private const int MaxShapeCapacity = int.MaxValue;

    private readonly GraphicsDevice Device;
    private readonly float VirtualWidth;
    private readonly float VirtualHeight;

    private VertexBuffer QuadBuffer;
    private IndexBuffer IndexBuffer;
    private DynamicVertexBuffer InstanceBuffer;
    private Shape[] Shapes;
    private int ShapeCapacity;

    /// <summary>Current number of shapes in the main batch.</summary>
    public int Count;

    /// <summary>Time in milliseconds for the last SetData (GPU upload) call.</summary>
    public float LastSetDataMs { get; private set; }

    /// <summary>Time in milliseconds for the last DrawInstancedPrimitives call.</summary>
    public float LastDrawMs { get; private set; }

    private int ParallelThreadCount;
    private Shape[][] ParallelShapeBuffers;
    private float[][] ParallelZBuffers;
    private int[][] ParallelSortIndices;
    private int[] ParallelShapeCounts;
    private int[] ParallelMergeIndices;
    private int ParallelCapacityPerThread;
    private (float z, int thread)[] MergeHeap;
    private int MergeHeapSize;

    public ShapeBatcher(GraphicsDevice device, float virtualWidth, float virtualHeight)
    {
        Device = device;
        VirtualWidth = virtualWidth;
        VirtualHeight = virtualHeight;

        var vertices = new ShapeQuadVertex[]
        {
            new(new Vector2(0, 0), new Vector2(0, 0)),
            new(new Vector2(1, 0), new Vector2(1, 0)),
            new(new Vector2(0, 1), new Vector2(0, 1)),
            new(new Vector2(1, 1), new Vector2(1, 1))
        };

        QuadBuffer = new VertexBuffer(Device, ShapeQuadVertex.Declaration, 4, BufferUsage.WriteOnly);
        QuadBuffer.SetData(vertices);

        var indices = new short[] { 0, 1, 2, 2, 1, 3 };
        IndexBuffer = new IndexBuffer(Device, IndexElementSize.SixteenBits, 6, BufferUsage.WriteOnly);
        IndexBuffer.SetData(indices);

        ShapeCapacity = DefaultShapeCapacity;
        Shapes = new Shape[ShapeCapacity];
        InstanceBuffer = new DynamicVertexBuffer(Device, Shape.Declaration, ShapeCapacity, BufferUsage.WriteOnly);
    }

    /// <summary>Adds a shape to the main batch.</summary>
    public void Add(Shape shape)
    {
        if (Count >= MaxShapeCapacity) return;
        EnsureCapacity(Count + 1);
        Shapes[Count++] = shape;
    }

    /// <summary>Grows the main shape buffer and instance buffer if needed.</summary>
    public void EnsureCapacity(int required)
    {
        if (required <= ShapeCapacity) return;

        int newCapacity = ShapeCapacity;
        while (newCapacity < required && newCapacity < MaxShapeCapacity)
            newCapacity *= 2;

        if (newCapacity > MaxShapeCapacity)
            newCapacity = MaxShapeCapacity;

        Array.Resize(ref Shapes, newCapacity);
        InstanceBuffer?.Dispose();
        InstanceBuffer = new DynamicVertexBuffer(Device, Shape.Declaration, newCapacity, BufferUsage.WriteOnly);
        ShapeCapacity = newCapacity;
    }

    /// <summary>Resets the main batch count to zero.</summary>
    public void Clear()
    {
        Count = 0;
    }

    /// <summary>
    /// Uploads and renders all batched shapes in a single instanced draw call.
    /// Clears the batch after rendering.
    /// </summary>
    public void Flush(RenderTarget2D target, Color? clearColor, BlendState blendState, Effect shapeShader, string technique)
    {
        Device.SetRenderTarget(target);

        if (clearColor.HasValue)
            Device.Clear(clearColor.Value);

        if (Count > 0)
        {
            InstanceBuffer.SetData(Shapes, 0, Count, SetDataOptions.Discard);

            Device.BlendState = blendState;
            Device.DepthStencilState = DepthStencilState.None;
            Device.RasterizerState = RasterizerState.CullNone;

            Device.SetVertexBuffers(
                new VertexBufferBinding(QuadBuffer, 0, 0),
                new VertexBufferBinding(InstanceBuffer, 0, 1)
            );
            Device.Indices = IndexBuffer;

            var viewProjection = Matrix.CreateOrthographicOffCenter(0, VirtualWidth, VirtualHeight, 0, 0, 1);

            shapeShader.CurrentTechnique = shapeShader.Techniques[technique];
            shapeShader.Parameters["ViewProjection"]?.SetValue(viewProjection);

            foreach (var pass in shapeShader.CurrentTechnique.Passes)
            {
                pass.Apply();
                Device.DrawInstancedPrimitives(PrimitiveType.TriangleList, 0, 0, 2, Count);
            }

            Count = 0;
        }
    }

    /// <summary>
    /// Renders shapes from an external buffer without copying into the internal array.
    /// Auto-grows the instance buffer if needed.
    /// </summary>
    public void FlushExternal(Shape[] shapes, int count, RenderTarget2D target, Color? clearColor, BlendState blendState, Effect shapeShader, string technique)
    {
        Device.SetRenderTarget(target);

        if (clearColor.HasValue)
            Device.Clear(clearColor.Value);

        if (count > 0)
        {
            if (count > ShapeCapacity)
            {
                int newCapacity = ShapeCapacity;
                while (newCapacity < count && newCapacity < MaxShapeCapacity)
                    newCapacity *= 2;
                if (newCapacity > MaxShapeCapacity)
                    newCapacity = MaxShapeCapacity;

                InstanceBuffer?.Dispose();
                InstanceBuffer = new DynamicVertexBuffer(Device, Shape.Declaration, newCapacity, BufferUsage.WriteOnly);
                ShapeCapacity = newCapacity;
            }

            int uploadCount = Math.Min(count, ShapeCapacity);
            var swSetData = Stopwatch.StartNew();
            InstanceBuffer.SetData(shapes, 0, uploadCount, SetDataOptions.Discard);
            LastSetDataMs = (float)swSetData.Elapsed.TotalMilliseconds;

            Device.BlendState = blendState;
            Device.DepthStencilState = DepthStencilState.None;
            Device.RasterizerState = RasterizerState.CullNone;

            Device.SetVertexBuffers(
                new VertexBufferBinding(QuadBuffer, 0, 0),
                new VertexBufferBinding(InstanceBuffer, 0, 1)
            );
            Device.Indices = IndexBuffer;

            var viewProjection = Matrix.CreateOrthographicOffCenter(0, VirtualWidth, VirtualHeight, 0, 0, 1);

            shapeShader.CurrentTechnique = shapeShader.Techniques[technique];
            shapeShader.Parameters["ViewProjection"]?.SetValue(viewProjection);

            var swDraw = Stopwatch.StartNew();
            foreach (var pass in shapeShader.CurrentTechnique.Passes)
            {
                pass.Apply();
                Device.DrawInstancedPrimitives(PrimitiveType.TriangleList, 0, 0, 2, uploadCount);
            }
            LastDrawMs = (float)swDraw.Elapsed.TotalMilliseconds;
        }
    }

    /// <summary>
    /// Allocates per-thread shape and Z buffers for parallel collection.
    /// </summary>
    public void InitializeParallel(int threadCount, int capacityPerThread = 16384)
    {
        ParallelThreadCount = threadCount;
        ParallelCapacityPerThread = capacityPerThread;
        ParallelShapeBuffers = new Shape[threadCount][];
        ParallelZBuffers = new float[threadCount][];
        ParallelSortIndices = new int[threadCount][];
        ParallelShapeCounts = new int[threadCount];
        ParallelMergeIndices = new int[threadCount];

        MergeHeap = new (float, int)[threadCount];

        for (int i = 0; i < threadCount; i++)
        {
            ParallelShapeBuffers[i] = new Shape[capacityPerThread];
            ParallelZBuffers[i] = new float[capacityPerThread];
            ParallelSortIndices[i] = new int[capacityPerThread];
        }
    }

    /// <summary>Grows all per-thread buffers to the specified capacity.</summary>
    public void EnsureParallelCapacity(int capacityPerThread)
    {
        if (capacityPerThread <= ParallelCapacityPerThread) return;

        ParallelCapacityPerThread = capacityPerThread;
        for (int i = 0; i < ParallelThreadCount; i++)
        {
            Array.Resize(ref ParallelShapeBuffers[i], capacityPerThread);
            Array.Resize(ref ParallelZBuffers[i], capacityPerThread);
            Array.Resize(ref ParallelSortIndices[i], capacityPerThread);
        }
    }

    /// <summary>Ensures parallel buffers can hold totalEntities divided across threads.</summary>
    public void EnsureParallelCapacityForEntities(int totalEntities)
    {
        EnsureCapacity(totalEntities);
        int perThread = (totalEntities + ParallelThreadCount - 1) / ParallelThreadCount;
        EnsureParallelCapacity(perThread);
    }

    /// <summary>Gets the shape buffer for a specific thread.</summary>
    public Shape[] GetParallelBuffer(int threadIndex) => ParallelShapeBuffers[threadIndex];

    /// <summary>Gets the Z buffer for a specific thread.</summary>
    public float[] GetParallelZBuffer(int threadIndex) => ParallelZBuffers[threadIndex];

    /// <summary>Sets the shape count for a thread after direct indexed writes.</summary>
    public void SetParallelCount(int threadIndex, int count)
    {
        ParallelShapeCounts[threadIndex] = count;
    }

    /// <summary>Appends a shape to a thread's buffer, auto-growing if needed.</summary>
    public void DrawParallel(int threadIndex, Shape shape)
    {
        var buffer = ParallelShapeBuffers[threadIndex];
        var count = ParallelShapeCounts[threadIndex];

        if (count >= buffer.Length)
        {
            int newCapacity = Math.Min(buffer.Length * 2, MaxShapeCapacity);
            Array.Resize(ref ParallelShapeBuffers[threadIndex], newCapacity);
            buffer = ParallelShapeBuffers[threadIndex];
        }

        buffer[count] = shape;
        ParallelShapeCounts[threadIndex] = count + 1;
    }

    /// <summary>
    /// Collects all per-thread buffers into the main Shapes array via Array.Copy (unsorted).
    /// Resets per-thread counts.
    /// </summary>
    public void CollectParallel()
    {
        int totalCount = 0;

        for (int i = 0; i < ParallelThreadCount; i++)
            totalCount += ParallelShapeCounts[i];

        EnsureCapacity(totalCount);

        int offset = 0;
        for (int threadIndex = 0; threadIndex < ParallelThreadCount; threadIndex++)
        {
            var buffer = ParallelShapeBuffers[threadIndex];
            var count = ParallelShapeCounts[threadIndex];

            Array.Copy(buffer, 0, Shapes, offset, count);
            offset += count;

            ParallelShapeCounts[threadIndex] = 0;
        }

        Count = totalCount;
    }

    /// <summary>Resets all per-thread shape counts without collecting.</summary>
    public void ClearParallel()
    {
        for (int i = 0; i < ParallelThreadCount; i++)
            ParallelShapeCounts[i] = 0;
    }

    /// <summary>
    /// Sorts a thread's shapes by Z value using an in-place index sort.
    /// Call from within parallel work after populating the buffer.
    /// </summary>
    public void SortBufferByZ(int threadIndex)
    {
        int count = ParallelShapeCounts[threadIndex];
        if (count <= 1) return;

        var shapes = ParallelShapeBuffers[threadIndex];
        var zValues = ParallelZBuffers[threadIndex];
        var indices = ParallelSortIndices[threadIndex];

        for (int i = 0; i < count; i++)
            indices[i] = i;

        Array.Sort(indices, 0, count, Comparer<int>.Create((a, b) => zValues[a].CompareTo(zValues[b])));

        for (int i = 0; i < count; i++)
        {
            while (indices[i] != i)
            {
                int target = indices[i];
                (shapes[i], shapes[target]) = (shapes[target], shapes[i]);
                (zValues[i], zValues[target]) = (zValues[target], zValues[i]);
                (indices[i], indices[target]) = (indices[target], indices[i]);
            }
        }
    }

    /// <summary>
    /// K-way merge of all per-thread buffers into the main Shapes array, sorted by Z.
    /// Each thread's buffer must be pre-sorted via SortBufferByZ.
    /// Uses a min-heap for O(N log T) merge.
    /// </summary>
    public void CollectParallelSorted()
    {
        int totalCount = 0;
        for (int i = 0; i < ParallelThreadCount; i++)
            totalCount += ParallelShapeCounts[i];

        if (totalCount == 0)
        {
            Count = 0;
            return;
        }

        EnsureCapacity(totalCount);

        Array.Clear(ParallelMergeIndices, 0, ParallelThreadCount);

        MergeHeapSize = 0;
        for (int threadIndex = 0; threadIndex < ParallelThreadCount; threadIndex++)
        {
            if (ParallelShapeCounts[threadIndex] > 0)
            {
                MergeHeap[MergeHeapSize++] = (ParallelZBuffers[threadIndex][0], threadIndex);
            }
        }
        HeapifyAll();

        int outputIndex = 0;
        while (MergeHeapSize > 0)
        {
            var (_, bestThread) = MergeHeap[0];
            int idx = ParallelMergeIndices[bestThread]++;
            Shapes[outputIndex++] = ParallelShapeBuffers[bestThread][idx];

            if (ParallelMergeIndices[bestThread] < ParallelShapeCounts[bestThread])
            {
                MergeHeap[0] = (ParallelZBuffers[bestThread][ParallelMergeIndices[bestThread]], bestThread);
                HeapSiftDown(0);
            }
            else
            {
                MergeHeap[0] = MergeHeap[--MergeHeapSize];
                if (MergeHeapSize > 0) HeapSiftDown(0);
            }
        }

        for (int i = 0; i < ParallelThreadCount; i++)
            ParallelShapeCounts[i] = 0;

        Count = totalCount;
    }

    private void HeapifyAll()
    {
        for (int i = MergeHeapSize / 2 - 1; i >= 0; i--)
            HeapSiftDown(i);
    }

    private void HeapSiftDown(int i)
    {
        while (true)
        {
            int smallest = i;
            int left = 2 * i + 1;
            int right = 2 * i + 2;

            if (left < MergeHeapSize && MergeHeap[left].z < MergeHeap[smallest].z)
                smallest = left;
            if (right < MergeHeapSize && MergeHeap[right].z < MergeHeap[smallest].z)
                smallest = right;

            if (smallest == i) break;

            (MergeHeap[i], MergeHeap[smallest]) = (MergeHeap[smallest], MergeHeap[i]);
            i = smallest;
        }
    }

    /// <summary>Disposes GPU buffers and releases parallel buffer arrays.</summary>
    public void Dispose()
    {
        QuadBuffer?.Dispose();
        IndexBuffer?.Dispose();
        InstanceBuffer?.Dispose();

        ParallelShapeBuffers = null;
        ParallelZBuffers = null;
        ParallelSortIndices = null;
        ParallelShapeCounts = null;
        ParallelMergeIndices = null;
        MergeHeap = null;
    }
}
