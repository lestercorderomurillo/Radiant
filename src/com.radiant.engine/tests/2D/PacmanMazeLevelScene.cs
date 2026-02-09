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
    private PacmanGhostAI GhostAI;
    private RainbowGhostAI RainbowAI;
    private PacmanPlayer PlayerSystem;
    private Color BaseWallLight;
    private Color BaseCoinColor;
    private float CoinPulseSpeed;
    private float CoinPulseMin;
    private float CoinPulseMax;
    private float PulseTime;
    private int CurrentLevel;

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

    private static readonly (int x, int y)[] DefaultGhostHouseCells =
    [
        (11, 13), (12, 13), (13, 13), (14, 13), (15, 13), (16, 13),
        (11, 14), (12, 14), (13, 14), (14, 14), (15, 14), (16, 14),
        (11, 15), (12, 15), (13, 15), (14, 15), (15, 15), (16, 15),
    ];

    private static readonly PacmanLevelConfig[] Levels =
    [
        // Level 1: Classic — Blinky, Pinky, Inky, Clyde
        new PacmanLevelConfig
        {
            Layout = PacmanLayout,

            WallColor = new Color((byte)0, (byte)0, (byte)40, (byte)255),
            WallLight = new Color((byte)120, (byte)80, (byte)255, (byte)255),
            WallThickness = 4f,

            CoinColor = new Color(165, 130, 15),

            Ghosts = [PacmanGhostType.Blinky, PacmanGhostType.Pinky, PacmanGhostType.Inky, PacmanGhostType.Clyde],
            GhostReleaseTimes = [1f, 3f, 5f, 7f],
            PlayerStartCell = (14, 23),
        },
        // Level 2: Pinky, Blinky, Shadow (seeded)
        new PacmanLevelConfig
        {
            Procedural = true,
            Ghosts = [PacmanGhostType.Pinky, PacmanGhostType.Blinky, PacmanGhostType.Shadow],
            GhostReleaseTimes = [1f, 6f, 10f],
            PlayerStartCell = (14, 23),

            MazeSeed = 2482,

            // oceanic colors, fast pulse
            WallColor = new Color((byte)0, (byte)0, (byte)80, (byte)255),
            WallLight = new Color((byte)80, (byte)120, (byte)255, (byte)255),
            CoinColor = new Color(80, 255, 200),
            CoinPulseSpeed = 1.5f,
            CoinPulseMin = 0.3f,
             CoinPulseMax = 0.6f,
        },
        // Level 3: Rainbow, Clyde, Inky, Blinky (seeded)
        new PacmanLevelConfig
        {
            Procedural = true,
            Ghosts = [PacmanGhostType.Rainbow, PacmanGhostType.Clyde, PacmanGhostType.Inky, PacmanGhostType.Blinky],
            GhostReleaseTimes = [1f, 8f, 10f, 25f],
            PlayerStartCell = (14, 23),

            MazeSeed = 1344,

            // light coral walls, white glowy coins, slow pulse, not so bright cap
            WallColor = new Color((byte)80, (byte)0, (byte)0, (byte)255),
            WallLight = new Color((byte)212, (byte)80, (byte)80, (byte)255),
            CoinColor = new Color(255, 255, 255),
            CoinPulseSpeed = 0.5f,
            CoinPulseMin = 0.5f,
            CoinPulseMax = 0.6f,

        },
        // Level 4: Dinky, Clyde, Blinky (seeded)
        new PacmanLevelConfig
        {
            Procedural = true,
            Ghosts = [PacmanGhostType.Dinky, PacmanGhostType.Clyde, PacmanGhostType.Blinky],
            GhostReleaseTimes = [1f, 3f, 5f],
            PlayerStartCell = (14, 23),

            MazeSeed = 1235,

            // jungle colors, slowe steady pulse
            WallColor = new Color((byte)0, (byte)40, (byte)0, (byte)255),
            WallLight = new Color((byte)80, (byte)128, (byte)80, (byte)255),
            CoinColor = new Color(120, 255, 120),
            CoinPulseSpeed = 0.8f,
            CoinPulseMin = 0.3f,
            CoinPulseMax = 0.35f,

        },
        // Level 5: Shadow, Pinky, Rainbow, Clyde (procedural)
        new PacmanLevelConfig
        {
            Procedural = true,
            Ghosts = [PacmanGhostType.Shadow, PacmanGhostType.Pinky, PacmanGhostType.Rainbow, PacmanGhostType.Clyde],
            GhostReleaseTimes = [1f, 5f, 7f, 9f],
            PlayerStartCell = (14, 23),

            // dark color, no light on walls, bright coins with fast pulse
            WallColor = new Color((byte)0, (byte)0, (byte)0, (byte)255),
            WallLight = new Color((byte)0, (byte)0, (byte)0, (byte)255),

            MazeSeed = 7131,
            
            CoinColor = new Color(255, 255, 255),
            CoinPulseSpeed = 2.0f,
            CoinPulseMin = 0.3f,
            CoinPulseMax = 0.35f,
        },
        // Level 6: Shadow x3 (procedural)
        new PacmanLevelConfig
        {
            Procedural = true,
            Ghosts = [PacmanGhostType.Shadow, PacmanGhostType.Shadow, PacmanGhostType.Shadow],
            GhostReleaseTimes = [1f, 7f, 13f],
            PlayerStartCell = (14, 23),

            CoinColor = new Color(255, 60, 60),
            CoinPulseSpeed = 3.0f,
            CoinPulseMin = 0.4f,
        },
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
        ECS.AddSystem<RainbowGhostAI>();

        Gizmos = ECS.AddSystem<GizmosRenderer>();

        base.SetupECS();
    }

    public override void SetupScene()
    {
        Maze = ECS.GetSystem<PacmanMazeBuilder>();
        GhostAI = ECS.GetSystem<PacmanGhostAI>();
        RainbowAI = ECS.GetSystem<RainbowGhostAI>();
        PlayerSystem = ECS.GetSystem<PacmanPlayer>();

        LoadLevel(0);
        UpdateUDRInput();

        base.SetupScene();
    }

    private void LoadLevel(int index)
    {
        // Clear previous level
        GhostAI.Clear();
        RainbowAI.Clear();
        PlayerSystem.Clear();
        Maze.ClearCoins();
        Maze.ClearMaze();

        CurrentLevel = index;
        var config = Levels[index];

        // Configure maze — use procedural generator or hardcoded layout
        Maze.Layout = config.Procedural
            ? PacmanMazeGenerator.Generate(index + config.MazeSeed)
            : config.Layout;
        Maze.WallThickness = config.WallThickness;
        Maze.WallColor = config.WallColor;
        BaseWallLight = config.WallLight;
        Maze.WallLight = config.WallLight;
        Maze.GhostHouse = config.GhostHouse;
        Maze.NoUpTiles = config.NoUpTiles;
        Maze.BuildMaze();

        // Coins
        Maze.SpawnCoins(config.CoinRadius, config.CoinColor, 1f);
        BaseCoinColor = config.CoinColor;
        CoinPulseSpeed = config.CoinPulseSpeed;
        CoinPulseMin = config.CoinPulseMin;
        CoinPulseMax = config.CoinPulseMax;

        var ghostTexture = Renderer.GetTexture("Ghost");
        var eyesTexture = Renderer.GetTexture("Eyes");

        // Partition ghosts by index: find rainbow position, build regular list
        int rainbowIndex = Array.IndexOf(config.Ghosts, PacmanGhostType.Rainbow);
        bool hasRainbow = rainbowIndex >= 0;

        // Build regular ghost arrays (skipping Rainbow entry)
        int regularCount = hasRainbow ? config.Ghosts.Length - 1 : config.Ghosts.Length;
        if (regularCount > 0)
        {
            var regularTypes = new PacmanGhostType[regularCount];
            var startCells = new (int x, int y)[regularCount];
            var colors = new Color[regularCount];
            float[] regularReleaseTimes = config.GhostReleaseTimes != null
                ? new float[regularCount] : null;
            int ri = 0;
            for (int i = 0; i < config.Ghosts.Length; i++)
            {
                if (config.Ghosts[i] == PacmanGhostType.Rainbow) continue;
                regularTypes[ri] = config.Ghosts[i];
                startCells[ri] = config.GhostStartCells != null
                    ? config.GhostStartCells[i]
                    : DefaultGhostHouseCells[i % DefaultGhostHouseCells.Length];
                colors[ri] = PacmanGhostAI.PersonalityColor(config.Ghosts[i]);
                if (regularReleaseTimes != null)
                    regularReleaseTimes[ri] = config.GhostReleaseTimes[i];
                ri++;
            }

            var ghostIds = Maze.SpawnAtCells(startCells, colors, 30f, 65530f, ghostTexture);
            GhostAI.BodyTexture = ghostTexture;
            GhostAI.EyesTexture = eyesTexture;
            GhostAI.GhostSpeed = config.GhostSpeed;
            GhostAI.Track(ghostIds, startCells, regularTypes, regularReleaseTimes);
        }

        // Rainbow ghost — start cell from its position in the Ghosts array
        if (hasRainbow)
        {
            var rainbowCell = config.GhostStartCells != null
                ? config.GhostStartCells[rainbowIndex]
                : DefaultGhostHouseCells[rainbowIndex % DefaultGhostHouseCells.Length];
            var rainbowCenter = Maze.CellCenter(rainbowCell.x, rainbowCell.y);
            var rainbowColor = LightFactory.HueToRGB(0f);
            int rainbowId = LightFactory.CreateLight(ECS, rainbowCenter, 30f,
                Color.Transparent, rainbowColor, 65530f, ghostTexture);
            ECS.AddComponent<MotionTrackable>(rainbowId);

            float rainbowRelease = config.GhostReleaseTimes != null
                ? config.GhostReleaseTimes[rainbowIndex] : 0f;
            RainbowAI.BodyTexture = ghostTexture;
            RainbowAI.EyesTexture = eyesTexture;
            RainbowAI.GhostSpeed = config.RainbowSpeed;
            RainbowAI.Track(rainbowId, rainbowCell, rainbowRelease);
        }

        // Player
        var playerCell = config.PlayerStartCell;
        var playerCells = new[] { playerCell };
        var playerColors = new[] { Color.White };
        var playerIds = Maze.SpawnAtCells(playerCells, playerColors, 30f, 65530f, ghostTexture);

        PlayerSystem.Track(playerIds[0], playerCell, 65530f);
        PlayerSystem.CoinColor = config.CoinColor;
        GhostAI.Player = PlayerSystem;
        RainbowAI.Player = PlayerSystem;
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

        if (keyboard.IsKeyDown(Keys.N) && PrevKeyboard.IsKeyUp(Keys.N))
            LoadLevel((CurrentLevel + 1) % Levels.Length);

        PrevKeyboard = keyboard;

        // Pulse wall borders
        PulseTime += (float)GameTime.ElapsedGameTime.TotalSeconds;
        float t = 0.5f + 0.5f * MathF.Sin(PulseTime * 0.6f);
        t = t * t * (3f - 2f * t); // smoothstep
        float pulse = 0.45f + 0.10f * t; // range [0.35 .. 0.55]
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

        // Pulse coins
        float coinT = 0.5f + 0.5f * MathF.Sin(PulseTime * CoinPulseSpeed);
        coinT = coinT * coinT * (3f - 2f * coinT);
        float coinPulse = CoinPulseMin + (CoinPulseMax - CoinPulseMin) * coinT;
        foreach (var coinId in Maze.CoinCells.Values)
        {
            ref var coinMat = ref ECS.GetComponent<Material>(coinId);
            coinMat.Emissive = new Color(
                (byte)(BaseCoinColor.R * coinPulse),
                (byte)(BaseCoinColor.G * coinPulse),
                (byte)(BaseCoinColor.B * coinPulse),
                BaseCoinColor.A);
        }

        Gizmos.Set("Scene", $"Level: {CurrentLevel + 1}/{Levels.Length} [N]");
        Gizmos.Set("Scene", $"GI: {GI.ActiveName} [Tab]");
        Gizmos.Set("Scene", $"Upscaler: {UDR.ActiveName} [F11]");
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
