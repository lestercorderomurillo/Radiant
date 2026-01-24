using System;

namespace com.radiant.engine.bundle;

/// <summary>
/// Shared quality settings for all UDR systems.
/// </summary>
public static class UDRQuality
{
    public static readonly int[] ScaleFactors = { 25, 50, 100 };
    public static readonly string[] Names = { "Performance", "Balanced", "Native" };

    public static int Index { get; private set; } = 1;
    public static int ScaleFactor => ScaleFactors[Index];
    public static float ScaleNormalized => ScaleFactor / 100f;

    public static event Action<int> Changed;

    public static void Set(int index)
    {
        index = Math.Clamp(index, 0, ScaleFactors.Length - 1);
        if (Index != index)
        {
            Index = index;
            Changed?.Invoke(index);
        }
    }

    public static void Cycle()
    {
        Set((Index + 1) % ScaleFactors.Length);
    }
}
