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
        new(255, 0, 0),
        new(255, 184, 255),
        new(0, 255, 255),
        new(255, 184, 82),
        new(255, 255, 0),
        new(0, 255, 0),
        new(128, 0, 255),
        new(255, 100, 100),
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

        var ghostTexture = Renderer.Window.Content.Load<Texture2D>("Ghost");
        var ghostIds = maze.SpawnAtCells(GhostStartCells, GhostColors, 25f, 65530f, ghostTexture);

        var ghosts = ECS.GetSystem<GhostAI>();
        ghosts.EyesTexture = Renderer.Window.Content.Load<Texture2D>("Eyes");
        ghosts.Track(ghostIds, GhostStartCells);

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
