using com.radiant.engine.bundle;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;

namespace com.radiant.engine.core;

public class MazeScene : Scene
{
    private KeyboardState PrevKeyboard;
    private Random Rng = new();

    // Render pipeline references
    private HRCGI HRCGISystem;
    private RCGI RCGISystem;
    private Bilinear Bilinear;
    private UDR1 UDR1System;
    private UDR2 UDR2System;
    private UDR3 UDR3System;
    private GizmosRenderer Gizmos;

    private bool UseHRCGI = true;
    private int UDRMode = 3;

    private static readonly string[] PacmanLayout =
    {
        "1111111111111111111111111111",
        "1000000000000110000000000001",
        "1011110111110110111110111101",
        "1011110111110110111110111101",
        "1011110111110110111110111101",
        "1000000000000000000000000001",
        "1011110110111111110110111101",
        "1011110110111111110110111101",
        "1000000110000110000110000001",
        "1111110111110110111110111111",
        "1111110111110110111110111111",
        "1111110110000000000110111111",
        "1111110110111001110110111111",
        "1111110110100000010110111111",
        "0000000000100000010000000000",
        "1111110110100000010110111111",
        "1111110110111111110110111111",
        "1111110110000000000110111111",
        "1111110110111111110110111111",
        "1111110110111111110110111111",
        "1000000000000110000000000001",
        "1011110111110110111110111101",
        "1011110111110110111110111101",
        "1000110000000000000000110001",
        "1110110110111111110110110111",
        "1110110110111111110110110111",
        "1000000110000110000110000001",
        "1011111111110110111111111101",
        "1011111111110110111111111101",
        "1000000000000000000000000001",
        "1111111111111111111111111111",
    };

    private static readonly Color[] GhostColors =
    {
        new(255, 0, 0),     // Blinky (red)
        new(255, 184, 255), // Pinky (pink)
        new(0, 255, 255),   // Inky (cyan)
        new(255, 184, 82),  // Clyde (orange)
        new(255, 255, 0),   // yellow
        new(0, 255, 0),     // green
        new(128, 0, 255),   // purple
        new(255, 100, 100), // light red
    };

    private static readonly (int x, int y)[] GhostStartCells =
    {
        (13, 11), (13, 14), (11, 14), (16, 14),
        (1, 1),   (26, 1),  (1, 29),  (26, 29),
    };

    public override void SetupECS()
    {
        ECS.AddSystem<PerformanceMonitor>();
        ECS.AddSystem<Geometry>();

        HRCGISystem = ECS.AddSystem<HRCGI>();
        RCGISystem = ECS.AddSystem<RCGI>(enabled: false);

        Bilinear = ECS.AddSystem<Bilinear>(enabled: false);
        UDR1System = ECS.AddSystem<UDR1>(enabled: false);
        UDR2System = ECS.AddSystem<UDR2>(enabled: false);
        UDR3System = ECS.AddSystem<UDR3>();

        ECS.AddSystem<MazeBuilder>();
        ECS.AddSystem<GhostAI>();
        ECS.AddSystem<MouseLight>();
        ECS.AddSystem<PaintBrush>();

        Gizmos = ECS.AddSystem<GizmosRenderer>();

        base.SetupECS();
    }

    public override void SetupScene()
    {
        var maze = ECS.GetSystem<MazeBuilder>();
        maze.Layout = PacmanLayout;
        maze.BuildMaze();

        var ghostTexture = Renderer.Window.Content.Load<Texture2D>("Ghost");
        var ghostIds = SpawnGhosts(maze, ghostTexture);

        var ghosts = ECS.GetSystem<GhostAI>();
        ghosts.EyesTexture = Renderer.Window.Content.Load<Texture2D>("Eyes");
        ghosts.Track(ghostIds, GhostStartCells);

        var mouseLight = ECS.GetSystem<MouseLight>();
        var paintBrush = ECS.GetSystem<PaintBrush>();
        paintBrush.ExcludeEntityId = mouseLight.EntityId;

        UpdateUDRInput();

        base.SetupScene();
    }

    private int[] SpawnGhosts(MazeBuilder maze, Texture2D ghostTexture)
    {
        const float radius = 25f;
        const float ghostZ = 65530f;
        var ids = new int[GhostStartCells.Length];

        for (int i = 0; i < ids.Length; i++)
        {
            var cell = GhostStartCells[i];
            var center = maze.CellCenter(cell.x, cell.y);
            ids[i] = LightFactory.CreateLight(ECS, center, radius, GhostColors[i], GhostColors[i], ghostZ, ghostTexture);
            ECS.AddComponent<MotionTrackable>(ids[i]);
        }

        return ids;
    }

    public override void Update()
    {
        var keyboard = Keyboard.GetState();

        if (keyboard.IsKeyDown(Keys.Tab) && PrevKeyboard.IsKeyUp(Keys.Tab))
            ToggleGISystem();

        if (keyboard.IsKeyDown(Keys.F11) && PrevKeyboard.IsKeyUp(Keys.F11))
            ToggleUDRSystem();

        if (keyboard.IsKeyDown(Keys.X) && PrevKeyboard.IsKeyUp(Keys.X))
            SpawnDebugEntities(100_000);

        PrevKeyboard = keyboard;

        Gizmos.Set("Scene", $"GI: {(UseHRCGI ? "HRCGI" : "RCGI")} [Tab]");
        Gizmos.Set("Scene", $"Upscaler: {GetUDRName()} [F11]");
        Gizmos.Set("Scene", "Left: Rainbow | Right: Black");
    }

    private void ToggleGISystem()
    {
        UseHRCGI = !UseHRCGI;

        if (UseHRCGI)
        {
            RCGISystem.Dispose();
            RCGISystem.Enabled = false;
            HRCGISystem.Initialize();
            HRCGISystem.Enabled = true;
        }
        else
        {
            HRCGISystem.Dispose();
            HRCGISystem.Enabled = false;
            RCGISystem.Initialize();
            RCGISystem.Enabled = true;
        }

        UpdateUDRInput();
    }

    private void UpdateUDRInput()
    {
        var inputSource = new Func<Texture2D>(() => UseHRCGI ? HRCGISystem.GetOutput() : RCGISystem.GetOutput());
        Bilinear.SetInputSource(inputSource);
        UDR1System.SetInputSource(inputSource);
        UDR2System.SetInputSource(inputSource);
        UDR3System.SetInputSource(inputSource);
    }

    private void ToggleUDRSystem()
    {
        switch (UDRMode)
        {
            case 0: Bilinear.Dispose(); Bilinear.Enabled = false; break;
            case 1: UDR1System.Dispose(); UDR1System.Enabled = false; break;
            case 2: UDR2System.Dispose(); UDR2System.Enabled = false; break;
            case 3: UDR3System.Dispose(); UDR3System.Enabled = false; break;
        }

        UDRMode = (UDRMode + 1) % 4;

        switch (UDRMode)
        {
            case 0: Bilinear.Initialize(); Bilinear.Enabled = true; break;
            case 1: UDR1System.Initialize(); UDR1System.Enabled = true; break;
            case 2: UDR2System.Initialize(); UDR2System.Enabled = true; break;
            case 3: UDR3System.Initialize(); UDR3System.Enabled = true; break;
        }

        UpdateUDRInput();
    }

    private string GetUDRName() => UDRMode switch
    {
        0 => "Bilinear",
        1 => "UDR1 (Spatial)",
        2 => "UDR2 (Spatial + Temporal)",
        3 => "UDR3 (Lanczos + Temporal)",
        _ => "Unknown"
    };

    private void SpawnDebugEntities(int count)
    {
        var screen = Renderer.VirtualSize;
        for (int i = 0; i < count; i++)
        {
            float x = (float)Rng.NextDouble() * screen.X;
            float y = (float)Rng.NextDouble() * screen.Y;
            float hue = (float)Rng.NextDouble();
            float r = MathF.Abs(hue * 6f - 3f) - 1f;
            float g = 2f - MathF.Abs(hue * 6f - 2f);
            float b = 2f - MathF.Abs(hue * 6f - 4f);
            var color = new Color(
                (byte)(Math.Clamp(r, 0f, 1f) * 255),
                (byte)(Math.Clamp(g, 0f, 1f) * 255),
                (byte)(Math.Clamp(b, 0f, 1f) * 255));
            LightFactory.CreateLight(ECS, new Vector2(x, y), 3f, color, color);
        }
    }
}
