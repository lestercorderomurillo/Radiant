using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace com.radiant.engine.bundle;

public enum RainbowPhase : byte { Solo, Duo, Trio, Merging }

[Pausable]
[RunAfter(typeof(PacmanMazeBuilder), typeof(PacmanPlayer))]
[RunBefore(typeof(GizmosRenderer))]
public class RainbowGhostAI : core.System
{
    public float GhostSpeed { get; set; } = 200f;
    public float GhostZ { get; set; } = 65530f;
    public Texture2D EyesTexture { get; set; }
    public Texture2D BodyTexture { get; set; }
    public float BodyRadius { get; set; } = 30f;
    public PacmanPlayer Player { get; set; }

    private const int MaxClones = 2;
    private const float SoloDuration = 5f;
    private const float DuoSplitDelay = 3f;
    private const float TrioDuration = 7f;
    private static readonly float[] MergeSpeedMults = [1.8f, 1.5f, 1.3f];
    private const float HueCycleSpeed = 0.35f;
    private const float RespawnDelay = 6f;
    private const float EatenSpeedMultiplier = 2.5f;
    private const float FrightenedSpeedMultiplier = 0.4f;
    private const float FrightenedExtraDuration = 5f;
    private const float FrightenedBlinkThreshold = 1.5f;
    private const float FrightenedBlinkRate = 8f;
    private static readonly Color FrightenedColor = new Color(30, 30, 200);
    private static readonly Color FrightenedBlinkColor = new Color(100, 150, 255);

    // Pac-Man direction priority: Up, Left, Down, Right
    private static readonly int[] DXs = [0, -1, 0, 1];
    private static readonly int[] DYs = [-1, 0, 1, 0];

    private PacmanMazeBuilder Maze;
    private Geometry Geometry;
    private Random Rng = new();

    // Main ghost
    private int MainId;
    private (int x, int y) MainCell;
    private (int x, int y) MainTarget;
    private (int dx, int dy) MainDir;
    private float MainHue;

    // Clones
    private int[] CloneIds = new int[MaxClones];
    private (int x, int y)[] CloneCells = new (int, int)[MaxClones];
    private (int x, int y)[] CloneTargets = new (int, int)[MaxClones];
    private (int dx, int dy)[] CloneDirs = new (int, int)[MaxClones];
    private float[] CloneHues = new float[MaxClones];
    private int CloneCount;

    // Eye rendering (2-frame delay for double-buffer sync)
    private Vector2[] EyePositions;      // 2-frame delayed
    private Vector2[] PrevPositions;     // 1-frame delayed
    private (int dx, int dy)[] EyeDirs;  // direction for eye offset
    private Vector2[] LogicalPositions;  // Un-wobbled positions

    // Ghost house exit
    private bool MainExitedHouse;

    // Frightened + eaten state
    private float FrightenedTimer;
    private bool MainEaten;
    private float MainRespawnTimer;

    // State machine
    private RainbowPhase Phase = RainbowPhase.Solo;
    private float PhaseTimer;
    private float ReleaseTime;
    private float ReleaseAtCoinPercent;
    private float ElapsedTime;
    private bool Initialized;

    // Wander targets (one per entity: [0]=main, [1..MaxClones]=clones)
    private (int x, int y)[] WanderTargetTiles = new (int, int)[1 + MaxClones];
    // Assigned corner per entity for spread-out behavior
    private (int x, int y)[] CornerTargets = new (int, int)[1 + MaxClones];

    public bool IsFrightened => FrightenedTimer > 0f;

    public override void Initialize()
    {
        Maze = Scene.ECS.GetSystem<PacmanMazeBuilder>();
        Geometry = Scene.ECS.GetSystem<Geometry>();
    }

    public void Track(int entityId, (int x, int y) startCell, float releaseTime = 0f, float releaseAtCoinPercent = 0f)
    {
        MainId = entityId;
        MainCell = startCell;
        MainTarget = startCell;
        MainDir = (1, 0);
        MainHue = 0f;
        MainExitedHouse = false;
        MainEaten = false;
        MainRespawnTimer = 0f;
        FrightenedTimer = 0f;
        CloneCount = 0;
        Phase = RainbowPhase.Solo;
        PhaseTimer = 0f;
        ReleaseTime = releaseTime;
        ReleaseAtCoinPercent = releaseAtCoinPercent;
        ElapsedTime = 0f;
        Initialized = true;

        // 1 main + MaxClones
        int total = 1 + MaxClones;
        EyePositions = new Vector2[total];
        PrevPositions = new Vector2[total];
        EyeDirs = new (int, int)[total];
        LogicalPositions = new Vector2[total];

        ref var t = ref Scene.ECS.GetComponent<Transform>(MainId);
        var pos = new Vector2(t.Position.X, t.Position.Y);
        EyePositions[0] = pos;
        PrevPositions[0] = pos;
        EyeDirs[0] = (0, 0);
        LogicalPositions[0] = pos;

        WanderTargetTiles[0] = PickRandomWalkable();

        UpdateMainColor();
    }

    public void Clear()
    {
        if (!Initialized) return;
        for (int i = CloneCount - 1; i >= 0; i--)
            DestroyClone(i);
        Scene.ECS.DestroyEntity(MainId);
        Initialized = false;
    }

    /// <summary>Trigger frightened mode (rainbow ghost and clones move randomly at half speed).</summary>
    public void SetFrightened(float duration)
    {
        if (!Initialized) return;
        FrightenedTimer = duration + FrightenedExtraDuration;

        // Change body colors to dark blue (skip eaten main)
        if (!MainEaten && MainExitedHouse)
        {
            ref var mat = ref Scene.ECS.GetComponent<Material>(MainId);
            mat.Albedo = Color.Transparent;
            mat.Emissive = FrightenedColor;
        }
        for (int i = 0; i < CloneCount; i++)
        {
            ref var mat = ref Scene.ECS.GetComponent<Material>(CloneIds[i]);
            mat.Albedo = Color.Transparent;
            mat.Emissive = FrightenedColor;
        }
    }

    public override void Update()
    {
        if (!Initialized) return;

        float dt = (float)GameTime.ElapsedGameTime.TotalSeconds;

        // Advance hue even before release (ghost glows in house)
        if (!MainEaten && MainRespawnTimer <= 0f)
            MainHue = (MainHue + HueCycleSpeed * dt) % 1f;

        // Shift position history for eye sync (2-frame delay)
        ShiftEyeHistory();

        // Update colors (skip if frightened or eaten)
        if (!IsFrightened && !MainEaten && MainRespawnTimer <= 0f)
        {
            UpdateMainColor();
            for (int i = 0; i < CloneCount; i++)
                UpdateCloneColor(i);
        }

        // Frightened timer countdown
        if (FrightenedTimer > 0f)
        {
            FrightenedTimer -= dt;
            if (FrightenedTimer <= 0f)
            {
                // Restore colors
                if (!MainEaten && MainRespawnTimer <= 0f)
                    UpdateMainColor();
                for (int i = 0; i < CloneCount; i++)
                    UpdateCloneColor(i);
            }
            else if (FrightenedTimer <= FrightenedBlinkThreshold)
            {
                bool blinkOn = MathF.Sin(FrightenedTimer * FrightenedBlinkRate * MathF.PI) > 0f;
                var blinkColor = blinkOn ? FrightenedBlinkColor : FrightenedColor;
                if (!MainEaten && MainRespawnTimer <= 0f && MainExitedHouse)
                {
                    ref var mainMat = ref Scene.ECS.GetComponent<Material>(MainId);
                    mainMat.Emissive = blinkColor;
                }
                for (int i = 0; i < CloneCount; i++)
                {
                    ref var cloneMat = ref Scene.ECS.GetComponent<Material>(CloneIds[i]);
                    cloneMat.Emissive = blinkColor;
                }
            }
        }

        // Respawn timer countdown (main ghost in house)
        if (MainRespawnTimer > 0f)
        {
            MainRespawnTimer -= dt;
            if (MainRespawnTimer <= 0f)
                RespawnMain();
        }

        // Freeze all movement until player moves
        if (Player == null || !Player.HasMoved) return;

        ElapsedTime += dt;

        // Before release: wander in house
        if (!IsReleased())
        {
            MoveEntity(ref MainCell, ref MainTarget, ref MainDir, MainId, dt, 0);
            return;
        }

        // Eaten main ghost: navigate to house
        if (MainEaten)
        {
            MoveEntity(ref MainCell, ref MainTarget, ref MainDir, MainId, dt, 0);
            return;
        }

        // Respawning in house: house wander
        if (MainRespawnTimer > 0f)
        {
            MoveEntity(ref MainCell, ref MainTarget, ref MainDir, MainId, dt, 0);
            return;
        }

        // State machine
        PhaseTimer += dt;
        switch (Phase)
        {
            case RainbowPhase.Solo:
                MoveEntity(ref MainCell, ref MainTarget, ref MainDir, MainId, dt, 0);
                if (PhaseTimer >= SoloDuration)
                {
                    CornerTargets[0] = GetCorner(0);
                    SpawnClone(0); // clone from main, goes opposite
                    Phase = RainbowPhase.Duo;
                    PhaseTimer = 0f;
                }
                break;

            case RainbowPhase.Duo:
                MoveEntity(ref MainCell, ref MainTarget, ref MainDir, MainId, dt, 0);
                MoveEntity(ref CloneCells[0], ref CloneTargets[0], ref CloneDirs[0], CloneIds[0], dt, 1);
                if (PhaseTimer >= DuoSplitDelay)
                {
                    SpawnClone(1); // 2nd clone from clone0, goes opposite
                    Phase = RainbowPhase.Trio;
                    PhaseTimer = 0f;
                }
                break;

            case RainbowPhase.Trio:
                MoveEntity(ref MainCell, ref MainTarget, ref MainDir, MainId, dt, 0);
                for (int i = 0; i < CloneCount; i++)
                    MoveEntity(ref CloneCells[i], ref CloneTargets[i], ref CloneDirs[i], CloneIds[i], dt, 1 + i);
                if (PhaseTimer >= TrioDuration)
                {
                    Phase = RainbowPhase.Merging;
                    PhaseTimer = 0f;
                }
                break;

            case RainbowPhase.Merging:
                MoveEntity(ref MainCell, ref MainTarget, ref MainDir, MainId, dt * MergeSpeedMults[0], 0);
                for (int i = 0; i < CloneCount; i++)
                    MoveEntity(ref CloneCells[i], ref CloneTargets[i], ref CloneDirs[i], CloneIds[i], dt * MergeSpeedMults[1 + i], 1 + i);

                CheckMerges();

                if (CloneCount == 0)
                {
                    Phase = RainbowPhase.Solo;
                    PhaseTimer = 0f;
                    WanderTargetTiles[0] = PickRandomWalkable();
                }
                break;
        }
    }

    public override void LateRender()
    {
        if (!Initialized || Geometry.IsDebugHidingGameplay) return;

        float sx = Renderer.ScreenWidth / Renderer.VirtualSize.X;
        float sy = Renderer.ScreenHeight / Renderer.VirtualSize.Y;
        float eyeR = BodyRadius * 0.667f;
        float eyeD = eyeR * 2f;
        float eyeOff = BodyRadius * 0.133f;

        Renderer.Reset()
            .Configure(BlendState.AlphaBlend)
            .SetTarget(null);

        int total = 1 + CloneCount;
        for (int i = 0; i < total; i++)
        {
            var (dx, dy) = EyeDirs[i];
            float cx = EyePositions[i].X + dx * eyeOff;
            float cy = EyePositions[i].Y + dy * eyeOff;

            if (EyesTexture != null)
            {
                Color eyeColor;
                if (i == 0 && MainEaten)
                    eyeColor = Color.White;
                else if (IsFrightened)
                    eyeColor = Color.White;
                else
                    eyeColor = Color.Black;

                Renderer.DrawTexture(EyesTexture,
                    new Rectangle(
                        (int)((cx - eyeR) * sx),
                        (int)((cy - eyeR) * sy),
                        (int)(eyeD * sx),
                        (int)(eyeD * sy)),
                    eyeColor);
            }
        }

        Renderer.Commit();
    }

    public override void Dispose()
    {
        if (!Initialized) return;
        for (int i = CloneCount - 1; i >= 0; i--)
            DestroyClone(i);
    }

    // ── Frightened + Eaten ─────────────────────────────────────────────

    private void EatEntity(int eyeIndex)
    {
        if (eyeIndex == 0)
        {
            // Main ghost eaten — destroy all clones, navigate to house
            for (int i = CloneCount - 1; i >= 0; i--)
                DestroyClone(i);
            MainEaten = true;
            ref var mat = ref Scene.ECS.GetComponent<Material>(MainId);
            mat.Albedo = Color.Transparent;
            mat.Emissive = Color.Transparent;
        }
        else
        {
            // Clone eaten — just destroy the clone
            DestroyClone(eyeIndex - 1);
        }
    }

    private void RespawnMain()
    {
        MainEaten = false;
        MainRespawnTimer = 0f;
        MainExitedHouse = false;
        Phase = RainbowPhase.Solo;
        PhaseTimer = 0f;
        UpdateMainColor();
    }

    // ── Clone Lifecycle ────────────────────────────────────────────────

    private void SpawnClone(int sourceEyeIndex)
    {
        int idx = CloneCount;
        var sourceCell = sourceEyeIndex == 0 ? MainCell : CloneCells[sourceEyeIndex - 1];
        var pos = LogicalPositions[sourceEyeIndex];

        CloneHues[idx] = MainHue;
        var color = IsFrightened ? FrightenedColor : LightFactory.HueToRGB(MainHue);
        int id = LightFactory.CreateLight(Scene.ECS, pos, BodyRadius,
            Color.Transparent, color, GhostZ, BodyTexture);
        Scene.ECS.AddComponent<MotionTrackable>(id);

        // Clone starts in opposite direction of source
        var sourceDir = sourceEyeIndex == 0 ? MainDir : CloneDirs[sourceEyeIndex - 1];
        var oppositeDir = (-sourceDir.dx, -sourceDir.dy);
        // Validate opposite direction is walkable; fall back to source dir
        if (oppositeDir != (0, 0) && CanMove(sourceCell.x, sourceCell.y, oppositeDir.Item1, oppositeDir.Item2))
        {
            CloneDirs[idx] = oppositeDir;
            CloneTargets[idx] = (sourceCell.x + oppositeDir.Item1, sourceCell.y + oppositeDir.Item2);
        }
        else
        {
            CloneDirs[idx] = sourceDir;
            CloneTargets[idx] = (sourceCell.x + sourceDir.dx, sourceCell.y + sourceDir.dy);
        }

        CloneIds[idx] = id;
        CloneCells[idx] = sourceCell;

        EyePositions[1 + idx] = pos;
        PrevPositions[1 + idx] = pos;
        EyeDirs[1 + idx] = CloneDirs[idx];
        LogicalPositions[1 + idx] = pos;

        WanderTargetTiles[1 + idx] = PickRandomWalkable();
        // Assign a different corner to each entity
        CornerTargets[1 + idx] = GetCorner(1 + idx);
        CloneCount++;
    }

    private void CheckMerges()
    {
        float thresholdSq = BodyRadius * 2f * BodyRadius * 2f;

        for (int i = CloneCount - 1; i >= 0; i--)
        {
            ref var cloneT = ref Scene.ECS.GetComponent<Transform>(CloneIds[i]);
            var clonePos = new Vector2(cloneT.Position.X, cloneT.Position.Y);

            // Check against main ghost
            ref var mainT = ref Scene.ECS.GetComponent<Transform>(MainId);
            var mainPos = new Vector2(mainT.Position.X, mainT.Position.Y);
            if ((clonePos - mainPos).LengthSquared() < thresholdSq)
            {
                DestroyClone(i);
                continue;
            }

            // Check against other clones (lower index to avoid double-destroy)
            bool merged = false;
            for (int j = i - 1; j >= 0; j--)
            {
                ref var otherT = ref Scene.ECS.GetComponent<Transform>(CloneIds[j]);
                var otherPos = new Vector2(otherT.Position.X, otherT.Position.Y);
                if ((clonePos - otherPos).LengthSquared() < thresholdSq)
                {
                    DestroyClone(i);
                    merged = true;
                    break;
                }
            }
            if (merged) continue;
        }
    }

    private void DestroyClone(int index)
    {
        Scene.ECS.DestroyEntity(CloneIds[index]);

        // Swap with last
        int last = CloneCount - 1;
        if (index < last)
        {
            CloneIds[index] = CloneIds[last];
            CloneCells[index] = CloneCells[last];
            CloneTargets[index] = CloneTargets[last];
            CloneDirs[index] = CloneDirs[last];
            CloneHues[index] = CloneHues[last];
            EyePositions[1 + index] = EyePositions[1 + last];
            PrevPositions[1 + index] = PrevPositions[1 + last];
            EyeDirs[1 + index] = EyeDirs[1 + last];
            LogicalPositions[1 + index] = LogicalPositions[1 + last];
            WanderTargetTiles[1 + index] = WanderTargetTiles[1 + last];
        }

        CloneCount--;
    }

    // ── Movement ───────────────────────────────────────────────────────

    private void MoveEntity(ref (int x, int y) Cell, ref (int x, int y) Target,
        ref (int dx, int dy) Dir, int EntityId, float dt, int EyeIndex)
    {
        float step = GhostSpeed * dt;
        if (EyeIndex == 0 && MainEaten)
            step = GhostSpeed * dt * EatenSpeedMultiplier;
        else if (EyeIndex == 0 && !MainExitedHouse && !IsReleased())
            step *= 0.5f;
        else if (IsFrightened)
            step *= FrightenedSpeedMultiplier;

        ref var transform = ref Scene.ECS.GetComponent<Transform>(EntityId);
        var pos = LogicalPositions[EyeIndex];
        var target = Maze.CellCenter(Target.x, Target.y);

        var diff = target - pos;
        float dist = diff.Length();

        if (dist <= step)
        {
            var raw = Target;
            Cell = (Maze.WrapX(raw.x), raw.y);
            pos = Maze.CellCenter(Cell.x, Cell.y);

            // Check house exit (main ghost only — clones always spawn outside)
            if (EyeIndex == 0 && !MainExitedHouse && !MainEaten)
            {
                if (!Maze.InGhostHouse(Cell.x, Cell.y) && !Maze.IsGhostDoor(Cell.x, Cell.y))
                {
                    MainExitedHouse = true;
                    if (IsFrightened)
                    {
                        ref var mat = ref Scene.ECS.GetComponent<Material>(MainId);
                        mat.Albedo = Color.Transparent;
                        mat.Emissive = FrightenedColor;
                    }
                }
                else
                {
                    if (IsReleased())
                        PickHouseExit(ref Cell, ref Target, ref Dir);
                    else
                        PickHouseWander(ref Cell, ref Target, ref Dir);
                    target = Maze.CellCenter(Target.x, Target.y);
                    diff = target - pos;
                    dist = diff.Length();
                    goto applyMove;
                }
            }

            // Eaten main ghost pathfinding
            if (EyeIndex == 0 && MainEaten)
            {
                PickEatenPath(ref Cell, ref Target, ref Dir);
                target = Maze.CellCenter(Target.x, Target.y);
                diff = target - pos;
                dist = diff.Length();
                goto applyMove;
            }

            // Respawning in house
            if (EyeIndex == 0 && MainRespawnTimer > 0f)
            {
                PickHouseWander(ref Cell, ref Target, ref Dir);
                target = Maze.CellCenter(Target.x, Target.y);
                diff = target - pos;
                dist = diff.Length();
                goto applyMove;
            }

            // Frightened: random movement
            if (IsFrightened)
                PickRandom(ref Cell, ref Target, ref Dir);
            else
                PickDirection(ref Cell, ref Target, ref Dir, EyeIndex);

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

        LogicalPositions[EyeIndex] = pos;

        // Sine wobble perpendicular to movement direction
        float wobble = MathF.Sin(ElapsedTime * 5f + EyeIndex * 2.5f) * 4f;
        transform.Position = new Vector3(
            pos.X + (Dir.dy != 0 ? wobble : 0f),
            pos.Y + (Dir.dx != 0 ? wobble : 0f), GhostZ);
        EyeDirs[EyeIndex] = Dir;

        // Collision with player (skip for eaten or respawning)
        if (Player != null && !Player.PlayerCaught && MainExitedHouse
            && !MainEaten && MainRespawnTimer <= 0f)
        {
            float dx = pos.X - Player.WorldPosition.X;
            float dy = pos.Y - Player.WorldPosition.Y;
            if (dx * dx + dy * dy < BodyRadius * BodyRadius)
            {
                if (IsFrightened)
                    EatEntity(EyeIndex);
                else
                    Player.PlayerCaught = true;
            }
        }
    }

    /// <summary>Eaten main ghost pathfinding: navigate toward ghost house door, then enter and respawn.</summary>
    private void PickEatenPath(ref (int x, int y) Cell, ref (int x, int y) Target,
        ref (int dx, int dy) Dir)
    {
        var (cx, cy) = Cell;
        var (pdx, pdy) = Dir;

        // Inside the house — start respawning with wander
        if (Maze.InGhostHouse(cx, cy))
        {
            MainEaten = false;
            MainRespawnTimer = RespawnDelay;
            MainExitedHouse = false;

            ref var material = ref Scene.ECS.GetComponent<Material>(MainId);
            material.Albedo = Color.Transparent;
            material.Emissive = LightFactory.HueToRGB(MainHue);

            PickHouseWander(ref Cell, ref Target, ref Dir);
            return;
        }

        // At ghost door — go straight down into the house
        if (Maze.IsGhostDoor(cx, cy))
        {
            Dir = (0, 1);
            Target = (cx, cy + 1);
            return;
        }

        // Navigate toward house door
        int targetX = Maze.HouseDoorLeft;
        int targetY = Maze.HouseDoorY;

        Span<(int dx, int dy)> options = stackalloc (int, int)[4];
        int count = 0;

        for (int d = 0; d < 4; d++)
        {
            int ndx = DXs[d], ndy = DYs[d];
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

        for (int j = 0; j < count; j++)
        {
            float dx2 = cx + options[j].dx - targetX;
            float dy2 = cy + options[j].dy - targetY;
            float dist = dx2 * dx2 + dy2 * dy2;
            if (dist < bestDist)
            {
                bestDist = dist;
                bestDir = options[j];
            }
        }

        Dir = bestDir;
        Target = (cx + bestDir.dx, cy + bestDir.dy);
    }

    /// <summary>Frightened mode: random direction at each intersection.</summary>
    private void PickRandom(ref (int x, int y) Cell, ref (int x, int y) Target,
        ref (int dx, int dy) Dir)
    {
        var (cx, cy) = Cell;
        var (pdx, pdy) = Dir;

        Span<(int dx, int dy)> options = stackalloc (int, int)[4];
        int count = 0;

        for (int d = 0; d < 4; d++)
        {
            if (!CanMove(cx, cy, DXs[d], DYs[d])) continue;
            if (DXs[d] == -pdx && DYs[d] == -pdy) continue;
            options[count++] = (DXs[d], DYs[d]);
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

    private void PickHouseExit(ref (int x, int y) Cell, ref (int x, int y) Target,
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
            for (int d = 0; d < 4; d++)
                if (Maze.CanMove(cx, cy, DXs[d], DYs[d]))
                {
                    Dir = (DXs[d], DYs[d]);
                    Target = (cx + DXs[d], cy + DYs[d]);
                    return;
                }
        }
    }

    private void PickHouseWander(ref (int x, int y) Cell, ref (int x, int y) Target,
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

    private void PickDirection(ref (int x, int y) Cell, ref (int x, int y) Target,
        ref (int dx, int dy) Dir, int EyeIndex)
    {
        var (cx, cy) = Cell;
        var (pdx, pdy) = Dir;

        // Determine target tile based on phase
        (int tx, int ty) targetTile;
        if (Phase == RainbowPhase.Merging)
        {
            // Seek nearest sibling to touch and merge
            targetTile = NearestSiblingCell(EyeIndex);
        }
        else if ((Phase == RainbowPhase.Duo || Phase == RainbowPhase.Trio) && Player != null)
        {
            // Chase player but each entity drifts toward its assigned corner
            var pc = Player.Cell;
            var cc = CornerTargets[EyeIndex];
            targetTile = ((pc.x * 3 + cc.x * 2) / 5, (pc.y * 3 + cc.y * 2) / 5);
        }
        else
        {
            // Solo or no player: wander randomly
            targetTile = WanderTargetTiles[EyeIndex];
            float manhattan = MathF.Abs(cx - targetTile.tx) + MathF.Abs(cy - targetTile.ty);
            if (manhattan <= 2 || Maze.IsWall(targetTile.tx, targetTile.ty))
                WanderTargetTiles[EyeIndex] = targetTile = PickRandomWalkable();
        }

        // Euclidean distance minimization with Pac-Man priority (Up > Left > Down > Right)
        Span<(int dx, int dy)> options = stackalloc (int, int)[4];
        int count = 0;

        for (int d = 0; d < 4; d++)
        {
            if (!CanMove(cx, cy, DXs[d], DYs[d])) continue;
            if (DXs[d] == -pdx && DYs[d] == -pdy) continue; // no reversal
            options[count++] = (DXs[d], DYs[d]);
        }

        if (count == 0)
        {
            // Dead end: reverse
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

        // Intersection: pick direction minimizing Euclidean distance to target tile
        float bestDist = float.MaxValue;
        (int dx, int dy) bestDir = options[0];

        for (int j = 0; j < count; j++)
        {
            float dx2 = cx + options[j].dx - targetTile.tx;
            float dy2 = cy + options[j].dy - targetTile.ty;
            float d = dx2 * dx2 + dy2 * dy2;
            if (d < bestDist)
            {
                bestDist = d;
                bestDir = options[j];
            }
        }

        Dir = bestDir;
        Target = (cx + bestDir.dx, cy + bestDir.dy);
    }

    private (int x, int y) NearestSiblingCell(int eyeIndex)
    {
        var self = eyeIndex == 0 ? MainCell : CloneCells[eyeIndex - 1];
        float bestDist = float.MaxValue;
        (int x, int y) best = self;

        int total = 1 + CloneCount;
        for (int i = 0; i < total; i++)
        {
            if (i == eyeIndex) continue;
            var other = i == 0 ? MainCell : CloneCells[i - 1];
            float dx = self.x - other.x;
            float dy = self.y - other.y;
            float d = dx * dx + dy * dy;
            if (d < bestDist)
            {
                bestDist = d;
                best = other;
            }
        }

        return best;
    }

    private bool CanMove(int cx, int cy, int dx, int dy)
    {
        if (!Maze.CanMove(cx, cy, dx, dy)) return false;
        if (MainEaten) return true; // eaten ghost can pass through door
        int nx = Maze.WrapX(cx + dx), ny = cy + dy;
        if (Maze.InGhostHouse(nx, ny)) return false;
        if (Maze.IsGhostDoor(nx, ny)) return false;
        return true;
    }

    private bool IsReleased()
    {
        if (MainRespawnTimer > 0f) return false;
        if (ReleaseTime > 0 && ElapsedTime >= ReleaseTime) return true;
        if (ReleaseAtCoinPercent > 0 && Player != null && Player.CoinsTotal > 0)
        {
            float coinPercent = (float)Player.CoinsCollected / Player.CoinsTotal;
            if (coinPercent >= ReleaseAtCoinPercent) return true;
        }
        // Both zero = immediate release
        if (ReleaseTime <= 0 && ReleaseAtCoinPercent <= 0) return true;
        return false;
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private void ShiftEyeHistory()
    {
        // Main ghost
        EyePositions[0] = PrevPositions[0];
        ref var mt = ref Scene.ECS.GetComponent<Transform>(MainId);
        PrevPositions[0] = new Vector2(mt.Position.X, mt.Position.Y);

        // Clones
        for (int i = 0; i < CloneCount; i++)
        {
            EyePositions[1 + i] = PrevPositions[1 + i];
            ref var ct = ref Scene.ECS.GetComponent<Transform>(CloneIds[i]);
            PrevPositions[1 + i] = new Vector2(ct.Position.X, ct.Position.Y);
        }
    }

    private void UpdateMainColor()
    {
        var color = LightFactory.HueToRGB(MainHue);
        ref var mat = ref Scene.ECS.GetComponent<Material>(MainId);
        mat.Albedo = Color.Transparent;
        mat.Emissive = color;
    }

    private void UpdateCloneColor(int index)
    {
        var color = LightFactory.HueToRGB(MainHue);
        ref var mat = ref Scene.ECS.GetComponent<Material>(CloneIds[index]);
        mat.Albedo = Color.Transparent;
        mat.Emissive = color;
    }

    private (int x, int y) GetCorner(int eyeIndex) => (eyeIndex % 4) switch
    {
        0 => (2, 1),
        1 => (Maze.Cols - 3, 1),
        2 => (0, Maze.Rows - 2),
        _ => (Maze.Cols - 1, Maze.Rows - 2),
    };

    private (int x, int y) PickRandomWalkable()
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            int x = Rng.Next(Maze.Cols);
            int y = Rng.Next(Maze.Rows);
            if (!Maze.IsWall(x, y) && !Maze.InGhostHouse(x, y) && !Maze.IsGhostDoor(x, y))
                return (x, y);
        }
        return (Maze.Cols / 2, 0);
    }
}
