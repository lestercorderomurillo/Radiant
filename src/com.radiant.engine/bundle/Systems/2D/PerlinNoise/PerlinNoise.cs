using System;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

public class PerlinNoise : core.System
{
    private static readonly int[] p = new int[512];
    private static bool initialized = false;

    public override void Initialize()
    {
        base.Initialize();

        if (initialized) return;

        var r = new Random();
        var perm = new int[256];
        for (int i = 0; i < 256; i++) perm[i] = i;
        for (int i = 255; i > 0; i--) { int j = r.Next(i + 1); (perm[i], perm[j]) = (perm[j], perm[i]); }
        for (int i = 0; i < 256; i++) p[i] = p[i + 256] = perm[i];

        initialized = true;
    }

    /// <summary>
    /// Raw Perlin noise. Returns -1 to 1.
    /// </summary>
    public float Sample(float x, float y)
    {
        int X = (int)Math.Floor(x) & 255, Y = (int)Math.Floor(y) & 255;
        x -= (float)Math.Floor(x); y -= (float)Math.Floor(y);

        float u = Fade(x), v = Fade(y);
        
        int A = p[X] + Y, B = p[X + 1] + Y;
        return Lerp(v, Lerp(u, Grad(p[A], x, y), Grad(p[B], x - 1, y)),
                       Lerp(u, Grad(p[A + 1], x, y - 1), Grad(p[B + 1], x - 1, y - 1)));
    }

    /// <summary>
    /// Normalized Perlin noise. Returns 0 to 1.
    /// </summary>
    public float NormalizedSample(float x, float y)
    {
        return (Sample(x, y) + 1f) * 0.5f;
    }

    /// <summary>
    /// Absolute Perlin noise. Returns 0 to 1. Creates vein-like patterns.
    /// </summary>
    public float AbsSample(float x, float y)
    {
        return Math.Abs(Sample(x, y));
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
    private static float Lerp(float t, float a, float b) => a + t * (b - a);
    private static float Grad(int hash, float x, float y) => ((hash & 1) == 0 ? x : -x) + ((hash & 2) == 0 ? y : -y);

    public override void Update() { }
    public override void Render() { }
    public override void Dispose() { }
}