using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.core;

public partial class Renderer
{
    /// <summary>
    /// Adds a shape to the current batch. Call FlushShapes() to render.
    /// Silently drops shapes beyond MaxShapeCapacity.
    /// </summary>
    public Renderer DrawShape(Shape shape)
    {
        ShapeBatch.Add(shape);
        return this;
    }

    /// <summary>Adds a rectangle shape to the current batch.</summary>
    public Renderer DrawRect(Vector2 position, Vector2 size, Color color) => DrawShape(Shape.Rect(position, size, color));

    /// <summary>Adds a rectangle shape to the current batch.</summary>
    public Renderer DrawRect(float x, float y, float width, float height, Color color) => DrawShape(Shape.Rect(x, y, width, height, color));

    /// <summary>Adds a circle shape to the current batch.</summary>
    public Renderer DrawCircle(Vector2 center, float radius, Color color) => DrawShape(Shape.Circle(center, radius, color));

    /// <summary>Adds a circle shape to the current batch.</summary>
    public Renderer DrawCircle(float x, float y, float radius, Color color) => DrawShape(Shape.Circle(x, y, radius, color));

    /// <summary>Adds a triangle shape to the current batch.</summary>
    public Renderer DrawTriangle(Vector2 position, Vector2 size, Color color) => DrawShape(Shape.Triangle(position, size, color));

    /// <summary>Adds a bordered (unfilled) triangle shape to the current batch.</summary>
    public Renderer DrawTriangleBorder(Vector2 position, Vector2 size, Color color) => DrawShape(Shape.TriangleBorder(position, size, color));

    /// <summary>
    /// Renders all batched shapes in a single draw call and clears the batch.
    /// </summary>
    /// <param name="target">Render target (null for backbuffer).</param>
    /// <param name="clearColor">Optional clear color before rendering.</param>
    /// <param name="technique">Shader technique: "Default" (AA), "Sharp" (no AA), or "Emissive" (sharp SDF for light sources).</param>
    public Renderer FlushShapes(RenderTarget2D target = null, Color? clearColor = null, string technique = "Default")
    {
        CommitTextures();
        ShapeBatch.Flush(target ?? SceneRT, clearColor, BlendState, Shaders.Get("InstancedShapes"), technique);
        return this;
    }

    /// <summary>Clears the shape batch without rendering.</summary>
    public Renderer ClearShapes()
    {
        ShapeBatch.Clear();
        return this;
    }

    /// <summary>Current number of shapes in the batch.</summary>
    public int ShapeBatchCount => ShapeBatch.Count;

    /// <summary>
    /// Renders shapes directly from an external buffer - ZERO COPY.
    /// Use this when shapes are pre-collected in Renderer.Shape format.
    /// </summary>
    public Renderer FlushShapesExternal(Shape[] shapes, int count, RenderTarget2D target = null, Color? clearColor = null, string technique = "Default")
    {
        CommitTextures();
        ShapeBatch.FlushExternal(shapes, count, target ?? SceneRT, clearColor, BlendState, Shaders.Get("InstancedShapes"), technique);
        return this;
    }

    /// <summary>GPU upload timing stats.</summary>
    public float LastSetDataMs => ShapeBatch.LastSetDataMs;

    /// <summary>GPU draw timing stats.</summary>
    public float LastDrawMs => ShapeBatch.LastDrawMs;

    /// <summary>
    /// Initializes parallel shape buffers for multi-threaded shape collection.
    /// Call once at startup or when thread count changes.
    /// </summary>
    /// <param name="threadCount">Number of worker threads.</param>
    /// <param name="capacityPerThread">Initial capacity per thread buffer.</param>
    public Renderer InitializeParallelShapes(int threadCount, int capacityPerThread = 16384)
    {
        ShapeBatch.InitializeParallel(threadCount, capacityPerThread);
        return this;
    }

    /// <summary>
    /// Ensures all parallel buffers have at least the specified capacity.
    /// Call when entity count grows.
    /// </summary>
    public Renderer EnsureParallelCapacity(int capacityPerThread)
    {
        ShapeBatch.EnsureParallelCapacity(capacityPerThread);
        return this;
    }

    /// <summary>
    /// Ensures parallel buffers can hold totalEntities divided across threads.
    /// Also ensures the main Shapes array can hold the total.
    /// Call when entity count grows: EnsureParallelCapacityForEntities(entityCount)
    /// </summary>
    public Renderer EnsureParallelCapacityForEntities(int totalEntities)
    {
        ShapeBatch.EnsureParallelCapacityForEntities(totalEntities);
        return this;
    }

    /// <summary>
    /// Gets the shape buffer for a specific thread for direct indexed writes.
    /// Thread writes directly: buffer[localIndex] = shape;
    /// </summary>
    public Shape[] GetParallelBuffer(int threadIndex) => ShapeBatch.GetParallelBuffer(threadIndex);

    /// <summary>
    /// Gets the Z buffer for a specific thread for direct indexed writes.
    /// Thread writes directly: zBuffer[localIndex] = z;
    /// </summary>
    public float[] GetParallelZBuffer(int threadIndex) => ShapeBatch.GetParallelZBuffer(threadIndex);

    /// <summary>Sets the shape count for a thread after direct indexed writes.</summary>
    public void SetParallelCount(int threadIndex, int count) => ShapeBatch.SetParallelCount(threadIndex, count);

    /// <summary>
    /// Adds a shape to a thread's buffer (append style).
    /// Thread-local, no synchronization needed.
    /// </summary>
    public void DrawShapeParallel(int threadIndex, Shape shape) => ShapeBatch.DrawParallel(threadIndex, shape);

    /// <summary>
    /// Collects all parallel thread buffers into the main Shapes array in order.
    /// Call on main thread after parallel work completes, before FlushShapes.
    /// </summary>
    public Renderer CollectParallelShapes()
    {
        ShapeBatch.CollectParallel();
        return this;
    }

    /// <summary>Clears all parallel shape buffers without collecting.</summary>
    public Renderer ClearParallelShapes()
    {
        ShapeBatch.ClearParallel();
        return this;
    }

    /// <summary>
    /// Sorts a thread's shapes by Z. Call from within parallel work after populating.
    /// Cache-friendly: sorts locally within thread's buffer.
    /// </summary>
    public void SortParallelBufferByZ(int threadIndex) => ShapeBatch.SortBufferByZ(threadIndex);

    /// <summary>
    /// Collects all parallel thread buffers with k-way merge by Z order.
    /// Each thread's buffer must be pre-sorted via SortParallelBufferByZ.
    /// Call on main thread after parallel work completes.
    /// Uses min-heap for O(N log T) instead of O(N*T).
    /// </summary>
    public Renderer CollectParallelShapesSorted()
    {
        ShapeBatch.CollectParallelSorted();
        return this;
    }
}
