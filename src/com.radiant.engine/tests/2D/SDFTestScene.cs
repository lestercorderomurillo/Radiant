using com.radiant.engine.bundle;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.core;

public class SDFTestScene : Scene
{
    private int MouseLightId;
    private int CenterBoxId;
    private MouseState PrevMouse;
    private Vector2 ScreenCenter;

    public override void SetupECS()
    {
        ECS.AddSystem<PerformanceMonitor>();
        ECS.AddSystem<GizmosRenderer>();
        ECS.AddSystem<SceneGeometry>();
        base.SetupECS();
    }

    public override void SetupScene()
    {
        ScreenCenter = Renderer.Window.GetScreenCenter();
        CreateCenterBox();
        CreateMouseLight();
        PrevMouse = Mouse.GetState();
        base.SetupScene();
    }

    private void CreateCenterBox()
    {
        CenterBoxId = ECS.CreateEntity();
        ref var t = ref ECS.AddComponent<Transform>(CenterBoxId);
        ref var r = ref ECS.AddComponent<Rectangle2D>(CenterBoxId);
        ref var m = ref ECS.AddComponent<Material>(CenterBoxId);

        t.Position = new Vector3(ScreenCenter.X - 75, ScreenCenter.Y - 75, 0);
        t.Rotation = new Vector3(1, 0, 0);
        r.Size = new Vector2(150, 150);
        m.Albedo = new Color(100, 100, 100);
        m.Emissive = Color.Transparent;
    }

    private void CreateMouseLight()
    {
        MouseLightId = ECS.CreateEntity();
        ref var t = ref ECS.AddComponent<Transform>(MouseLightId);
        ref var r = ref ECS.AddComponent<Rectangle2D>(MouseLightId);
        ref var m = ref ECS.AddComponent<Material>(MouseLightId);

        t.Position = new Vector3(ScreenCenter.X, ScreenCenter.Y, 0);
        t.Rotation = new Vector3(1, 0, 0);
        r.Size = new Vector2(50, 50);
        m.Emissive = new Color(255, 200, 100);
    }

    public override void Update()
    {
        MouseState mouse = Mouse.GetState();
        Vector2 pos = new Vector2(mouse.X, mouse.Y);

        ref var t = ref ECS.GetComponent<Transform>(MouseLightId);
        t.Position = new Vector3(pos.X - 25, pos.Y - 25, 0);

        if (mouse.LeftButton == ButtonState.Pressed && PrevMouse.LeftButton == ButtonState.Released)
            SpawnLight(pos, new Color(255, 255, 255));

        if (mouse.RightButton == ButtonState.Pressed && PrevMouse.RightButton == ButtonState.Released)
            SpawnOccluder(pos);

        PrevMouse = mouse;
    }

    private void SpawnLight(Vector2 pos, Color color)
    {
        int id = ECS.CreateEntity();
        ref var t = ref ECS.AddComponent<Transform>(id);
        ref var r = ref ECS.AddComponent<Rectangle2D>(id);
        ref var m = ref ECS.AddComponent<Material>(id);

        t.Position = new Vector3(pos.X - 25, pos.Y - 25, 0);
        t.Rotation = new Vector3(1, 0, 0);
        r.Size = new Vector2(50, 50);
        m.Emissive = color;
    }

    private void SpawnOccluder(Vector2 pos)
    {
        int id = ECS.CreateEntity();
        ref var t = ref ECS.AddComponent<Transform>(id);
        ref var r = ref ECS.AddComponent<Rectangle2D>(id);
        ref var m = ref ECS.AddComponent<Material>(id);

        t.Position = new Vector3(pos.X - 40, pos.Y - 40, 0);
        t.Rotation = new Vector3(1, 0, 0);
        r.Size = new Vector2(80, 80);
        m.Albedo = new Color(80, 80, 80);
        m.Emissive = Color.Transparent;
    }
}