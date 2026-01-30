using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

/// <summary>
/// Add this component to entities that need motion vector tracking.
/// Contains a fixed-size circular buffer for position history - no allocations.
/// </summary>
public struct MotionTrackable : Component
{
    private const int MaxHistory = 4;

    // Fixed-size circular buffer (no heap allocations)
    private Vector3 _pos0, _pos1, _pos2, _pos3;
    private int _head;
    private int _count;

    public MotionTrackable()
    {
        _pos0 = _pos1 = _pos2 = _pos3 = Vector3.Zero;
        _head = 0;
        _count = 0;
    }

    public void Push(Vector3 position)
    {
        // Write to head position
        switch (_head)
        {
            case 0: _pos0 = position; break;
            case 1: _pos1 = position; break;
            case 2: _pos2 = position; break;
            case 3: _pos3 = position; break;
        }

        _head = (_head + 1) % MaxHistory;
        if (_count < MaxHistory) _count++;
    }

    public readonly Vector2 CalculateVelocity(Vector3 currentPos, int historyFrames)
    {
        if (_count == 0) return Vector2.Zero;

        int framesToUse = Math.Min(_count, Math.Min(historyFrames, MaxHistory));
        if (framesToUse == 0) return Vector2.Zero;

        Vector2 weightedVelocity = Vector2.Zero;
        float totalWeight = 0;

        // Read from oldest to newest (circular buffer traversal)
        int start = (_head - _count + MaxHistory) % MaxHistory;
        Vector3 prev = GetAt(start);

        for (int i = 1; i < framesToUse; i++)
        {
            int idx = (start + i) % MaxHistory;
            Vector3 pos = GetAt(idx);

            float weight = i;
            weightedVelocity.X += (pos.X - prev.X) * weight;
            weightedVelocity.Y += (pos.Y - prev.Y) * weight;
            totalWeight += weight;
            prev = pos;
        }

        // Final transition to current position
        float finalWeight = framesToUse;
        weightedVelocity.X += (currentPos.X - prev.X) * finalWeight;
        weightedVelocity.Y += (currentPos.Y - prev.Y) * finalWeight;
        totalWeight += finalWeight;

        return totalWeight > 0 ? weightedVelocity / totalWeight : Vector2.Zero;
    }

    private readonly Vector3 GetAt(int idx)
    {
        return idx switch
        {
            0 => _pos0,
            1 => _pos1,
            2 => _pos2,
            3 => _pos3,
            _ => Vector3.Zero
        };
    }
}
