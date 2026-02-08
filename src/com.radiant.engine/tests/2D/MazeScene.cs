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
    private SystemGroup GI;
    private SystemGroup UDR;
    private GizmosRenderer Gizmos;

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
        new(255, 59, 48),   // Coral Red
        new(255, 204, 0),   // Golden Yellow
        new(124, 77, 255),  // Electric Indigo
        new(52, 199, 89),   // Emerald
        new(255, 45, 85),   // Hot Pink
        new(48, 176, 199),  // Teal Blue
        new(255, 159, 10),  // Tangerine
        new(175, 82, 222),  // Amethyst
        new(102, 212, 50),  // Lime
        new(255, 69, 58),   // Vivid Red
        new(0, 199, 190),   // Ocean Teal
        new(236, 64, 122),  // Fuchsia
        new(180, 220, 36),  // Chartreuse
        new(88, 86, 214),   // Royal Purple
        new(255, 105, 180), // Bubblegum
        new(255, 94, 58),   // Flame Orange
    };

    private const int GhostCount = 32;

    private static readonly (int x, int y)[] GhostHouseCells =
    {
        (11, 13), (12, 13), (13, 13), (14, 13), (15, 13), (16, 13),
        (11, 14), (12, 14), (13, 14), (14, 14), (15, 14), (16, 14),
        (11, 15), (12, 15), (13, 15), (14, 15), (15, 15), (16, 15),
    };

    public override void SetupECS()
    {
        ECS.AddSystem<PerformanceMonitor>();
        ECS.AddSystem<Geometry>();

        GI = new SystemGroup(
            ("HRCGI", ECS.AddSystem<HRCGI>()),
            ("RCGI", ECS.AddSystem<RCGI>(enabled: false))
        );

        UDR = new SystemGroup(
            ("Raw", ECS.AddSystem<Bilinear>(enabled: false)),
            ("UDR1.0", ECS.AddSystem<UDR1>(enabled: false)),
            ("UDR2.0", ECS.AddSystem<UDR2>(enabled: false)),
            ("UDR3.0", ECS.AddSystem<UDR3>())
        );

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

        int count = Math.Clamp(GhostCount, 1, 255);
        var startCells = new (int x, int y)[count];
        var colors = new Color[count];
        for (int i = 0; i < count; i++)
        {
            startCells[i] = GhostHouseCells[i % GhostHouseCells.Length];
            colors[i] = GhostColors[i % GhostColors.Length];
        }

        var ghostTexture = Renderer.Window.Content.Load<Texture2D>("Ghost");
        var ghostIds = maze.SpawnAtCells(startCells, colors, 30f, 65530f, ghostTexture);

        var ghosts = ECS.GetSystem<GhostAI>();
        ghosts.EyesTexture = Renderer.Window.Content.Load<Texture2D>("Eyes");
        ghosts.Track(ghostIds, startCells);

        var mouseLight = ECS.GetSystem<MouseLight>();
        var paintBrush = ECS.GetSystem<PaintBrush>();
        paintBrush.ExcludeEntityId = mouseLight.EntityId;

        UpdateUDRInput();

        base.SetupScene();
    }

    public override void Update()
    {
        var keyboard = Keyboard.GetState();

        if (keyboard.IsKeyDown(Keys.Tab) && PrevKeyboard.IsKeyUp(Keys.Tab))
        {
            GI.Toggle();
            UpdateUDRInput();
        }

        if (keyboard.IsKeyDown(Keys.F11) && PrevKeyboard.IsKeyUp(Keys.F11))
        {
            UDR.Toggle();
            UpdateUDRInput();
        }

        if (keyboard.IsKeyDown(Keys.X) && PrevKeyboard.IsKeyUp(Keys.X))
            LightFactory.SpawnRandom(ECS, 100_000, Renderer.VirtualSize);

        PrevKeyboard = keyboard;

        Gizmos.Set("Scene", $"GI: {GI.ActiveName} [Tab]");
        Gizmos.Set("Scene", $"Upscaler: {UDR.ActiveName} [F11]");
        Gizmos.Set("Scene", "Left: Rainbow | Right: Black");
    }

    private void UpdateUDRInput()
    {
        Func<Texture2D> inputSource = () =>
            GI.Active is HRCGI h ? h.GetOutput() :
            GI.Active is RCGI r ? r.GetOutput() : null;

        UDR.ForEach(s =>
        {
            switch (s)
            {
                case Bilinear b: b.SetInputSource(inputSource); break;
                case UDR1 u1: u1.SetInputSource(inputSource); break;
                case UDR2 u2: u2.SetInputSource(inputSource); break;
                case UDR3 u3: u3.SetInputSource(inputSource); break;
            }
        });
    }
}
