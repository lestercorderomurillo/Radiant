using com.radiant.engine.bundle;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.core;

public class SimpleLightScene : Scene
{
    private HRCGI HRCGISystem;
    private Bilinear BilinearSystem;

    public override void SetupECS()
    {
        ECS.AddSystem<PerformanceMonitor>();
        ECS.AddSystem<Geometry>();
        HRCGISystem = ECS.AddSystem<HRCGI>();
        BilinearSystem = ECS.AddSystem<Bilinear>();
        ECS.AddSystem<GizmosRenderer>();

        base.SetupECS();
    }

    public override void SetupScene()
    {
        CreateCenterLight();
        BilinearSystem.SetInputSource(() => HRCGISystem.GetOutput());

        base.SetupScene();
    }

    private void CreateCenterLight()
    {
        var center = Renderer.Window.GetScreenCenter();
        var warmColor = new Color(255, 180, 100); // Warm orange-ish color

        int id = ECS.CreateEntity();

        ref var transform = ref ECS.AddComponent<Transform>(id);
        ref var circle = ref ECS.AddComponent<Circle2D>(id);
        ref var material = ref ECS.AddComponent<Material>(id);

        transform.Position = new Vector3(center, 0);
        transform.Rotation = Vector3.UnitX;

        circle.Radius = 100f;

        material.Albedo = warmColor;
        material.Emissive = warmColor;
    }
}
