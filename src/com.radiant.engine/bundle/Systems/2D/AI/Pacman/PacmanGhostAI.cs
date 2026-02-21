using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

[Pausable(PauseGroup.Gameplay | PauseGroup.Animation)]
[SystemTag("Pacman")]
public class PacmanGhostAI : core.System
{
    public float GhostSpeed { get; set; } = 200f;
    public float RainbowSpeed { get; set; } = 200f;
    public float GhostZ { get; set; } = 65530f;
    public Texture2D BodyTexture { get; set; }
    public float BodyRadius { get; set; } = 30f;
    public float DefaultReleaseInterval { get; set; } = 0.5f;
    public PacmanPlayerController Player { get; set; }

    public int GhostCount => GhostIds?.Length ?? 0;
    public Vector2 GetGhostEyePosition(int Index) => EyePositions[Index];
    public (int dx, int dy) GetGhostDir(int Index) => GhostDirs[Index];
    public bool IsGhostFrightened(int Index) => Frightened[Index];
    public bool IsGhostEaten(int Index) => Eaten[Index];
    public PacmanGhostType GetGhostType(int Index) => GhostEntries[Index].Type;

    public bool RainbowActive => RainbowInitialized;
    public int RainbowTotalCount => RainbowInitialized ? 1 + RainbowCloneCount : 0;
    public Vector2 GetRainbowEyePosition(int Index) => RainbowEyePositions[Index];
    public (int dx, int dy) GetRainbowEyeDir(int Index) => RainbowEyeDirs[Index];
    public bool RainbowIsMainEaten => RainbowMainEaten;
    public bool RainbowIsFrightened => RainbowFrightenedTimer > 0f;

    private const float RespawnDelay = 6f;
    private const float EatenSpeedMultiplier = 2.5f;
    private const float FrightenedSpeedMultiplier = 0.4f;
    private const float FrightenedShrink = 0.85f;
    private const float FrightenedExtraDuration = 5f;
    private const float FrightenedBlinkThreshold = 1.5f;
    private const float FrightenedBlinkRate = 8f;
    private static readonly Color FrightenedColor = new Color(30, 30, 200);
    private static readonly Color FrightenedBlinkColor = new Color(100, 150, 255);

    private static readonly int[] DXs = [0, -1, 0, 1];
    private static readonly int[] DYs = [-1, 0, 1, 0];

    private int[] GhostIds;
    private PacmanMazeBuilder Maze;
    private Geometry Geometry;
    private (int x, int y)[] GhostCells;
    private (int x, int y)[] GhostTargets;
    private (int dx, int dy)[] GhostDirs;
    private GhostEntry[] GhostEntries;
    private (int x, int y)[] ChaseTargets;
    private bool[] ExitedHouse;
    private bool[] Eaten;
    private float[] RespawnTimer;
    private Color[] GhostColors;
    private Vector2[] EyePositions;
    private Vector2[] PrevPositions;
    private Vector2[] Positions;
    private float ElapsedTime;
    private float IdleTime;
    private Random Rng = new();

    private PacmanGhostMode CurrentMode = PacmanGhostMode.Scatter;
    private float ModeTimer;
    private int ModePhase;

    private bool[] Frightened;
    private float[] FrightenedTimers;

    private static readonly (PacmanGhostMode mode, float duration)[] ModeCycle =
    [
        (PacmanGhostMode.Scatter, 7f),
        (PacmanGhostMode.Chase, 20f),
        (PacmanGhostMode.Scatter, 7f),
        (PacmanGhostMode.Chase, 20f),
        (PacmanGhostMode.Scatter, 5f),
        (PacmanGhostMode.Chase, 20f),
        (PacmanGhostMode.Scatter, 5f),
    ];

    private const int MaxClones = 2;
    private const float SoloDuration = 5f;
    private const float DuoSplitDelay = 3f;
    private const float TrioDuration = 7f;
    private static readonly float[] MergeSpeedMults = [1.8f, 1.5f, 1.3f];
    private const float HueCycleSpeed = 0.35f;

    private int RainbowMainId;
    private (int x, int y) RainbowMainCell;
    private (int x, int y) RainbowMainTarget;
    private (int dx, int dy) RainbowMainDir;
    private float RainbowMainHue;

    private int[] RainbowCloneIds = new int[MaxClones];
    private (int x, int y)[] RainbowCloneCells = new (int, int)[MaxClones];
    private (int x, int y)[] RainbowCloneTargets = new (int, int)[MaxClones];
    private (int dx, int dy)[] RainbowCloneDirs = new (int, int)[MaxClones];
    private float[] RainbowCloneHues = new float[MaxClones];
    private int RainbowCloneCount;

    private Vector2[] RainbowEyePositions;
    private Vector2[] RainbowPrevPositions;
    private (int dx, int dy)[] RainbowEyeDirs;
    private Vector2[] RainbowLogicalPositions;

    private bool RainbowMainExitedHouse;
    private float RainbowFrightenedTimer;
    private bool RainbowMainEaten;
    private float RainbowMainRespawnTimer;

    private RainbowPhase RainbowPhaseState = RainbowPhase.Solo;
    private float RainbowPhaseTimer;
    private float RainbowReleaseTime;
    private float RainbowReleaseAtCoinPercent;
    private float RainbowElapsedTime;
    private bool RainbowInitialized;

    private (int x, int y)[] RainbowWanderTargetTiles = new (int, int)[1 + MaxClones];
    private (int x, int y)[] RainbowCornerTargets = new (int, int)[1 + MaxClones];
    private float RainbowIdleTime;

    public static Color PersonalityColor(PacmanGhostType Type) => Type switch
    {
        PacmanGhostType.Blinky => new Color(255, 0, 20),
        PacmanGhostType.Pinky => new Color(255, 184, 255),
        PacmanGhostType.Inky => new Color(0, 255, 255),
        PacmanGhostType.Clyde => new Color(255, 184, 82),
        PacmanGhostType.Dinky => new Color(0, 220, 80),
        PacmanGhostType.Shadow => new Color(100, 0, 160),
        PacmanGhostType.Rainbow => Color.White,
        _ => Color.White
    };

    private (int x, int y) RandomScatterCorner() => Rng.Next(4) switch
    {
        0 => (Maze.Cols - 3, 0),
        1 => (2, 0),
        2 => (Maze.Cols - 1, Maze.Rows - 1),
        _ => (0, Maze.Rows - 1)
    };

    private (int x, int y) ScatterTarget(PacmanGhostType Type) => Type switch
    {
        PacmanGhostType.Blinky => (Maze.Cols - 3, 0),
        PacmanGhostType.Pinky => (2, 0),
        PacmanGhostType.Inky => (Maze.Cols - 1, Maze.Rows - 1),
        PacmanGhostType.Clyde => (0, Maze.Rows - 1),
        PacmanGhostType.Dinky => RandomScatterCorner(),
        PacmanGhostType.Shadow => (Maze.Cols / 2, Maze.Rows / 2),
        _ => (Maze.Cols / 2, 0)
    };

    Texture2D EyesTexture;

    public override void Initialize()
    {
        Maze = Scene.ECS.GetSystem<PacmanMazeBuilder>();
        Geometry = Scene.ECS.GetSystem<Geometry>();
        EyesTexture = Renderer.GetTexture("Eyes");
    }

    public void Track(int[] EntityIds, (int x, int y)[] StartCells, GhostEntry[] Entries)
    {
        int count = EntityIds.Length;
        GhostIds = EntityIds;
        GhostEntries = Entries;
        GhostCells = new (int, int)[count];
        GhostTargets = new (int, int)[count];
        GhostDirs = new (int, int)[count];
        ChaseTargets = new (int, int)[count];
        ExitedHouse = new bool[count];
        Eaten = new bool[count];
        RespawnTimer = new float[count];
        Frightened = new bool[count];
        FrightenedTimers = new float[count];
        GhostColors = new Color[count];
        EyePositions = new Vector2[count];
        PrevPositions = new Vector2[count];
        Positions = new Vector2[count];

        for (int index = 0; index < count; index++)
        {
            GhostCells[index] = StartCells[index];
            GhostTargets[index] = StartCells[index];
            GhostDirs[index] = (1, 0);
            ChaseTargets[index] = PickRandomWalkable();
            ExitedHouse[index] = false;
            Eaten[index] = false;
            RespawnTimer[index] = 0f;

            GhostColors[index] = PersonalityColor(Entries[index].Type);
            ref var material = ref Scene.ECS.GetComponent<Material>(GhostIds[index]);
            if (Entries[index].Type == PacmanGhostType.Shadow)
            {
                material.Albedo = GhostColors[index];
                material.Emissive = Color.Black;
            }
            else
            {
                material.Albedo = Color.Transparent;
                material.Emissive = GhostColors[index];
            }

            ref var transform = ref Scene.ECS.GetComponent<Transform>(GhostIds[index]);
            var pos = new Vector2(transform.Position.X, transform.Position.Y);
            EyePositions[index] = pos;
            PrevPositions[index] = pos;
            Positions[index] = pos;
        }

        ElapsedTime = 0f;
        ModeTimer = 0f;
        ModePhase = 0;
        CurrentMode = PacmanGhostMode.Scatter;
    }

    public void Clear()
    {
        if (GhostIds == null) return;
        for (int index = 0; index < GhostIds.Length; index++)
            Scene.ECS.DestroyEntity(GhostIds[index]);
        GhostIds = null;
    }

    public void SetFrightened(float Duration)
    {
        if (GhostIds == null) return;

        float timer = Duration + FrightenedExtraDuration;
        for (int index = 0; index < GhostIds.Length; index++)
        {
            if (Eaten[index] || RespawnTimer[index] > 0f || !ExitedHouse[index]) continue;
            Frightened[index] = true;
            FrightenedTimers[index] = timer;

            ref var material = ref Scene.ECS.GetComponent<Material>(GhostIds[index]);
            material.Albedo = Color.Transparent;
            material.Emissive = FrightenedColor;
            ref var circle = ref Scene.ECS.GetComponent<Circle2D>(GhostIds[index]);
            circle.Radius = BodyRadius * FrightenedShrink;

            var (pdx, pdy) = GhostDirs[index];
            if (pdx == 0 && pdy == 0) continue;
            var (cx, cy) = GhostCells[index];
            GhostDirs[index] = (-pdx, -pdy);
            if (CanGhostMove(index, cx, cy, -pdx, -pdy))
                GhostTargets[index] = (cx - pdx, cy - pdy);
        }
    }

    public override void Update()
    {
        float dt = (float)GameTime.ElapsedGameTime.TotalSeconds;
        UpdateRegularGhosts(dt);
        UpdateRainbowGhost(dt);
    }

    public void HideGhostEntities()
    {
        if (GhostIds != null)
        {
            for (int index = 0; index < GhostIds.Length; index++)
            {
                if (GhostIds[index] == -1 || !Scene.ECS.IsAlive(GhostIds[index])) continue;
                ref var material = ref Scene.ECS.GetComponent<Material>(GhostIds[index]);
                material.Albedo = Color.Transparent;
                material.Emissive = Color.Transparent;
            }
        }
        if (RainbowInitialized)
        {
            if (Scene.ECS.IsAlive(RainbowMainId))
            {
                ref var material = ref Scene.ECS.GetComponent<Material>(RainbowMainId);
                material.Albedo = Color.Transparent;
                material.Emissive = Color.Transparent;
            }
            for (int index = 0; index < RainbowCloneCount; index++)
            {
                if (!Scene.ECS.IsAlive(RainbowCloneIds[index])) continue;
                ref var material = ref Scene.ECS.GetComponent<Material>(RainbowCloneIds[index]);
                material.Albedo = Color.Transparent;
                material.Emissive = Color.Transparent;
            }
        }
    }

    public void ShowGhostEntities()
    {
        if (GhostIds != null)
        {
            for (int index = 0; index < GhostIds.Length; index++)
            {
                if (GhostIds[index] == -1 || !Scene.ECS.IsAlive(GhostIds[index])) continue;
                if (Eaten[index]) continue;
                if (Frightened[index])
                {
                    ref var material = ref Scene.ECS.GetComponent<Material>(GhostIds[index]);
                    material.Albedo = Color.Transparent;
                    material.Emissive = FrightenedColor;
                    continue;
                }
                ref var mat = ref Scene.ECS.GetComponent<Material>(GhostIds[index]);
                if (GhostEntries[index].Type == PacmanGhostType.Shadow)
                {
                    mat.Albedo = GhostColors[index];
                    mat.Emissive = Color.Black;
                }
                else
                {
                    mat.Albedo = Color.Transparent;
                    mat.Emissive = GhostColors[index];
                }
            }
        }
        if (RainbowInitialized)
        {
            if (Scene.ECS.IsAlive(RainbowMainId) && !RainbowMainEaten)
                RainbowUpdateMainColor();
            for (int index = 0; index < RainbowCloneCount; index++)
            {
                if (Scene.ECS.IsAlive(RainbowCloneIds[index]))
                    RainbowUpdateCloneColor(index);
            }
        }
    }

    public override void Dispose()
    {
        if (!RainbowInitialized) return;
        for (int index = RainbowCloneCount - 1; index >= 0; index--)
            RainbowDestroyClone(index);
    }

    private void UpdateRegularGhosts(float DeltaTime)
    {
        if (GhostIds == null) return;

        IdleTime += DeltaTime;

        for (int index = 0; index < GhostIds.Length; index++)
        {
            EyePositions[index] = PrevPositions[index];
            if (GhostIds[index] == -1 || !Scene.ECS.IsAlive(GhostIds[index])) continue;
            if (Scene.ECS.IsDisabled(GhostIds[index])) continue;
            ref var transform = ref Scene.ECS.GetComponent<Transform>(GhostIds[index]);
            PrevPositions[index] = new Vector2(transform.Position.X, transform.Position.Y);
        }

        if (Player == null || !Player.HasMoved)
        {
            for (int index = 0; index < GhostIds.Length; index++)
            {
                if (GhostIds[index] == -1 || !Scene.ECS.IsAlive(GhostIds[index])) continue;
                if (Scene.ECS.IsDisabled(GhostIds[index])) continue;
                ref var transform = ref Scene.ECS.GetComponent<Transform>(GhostIds[index]);
                float wobble = MathF.Sin(IdleTime * 3f + index * 1.7f) * 3f;
                var pos = Positions[index];
                transform.Position = new Vector3(pos.X, pos.Y + wobble, GhostZ);
            }
            return;
        }

        ElapsedTime += DeltaTime;
        UpdateMode(DeltaTime);

        float step = GhostSpeed * DeltaTime;
        float frightenedStep = step * FrightenedSpeedMultiplier;

        for (int index = 0; index < GhostIds.Length; index++)
        {
            if (GhostIds[index] == -1 || !Scene.ECS.IsAlive(GhostIds[index])) continue;
            if (Scene.ECS.IsDisabled(GhostIds[index])) continue;

            if (RespawnTimer[index] > 0f)
            {
                RespawnTimer[index] -= DeltaTime;
                if (RespawnTimer[index] <= 0f)
                    RespawnGhost(index);
            }

            float currentStep;
            if (Eaten[index])
                currentStep = step * EatenSpeedMultiplier;
            else if (!ExitedHouse[index] && !IsReleased(index))
                currentStep = step * 0.5f;
            else if (Frightened[index])
                currentStep = frightenedStep;
            else
                currentStep = step;

            if (GhostEntries[index].Type == PacmanGhostType.Shadow && ExitedHouse[index]
                && !Eaten[index] && !Frightened[index])
            {
                var (scx, scy) = GhostCells[index];
                var (stx, sty) = Player != null ? Player.Cell : ChaseTargets[index];
                float shadowDist = MathF.Sqrt((scx - stx) * (scx - stx) + (scy - sty) * (scy - sty));
                float mult = shadowDist < 4f ? 1.0f : shadowDist < 12f ? 1.5f : 1.25f;
                currentStep = step * mult;
            }

            ref var transform = ref Scene.ECS.GetComponent<Transform>(GhostIds[index]);
            var pos = Positions[index];
            var target = Maze.CellCenter(GhostTargets[index].x, GhostTargets[index].y);

            var diff = target - pos;
            float dist = diff.Length();

            if (dist <= currentStep)
            {
                var raw = GhostTargets[index];
                GhostCells[index] = (Maze.WrapX(raw.x), raw.y);
                pos = Maze.CellCenter(GhostCells[index].x, GhostCells[index].y);
                PickDirection(index);

                target = Maze.CellCenter(GhostTargets[index].x, GhostTargets[index].y);
                diff = target - pos;
                dist = diff.Length();
            }

            if (dist > 0.01f)
            {
                var move = diff / dist * MathF.Min(currentStep, dist);
                pos += move;
            }

            Positions[index] = pos;

            var (dirX, dirY) = GhostDirs[index];
            float wobble = MathF.Sin(ElapsedTime * 5f + index * 2.5f) * 4f;
            transform.Position = new Vector3(
                pos.X + (dirY != 0 ? wobble : 0f),
                pos.Y + (dirX != 0 ? wobble : 0f), GhostZ);

            if (ExitedHouse[index] && !Eaten[index] && RespawnTimer[index] <= 0f
                && Player != null && !Player.PlayerCaught)
            {
                float catchDx = pos.X - Player.WorldPosition.X;
                float catchDy = pos.Y - Player.WorldPosition.Y;
                float catchRadius = BodyRadius + Player.HitboxRadius;
                if (catchDx * catchDx + catchDy * catchDy < catchRadius * catchRadius)
                {
                    if (Frightened[index])
                        EatGhost(index);
                    else
                        Player.PlayerCaught = true;
                }
            }
        }
    }

    private void EatGhost(int GhostIndex)
    {
        Eaten[GhostIndex] = true;
        Frightened[GhostIndex] = false;
        FrightenedTimers[GhostIndex] = 0f;
        ref var material = ref Scene.ECS.GetComponent<Material>(GhostIds[GhostIndex]);
        material.Albedo = Color.Transparent;
        material.Emissive = Color.Transparent;
        ref var circle = ref Scene.ECS.GetComponent<Circle2D>(GhostIds[GhostIndex]);
        circle.Radius = BodyRadius;
    }

    private void RespawnGhost(int GhostIndex)
    {
        RespawnTimer[GhostIndex] = 0f;
        ExitedHouse[GhostIndex] = false;
        ref var material = ref Scene.ECS.GetComponent<Material>(GhostIds[GhostIndex]);
        if (GhostEntries[GhostIndex].Type == PacmanGhostType.Shadow)
        {
            material.Albedo = GhostColors[GhostIndex];
            material.Emissive = Color.Black;
        }
        else
        {
            material.Albedo = Color.Transparent;
            material.Emissive = GhostColors[GhostIndex];
        }
    }

    private void RestoreGhostColor(int GhostIndex)
    {
        if (Eaten[GhostIndex] || RespawnTimer[GhostIndex] > 0f) return;
        ref var material = ref Scene.ECS.GetComponent<Material>(GhostIds[GhostIndex]);
        if (GhostEntries[GhostIndex].Type == PacmanGhostType.Shadow)
        {
            material.Albedo = GhostColors[GhostIndex];
            material.Emissive = Color.Black;
        }
        else
        {
            material.Albedo = Color.Transparent;
            material.Emissive = GhostColors[GhostIndex];
        }
    }

    private void UpdateMode(float DeltaTime)
    {
        for (int index = 0; index < GhostIds.Length; index++)
        {
            if (!Frightened[index]) continue;

            FrightenedTimers[index] -= DeltaTime;
            if (FrightenedTimers[index] <= 0f)
            {
                Frightened[index] = false;
                FrightenedTimers[index] = 0f;
                RestoreGhostColor(index);
                ref var circle = ref Scene.ECS.GetComponent<Circle2D>(GhostIds[index]);
                circle.Radius = BodyRadius;
            }
            else if (FrightenedTimers[index] <= FrightenedBlinkThreshold)
            {
                bool blinkOn = MathF.Sin(FrightenedTimers[index] * FrightenedBlinkRate * MathF.PI) > 0f;
                ref var material = ref Scene.ECS.GetComponent<Material>(GhostIds[index]);
                material.Emissive = blinkOn ? FrightenedBlinkColor : FrightenedColor;
            }
            else
            {
                float hueShift = MathF.Sin(IdleTime * 2.5f + index * 1.3f);
                byte red = (byte)(30 + 42 * MathF.Max(0, -hueShift));
                byte green = (byte)(30 + 55 * MathF.Max(0, hueShift));
                byte blue = (byte)(200 + 42 * MathF.Abs(hueShift));
                ref var material = ref Scene.ECS.GetComponent<Material>(GhostIds[index]);
                material.Emissive = new Color(red, green, blue);
            }
        }

        if (ModePhase >= ModeCycle.Length)
        {
            CurrentMode = PacmanGhostMode.Chase;
            return;
        }

        ModeTimer += DeltaTime;
        if (ModeTimer >= ModeCycle[ModePhase].duration)
        {
            ModeTimer -= ModeCycle[ModePhase].duration;
            ModePhase++;

            CurrentMode = ModePhase < ModeCycle.Length
                ? ModeCycle[ModePhase].mode
                : PacmanGhostMode.Chase;
        }
    }

    private bool CanGhostMove(int GhostIndex, int CellX, int CellY, int DirX, int DirY)
    {
        if (!Maze.CanMove(CellX, CellY, DirX, DirY)) return false;
        if (ExitedHouse[GhostIndex])
        {
            int nextX = Maze.WrapX(CellX + DirX), nextY = CellY + DirY;
            if (Maze.IsGhostDoor(nextX, nextY) && !Eaten[GhostIndex]) return false;
            if (GhostEntries[GhostIndex].Type == PacmanGhostType.Shadow && !Eaten[GhostIndex] && nextX != CellX + DirX) return false;
        }
        return true;
    }

    private (int x, int y) GetTargetTile(int GhostIndex)
    {
        if (CurrentMode == PacmanGhostMode.Scatter)
            return ScatterTarget(GhostEntries[GhostIndex].Type);

        var (cx, cy) = GhostCells[GhostIndex];

        if (Player != null)
        {
            var (px, py) = Player.Cell;

            if (GhostEntries[GhostIndex].Type == PacmanGhostType.Clyde)
            {
                float dist = MathF.Sqrt((cx - px) * (cx - px) + (cy - py) * (cy - py));
                if (dist < 8f)
                    return ScatterTarget(PacmanGhostType.Clyde);
            }

            if (GhostEntries[GhostIndex].Type == PacmanGhostType.Dinky)
            {
                float dist = MathF.Sqrt((cx - px) * (cx - px) + (cy - py) * (cy - py));
                if (dist < 10f)
                    return RandomScatterCorner();
            }

            return (px, py);
        }

        var (tx, ty) = ChaseTargets[GhostIndex];

        if (GhostEntries[GhostIndex].Type == PacmanGhostType.Clyde)
        {
            float dist = MathF.Sqrt((cx - tx) * (cx - tx) + (cy - ty) * (cy - ty));
            if (dist < 8f)
                return ScatterTarget(PacmanGhostType.Clyde);
        }

        if (GhostEntries[GhostIndex].Type == PacmanGhostType.Dinky)
        {
            float dist = MathF.Sqrt((cx - tx) * (cx - tx) + (cy - ty) * (cy - ty));
            if (dist < 10f)
                return RandomScatterCorner();
        }

        if (GhostEntries[GhostIndex].Type == PacmanGhostType.Shadow)
        {
            float manhattan = MathF.Abs(cx - tx) + MathF.Abs(cy - ty);
            if (manhattan <= 1 || Maze.IsWall(tx, ty))
                ChaseTargets[GhostIndex] = PickRandomWalkable();
            return ChaseTargets[GhostIndex];
        }

        float manhattan2 = MathF.Abs(cx - tx) + MathF.Abs(cy - ty);
        if (manhattan2 <= 2 || Maze.IsWall(tx, ty))
            ChaseTargets[GhostIndex] = PickRandomWalkable();

        return ChaseTargets[GhostIndex];
    }

    private void PickDirection(int GhostIndex)
    {
        var (cx, cy) = GhostCells[GhostIndex];
        var (pdx, pdy) = GhostDirs[GhostIndex];

        if (!ExitedHouse[GhostIndex])
        {
            if (!Maze.InGhostHouse(cx, cy) && !Maze.IsGhostDoor(cx, cy))
            {
                ExitedHouse[GhostIndex] = true;
            }
            else
            {
                if (IsReleased(GhostIndex))
                    PickHouseExit(GhostIndex);
                else
                    PickHouseWander(GhostIndex);
                return;
            }
        }

        if (Eaten[GhostIndex])
        {
            PickEatenPath(GhostIndex);
            return;
        }

        if (Frightened[GhostIndex])
        {
            PickRandom(GhostIndex);
            return;
        }

        var targetTile = GetTargetTile(GhostIndex);

        Span<(int dx, int dy)> options = stackalloc (int, int)[4];
        int count = 0;

        for (int dir = 0; dir < 4; dir++)
        {
            if (!CanGhostMove(GhostIndex, cx, cy, DXs[dir], DYs[dir])) continue;
            if (DXs[dir] == -pdx && DYs[dir] == -pdy) continue;
            if (DYs[dir] == -1 && Maze.IsNoUpTile(cx, cy)) continue;
            options[count++] = (DXs[dir], DYs[dir]);
        }

        if (count == 0)
        {
            GhostDirs[GhostIndex] = (-pdx, -pdy);
            GhostTargets[GhostIndex] = (cx - pdx, cy - pdy);
            return;
        }

        if (count == 1)
        {
            var only = options[0];
            GhostDirs[GhostIndex] = only;
            GhostTargets[GhostIndex] = (cx + only.dx, cy + only.dy);
            return;
        }

        float bestDist = float.MaxValue;
        (int dx, int dy) bestDir = options[0];

        for (int optIndex = 0; optIndex < count; optIndex++)
        {
            float distX = cx + options[optIndex].dx - targetTile.x;
            float distY = cy + options[optIndex].dy - targetTile.y;
            float dist = distX * distX + distY * distY;

            if (dist < bestDist)
            {
                bestDist = dist;
                bestDir = options[optIndex];
            }
        }

        GhostDirs[GhostIndex] = bestDir;
        GhostTargets[GhostIndex] = (cx + bestDir.dx, cy + bestDir.dy);
    }

    private void PickEatenPath(int GhostIndex)
    {
        var (cx, cy) = GhostCells[GhostIndex];
        var (pdx, pdy) = GhostDirs[GhostIndex];

        if (Maze.InGhostHouse(cx, cy))
        {
            Eaten[GhostIndex] = false;
            RespawnTimer[GhostIndex] = RespawnDelay;
            ExitedHouse[GhostIndex] = false;

            ref var material = ref Scene.ECS.GetComponent<Material>(GhostIds[GhostIndex]);
            material.Albedo = Color.Transparent;
            material.Emissive = GhostColors[GhostIndex];

            PickHouseWander(GhostIndex);
            return;
        }

        if (Maze.IsGhostDoor(cx, cy))
        {
            GhostDirs[GhostIndex] = (0, 1);
            GhostTargets[GhostIndex] = (cx, cy + 1);
            return;
        }

        int targetX = Maze.HouseDoorLeft;
        int targetY = Maze.HouseDoorY;

        Span<(int dx, int dy)> options = stackalloc (int, int)[4];
        int count = 0;

        for (int dir = 0; dir < 4; dir++)
        {
            if (!CanGhostMove(GhostIndex, cx, cy, DXs[dir], DYs[dir])) continue;
            if (DXs[dir] == -pdx && DYs[dir] == -pdy) continue;
            options[count++] = (DXs[dir], DYs[dir]);
        }

        if (count == 0)
        {
            GhostDirs[GhostIndex] = (-pdx, -pdy);
            GhostTargets[GhostIndex] = (cx - pdx, cy - pdy);
            return;
        }

        if (count == 1)
        {
            var only = options[0];
            GhostDirs[GhostIndex] = only;
            GhostTargets[GhostIndex] = (cx + only.dx, cy + only.dy);
            return;
        }

        float bestDist = float.MaxValue;
        (int dx, int dy) bestDir = options[0];

        for (int optIndex = 0; optIndex < count; optIndex++)
        {
            float distX = cx + options[optIndex].dx - targetX;
            float distY = cy + options[optIndex].dy - targetY;
            float dist = distX * distX + distY * distY;
            if (dist < bestDist)
            {
                bestDist = dist;
                bestDir = options[optIndex];
            }
        }

        GhostDirs[GhostIndex] = bestDir;
        GhostTargets[GhostIndex] = (cx + bestDir.dx, cy + bestDir.dy);
    }

    private void PickHouseExit(int GhostIndex)
    {
        var (cx, cy) = GhostCells[GhostIndex];
        int doorL = Maze.HouseDoorLeft;
        int doorR = Maze.HouseDoorRight;

        if (cx < doorL && Maze.CanMove(cx, cy, 1, 0))
        {
            GhostDirs[GhostIndex] = (1, 0);
            GhostTargets[GhostIndex] = (cx + 1, cy);
        }
        else if (cx > doorR && Maze.CanMove(cx, cy, -1, 0))
        {
            GhostDirs[GhostIndex] = (-1, 0);
            GhostTargets[GhostIndex] = (cx - 1, cy);
        }
        else if (Maze.CanMove(cx, cy, 0, -1))
        {
            GhostDirs[GhostIndex] = (0, -1);
            GhostTargets[GhostIndex] = (cx, cy - 1);
        }
        else
        {
            for (int dir = 0; dir < 4; dir++)
                if (Maze.CanMove(cx, cy, DXs[dir], DYs[dir]))
                {
                    GhostDirs[GhostIndex] = (DXs[dir], DYs[dir]);
                    GhostTargets[GhostIndex] = (cx + DXs[dir], cy + DYs[dir]);
                    return;
                }
        }
    }

    private void PickHouseWander(int GhostIndex)
    {
        var (cx, cy) = GhostCells[GhostIndex];
        var (pdx, _) = GhostDirs[GhostIndex];

        if (pdx == 0)
            pdx = Rng.Next(2) == 0 ? -1 : 1;

        if (Maze.CanMove(cx, cy, pdx, 0) && Maze.InGhostHouse(cx + pdx, cy))
        {
            GhostDirs[GhostIndex] = (pdx, 0);
            GhostTargets[GhostIndex] = (cx + pdx, cy);
        }
        else if (Maze.CanMove(cx, cy, -pdx, 0) && Maze.InGhostHouse(cx - pdx, cy))
        {
            GhostDirs[GhostIndex] = (-pdx, 0);
            GhostTargets[GhostIndex] = (cx - pdx, cy);
        }
        else
        {
            GhostDirs[GhostIndex] = (0, 0);
            GhostTargets[GhostIndex] = (cx, cy);
        }
    }

    private void PickRandom(int GhostIndex)
    {
        var (cx, cy) = GhostCells[GhostIndex];
        var (pdx, pdy) = GhostDirs[GhostIndex];

        Span<(int dx, int dy)> options = stackalloc (int, int)[4];
        int count = 0;

        for (int dir = 0; dir < 4; dir++)
            if (CanGhostMove(GhostIndex, cx, cy, DXs[dir], DYs[dir]) && !(DXs[dir] == -pdx && DYs[dir] == -pdy))
                options[count++] = (DXs[dir], DYs[dir]);

        if (count == 0)
        {
            GhostDirs[GhostIndex] = (-pdx, -pdy);
            GhostTargets[GhostIndex] = (cx - pdx, cy - pdy);
            return;
        }

        var pick = options[Rng.Next(count)];
        GhostDirs[GhostIndex] = pick;
        GhostTargets[GhostIndex] = (cx + pick.dx, cy + pick.dy);
    }

    private bool IsReleased(int GhostIndex)
    {
        if (RespawnTimer[GhostIndex] > 0f) return false;
        ref var entry = ref GhostEntries[GhostIndex];
        if (entry.ReleaseAfter > 0 && ElapsedTime >= entry.ReleaseAfter) return true;
        if (entry.ReleaseAtCoinPercent > 0 && Player != null && Player.CoinsTotal > 0)
        {
            float coinPercent = (float)Player.CoinsCollected / Player.CoinsTotal;
            if (coinPercent >= entry.ReleaseAtCoinPercent) return true;
        }
        if (entry.ReleaseAfter <= 0 && entry.ReleaseAtCoinPercent <= 0) return true;
        return false;
    }

    private (int x, int y) PickRandomWalkable()
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            int cellX = Rng.Next(Maze.Cols);
            int cellY = Rng.Next(Maze.Rows);
            if (!Maze.IsWall(cellX, cellY) && !Maze.InGhostHouse(cellX, cellY) && !Maze.IsGhostDoor(cellX, cellY))
                return (cellX, cellY);
        }
        return (Maze.Cols / 2, 0);
    }

    public void TrackRainbow(int EntityId, (int x, int y) StartCell, float ReleaseTime = 0f, float ReleaseAtCoinPercent = 0f)
    {
        RainbowMainId = EntityId;
        RainbowMainCell = StartCell;
        RainbowMainTarget = StartCell;
        RainbowMainDir = (1, 0);
        RainbowMainHue = 0f;
        RainbowMainExitedHouse = false;
        RainbowMainEaten = false;
        RainbowMainRespawnTimer = 0f;
        RainbowFrightenedTimer = 0f;
        RainbowCloneCount = 0;
        RainbowPhaseState = RainbowPhase.Solo;
        RainbowPhaseTimer = 0f;
        RainbowReleaseTime = ReleaseTime;
        RainbowReleaseAtCoinPercent = ReleaseAtCoinPercent;
        RainbowElapsedTime = 0f;
        RainbowInitialized = true;

        int total = 1 + MaxClones;
        RainbowEyePositions = new Vector2[total];
        RainbowPrevPositions = new Vector2[total];
        RainbowEyeDirs = new (int, int)[total];
        RainbowLogicalPositions = new Vector2[total];

        ref var transform = ref Scene.ECS.GetComponent<Transform>(RainbowMainId);
        var pos = new Vector2(transform.Position.X, transform.Position.Y);
        RainbowEyePositions[0] = pos;
        RainbowPrevPositions[0] = pos;
        RainbowEyeDirs[0] = (1, 0);
        RainbowLogicalPositions[0] = pos;

        RainbowWanderTargetTiles[0] = RainbowPickRandomWalkable();

        RainbowUpdateMainColor();
    }

    public void ClearRainbow()
    {
        if (!RainbowInitialized) return;
        for (int index = RainbowCloneCount - 1; index >= 0; index--)
            RainbowDestroyClone(index);
        Scene.ECS.DestroyEntity(RainbowMainId);
        RainbowInitialized = false;
    }

    public void SetRainbowFrightened(float Duration)
    {
        if (!RainbowInitialized) return;
        RainbowFrightenedTimer = Duration + FrightenedExtraDuration;

        if (!RainbowMainEaten && RainbowMainExitedHouse)
        {
            ref var mainMaterial = ref Scene.ECS.GetComponent<Material>(RainbowMainId);
            mainMaterial.Albedo = Color.Transparent;
            mainMaterial.Emissive = FrightenedColor;
            ref var mainCircle = ref Scene.ECS.GetComponent<Circle2D>(RainbowMainId);
            mainCircle.Radius = BodyRadius * FrightenedShrink;

            var (pdx, pdy) = RainbowMainDir;
            if (pdx != 0 || pdy != 0)
            {
                RainbowMainDir = (-pdx, -pdy);
                var (cx, cy) = RainbowMainCell;
                if (RainbowCanMove(cx, cy, -pdx, -pdy))
                    RainbowMainTarget = (cx - pdx, cy - pdy);
            }
        }
        for (int index = 0; index < RainbowCloneCount; index++)
        {
            ref var cloneMaterial = ref Scene.ECS.GetComponent<Material>(RainbowCloneIds[index]);
            cloneMaterial.Albedo = Color.Transparent;
            cloneMaterial.Emissive = FrightenedColor;
            ref var cloneCircle = ref Scene.ECS.GetComponent<Circle2D>(RainbowCloneIds[index]);
            cloneCircle.Radius = BodyRadius * FrightenedShrink;

            var (cdx, cdy) = RainbowCloneDirs[index];
            if (cdx != 0 || cdy != 0)
            {
                RainbowCloneDirs[index] = (-cdx, -cdy);
                var (ccx, ccy) = RainbowCloneCells[index];
                if (RainbowCanMove(ccx, ccy, -cdx, -cdy))
                    RainbowCloneTargets[index] = (ccx - cdx, ccy - cdy);
            }
        }
    }

    private void UpdateRainbowGhost(float DeltaTime)
    {
        if (!RainbowInitialized) return;

        RainbowIdleTime += DeltaTime;

        if (!RainbowMainEaten && RainbowMainRespawnTimer <= 0f)
            RainbowMainHue = (RainbowMainHue + HueCycleSpeed * DeltaTime) % 1f;

        RainbowShiftEyeHistory();

        if (RainbowFrightenedTimer <= 0f && !RainbowMainEaten && RainbowMainRespawnTimer <= 0f)
        {
            RainbowUpdateMainColor();
            for (int index = 0; index < RainbowCloneCount; index++)
                RainbowUpdateCloneColor(index);
        }

        if (RainbowFrightenedTimer > 0f)
        {
            RainbowFrightenedTimer -= DeltaTime;
            if (RainbowFrightenedTimer <= 0f)
            {
                if (!RainbowMainEaten && RainbowMainRespawnTimer <= 0f)
                {
                    RainbowUpdateMainColor();
                    ref var mainCircle = ref Scene.ECS.GetComponent<Circle2D>(RainbowMainId);
                    mainCircle.Radius = BodyRadius;
                }
                for (int index = 0; index < RainbowCloneCount; index++)
                {
                    RainbowUpdateCloneColor(index);
                    ref var cloneCircle = ref Scene.ECS.GetComponent<Circle2D>(RainbowCloneIds[index]);
                    cloneCircle.Radius = BodyRadius;
                }
            }
            else if (RainbowFrightenedTimer <= FrightenedBlinkThreshold)
            {
                bool blinkOn = MathF.Sin(RainbowFrightenedTimer * FrightenedBlinkRate * MathF.PI) > 0f;
                var blinkColor = blinkOn ? FrightenedBlinkColor : FrightenedColor;
                if (!RainbowMainEaten && RainbowMainRespawnTimer <= 0f && RainbowMainExitedHouse)
                {
                    ref var mainMaterial = ref Scene.ECS.GetComponent<Material>(RainbowMainId);
                    mainMaterial.Emissive = blinkColor;
                }
                for (int index = 0; index < RainbowCloneCount; index++)
                {
                    ref var cloneMaterial = ref Scene.ECS.GetComponent<Material>(RainbowCloneIds[index]);
                    cloneMaterial.Emissive = blinkColor;
                }
            }
            else
            {
                float hueShift = MathF.Sin(RainbowIdleTime * 2.5f);
                byte red = (byte)(30 + 42 * MathF.Max(0, -hueShift));
                byte green = (byte)(30 + 55 * MathF.Max(0, hueShift));
                byte blue = (byte)(200 + 42 * MathF.Abs(hueShift));
                var cycledColor = new Color(red, green, blue);
                if (!RainbowMainEaten && RainbowMainRespawnTimer <= 0f && RainbowMainExitedHouse)
                {
                    ref var mainMaterial = ref Scene.ECS.GetComponent<Material>(RainbowMainId);
                    mainMaterial.Emissive = cycledColor;
                }
                for (int index = 0; index < RainbowCloneCount; index++)
                {
                    ref var cloneMaterial = ref Scene.ECS.GetComponent<Material>(RainbowCloneIds[index]);
                    cloneMaterial.Emissive = cycledColor;
                }
            }
        }

        if (RainbowMainRespawnTimer > 0f)
        {
            RainbowMainRespawnTimer -= DeltaTime;
            if (RainbowMainRespawnTimer <= 0f)
                RainbowRespawnMain();
        }

        if (Player == null || !Player.HasMoved)
        {
            ref var transform = ref Scene.ECS.GetComponent<Transform>(RainbowMainId);
            var pos = RainbowLogicalPositions[0];
            float wobble = MathF.Sin(RainbowIdleTime * 3f) * 3f;
            transform.Position = new Vector3(pos.X, pos.Y + wobble, GhostZ);
            return;
        }

        RainbowElapsedTime += DeltaTime;

        if (!RainbowIsReleased())
        {
            RainbowMoveEntity(ref RainbowMainCell, ref RainbowMainTarget, ref RainbowMainDir, RainbowMainId, DeltaTime, 0);
            return;
        }

        if (RainbowMainEaten)
        {
            RainbowMoveEntity(ref RainbowMainCell, ref RainbowMainTarget, ref RainbowMainDir, RainbowMainId, DeltaTime, 0);
            return;
        }

        if (RainbowMainRespawnTimer > 0f)
        {
            RainbowMoveEntity(ref RainbowMainCell, ref RainbowMainTarget, ref RainbowMainDir, RainbowMainId, DeltaTime, 0);
            return;
        }

        RainbowPhaseTimer += DeltaTime;
        switch (RainbowPhaseState)
        {
            case RainbowPhase.Solo:
                RainbowMoveEntity(ref RainbowMainCell, ref RainbowMainTarget, ref RainbowMainDir, RainbowMainId, DeltaTime, 0);
                if (RainbowPhaseTimer >= SoloDuration)
                {
                    RainbowCornerTargets[0] = RainbowGetCorner(0);
                    RainbowSpawnClone(0);
                    RainbowPhaseState = RainbowPhase.Duo;
                    RainbowPhaseTimer = 0f;
                }
                break;

            case RainbowPhase.Duo:
                RainbowMoveEntity(ref RainbowMainCell, ref RainbowMainTarget, ref RainbowMainDir, RainbowMainId, DeltaTime, 0);
                RainbowMoveEntity(ref RainbowCloneCells[0], ref RainbowCloneTargets[0], ref RainbowCloneDirs[0], RainbowCloneIds[0], DeltaTime, 1);
                if (RainbowPhaseTimer >= DuoSplitDelay)
                {
                    RainbowSpawnClone(1);
                    RainbowPhaseState = RainbowPhase.Trio;
                    RainbowPhaseTimer = 0f;
                }
                break;

            case RainbowPhase.Trio:
                RainbowMoveEntity(ref RainbowMainCell, ref RainbowMainTarget, ref RainbowMainDir, RainbowMainId, DeltaTime, 0);
                for (int index = 0; index < RainbowCloneCount; index++)
                    RainbowMoveEntity(ref RainbowCloneCells[index], ref RainbowCloneTargets[index], ref RainbowCloneDirs[index], RainbowCloneIds[index], DeltaTime, 1 + index);
                if (RainbowPhaseTimer >= TrioDuration)
                {
                    RainbowPhaseState = RainbowPhase.Merging;
                    RainbowPhaseTimer = 0f;
                }
                break;

            case RainbowPhase.Merging:
                RainbowMoveEntity(ref RainbowMainCell, ref RainbowMainTarget, ref RainbowMainDir, RainbowMainId, DeltaTime * MergeSpeedMults[0], 0);
                for (int index = 0; index < RainbowCloneCount; index++)
                    RainbowMoveEntity(ref RainbowCloneCells[index], ref RainbowCloneTargets[index], ref RainbowCloneDirs[index], RainbowCloneIds[index], DeltaTime * MergeSpeedMults[1 + index], 1 + index);

                RainbowCheckMerges();

                if (RainbowCloneCount == 0)
                {
                    RainbowPhaseState = RainbowPhase.Solo;
                    RainbowPhaseTimer = 0f;
                    RainbowWanderTargetTiles[0] = RainbowPickRandomWalkable();
                }
                break;
        }
    }

    private void RainbowEatEntity(int EyeIndex)
    {
        if (EyeIndex == 0)
        {
            for (int index = RainbowCloneCount - 1; index >= 0; index--)
                RainbowDestroyClone(index);
            RainbowMainEaten = true;
            ref var material = ref Scene.ECS.GetComponent<Material>(RainbowMainId);
            material.Albedo = Color.Transparent;
            material.Emissive = Color.Transparent;
        }
        else
        {
            RainbowDestroyClone(EyeIndex - 1);
        }
    }

    private void RainbowRespawnMain()
    {
        RainbowMainEaten = false;
        RainbowMainRespawnTimer = 0f;
        RainbowMainExitedHouse = false;
        RainbowPhaseState = RainbowPhase.Solo;
        RainbowPhaseTimer = 0f;
        RainbowUpdateMainColor();
    }

    private void RainbowSpawnClone(int SourceEyeIndex)
    {
        int cloneIndex = RainbowCloneCount;
        var sourceCell = SourceEyeIndex == 0 ? RainbowMainCell : RainbowCloneCells[SourceEyeIndex - 1];
        var pos = RainbowLogicalPositions[SourceEyeIndex];

        RainbowCloneHues[cloneIndex] = RainbowMainHue;
        var color = RainbowFrightenedTimer > 0f ? FrightenedColor : LightFactory.HueToRGB(RainbowMainHue);
        int entityId = LightFactory.CreateLight(Scene.ECS, pos, BodyRadius,
            Color.Transparent, color, GhostZ, BodyTexture);
        Scene.ECS.AddComponent<MotionTrackable>(entityId);

        var sourceDir = SourceEyeIndex == 0 ? RainbowMainDir : RainbowCloneDirs[SourceEyeIndex - 1];
        var oppositeDir = (-sourceDir.dx, -sourceDir.dy);
        if (oppositeDir != (0, 0) && RainbowCanMove(sourceCell.x, sourceCell.y, oppositeDir.Item1, oppositeDir.Item2))
        {
            RainbowCloneDirs[cloneIndex] = oppositeDir;
            RainbowCloneTargets[cloneIndex] = (sourceCell.x + oppositeDir.Item1, sourceCell.y + oppositeDir.Item2);
        }
        else
        {
            RainbowCloneDirs[cloneIndex] = sourceDir;
            RainbowCloneTargets[cloneIndex] = (sourceCell.x + sourceDir.dx, sourceCell.y + sourceDir.dy);
        }

        RainbowCloneIds[cloneIndex] = entityId;
        RainbowCloneCells[cloneIndex] = sourceCell;

        RainbowEyePositions[1 + cloneIndex] = pos;
        RainbowPrevPositions[1 + cloneIndex] = pos;
        RainbowEyeDirs[1 + cloneIndex] = RainbowCloneDirs[cloneIndex];
        RainbowLogicalPositions[1 + cloneIndex] = pos;

        RainbowWanderTargetTiles[1 + cloneIndex] = RainbowPickRandomWalkable();
        RainbowCornerTargets[1 + cloneIndex] = RainbowGetCorner(1 + cloneIndex);
        RainbowCloneCount++;
    }

    private void RainbowCheckMerges()
    {
        float thresholdSq = BodyRadius * 2f * BodyRadius * 2f;

        for (int index = RainbowCloneCount - 1; index >= 0; index--)
        {
            ref var cloneTransform = ref Scene.ECS.GetComponent<Transform>(RainbowCloneIds[index]);
            var clonePos = new Vector2(cloneTransform.Position.X, cloneTransform.Position.Y);

            ref var mainTransform = ref Scene.ECS.GetComponent<Transform>(RainbowMainId);
            var mainPos = new Vector2(mainTransform.Position.X, mainTransform.Position.Y);
            if ((clonePos - mainPos).LengthSquared() < thresholdSq)
            {
                RainbowDestroyClone(index);
                continue;
            }

            bool merged = false;
            for (int otherIndex = index - 1; otherIndex >= 0; otherIndex--)
            {
                ref var otherTransform = ref Scene.ECS.GetComponent<Transform>(RainbowCloneIds[otherIndex]);
                var otherPos = new Vector2(otherTransform.Position.X, otherTransform.Position.Y);
                if ((clonePos - otherPos).LengthSquared() < thresholdSq)
                {
                    RainbowDestroyClone(index);
                    merged = true;
                    break;
                }
            }
            if (merged) continue;
        }
    }

    private void RainbowDestroyClone(int CloneIndex)
    {
        Scene.ECS.DestroyEntity(RainbowCloneIds[CloneIndex]);

        int last = RainbowCloneCount - 1;
        if (CloneIndex < last)
        {
            RainbowCloneIds[CloneIndex] = RainbowCloneIds[last];
            RainbowCloneCells[CloneIndex] = RainbowCloneCells[last];
            RainbowCloneTargets[CloneIndex] = RainbowCloneTargets[last];
            RainbowCloneDirs[CloneIndex] = RainbowCloneDirs[last];
            RainbowCloneHues[CloneIndex] = RainbowCloneHues[last];
            RainbowEyePositions[1 + CloneIndex] = RainbowEyePositions[1 + last];
            RainbowPrevPositions[1 + CloneIndex] = RainbowPrevPositions[1 + last];
            RainbowEyeDirs[1 + CloneIndex] = RainbowEyeDirs[1 + last];
            RainbowLogicalPositions[1 + CloneIndex] = RainbowLogicalPositions[1 + last];
            RainbowWanderTargetTiles[1 + CloneIndex] = RainbowWanderTargetTiles[1 + last];
        }

        RainbowCloneCount--;
    }

    private void RainbowMoveEntity(ref (int x, int y) Cell, ref (int x, int y) Target,
        ref (int dx, int dy) Dir, int EntityId, float DeltaTime, int EyeIndex)
    {
        float step = RainbowSpeed * DeltaTime;
        if (EyeIndex == 0 && RainbowMainEaten)
            step = RainbowSpeed * DeltaTime * EatenSpeedMultiplier;
        else if (EyeIndex == 0 && !RainbowMainExitedHouse && !RainbowIsReleased())
            step *= 0.5f;
        else if (RainbowFrightenedTimer > 0f)
            step *= FrightenedSpeedMultiplier;

        ref var transform = ref Scene.ECS.GetComponent<Transform>(EntityId);
        var pos = RainbowLogicalPositions[EyeIndex];
        var target = Maze.CellCenter(Target.x, Target.y);

        var diff = target - pos;
        float dist = diff.Length();

        if (dist <= step)
        {
            var raw = Target;
            Cell = (Maze.WrapX(raw.x), raw.y);
            pos = Maze.CellCenter(Cell.x, Cell.y);

            if (EyeIndex == 0 && !RainbowMainExitedHouse && !RainbowMainEaten)
            {
                if (!Maze.InGhostHouse(Cell.x, Cell.y) && !Maze.IsGhostDoor(Cell.x, Cell.y))
                {
                    RainbowMainExitedHouse = true;
                    if (RainbowFrightenedTimer > 0f)
                    {
                        ref var material = ref Scene.ECS.GetComponent<Material>(RainbowMainId);
                        material.Albedo = Color.Transparent;
                        material.Emissive = FrightenedColor;
                    }
                }
                else
                {
                    if (RainbowIsReleased())
                        RainbowPickHouseExit(ref Cell, ref Target, ref Dir);
                    else
                        RainbowPickHouseWander(ref Cell, ref Target, ref Dir);
                    target = Maze.CellCenter(Target.x, Target.y);
                    diff = target - pos;
                    dist = diff.Length();
                    goto applyMove;
                }
            }

            if (EyeIndex == 0 && RainbowMainEaten)
            {
                RainbowPickEatenPath(ref Cell, ref Target, ref Dir);
                target = Maze.CellCenter(Target.x, Target.y);
                diff = target - pos;
                dist = diff.Length();
                goto applyMove;
            }

            if (EyeIndex == 0 && RainbowMainRespawnTimer > 0f)
            {
                RainbowPickHouseWander(ref Cell, ref Target, ref Dir);
                target = Maze.CellCenter(Target.x, Target.y);
                diff = target - pos;
                dist = diff.Length();
                goto applyMove;
            }

            if (RainbowFrightenedTimer > 0f)
                RainbowPickRandom(ref Cell, ref Target, ref Dir);
            else
                RainbowPickDirection(ref Cell, ref Target, ref Dir, EyeIndex);

            target = Maze.CellCenter(Target.x, Target.y);
            diff = target - pos;
            dist = diff.Length();
        }

        applyMove:
        if (dist > 0.01f)
        {
            var move = diff / dist * MathF.Min(step, dist);
            pos += move;
        }

        RainbowLogicalPositions[EyeIndex] = pos;

        float wobble = MathF.Sin(RainbowElapsedTime * 5f + EyeIndex * 2.5f) * 4f;
        transform.Position = new Vector3(
            pos.X + (Dir.dy != 0 ? wobble : 0f),
            pos.Y + (Dir.dx != 0 ? wobble : 0f), GhostZ);
        RainbowEyeDirs[EyeIndex] = Dir;

        if (Player != null && !Player.PlayerCaught && RainbowMainExitedHouse
            && !RainbowMainEaten && RainbowMainRespawnTimer <= 0f)
        {
            float catchDx = pos.X - Player.WorldPosition.X;
            float catchDy = pos.Y - Player.WorldPosition.Y;
            float catchRadius = BodyRadius + Player.HitboxRadius;
            if (catchDx * catchDx + catchDy * catchDy < catchRadius * catchRadius)
            {
                if (RainbowFrightenedTimer > 0f)
                    RainbowEatEntity(EyeIndex);
                else
                    Player.PlayerCaught = true;
            }
        }
    }

    private void RainbowPickEatenPath(ref (int x, int y) Cell, ref (int x, int y) Target,
        ref (int dx, int dy) Dir)
    {
        var (cx, cy) = Cell;
        var (pdx, pdy) = Dir;

        if (Maze.InGhostHouse(cx, cy))
        {
            RainbowMainEaten = false;
            RainbowMainRespawnTimer = RespawnDelay;
            RainbowMainExitedHouse = false;

            ref var material = ref Scene.ECS.GetComponent<Material>(RainbowMainId);
            material.Albedo = Color.Transparent;
            material.Emissive = LightFactory.HueToRGB(RainbowMainHue);

            RainbowPickHouseWander(ref Cell, ref Target, ref Dir);
            return;
        }

        if (Maze.IsGhostDoor(cx, cy))
        {
            Dir = (0, 1);
            Target = (cx, cy + 1);
            return;
        }

        int targetX = Maze.HouseDoorLeft;
        int targetY = Maze.HouseDoorY;

        Span<(int dx, int dy)> options = stackalloc (int, int)[4];
        int count = 0;

        for (int dir = 0; dir < 4; dir++)
        {
            int ndx = DXs[dir], ndy = DYs[dir];
            if (!Maze.CanMove(cx, cy, ndx, ndy)) continue;
            if (ndx == -pdx && ndy == -pdy) continue;
            options[count++] = (ndx, ndy);
        }

        if (count == 0)
        {
            Dir = (-pdx, -pdy);
            Target = (cx - pdx, cy - pdy);
            return;
        }

        if (count == 1)
        {
            var only = options[0];
            Dir = only;
            Target = (cx + only.dx, cy + only.dy);
            return;
        }

        float bestDist = float.MaxValue;
        (int dx, int dy) bestDir = options[0];

        for (int optIndex = 0; optIndex < count; optIndex++)
        {
            float distX = cx + options[optIndex].dx - targetX;
            float distY = cy + options[optIndex].dy - targetY;
            float dist = distX * distX + distY * distY;
            if (dist < bestDist)
            {
                bestDist = dist;
                bestDir = options[optIndex];
            }
        }

        Dir = bestDir;
        Target = (cx + bestDir.dx, cy + bestDir.dy);
    }

    private void RainbowPickRandom(ref (int x, int y) Cell, ref (int x, int y) Target,
        ref (int dx, int dy) Dir)
    {
        var (cx, cy) = Cell;
        var (pdx, pdy) = Dir;

        Span<(int dx, int dy)> options = stackalloc (int, int)[4];
        int count = 0;

        for (int dir = 0; dir < 4; dir++)
        {
            if (!RainbowCanMove(cx, cy, DXs[dir], DYs[dir])) continue;
            if (DXs[dir] == -pdx && DYs[dir] == -pdy) continue;
            options[count++] = (DXs[dir], DYs[dir]);
        }

        if (count == 0)
        {
            Dir = (-pdx, -pdy);
            Target = (cx - pdx, cy - pdy);
            return;
        }

        var pick = options[Rng.Next(count)];
        Dir = pick;
        Target = (cx + pick.dx, cy + pick.dy);
    }

    private void RainbowPickHouseExit(ref (int x, int y) Cell, ref (int x, int y) Target,
        ref (int dx, int dy) Dir)
    {
        var (cx, cy) = Cell;
        int doorL = Maze.HouseDoorLeft;
        int doorR = Maze.HouseDoorRight;

        if (cx < doorL && Maze.CanMove(cx, cy, 1, 0))
        {
            Dir = (1, 0);
            Target = (cx + 1, cy);
        }
        else if (cx > doorR && Maze.CanMove(cx, cy, -1, 0))
        {
            Dir = (-1, 0);
            Target = (cx - 1, cy);
        }
        else if (Maze.CanMove(cx, cy, 0, -1))
        {
            Dir = (0, -1);
            Target = (cx, cy - 1);
        }
        else
        {
            for (int dir = 0; dir < 4; dir++)
                if (Maze.CanMove(cx, cy, DXs[dir], DYs[dir]))
                {
                    Dir = (DXs[dir], DYs[dir]);
                    Target = (cx + DXs[dir], cy + DYs[dir]);
                    return;
                }
        }
    }

    private void RainbowPickHouseWander(ref (int x, int y) Cell, ref (int x, int y) Target,
        ref (int dx, int dy) Dir)
    {
        var (cx, cy) = Cell;
        var (pdx, _) = Dir;

        if (pdx == 0)
            pdx = 1;

        if (Maze.CanMove(cx, cy, pdx, 0) && Maze.InGhostHouse(cx + pdx, cy))
        {
            Dir = (pdx, 0);
            Target = (cx + pdx, cy);
        }
        else if (Maze.CanMove(cx, cy, -pdx, 0) && Maze.InGhostHouse(cx - pdx, cy))
        {
            Dir = (-pdx, 0);
            Target = (cx - pdx, cy);
        }
        else
        {
            Dir = (0, 0);
            Target = (cx, cy);
        }
    }

    private void RainbowPickDirection(ref (int x, int y) Cell, ref (int x, int y) Target,
        ref (int dx, int dy) Dir, int EyeIndex)
    {
        var (cx, cy) = Cell;
        var (pdx, pdy) = Dir;

        (int tx, int ty) targetTile;
        if (RainbowPhaseState == RainbowPhase.Merging)
        {
            targetTile = RainbowNearestSiblingCell(EyeIndex);
        }
        else if ((RainbowPhaseState == RainbowPhase.Duo || RainbowPhaseState == RainbowPhase.Trio) && Player != null)
        {
            var playerCell = Player.Cell;
            var cornerCell = RainbowCornerTargets[EyeIndex];
            targetTile = ((playerCell.x * 3 + cornerCell.x * 2) / 5, (playerCell.y * 3 + cornerCell.y * 2) / 5);
        }
        else
        {
            targetTile = RainbowWanderTargetTiles[EyeIndex];
            float manhattan = MathF.Abs(cx - targetTile.tx) + MathF.Abs(cy - targetTile.ty);
            if (manhattan <= 2 || Maze.IsWall(targetTile.tx, targetTile.ty))
                RainbowWanderTargetTiles[EyeIndex] = targetTile = RainbowPickRandomWalkable();
        }

        Span<(int dx, int dy)> options = stackalloc (int, int)[4];
        int count = 0;

        for (int dir = 0; dir < 4; dir++)
        {
            if (!RainbowCanMove(cx, cy, DXs[dir], DYs[dir])) continue;
            if (DXs[dir] == -pdx && DYs[dir] == -pdy) continue;
            options[count++] = (DXs[dir], DYs[dir]);
        }

        if (count == 0)
        {
            Dir = (-pdx, -pdy);
            Target = (cx - pdx, cy - pdy);
            return;
        }

        if (count == 1)
        {
            var only = options[0];
            Dir = only;
            Target = (cx + only.dx, cy + only.dy);
            return;
        }

        float bestDist = float.MaxValue;
        (int dx, int dy) bestDir = options[0];

        for (int optIndex = 0; optIndex < count; optIndex++)
        {
            float distX = cx + options[optIndex].dx - targetTile.tx;
            float distY = cy + options[optIndex].dy - targetTile.ty;
            float dist = distX * distX + distY * distY;
            if (dist < bestDist)
            {
                bestDist = dist;
                bestDir = options[optIndex];
            }
        }

        Dir = bestDir;
        Target = (cx + bestDir.dx, cy + bestDir.dy);
    }

    private (int x, int y) RainbowNearestSiblingCell(int EyeIndex)
    {
        var self = EyeIndex == 0 ? RainbowMainCell : RainbowCloneCells[EyeIndex - 1];
        float bestDist = float.MaxValue;
        (int x, int y) best = self;

        int total = 1 + RainbowCloneCount;
        for (int index = 0; index < total; index++)
        {
            if (index == EyeIndex) continue;
            var other = index == 0 ? RainbowMainCell : RainbowCloneCells[index - 1];
            float distX = self.x - other.x;
            float distY = self.y - other.y;
            float dist = distX * distX + distY * distY;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = other;
            }
        }

        return best;
    }

    private bool RainbowCanMove(int CellX, int CellY, int DirX, int DirY)
    {
        if (!Maze.CanMove(CellX, CellY, DirX, DirY)) return false;
        if (RainbowMainEaten) return true;
        int nextX = Maze.WrapX(CellX + DirX), nextY = CellY + DirY;
        if (Maze.InGhostHouse(nextX, nextY)) return false;
        if (Maze.IsGhostDoor(nextX, nextY)) return false;
        return true;
    }

    private bool RainbowIsReleased()
    {
        if (RainbowMainRespawnTimer > 0f) return false;
        if (RainbowReleaseTime > 0 && RainbowElapsedTime >= RainbowReleaseTime) return true;
        if (RainbowReleaseAtCoinPercent > 0 && Player != null && Player.CoinsTotal > 0)
        {
            float coinPercent = (float)Player.CoinsCollected / Player.CoinsTotal;
            if (coinPercent >= RainbowReleaseAtCoinPercent) return true;
        }
        if (RainbowReleaseTime <= 0 && RainbowReleaseAtCoinPercent <= 0) return true;
        return false;
    }

    private void RainbowShiftEyeHistory()
    {
        RainbowEyePositions[0] = RainbowPrevPositions[0];
        ref var mainTransform = ref Scene.ECS.GetComponent<Transform>(RainbowMainId);
        RainbowPrevPositions[0] = new Vector2(mainTransform.Position.X, mainTransform.Position.Y);

        for (int index = 0; index < RainbowCloneCount; index++)
        {
            RainbowEyePositions[1 + index] = RainbowPrevPositions[1 + index];
            ref var cloneTransform = ref Scene.ECS.GetComponent<Transform>(RainbowCloneIds[index]);
            RainbowPrevPositions[1 + index] = new Vector2(cloneTransform.Position.X, cloneTransform.Position.Y);
        }
    }

    private void RainbowUpdateMainColor()
    {
        var color = LightFactory.HueToRGB(RainbowMainHue);
        ref var material = ref Scene.ECS.GetComponent<Material>(RainbowMainId);
        material.Albedo = Color.Transparent;
        material.Emissive = color;
    }

    private void RainbowUpdateCloneColor(int CloneIndex)
    {
        var color = LightFactory.HueToRGB(RainbowMainHue);
        ref var material = ref Scene.ECS.GetComponent<Material>(RainbowCloneIds[CloneIndex]);
        material.Albedo = Color.Transparent;
        material.Emissive = color;
    }

    private (int x, int y) RainbowGetCorner(int EyeIndex) => (EyeIndex % 4) switch
    {
        0 => (2, 1),
        1 => (Maze.Cols - 3, 1),
        2 => (0, Maze.Rows - 2),
        _ => (Maze.Cols - 1, Maze.Rows - 2),
    };

    private (int x, int y) RainbowPickRandomWalkable()
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            int cellX = Rng.Next(Maze.Cols);
            int cellY = Rng.Next(Maze.Rows);
            if (!Maze.IsWall(cellX, cellY) && !Maze.InGhostHouse(cellX, cellY) && !Maze.IsGhostDoor(cellX, cellY))
                return (cellX, cellY);
        }
        return (Maze.Cols / 2, 0);
    }

    public override void LateRender()
    {
        if (Geometry.IsDebugHidingGameplay) return;
        DrawGhostEyes();
    }

    void DrawGhostEyes()
    {
        int regularCount = GhostCount;
        int rainbowCount = RainbowTotalCount;
        if (regularCount == 0 && rainbowCount == 0) return;

        float sx = Renderer.ScreenWidth / Renderer.VirtualSize.X;
        float sy = Renderer.ScreenHeight / Renderer.VirtualSize.Y;

        Renderer.Reset()
            .Configure(BlendState.AlphaBlend)
            .SetTarget(null);

        for (int i = 0; i < regularCount; i++)
        {
            if (GhostIds[i] == -1 || !Scene.ECS.IsAlive(GhostIds[i])) continue;
            if (Scene.ECS.IsDisabled(GhostIds[i])) continue;
            float radius = IsGhostFrightened(i) ? BodyRadius * FrightenedShrink : BodyRadius;
            float eyeR = radius * 0.667f;
            float eyeD = eyeR * 2f;
            float eyeOff = radius * 0.133f;
            var dir = GetGhostDir(i);
            var eyePos = GetGhostEyePosition(i);
            float cx = eyePos.X + dir.dx * eyeOff;
            float cy = eyePos.Y + dir.dy * eyeOff;

            Color eyeColor;
            if (IsGhostEaten(i))
                eyeColor = Color.White;
            else if (IsGhostFrightened(i))
                eyeColor = Color.White;
            else
                eyeColor = GetGhostType(i) == PacmanGhostType.Shadow ? Color.White : Color.Black;

            if (EyesTexture != null)
                Renderer.DrawTexture(EyesTexture,
                    new Rectangle(
                        (int)((cx - eyeR) * sx),
                        (int)((cy - eyeR) * sy),
                        (int)(eyeD * sx),
                        (int)(eyeD * sy)),
                    eyeColor);
        }

        for (int i = 0; i < rainbowCount; i++)
        {
            int rainbowEntityId = i == 0 ? RainbowMainId : RainbowCloneIds[i - 1];
            if (!Scene.ECS.IsAlive(rainbowEntityId)) continue;
            float radius = RainbowIsFrightened ? BodyRadius * FrightenedShrink : BodyRadius;
            float eyeR = radius * 0.667f;
            float eyeD = eyeR * 2f;
            float eyeOff = radius * 0.133f;
            var dir = GetRainbowEyeDir(i);
            var eyePos = GetRainbowEyePosition(i);
            float cx = eyePos.X + dir.dx * eyeOff;
            float cy = eyePos.Y + dir.dy * eyeOff;

            Color eyeColor;
            if (i == 0 && RainbowIsMainEaten)
                eyeColor = Color.White;
            else if (RainbowIsFrightened)
                eyeColor = Color.White;
            else
                eyeColor = Color.Black;

            if (EyesTexture != null)
                Renderer.DrawTexture(EyesTexture,
                    new Rectangle(
                        (int)((cx - eyeR) * sx),
                        (int)((cy - eyeR) * sy),
                        (int)(eyeD * sx),
                        (int)(eyeD * sy)),
                    eyeColor);
        }

        Renderer.Commit();
    }
}
