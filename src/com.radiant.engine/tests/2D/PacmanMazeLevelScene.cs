using com.radiant.engine.bundle;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace com.radiant.engine.core;

public class PacmanMazeLevelScene : Scene
{
    private SystemGroup GIGroup;
    private SystemGroup UpscalerGroup;
    private ColorManagement Tonemapper;
    private GizmosRenderer Gizmos;

    private PacmanMazeBuilder Maze;
    private PacmanGhostAI GhostAI;
    private RainbowGhostAI RainbowAI;
    private PacmanPlayer PlayerSystem;

    private Color BaseCoinColor;
    private Color BasePowerPelletColor;
    private float CoinWaveTime;
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
        (11, 15), (12, 15), (13, 15), (14, 15), (15, 15), (16, 15),
        (11, 14), (12, 14), (13, 14), (14, 14), (15, 14), (16, 14),
        (11, 13), (12, 13), (13, 13), (14, 13), (15, 13), (16, 13),
    ];

    private static readonly PacmanLevelConfig[] Levels =
    [
        // Level 1: Classic — Blinky, Pinky, Inky, Clyde
        new PacmanLevelConfig
        {
            Tag = "1-1",
            Layout = PacmanLayout,
            WallAlbedo = new Color((byte)0, (byte)0, (byte)40, (byte)255),
            WallEmissive = new Color((byte)90, (byte)45, (byte)160, (byte)170),
            CoinColor = new Color(225, 180, 35),
            PowerPelletCells = [(1, 3), (26, 3), (1, 23), (26, 23)],
            FrightenedDuration = 6f,
            Ghosts =
            [
                new() { Type = PacmanGhostType.Blinky, ReleaseAfter = 4f },
                new() { Type = PacmanGhostType.Pinky,  ReleaseAfter = 8f },
                new() { Type = PacmanGhostType.Inky,   ReleaseAfter = 12f },
                new() { Type = PacmanGhostType.Clyde,  ReleaseAfter = 16f },
            ],
            PlayerStartCell = (14, 23),
        },

        // Level 2: Pinky, Blinky, Shadow (seeded)
        new PacmanLevelConfig
        {
            Tag = "1-2",
            Procedural = true,
            MazeSeed = 2482,
            WallAlbedo = new Color(0, 0, 55),
            WallEmissive = new Color(56, 84, 107),
            CoinColor = new Color(102, 210, 227),
            FrightenedDuration = 5f,
            Ghosts =
            [
                new() { Type = PacmanGhostType.Pinky,  ReleaseAfter = 4f },
                new() { Type = PacmanGhostType.Blinky, ReleaseAfter = 8f },
                new() { Type = PacmanGhostType.Shadow, ReleaseAfter = 24f },
            ],
            PlayerStartCell = (14, 23),
        },

        // Level 3: Rainbow, Clyde, Inky, Blinky (seeded)
        new PacmanLevelConfig
        {
            Tag = "1-3",
            Procedural = true,
            MazeSeed = 1344,
            WallAlbedo = new Color(30, 30, 20),
            WallEmissive = new Color(39, 38, 36),
            CoinColor = new Color(187, 186, 147),
            FrightenedDuration = 5f,
            Ghosts =
            [
                new() { Type = PacmanGhostType.Blinky,  ReleaseAfter = 4f },
                new() { Type = PacmanGhostType.Clyde,   ReleaseAfter = 8f },
                new() { Type = PacmanGhostType.Inky,    ReleaseAtCoinPercent = 0.25f },
                new() { Type = PacmanGhostType.Rainbow, ReleaseAtCoinPercent = 0.50f },
            ],
            PlayerStartCell = (14, 23),
        },

        // Level 4: Dinky, Clyde, Blinky (seeded)
        new PacmanLevelConfig
        {
            Tag = "1-4",
            Procedural = true,
            MazeSeed = 1235,
            WallAlbedo = new Color(101, 61, 65),
            WallEmissive = new Color(53, 106, 74),
            CoinColor = new Color(183, 199, 65),
            FrightenedDuration = 7f,
            Ghosts =
            [
                new() { Type = PacmanGhostType.Dinky,  ReleaseAfter = 4f },
                new() { Type = PacmanGhostType.Clyde,  ReleaseAfter = 4f },
                new() { Type = PacmanGhostType.Blinky, ReleaseAfter = 4f },
            ],
            PlayerStartCell = (14, 23),
        },

        // Level 5: Shadow, Pinky, Rainbow, Clyde (procedural)
        new PacmanLevelConfig
        {
            Tag = "2-1",
            Procedural = true,
            MazeSeed = 7131,
            WallAlbedo = new Color(38, 16, 25),
            WallEmissive = new Color(79, 43, 123),
            CoinColor = new Color(69, 186, 163),
            FrightenedDuration = 4f,
            Ghosts =
            [
                new() { Type = PacmanGhostType.Shadow,  ReleaseAfter = 4f },
                new() { Type = PacmanGhostType.Pinky,   ReleaseAfter = 8f },
                new() { Type = PacmanGhostType.Rainbow, ReleaseAfter = 16f },
                new() { Type = PacmanGhostType.Clyde,   ReleaseAtCoinPercent = 0.30f },
            ],
            PlayerStartCell = (14, 23),
        },

        // Level 6: Shadow x3 (procedural)
        new PacmanLevelConfig
        {
            Tag = "2-2",
            Procedural = true,
            MazeSeed = 8264,
            WallAlbedo = new Color(55, 5, 5),
            WallEmissive = new Color(20, 0, 0),
            CoinColor = new Color(236, 37, 0),
            FrightenedDuration = 3f,
            Ghosts =
            [
                new() { Type = PacmanGhostType.Shadow, ReleaseAfter = 1f },
                new() { Type = PacmanGhostType.Shadow, ReleaseAfter = 7f },
                new() { Type = PacmanGhostType.Shadow, ReleaseAfter = 13f },
            ],
            PlayerStartCell = (14, 23),
        },
    ];

    public override void SetupECS()
    {
        ECS.AddSystem<Inspector>();
        ECS.AddSystem<PerformanceMonitor>();
        ECS.AddSystem<Geometry>();

        GIGroup = new SystemGroup(
            ("Holographic Radiance Cascades", ECS.AddSystem<HRCGI>()),
            ("Radiance Cascades", ECS.AddSystem<RCGI>(enabled: false))
        );

        Tonemapper = ECS.AddSystem<ColorManagement>();

        UpscalerGroup = new SystemGroup(
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
        UpdateUpscalerInput();

        // Scene control window
        Inspector.CreateWindow("scene", "Scene", 2);

        Inspector.AddLabel("scene", "level", $"Level: 1/{Levels.Length}");

        Inspector.AddButton("scene", "restartLevel", "Restart Level", RestartLevel);
        Inspector.AddButton("scene", "nextLevel", "Next Level", () => LoadLevel((CurrentLevel + 1) % Levels.Length));
        Inspector.AddButton("scene", "spawnLights", "Spawn Lights", () => LightFactory.SpawnRandom(ECS, 100_000, Renderer.VirtualSize));
        Inspector.AddToggle("scene", "pauseGameplay", "Pause Gameplay", false, (paused) => ECS.GameplayPaused = paused);
        Inspector.AddToggle("scene", "pauseAll", "Pause Gameplay + Animations", false, (paused) =>
        {
            ECS.AnimationPaused = paused;
            ECS.GameplayPaused = paused;
            Inspector.SetToggleValue("scene", "pauseGameplay", paused);
        });

        Inspector.AddDropdown("pipeline", "upscaler", "Upscaler", UpscalerGroup.Names, UpscalerGroup.ActiveIdx, (index) =>
        {
            UpscalerGroup.SetActive(index);
            UpdateUpscalerInput();
            UpdateWindowVisibility();
        });

        Inspector.AddSectionLabel("pipeline", "techniquesHeader", "Techniques");
        Inspector.AddDropdown("pipeline", "lighting", "Lighting", GIGroup.Names, GIGroup.ActiveIdx, (index) =>
        {
            GIGroup.SetActive(index);
            UpdateUpscalerInput();
            UpdateWindowVisibility();
        });

        Inspector.AddToggle("inspector", "gizmos", "Gizmos", Gizmos.Enabled, (enabled) => Gizmos.Enabled = enabled);

        Inspector.WindowsRestored += UpdateWindowVisibility;
        UpdateWindowVisibility();

        base.SetupScene();
    }

    private void RestartLevel()
    {
        ECS.DestroyAllEntities();
        GhostAI.Clear();
        RainbowAI.Clear();
        PlayerSystem.Clear();
        Maze.ClearCoins();
        Maze.ClearMaze();
        LoadLevel(CurrentLevel);
    }

    private void LoadLevel(int index)
    {
        GhostAI.Clear();
        RainbowAI.Clear();
        PlayerSystem.Clear();
        Maze.ClearCoins();
        Maze.ClearMaze();

        CurrentLevel = index;
        var config = Levels[index];
        float scale = 0.92f;

        // Configure maze — use procedural generator or hardcoded layout
        Maze.Layout = config.Procedural
            ? PacmanMazeGenerator.Generate(index + config.MazeSeed)
            : config.Layout;
        Maze.CellSize = 70f * scale;
        Maze.WallColor = config.WallAlbedo;
        Maze.WallLight = config.WallEmissive;
        Maze.GhostHouse = config.GhostHouse;
        Maze.NoUpTiles = config.NoUpTiles;
        Maze.BuildMaze();

        // Coins + power pellets
        Maze.SpawnCoins(config.CoinRadius * scale, config.CoinColor, 1f, config.PlayerStartCell);
        var pelletPositions = config.PowerPelletCells ?? Maze.FindCornerPelletPositions();
        if (pelletPositions.Length > 0)
            Maze.SpawnPowerPellets(pelletPositions, config.PowerPelletRadius * scale, config.PowerPelletColor, 1f);
        BaseCoinColor = config.CoinColor;
        BasePowerPelletColor = config.PowerPelletColor;

        var ghostTexture = Renderer.GetTexture("Ghost");
        var eyesTexture = Renderer.GetTexture("Eyes");
        float bodyRadius = 28.5f * scale;

        // Partition ghosts: find rainbow entry, build regular list
        int rainbowIndex = -1;
        for (int i = 0; i < config.Ghosts.Length; i++)
            if (config.Ghosts[i].Type == PacmanGhostType.Rainbow) { rainbowIndex = i; break; }
        bool hasRainbow = rainbowIndex >= 0;

        // Regular ghosts (everything except Rainbow)
        int regularCount = hasRainbow ? config.Ghosts.Length - 1 : config.Ghosts.Length;
        if (regularCount > 0)
        {
            var regularEntries = new GhostEntry[regularCount];
            var startCells = new (int x, int y)[regularCount];
            var colors = new Color[regularCount];

            int regularIndex = 0;
            for (int i = 0; i < config.Ghosts.Length; i++)
            {
                if (config.Ghosts[i].Type == PacmanGhostType.Rainbow) continue;
                regularEntries[regularIndex] = config.Ghosts[i];
                startCells[regularIndex] = config.Ghosts[i].StartCell
                    ?? DefaultGhostHouseCells[i % DefaultGhostHouseCells.Length];
                colors[regularIndex] = PacmanGhostAI.PersonalityColor(config.Ghosts[i].Type);
                regularIndex++;
            }

            var ghostIds = Maze.SpawnAtCells(startCells, colors, bodyRadius, 65530f, ghostTexture);

            GhostAI.BodyTexture = ghostTexture;
            GhostAI.EyesTexture = eyesTexture;
            GhostAI.BodyRadius = bodyRadius;
            GhostAI.GhostSpeed = config.GhostSpeed * scale;
            GhostAI.Track(ghostIds, startCells, regularEntries);
        }

        // Rainbow ghost
        if (hasRainbow)
        {
            var rainbowEntry = config.Ghosts[rainbowIndex];
            var rainbowCell = rainbowEntry.StartCell
                ?? DefaultGhostHouseCells[rainbowIndex % DefaultGhostHouseCells.Length];
            var rainbowCenter = Maze.CellCenter(rainbowCell.x, rainbowCell.y);
            var rainbowColor = LightFactory.HueToRGB(0f);

            int rainbowId = LightFactory.CreateLight(ECS, rainbowCenter, bodyRadius,
                Color.Transparent, rainbowColor, 65530f, ghostTexture);
            ECS.AddComponent<MotionTrackable>(rainbowId);

            RainbowAI.BodyTexture = ghostTexture;
            RainbowAI.EyesTexture = eyesTexture;
            RainbowAI.BodyRadius = bodyRadius;
            RainbowAI.GhostSpeed = config.RainbowSpeed * scale;
            RainbowAI.Track(rainbowId, rainbowCell, rainbowEntry.ReleaseAfter, rainbowEntry.ReleaseAtCoinPercent);
        }

        // Player — golden yellow ball
        var playerCell = config.PlayerStartCell;
        var playerColor = new Color(255, 210, 30);
        var playerEmissive = new Color((byte)255, (byte)220, (byte)50, (byte)255);
        var playerCenter = Maze.CellCenter(playerCell.x, playerCell.y);

        int playerId = LightFactory.CreateLight(ECS, playerCenter, bodyRadius,
            playerColor, playerEmissive, 65530f);
        ECS.AddComponent<MotionTrackable>(playerId);

        PlayerSystem.Speed = config.GhostSpeed * scale;
        PlayerSystem.LevelTag = config.Tag;
        PlayerSystem.Track(playerId, playerCell, 65530f);
        PlayerSystem.CoinColor = config.CoinColor;
        PlayerSystem.GhostAI = GhostAI;
        PlayerSystem.RainbowAI = RainbowAI;
        PlayerSystem.FrightenedDuration = config.FrightenedDuration;

        GhostAI.Player = PlayerSystem;
        RainbowAI.Player = PlayerSystem;
    }

    public override void Update()
    {
        // Ghost caught player — reset level
        if (PlayerSystem.PlayerCaught)
        {
            LoadLevel(CurrentLevel);
            return;
        }

        // All coins collected — next level
        if (PlayerSystem.CoinsTotal > 0 && PlayerSystem.CoinsCollected >= PlayerSystem.CoinsTotal)
        {
            LoadLevel((CurrentLevel + 1) % Levels.Length);
            return;
        }

        if (ECS.AnimationPaused) return;

        CoinWaveTime += (float)GameTime.ElapsedGameTime.TotalSeconds;
        var playerPos = PlayerSystem.WorldPosition;
        const float attractRadius = 200f;
        const float attractStrength = 0.25f;
        const float glowRadius = 300f;

        // Animate coins — wave + attraction toward player
        foreach (var (cell, coinId) in Maze.CoinCells)
        {
            float phase = CoinWaveTime * 1.8f - cell.Item1 * 0.5f;
            float sinVal = MathF.Sin(phase);
            float wave = 0.85f + 0.15f * sinVal;

            var basePos = Maze.CellCenter(cell.Item1, cell.Item2);
            float coinX = basePos.X;
            float coinY = basePos.Y + 4.5f * MathF.Sin(phase);

            float dx = playerPos.X - coinX;
            float dy = playerPos.Y - coinY;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            float proximity = 0f;
            if (dist < attractRadius && dist > 0.1f)
            {
                proximity = 1f - dist / attractRadius;
                float pull = proximity * attractStrength;
                coinX += dx * pull;
                coinY += dy * pull;
            }

            // Near player: smooth override from wave to full brightness
            float glow = dist < glowRadius ? 1f - dist / glowRadius : 0f;
            glow *= glow;
            float bright = wave * (1f - glow) + 1.0f * glow + proximity * 0.6f;
            byte alpha = (byte)(BaseCoinColor.A * ((0.9f + 0.1f * sinVal) * (1f - glow) + glow));

            ref var coinMat = ref ECS.GetComponent<Material>(coinId);
            coinMat.Emissive = new Color(
                (byte)MathF.Min(BaseCoinColor.R * bright, 255f),
                (byte)MathF.Min(BaseCoinColor.G * bright, 255f),
                (byte)MathF.Min(BaseCoinColor.B * bright, 255f),
                alpha);

            ref var coinTransform = ref ECS.GetComponent<Transform>(coinId);
            coinTransform.Position.X = coinX;
            coinTransform.Position.Y = coinY;
            float coinScale = 1f + proximity * 0.6f;
            coinTransform.Scale = new Vector3(coinScale, coinScale, 1f);
        }

        // Animate power pellets — prismatic white + attraction
        foreach (var (cell, pelletId) in Maze.PowerPelletCells)
        {
            float phase = CoinWaveTime * 1.8f - cell.Item1 * 0.5f;

            var basePos = Maze.CellCenter(cell.Item1, cell.Item2);
            float pelletX = basePos.X + 3f * MathF.Sin(phase * 1.3f + 0.7f);
            float pelletY = basePos.Y + 6f * MathF.Sin(phase);

            float dx = playerPos.X - pelletX;
            float dy = playerPos.Y - pelletY;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            float proximity = 0f;
            if (dist < attractRadius && dist > 0.1f)
            {
                proximity = 1f - dist / attractRadius;
                float pull = proximity * attractStrength;
                pelletX += dx * pull;
                pelletY += dy * pull;
            }

            float bright = 1f + proximity * 0.6f;

            // Rapid hue cycling, very close to white
            float hue = (CoinWaveTime * 2.5f + cell.Item1 * 0.7f) % 1f;
            Color rainbow = LightFactory.HueToRGB(hue);
            const float tint = 0.15f;

            ref var pelletMat = ref ECS.GetComponent<Material>(pelletId);
            pelletMat.Emissive = new Color(
                (byte)MathF.Min((255f * (1f - tint) + rainbow.R * tint) * bright, 255f),
                (byte)MathF.Min((255f * (1f - tint) + rainbow.G * tint) * bright, 255f),
                (byte)MathF.Min((255f * (1f - tint) + rainbow.B * tint) * bright, 255f),
                BasePowerPelletColor.A);

            ref var pelletTransform = ref ECS.GetComponent<Transform>(pelletId);
            pelletTransform.Position.X = pelletX;
            pelletTransform.Position.Y = pelletY;
            float pelletScale = 1f + proximity * 0.6f;
            pelletTransform.Scale = new Vector3(pelletScale, pelletScale, 1f);
        }

        // Update HUD labels
        Inspector.SetLabel("scene", "level", $"Level: {CurrentLevel + 1}/{Levels.Length}");
    }

    private void UpdateUpscalerInput()
    {
        Func<Texture2D> giSource = () =>
            GIGroup.Active is HRCGI hrcgi ? hrcgi.GetOutput() :
            GIGroup.Active is RCGI rcgi ? rcgi.GetOutput() : null;

        Tonemapper.SetInputSource(giSource);

        Func<Texture2D> colorOutput = Tonemapper.GetOutput;

        UpscalerGroup.ForEach(system =>
        {
            switch (system)
            {
                case Bilinear bilinear: bilinear.SetInputSource(colorOutput); break;
                case UDR1 udr1: udr1.SetInputSource(colorOutput); break;
                case UDR2 udr2: udr2.SetInputSource(colorOutput); break;
                case UDR3 udr3: udr3.SetInputSource(colorOutput); break;
            }
        });
    }

    private void UpdateWindowVisibility()
    {
        // GI: show active, hide inactive
        if (GIGroup.Active is HRCGI)
        {
            Inspector.ShowWindow("hrcgi");
            Inspector.HideWindow("rcgi");
        }
        else
        {
            Inspector.HideWindow("hrcgi");
            Inspector.ShowWindow("rcgi");
        }

        // Upscaler: show active, hide rest
        string[] upscalerWindows = ["bilinear", "udr1", "udr2", "udr3"];
        string activeWindow = UpscalerGroup.Active switch
        {
            Bilinear => "bilinear",
            UDR1 => "udr1",
            UDR2 => "udr2",
            UDR3 => "udr3",
            _ => ""
        };

        foreach (var windowId in upscalerWindows)
        {
            if (windowId == activeWindow)
                Inspector.ShowWindow(windowId);
            else
                Inspector.HideWindow(windowId);
        }
    }
}
