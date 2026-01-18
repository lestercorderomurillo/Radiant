using com.radiant.engine.bundle;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.core;

public class SimpleLightsScene : Scene
{
    private int mouseEmitterEntityId;

    public override void SetupECS()
    {
        ECS.AddSystem<PerformanceMonitor>();
        ECS.AddSystem<GizmosRenderer>();
        ECS.AddSystem<SceneGeometry>();
        ECS.AddSystem<RCGI>();

        base.SetupECS();
    }

    public override void SetupScene()
    {
        Vector2 center = Renderer.Window.GetScreenCenter();

        // === 2 OCCLUDER BOXES ===
        CreateBox(new Vector2(center.X - 200, center.Y), new Color(40, 40, 40));
        CreateBox(new Vector2(center.X + 200, center.Y), new Color(40, 40, 40));

        // === 3 LIGHTS (bigger, sparser) ===
        CreateLight(new Vector2(center.X, center.Y - 300), Color.Red);
        CreateLight(new Vector2(center.X - 350, center.Y + 250), Color.Green);
        CreateLight(new Vector2(center.X + 350, center.Y + 250), Color.Blue);

        // === MOUSE LIGHT ===
        CreateMouseEmitter();

        base.SetupScene();
    }

    private void CreateBox(Vector2 position, Color color)
    {
        int entity = ECS.CreateEntity();

        ref var transform = ref ECS.AddComponent<Transform>(entity);
        ref var rect = ref ECS.AddComponent<Rectangle2D>(entity);
        ref var material = ref ECS.AddComponent<Material>(entity);

        transform.Position = new Vector3(position.X, position.Y, 0);
        transform.Rotation = new Vector3(1, 0, 0);
        rect.Size = new Vector2(100, 100);
        material.Albedo = color;
        material.Emissive = Color.Black;
    }

    private void CreateLight(Vector2 position, Color color)
    {
        int entity = ECS.CreateEntity();

        ref var transform = ref ECS.AddComponent<Transform>(entity);
        ref var rect = ref ECS.AddComponent<Rectangle2D>(entity);
        ref var material = ref ECS.AddComponent<Material>(entity);

        transform.Position = new Vector3(position.X, position.Y, 0);
        transform.Rotation = new Vector3(1, 0, 0);
        rect.Size = new Vector2(120, 120);
        material.Emissive = color;
    }

    private void CreateMouseEmitter()
    {
        mouseEmitterEntityId = ECS.CreateEntity();

        ref var transform = ref ECS.AddComponent<Transform>(mouseEmitterEntityId);
        ref var rect = ref ECS.AddComponent<Rectangle2D>(mouseEmitterEntityId);
        ref var material = ref ECS.AddComponent<Material>(mouseEmitterEntityId);

        MouseState mouse = Mouse.GetState();
        transform.Position = new Vector3(mouse.X, mouse.Y, 0);
        transform.Rotation = new Vector3(1, 0, 0);
        rect.Size = new Vector2(100, 100);
        material.Emissive = new Color(255, 255, 100);
    }

    public override void Update()
    {
        MouseState mouse = Mouse.GetState();
        ref var transform = ref ECS.GetComponent<Transform>(mouseEmitterEntityId);
        transform.Position = new Vector3(mouse.X, mouse.Y, 0);
    }
}