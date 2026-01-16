using com.radiant.engine.bundle;
namespace com.radiant.engine.core;

public class TilesetScene : Scene
{
    private const int WorldWidth = 2056;
    
    private const int WorldHeight = 1024;
    
    public override void SetupECS()
    {
        ECS.AddSystem<PerlinNoise2D>();
        ECS.AddSystem<GizmosRenderer>();
        ECS.AddSystem<PerformanceMonitor>();
        ECS.AddSystem<WorldGen>();
        ECS.AddSystem<Tileset>();
        
        base.SetupECS();
    }
    
    public override void SetupScene()
    {
        base.SetupScene();
        
        // Scene is the bridge: generate world first
        var worldGen = ECS.GetSystem<WorldGen>();
        worldGen.GenerateWorld(WorldWidth, WorldHeight);
        
        // Then tell Tileset to load from the generated world
        var tileset = ECS.GetSystem<Tileset>();
        tileset.LoadWorld(worldGen);
    }

    public override void Update()
    {
        base.Update();
    }
}