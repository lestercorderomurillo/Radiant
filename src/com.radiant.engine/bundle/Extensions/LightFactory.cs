using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace com.radiant.engine.bundle;

public static class LightFactory
{
    public static int CreateLight(ECS ecs, Vector2 position, float radius,
        Color albedo, Color emissive, float? z = null, Texture2D texture = null)
    {
        int id = ecs.CreateEntity();
        ecs.AddComponent<Transform>(id);
        ecs.AddComponent<Circle2D>(id);
        ecs.AddComponent<Material>(id);

        ref var transform = ref ecs.GetComponent<Transform>(id);
        ref var circle = ref ecs.GetComponent<Circle2D>(id);
        ref var material = ref ecs.GetComponent<Material>(id);

        transform.Position = new Vector3(position, z ?? id);
        transform.Rotation = Vector3.UnitX;
        circle.Radius = radius;
        material.Albedo = albedo;
        material.Emissive = emissive;
        material.Texture = texture;

        return id;
    }

    public static void SpawnRandom(ECS ecs, int count, Vector2 screenSize, float radius = 3f)
    {
        var rng = new Random();
        for (int i = 0; i < count; i++)
        {
            float x = (float)rng.NextDouble() * screenSize.X;
            float y = (float)rng.NextDouble() * screenSize.Y;
            var color = HueToRGB((float)rng.NextDouble());
            CreateLight(ecs, new Vector2(x, y), radius, color, color);
        }
    }

    public static Color HueToRGB(float hue)
    {
        float r = MathF.Abs(hue * 6f - 3f) - 1f;
        float g = 2f - MathF.Abs(hue * 6f - 2f);
        float b = 2f - MathF.Abs(hue * 6f - 4f);
        return new Color(
            (byte)(Math.Clamp(r, 0f, 1f) * 255),
            (byte)(Math.Clamp(g, 0f, 1f) * 255),
            (byte)(Math.Clamp(b, 0f, 1f) * 255));
    }
}
