using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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
}
