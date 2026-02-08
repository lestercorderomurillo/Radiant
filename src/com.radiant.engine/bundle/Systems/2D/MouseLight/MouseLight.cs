using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

public class MouseLight : core.System
{
    public float Radius { get; set; } = 100f;
    public Color Albedo { get; set; } = new Color(0, 0, 0, 128);
    public Color Emissive { get; set; } = new Color(0, 0, 0, 255);
    public float Z { get; set; } = 65535f;

    public int EntityId { get; private set; }

    public override void Initialize()
    {
        var mouse = Mouse.GetState();
        var worldPos = Renderer.ScreenToWorld(new Vector2(mouse.X, mouse.Y));

        EntityId = LightFactory.CreateLight(Scene.ECS, worldPos, Radius, Albedo, Emissive, Z);
        Scene.ECS.AddComponent<MotionTrackable>(EntityId);
    }

    public override void Update()
    {
        var mouse = Mouse.GetState();
        var worldPos = Renderer.ScreenToWorld(new Vector2(mouse.X, mouse.Y));

        ref var transform = ref Scene.ECS.GetComponent<Transform>(EntityId);
        transform.Position = new Vector3(worldPos, Z);
    }
}
