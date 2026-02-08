using com.radiant.engine.bundle;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;

namespace com.radiant.engine.core;

public class PacmanMazeLevelScene : Scene
{
    private KeyboardState PrevKeyboard;
    private SystemGroup GI;
    private SystemGroup UDR;
    private GizmosRenderer Gizmos;
    private PacmanMazeBuilder Maze;
    private Color BaseWallLight;
    private float PulseTime;

    private static readonly string[] PacmanLayout =
    [
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
    ];

    private const int GhostCount = 6;

    private static readonly (int x, int y)[] GhostHouseCells =
    [
        (11, 13), (12, 13), (13, 13), (14, 13), (15, 13), (16, 13),
        (11, 14), (12, 14), (13, 14), (14, 14), (15, 14), (16, 14),
        (11, 15), (12, 15), (13, 15), (14, 15), (15, 15), (16, 15),
    ];

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

        ECS.AddSystem<PacmanMazeBuilder>();
        ECS.AddSystem<PacmanPlayer>();
        ECS.AddSystem<PacmanGhostAI>();

        Gizmos = ECS.AddSystem<GizmosRenderer>();

        base.SetupECS();
    }

    public override void SetupScene()
    {
        Maze = ECS.GetSystem<PacmanMazeBuilder>();
        Maze.Layout = PacmanLayout;

        Maze.WallThickness = 4f;
        Maze.WallColor = new Color((byte)0, (byte)0, (byte)40, (byte)255);
        BaseWallLight = new Color((byte)120, (byte)80, (byte)255, (byte)255);
        Maze.WallLight = BaseWallLight;

        Maze.GhostHouse = (11, 13, 16, 15);
        Maze.NoUpTiles = [(12, 11), (15, 11)];
        Maze.BuildMaze();

        Maze.SpawnCoins(6f, new Color(165, 130, 15), 1f);

        int count = Math.Clamp(GhostCount, 1, 255);
        var startCells = new (int x, int y)[count];
        var colors = new Color[count];
        for (int i = 0; i < count; i++)
        {
            startCells[i] = GhostHouseCells[i % GhostHouseCells.Length];
            colors[i] = PacmanGhostAI.PersonalityColor((PacmanGhostType)(i % 6));
        }

        var ghostTexture = Renderer.GetTexture("Ghost");
        var ghostIds = Maze.SpawnAtCells(startCells, colors, 30f, 65530f, ghostTexture);

        var ghosts = ECS.GetSystem<PacmanGhostAI>();
        ghosts.BodyTexture = ghostTexture;
        ghosts.EyesTexture = Renderer.GetTexture("Eyes");
        ghosts.Track(ghostIds, startCells);

        // Player
        var playerCell = (x: 14, y: 23);
        var playerCells = new[] { playerCell };
        var playerColors = new[] { Color.White };
        var playerIds = Maze.SpawnAtCells(playerCells, playerColors, 30f, 65530f, ghostTexture);

        var player = ECS.GetSystem<PacmanPlayer>();
        player.Track(playerIds[0], playerCell, 65530f);
        ghosts.Player = player;

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

        // Pulse wall borders
        PulseTime += (float)GameTime.ElapsedGameTime.TotalSeconds;
        float t = 0.5f + 0.5f * MathF.Sin(PulseTime * 0.6f);
        t = t * t * (3f - 2f * t); // smoothstep
        float pulse = 0.55f + 0.05f * t; // range [0.50 .. 0.60]
        var ids = Maze.BorderIds;
        for (int i = 0; i < ids.Count; i++)
        {
            ref var mat = ref ECS.GetComponent<Material>(ids[i]);
            mat.Emissive = new Color(
                (byte)(BaseWallLight.R * pulse),
                (byte)(BaseWallLight.G * pulse),
                (byte)(BaseWallLight.B * pulse),
                BaseWallLight.A);
        }

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
