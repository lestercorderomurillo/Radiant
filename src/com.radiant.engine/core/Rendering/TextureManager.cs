using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.core;

/// <summary>
/// Manages cached textures: content assets, solid colors, and procedural shape textures.
/// Shape textures use a registry pattern — each shape type is registered with a generator
/// function and cached by size. Icons are plain content textures in presets/icons/.
/// </summary>
internal class TextureManager : IDisposable
{
    /// <summary>
    /// A registered shape type with its generator function and per-size cache.
    /// </summary>
    private sealed class ShapeEntry
    {
        public readonly Func<GraphicsDevice, int, Texture2D> Generator;
        public readonly int MinSize;
        public readonly Dictionary<int, Texture2D> Cache = new();

        public ShapeEntry(Func<GraphicsDevice, int, Texture2D> generator, int minSize)
        {
            Generator = generator;
            MinSize = minSize;
        }
    }

    private readonly GraphicsDevice Device;
    private readonly ContentManager Content;
    private readonly Dictionary<string, Texture2D> ContentCache = new();
    private readonly Dictionary<(Color, int, int), Texture2D> SolidCache = new();
    private readonly Dictionary<string, ShapeEntry> ShapeEntries = new();

    public TextureManager(GraphicsDevice device, ContentManager content)
    {
        Device = device;
        Content = content;

        RegisterShape("Circle", GenerateCircle, minSize: 1);
        RegisterShape("Triangle", GenerateTriangle, minSize: 4);
        RegisterShape("RoundedRect", GenerateRoundedRect, minSize: 1);
    }

    /// <summary>
    /// Registers a procedural shape texture generator with a minimum size clamp.
    /// </summary>
    public void RegisterShape(string name, Func<GraphicsDevice, int, Texture2D> generator, int minSize = 1)
    {
        ShapeEntries[name] = new ShapeEntry(generator, minSize);
    }

    /// <summary>
    /// Gets or creates a cached procedural shape texture by name and size.
    /// </summary>
    public Texture2D GetShape(string name, int size)
    {
        var entry = ShapeEntries[name];
        if (size < entry.MinSize) size = entry.MinSize;
        if (!entry.Cache.TryGetValue(size, out var texture))
        {
            texture = entry.Generator(Device, size);
            entry.Cache[size] = texture;
        }
        return texture;
    }

    /// <summary>
    /// Loads and caches a content texture by asset name.
    /// </summary>
    public Texture2D Get(string name)
    {
        if (!ContentCache.TryGetValue(name, out var texture))
        {
            texture = Content.Load<Texture2D>(name);
            ContentCache[name] = texture;
        }
        return texture;
    }

    /// <summary>
    /// Gets or creates a cached solid color texture.
    /// </summary>
    public Texture2D GetSolid(Color color, int width = 1, int height = 1)
    {
        var key = (color, width, height);
        if (!SolidCache.TryGetValue(key, out var texture))
        {
            texture = new Texture2D(Device, width, height);
            var data = new Color[width * height];
            Array.Fill(data, color);
            texture.SetData(data);
            SolidCache[key] = texture;
        }
        return texture;
    }

    private static Texture2D GenerateCircle(GraphicsDevice device, int diameter)
    {
        var texture = new Texture2D(device, diameter, diameter);
        var data = new Color[diameter * diameter];

        float radius = diameter / 2f;
        float centerX = radius - 0.5f;
        float centerY = radius - 0.5f;

        const float aaWidth = 1.0f;
        float innerRadius = radius - aaWidth * 0.5f;

        for (int y = 0; y < diameter; y++)
        {
            for (int x = 0; x < diameter; x++)
            {
                float dx = x - centerX;
                float dy = y - centerY;
                float dist = MathF.Sqrt(dx * dx + dy * dy);

                float alpha = 1.0f - MathHelper.Clamp((dist - innerRadius) / aaWidth, 0f, 1f);
                byte a = (byte)(alpha * 255f + 0.5f);

                data[y * diameter + x] = new Color(a, a, a, a);
            }
        }

        texture.SetData(data);
        return texture;
    }

    private static Texture2D GenerateTriangle(GraphicsDevice device, int size)
    {
        var texture = new Texture2D(device, size, size);
        var data = new Color[size * size];
        float half = size / 2f;
        Vector2 a = new(half, size - 0.5f);
        Vector2 b = new(0.5f, 0.5f);
        Vector2 c = new(size - 0.5f, 0.5f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new(x + 0.5f, y + 0.5f);
                float d0 = EdgeDist(p, b, c);
                float d1 = EdgeDist(p, c, a);
                float d2 = EdgeDist(p, a, b);
                float dist = MathF.Max(d0, MathF.Max(d1, d2));
                float alpha = MathHelper.Clamp(0.5f - dist, 0f, 1f);
                byte val = (byte)(alpha * 255f + 0.5f);
                data[y * size + x] = new Color(val, val, val, val);
            }
        }
        texture.SetData(data);
        return texture;

        static float EdgeDist(Vector2 p, Vector2 v0, Vector2 v1)
        {
            Vector2 edge = v1 - v0;
            Vector2 normal = new(edge.Y, -edge.X);
            float len = normal.Length();
            return (normal.X * (p.X - v0.X) + normal.Y * (p.Y - v0.Y)) / len;
        }
    }

    private static Texture2D GenerateRoundedRect(GraphicsDevice device, int radius)
    {
        int size = radius * 2 + 2;
        var texture = new Texture2D(device, size, size);
        var data = new Color[size * size];
        float center = size / 2f;
        float innerDist = center - radius;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                float dx = MathF.Max(MathF.Abs(px - center) - innerDist, 0f);
                float dy = MathF.Max(MathF.Abs(py - center) - innerDist, 0f);
                float dist = MathF.Sqrt(dx * dx + dy * dy) - radius;
                float alpha = MathHelper.Clamp(0.5f - dist, 0f, 1f);
                byte a = (byte)(alpha * 255f + 0.5f);
                data[y * size + x] = new Color(a, a, a, a);
            }
        }

        texture.SetData(data);
        return texture;
    }

    public void Dispose()
    {
        foreach (var texture in ContentCache.Values)
            texture?.Dispose();
        ContentCache.Clear();

        foreach (var texture in SolidCache.Values)
            texture?.Dispose();
        SolidCache.Clear();

        foreach (var entry in ShapeEntries.Values)
            foreach (var texture in entry.Cache.Values)
                texture?.Dispose();
        ShapeEntries.Clear();
    }
}
