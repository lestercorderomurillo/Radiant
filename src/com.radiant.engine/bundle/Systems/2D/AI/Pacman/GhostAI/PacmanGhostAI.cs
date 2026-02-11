using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace com.radiant.engine.bundle;

public enum PacmanGhostType : byte { Blinky, Pinky, Inky, Clyde, Dinky, Shadow, Rainbow }
public enum PacmanGhostMode : byte { Scatter, Chase, Frightened }

[Pausable]
public class PacmanGhostAI : core.System
{
    public float GhostSpeed { get; set; } = 200f;
    public float GhostZ { get; set; } = 65530f;
    public Texture2D EyesTexture { get; set; }
    public Texture2D BodyTexture { get; set; }
    public float BodyRadius { get; set; } = 30f;
    public float DefaultReleaseInterval { get; set; } = 0.5f;
    public PacmanPlayer Player { get; set; }

    private const float RespawnDelay = 6f;
    private const float EatenSpeedMultiplier = 2.5f;
    private const float FrightenedSpeedMultiplier = 0.4f;
    private const float FrightenedShrink = 0.85f;
    private const float FrightenedExtraDuration = 5f;
    private const float FrightenedBlinkThreshold = 1.5f;
    private const float FrightenedBlinkRate = 8f;
    private static readonly Color FrightenedColor = new Color(30, 30, 200);
    private static readonly Color FrightenedBlinkColor = new Color(100, 150, 255);

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
    private Vector2[] EyePositions;      // 2-frame delayed positions (matches Geometry's rendered body)
    private Vector2[] PrevPositions;     // 1-frame delayed (intermediate)
    private Vector2[] Positions;         // Un-wobbled logical positions
    private float ElapsedTime;
    private float IdleTime;
    private Random Rng = new();

    private PacmanGhostMode CurrentMode = PacmanGhostMode.Scatter;
    private float ModeTimer;
    private int ModePhase;

    // Per-ghost frightened state (power pellet only affects ghosts alive at that moment)
    private bool[] Frightened;
    private float[] FrightenedTimers;

    // Classic Pac-Man Level 1 mode timing
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

    // Pac-Man direction priority: Up, Left, Down, Right
    private static readonly int[] DXs = [0, -1, 0, 1];
    private static readonly int[] DYs = [-1, 0, 1, 0];

    public static Color PersonalityColor(PacmanGhostType type) => type switch
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

    // Dinky picks a random corner each time — never knows where it's going
    private (int x, int y) RandomScatterCorner() => Rng.Next(4) switch
    {
        0 => (Maze.Cols - 3, 0),
        1 => (2, 0),
        2 => (Maze.Cols - 1, Maze.Rows - 1),
        _ => (0, Maze.Rows - 1)
    };

    private (int x, int y) ScatterTarget(PacmanGhostType type) => type switch
    {
        PacmanGhostType.Blinky => (Maze.Cols - 3, 0),
        PacmanGhostType.Pinky => (2, 0),
        PacmanGhostType.Inky => (Maze.Cols - 1, Maze.Rows - 1),
        PacmanGhostType.Clyde => (0, Maze.Rows - 1),
        PacmanGhostType.Dinky => RandomScatterCorner(),
        PacmanGhostType.Shadow => (Maze.Cols / 2, Maze.Rows / 2),
        _ => (Maze.Cols / 2, 0)
    };

    public override void Initialize()
    {
        Maze = Scene.ECS.GetSystem<PacmanMazeBuilder>();
        Geometry = Scene.ECS.GetSystem<Geometry>();
    }

    public void Track(int[] entityIds, (int x, int y)[] startCells, GhostEntry[] entries)
    {
        int count = entityIds.Length;
        GhostIds = entityIds;
        GhostEntries = entries;
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

        for (int i = 0; i < count; i++)
        {
            GhostCells[i] = startCells[i];
            GhostTargets[i] = startCells[i];
            GhostDirs[i] = (1, 0);
            ChaseTargets[i] = PickRandomWalkable();
            ExitedHouse[i] = false;
            Eaten[i] = false;
            RespawnTimer[i] = 0f;

            // GI emission via Geometry texture draw, eyes overlay via LateRender
            GhostColors[i] = PersonalityColor(entries[i].Type);
            ref var mat = ref Scene.ECS.GetComponent<Material>(GhostIds[i]);
            if (entries[i].Type == PacmanGhostType.Shadow)
            {
                mat.Albedo = GhostColors[i];
                mat.Emissive = Color.Black;
            }
            else
            {
                mat.Albedo = Color.Transparent;
                mat.Emissive = GhostColors[i];
            }

            // Initialize position history for eye sync
            ref var transform = ref Scene.ECS.GetComponent<Transform>(GhostIds[i]);
            var pos = new Vector2(transform.Position.X, transform.Position.Y);
            EyePositions[i] = pos;
            PrevPositions[i] = pos;
            Positions[i] = pos;
        }

        ElapsedTime = 0f;
        ModeTimer = 0f;
        ModePhase = 0;
        CurrentMode = PacmanGhostMode.Scatter;
    }

    public void Clear()
    {
        if (GhostIds == null) return;
        for (int i = 0; i < GhostIds.Length; i++)
            Scene.ECS.DestroyEntity(GhostIds[i]);
        GhostIds = null;
    }

    /// <summary>Trigger frightened mode (only ghosts alive and out of house are affected).</summary>
    public void SetFrightened(float duration)
    {
        if (GhostIds == null) return;

        float timer = duration + FrightenedExtraDuration;
        for (int i = 0; i < GhostIds.Length; i++)
        {
            if (Eaten[i] || RespawnTimer[i] > 0f || !ExitedHouse[i]) continue;
            Frightened[i] = true;
            FrightenedTimers[i] = timer;

            ref var material = ref Scene.ECS.GetComponent<Material>(GhostIds[i]);
            material.Albedo = Color.Transparent;
            material.Emissive = FrightenedColor;
            ref var circle = ref Scene.ECS.GetComponent<Circle2D>(GhostIds[i]);
            circle.Radius = BodyRadius * FrightenedShrink;

            // Reverse direction
            var (pdx, pdy) = GhostDirs[i];
            if (pdx == 0 && pdy == 0) continue;
            var (cx, cy) = GhostCells[i];
            GhostDirs[i] = (-pdx, -pdy);
            if (CanGhostMove(i, cx, cy, -pdx, -pdy))
                GhostTargets[i] = (cx - pdx, cy - pdy);
        }
    }

    public override void Update()
    {
        if (GhostIds == null) return;

        float dt = (float)GameTime.ElapsedGameTime.TotalSeconds;
        IdleTime += dt;

        // Shift position history: Geometry renders ReadBuffer (2 frames behind current)
        // so eyes must use 2-frame-delayed positions to match the body in EmissiveTexture
        for (int i = 0; i < GhostIds.Length; i++)
        {
            EyePositions[i] = PrevPositions[i];
            ref var t = ref Scene.ECS.GetComponent<Transform>(GhostIds[i]);
            PrevPositions[i] = new Vector2(t.Position.X, t.Position.Y);
        }

        if (Player == null || !Player.HasMoved)
        {
            // Idle floating wobble before match starts
            for (int i = 0; i < GhostIds.Length; i++)
            {
                ref var transform = ref Scene.ECS.GetComponent<Transform>(GhostIds[i]);
                float wobble = MathF.Sin(IdleTime * 3f + i * 1.7f) * 3f;
                var pos = Positions[i];
                transform.Position = new Vector3(pos.X, pos.Y + wobble, GhostZ);
            }
            return;
        }

        ElapsedTime += dt;
        UpdateMode(dt);

        float step = GhostSpeed * dt;
        float frightenedStep = step * FrightenedSpeedMultiplier;

        for (int i = 0; i < GhostIds.Length; i++)
        {
            // Respawn timer countdown (ghost waiting in house)
            if (RespawnTimer[i] > 0f)
            {
                RespawnTimer[i] -= dt;
                if (RespawnTimer[i] <= 0f)
                    RespawnGhost(i);
            }

            // Speed calculation
            float currentStep;
            if (Eaten[i])
                currentStep = step * EatenSpeedMultiplier;
            else if (!ExitedHouse[i] && !IsReleased(i))
                currentStep = step * 0.5f;
            else if (Frightened[i])
                currentStep = frightenedStep;
            else
                currentStep = step;

            // Shadow: 1.25x base, ramps to 1.5x when approaching, normal speed when very close
            if (GhostEntries[i].Type == PacmanGhostType.Shadow && ExitedHouse[i]
                && !Eaten[i] && !Frightened[i])
            {
                var (scx, scy) = GhostCells[i];
                var (stx, sty) = Player != null ? Player.Cell : ChaseTargets[i];
                float sd = MathF.Sqrt((scx - stx) * (scx - stx) + (scy - sty) * (scy - sty));
                float mult = sd < 4f ? 1.0f : sd < 12f ? 1.5f : 1.25f;
                currentStep = step * mult;
            }

            ref var transform = ref Scene.ECS.GetComponent<Transform>(GhostIds[i]);
            var pos = Positions[i];
            var target = Maze.CellCenter(GhostTargets[i].x, GhostTargets[i].y);

            var diff = target - pos;
            float dist = diff.Length();

            if (dist <= currentStep)
            {
                // Wrap coordinates (tunnel teleport)
                var raw = GhostTargets[i];
                GhostCells[i] = (Maze.WrapX(raw.x), raw.y);
                pos = Maze.CellCenter(GhostCells[i].x, GhostCells[i].y);
                PickDirection(i);

                target = Maze.CellCenter(GhostTargets[i].x, GhostTargets[i].y);
                diff = target - pos;
                dist = diff.Length();
            }

            if (dist > 0.01f)
            {
                var move = diff / dist * MathF.Min(currentStep, dist);
                pos += move;
            }

            Positions[i] = pos;

            // Sine wobble perpendicular to movement direction
            var (dirX, dirY) = GhostDirs[i];
            float wobble = MathF.Sin(ElapsedTime * 5f + i * 2.5f) * 4f;
            transform.Position = new Vector3(
                pos.X + (dirY != 0 ? wobble : 0f),
                pos.Y + (dirX != 0 ? wobble : 0f), GhostZ);

            // Collision with player (skip for eaten or respawning ghosts)
            if (ExitedHouse[i] && !Eaten[i] && RespawnTimer[i] <= 0f
                && Player != null && !Player.PlayerCaught)
            {
                float dx = pos.X - Player.WorldPosition.X;
                float dy = pos.Y - Player.WorldPosition.Y;
                if (dx * dx + dy * dy < BodyRadius * BodyRadius)
                {
                    if (Frightened[i])
                        EatGhost(i);
                    else
                        Player.PlayerCaught = true;
                }
            }
        }
    }

    public override void LateRender()
    {
        if (GhostIds == null || Geometry.IsDebugHidingGameplay) return;

        float sx = Renderer.ScreenWidth / Renderer.VirtualSize.X;
        float sy = Renderer.ScreenHeight / Renderer.VirtualSize.Y;

        Renderer.Reset()
            .Configure(BlendState.AlphaBlend)
            .SetTarget(null);

        for (int i = 0; i < GhostIds.Length; i++)
        {
            float radius = Frightened[i] ? BodyRadius * FrightenedShrink : BodyRadius;
            float eyeR = radius * 0.667f;
            float eyeD = eyeR * 2f;
            float eyeOff = radius * 0.133f;
            var (dx, dy) = GhostDirs[i];
            float cx = EyePositions[i].X + dx * eyeOff;
            float cy = EyePositions[i].Y + dy * eyeOff;

            if (EyesTexture != null)
            {
                Color eyeColor;
                if (Eaten[i])
                    eyeColor = Color.White;
                else if (Frightened[i])
                    eyeColor = Color.White;
                else
                    eyeColor = GhostEntries[i].Type == PacmanGhostType.Shadow ? Color.White : Color.Black;

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

    private void EatGhost(int i)
    {
        Eaten[i] = true;
        Frightened[i] = false;
        FrightenedTimers[i] = 0f;
        ref var material = ref Scene.ECS.GetComponent<Material>(GhostIds[i]);
        material.Albedo = Color.Transparent;
        material.Emissive = Color.Transparent;
        ref var circle = ref Scene.ECS.GetComponent<Circle2D>(GhostIds[i]);
        circle.Radius = BodyRadius;
    }

    private void RespawnGhost(int i)
    {
        RespawnTimer[i] = 0f;
        ExitedHouse[i] = false;

        // Always restore normal color — respawned ghosts are never frightened
        ref var material = ref Scene.ECS.GetComponent<Material>(GhostIds[i]);
        if (GhostEntries[i].Type == PacmanGhostType.Shadow)
        {
            material.Albedo = GhostColors[i];
            material.Emissive = Color.Black;
        }
        else
        {
            material.Albedo = Color.Transparent;
            material.Emissive = GhostColors[i];
        }
    }

    private void RestoreGhostColor(int i)
    {
        if (Eaten[i] || RespawnTimer[i] > 0f) return;
        ref var material = ref Scene.ECS.GetComponent<Material>(GhostIds[i]);
        if (GhostEntries[i].Type == PacmanGhostType.Shadow)
        {
            material.Albedo = GhostColors[i];
            material.Emissive = Color.Black;
        }
        else
        {
            material.Albedo = Color.Transparent;
            material.Emissive = GhostColors[i];
        }
    }

    private void UpdateMode(float dt)
    {
        // Per-ghost frightened timers
        for (int i = 0; i < GhostIds.Length; i++)
        {
            if (!Frightened[i]) continue;

            FrightenedTimers[i] -= dt;
            if (FrightenedTimers[i] <= 0f)
            {
                Frightened[i] = false;
                FrightenedTimers[i] = 0f;
                RestoreGhostColor(i);
                ref var circle = ref Scene.ECS.GetComponent<Circle2D>(GhostIds[i]);
                circle.Radius = BodyRadius;
            }
            else if (FrightenedTimers[i] <= FrightenedBlinkThreshold)
            {
                bool blinkOn = MathF.Sin(FrightenedTimers[i] * FrightenedBlinkRate * MathF.PI) > 0f;
                ref var material = ref Scene.ECS.GetComponent<Material>(GhostIds[i]);
                material.Emissive = blinkOn ? FrightenedBlinkColor : FrightenedColor;
            }
            else
            {
                // Hue cycling around blue (azul → celeste → morado)
                float hueShift = MathF.Sin(IdleTime * 2.5f + i * 1.3f);
                byte r = (byte)(30 + 42 * MathF.Max(0, -hueShift));
                byte g = (byte)(30 + 55 * MathF.Max(0, hueShift));
                byte b = (byte)(200 + 42 * MathF.Abs(hueShift));
                ref var material = ref Scene.ECS.GetComponent<Material>(GhostIds[i]);
                material.Emissive = new Color(r, g, b);
            }
        }

        // Scatter/chase cycle continues independently
        if (ModePhase >= ModeCycle.Length)
        {
            CurrentMode = PacmanGhostMode.Chase;
            return;
        }

        ModeTimer += dt;
        if (ModeTimer >= ModeCycle[ModePhase].duration)
        {
            ModeTimer -= ModeCycle[ModePhase].duration;
            ModePhase++;

            CurrentMode = ModePhase < ModeCycle.Length
                ? ModeCycle[ModePhase].mode
                : PacmanGhostMode.Chase;
        }
    }

    private bool CanGhostMove(int gi, int cx, int cy, int dx, int dy)
    {
        if (!Maze.CanMove(cx, cy, dx, dy)) return false;
        if (ExitedHouse[gi])
        {
            int nx = Maze.WrapX(cx + dx), ny = cy + dy;
            // Eaten ghosts can pass through ghost door (returning to house)
            if (Maze.IsGhostDoor(nx, ny) && !Eaten[gi]) return false;
            // Shadow: won't use teleport tunnels
            if (GhostEntries[gi].Type == PacmanGhostType.Shadow && !Eaten[gi] && nx != cx + dx) return false;
        }
        return true;
    }

    private (int x, int y) GetTargetTile(int i)
    {
        if (CurrentMode == PacmanGhostMode.Scatter)
            return ScatterTarget(GhostEntries[i].Type);

        var (cx, cy) = GhostCells[i];

        // If player exists, all ghosts chase the player
        if (Player != null)
        {
            var (px, py) = Player.Cell;

            // Clyde: retreats to scatter corner when close to player (< 8 tiles)
            if (GhostEntries[i].Type == PacmanGhostType.Clyde)
            {
                float d = MathF.Sqrt((cx - px) * (cx - px) + (cy - py) * (cy - py));
                if (d < 8f)
                    return ScatterTarget(PacmanGhostType.Clyde);
            }

            // Dinky: charges at player but chickens out within 10 tiles — flees to a random corner
            if (GhostEntries[i].Type == PacmanGhostType.Dinky)
            {
                float d = MathF.Sqrt((cx - px) * (cx - px) + (cy - py) * (cy - py));
                if (d < 10f)
                    return RandomScatterCorner();
            }

            return (px, py);
        }

        // Fallback: random walkable targets when no player
        var (tx, ty) = ChaseTargets[i];

        if (GhostEntries[i].Type == PacmanGhostType.Clyde)
        {
            float d = MathF.Sqrt((cx - tx) * (cx - tx) + (cy - ty) * (cy - ty));
            if (d < 8f)
                return ScatterTarget(PacmanGhostType.Clyde);
        }

        if (GhostEntries[i].Type == PacmanGhostType.Dinky)
        {
            float d = MathF.Sqrt((cx - tx) * (cx - tx) + (cy - ty) * (cy - ty));
            if (d < 10f)
                return RandomScatterCorner();
        }

        if (GhostEntries[i].Type == PacmanGhostType.Shadow)
        {
            float manhattan = MathF.Abs(cx - tx) + MathF.Abs(cy - ty);
            if (manhattan <= 1 || Maze.IsWall(tx, ty))
                ChaseTargets[i] = PickRandomWalkable();
            return ChaseTargets[i];
        }

        float manhattan2 = MathF.Abs(cx - tx) + MathF.Abs(cy - ty);
        if (manhattan2 <= 2 || Maze.IsWall(tx, ty))
            ChaseTargets[i] = PickRandomWalkable();

        return ChaseTargets[i];
    }

    private void PickDirection(int i)
    {
        var (cx, cy) = GhostCells[i];
        var (pdx, pdy) = GhostDirs[i];

        // Ghost house: wander horizontally until released, then exit
        if (!ExitedHouse[i])
        {
            if (!Maze.InGhostHouse(cx, cy) && !Maze.IsGhostDoor(cx, cy))
            {
                ExitedHouse[i] = true;
            }
            else
            {
                if (IsReleased(i))
                    PickHouseExit(i);
                else
                    PickHouseWander(i);
                return;
            }
        }

        // Eaten ghost: navigate toward ghost house
        if (Eaten[i])
        {
            PickEatenPath(i);
            return;
        }

        // Frightened mode: random turns at intersections
        if (Frightened[i])
        {
            PickRandom(i);
            return;
        }

        // Target-tile pathfinding (core Pac-Man ghost AI)
        var targetTile = GetTargetTile(i);

        Span<(int dx, int dy)> options = stackalloc (int, int)[4];
        int count = 0;

        for (int d = 0; d < 4; d++)
        {
            if (!CanGhostMove(i, cx, cy, DXs[d], DYs[d])) continue;
            if (DXs[d] == -pdx && DYs[d] == -pdy) continue;
            // No-up restriction at specific tiles (scatter/chase only)
            if (DYs[d] == -1 && Maze.IsNoUpTile(cx, cy)) continue;
            options[count++] = (DXs[d], DYs[d]);
        }

        if (count == 0)
        {
            GhostDirs[i] = (-pdx, -pdy);
            GhostTargets[i] = (cx - pdx, cy - pdy);
            return;
        }

        if (count == 1)
        {
            var only = options[0];
            GhostDirs[i] = only;
            GhostTargets[i] = (cx + only.dx, cy + only.dy);
            return;
        }

        // Intersection: pick direction minimizing Euclidean distance to target tile
        // Ties broken by priority order (Up > Left > Down > Right) since we iterate in that order
        float bestDist = float.MaxValue;
        (int dx, int dy) bestDir = options[0];

        for (int j = 0; j < count; j++)
        {
            float dx2 = cx + options[j].dx - targetTile.x;
            float dy2 = cy + options[j].dy - targetTile.y;
            float dist = dx2 * dx2 + dy2 * dy2;

            if (dist < bestDist)
            {
                bestDist = dist;
                bestDir = options[j];
            }
        }

        GhostDirs[i] = bestDir;
        GhostTargets[i] = (cx + bestDir.dx, cy + bestDir.dy);
    }

    /// <summary>Eaten ghost pathfinding: navigate toward ghost house door, then enter and start respawn.</summary>
    private void PickEatenPath(int i)
    {
        var (cx, cy) = GhostCells[i];
        var (pdx, pdy) = GhostDirs[i];

        // Inside the house — start respawning with wander
        if (Maze.InGhostHouse(cx, cy))
        {
            Eaten[i] = false;
            RespawnTimer[i] = RespawnDelay;
            ExitedHouse[i] = false;

            ref var material = ref Scene.ECS.GetComponent<Material>(GhostIds[i]);
            material.Albedo = Color.Transparent;
            material.Emissive = GhostColors[i];

            PickHouseWander(i);
            return;
        }

        // At ghost door — go straight down into the house
        if (Maze.IsGhostDoor(cx, cy))
        {
            GhostDirs[i] = (0, 1);
            GhostTargets[i] = (cx, cy + 1);
            return;
        }

        // Navigate toward house door using target-tile pathfinding
        int targetX = Maze.HouseDoorLeft;
        int targetY = Maze.HouseDoorY;

        Span<(int dx, int dy)> options = stackalloc (int, int)[4];
        int count = 0;

        for (int d = 0; d < 4; d++)
        {
            if (!CanGhostMove(i, cx, cy, DXs[d], DYs[d])) continue;
            if (DXs[d] == -pdx && DYs[d] == -pdy) continue;
            options[count++] = (DXs[d], DYs[d]);
        }

        if (count == 0)
        {
            GhostDirs[i] = (-pdx, -pdy);
            GhostTargets[i] = (cx - pdx, cy - pdy);
            return;
        }

        if (count == 1)
        {
            var only = options[0];
            GhostDirs[i] = only;
            GhostTargets[i] = (cx + only.dx, cy + only.dy);
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

        GhostDirs[i] = bestDir;
        GhostTargets[i] = (cx + bestDir.dx, cy + bestDir.dy);
    }

    /// <summary>Ghost house exit: align to door column, then go straight up.</summary>
    private void PickHouseExit(int i)
    {
        var (cx, cy) = GhostCells[i];
        int doorL = Maze.HouseDoorLeft;
        int doorR = Maze.HouseDoorRight;

        if (cx < doorL && Maze.CanMove(cx, cy, 1, 0))
        {
            GhostDirs[i] = (1, 0);
            GhostTargets[i] = (cx + 1, cy);
        }
        else if (cx > doorR && Maze.CanMove(cx, cy, -1, 0))
        {
            GhostDirs[i] = (-1, 0);
            GhostTargets[i] = (cx - 1, cy);
        }
        else if (Maze.CanMove(cx, cy, 0, -1))
        {
            GhostDirs[i] = (0, -1);
            GhostTargets[i] = (cx, cy - 1);
        }
        else
        {
            // Fallback: try any direction
            for (int d = 0; d < 4; d++)
                if (Maze.CanMove(cx, cy, DXs[d], DYs[d]))
                {
                    GhostDirs[i] = (DXs[d], DYs[d]);
                    GhostTargets[i] = (cx + DXs[d], cy + DYs[d]);
                    return;
                }
        }
    }

    /// <summary>Ghost house wander: bounce horizontally on current row until released.</summary>
    private void PickHouseWander(int i)
    {
        var (cx, cy) = GhostCells[i];
        var (pdx, _) = GhostDirs[i];

        if (pdx == 0)
            pdx = Rng.Next(2) == 0 ? -1 : 1;

        if (Maze.CanMove(cx, cy, pdx, 0) && Maze.InGhostHouse(cx + pdx, cy))
        {
            GhostDirs[i] = (pdx, 0);
            GhostTargets[i] = (cx + pdx, cy);
        }
        else if (Maze.CanMove(cx, cy, -pdx, 0) && Maze.InGhostHouse(cx - pdx, cy))
        {
            GhostDirs[i] = (-pdx, 0);
            GhostTargets[i] = (cx - pdx, cy);
        }
        else
        {
            GhostDirs[i] = (0, 0);
            GhostTargets[i] = (cx, cy);
        }
    }

    private void PickRandom(int i)
    {
        var (cx, cy) = GhostCells[i];
        var (pdx, pdy) = GhostDirs[i];

        Span<(int dx, int dy)> options = stackalloc (int, int)[4];
        int count = 0;

        for (int d = 0; d < 4; d++)
            if (CanGhostMove(i, cx, cy, DXs[d], DYs[d]) && !(DXs[d] == -pdx && DYs[d] == -pdy))
                options[count++] = (DXs[d], DYs[d]);

        if (count == 0)
        {
            GhostDirs[i] = (-pdx, -pdy);
            GhostTargets[i] = (cx - pdx, cy - pdy);
            return;
        }

        var pick = options[Rng.Next(count)];
        GhostDirs[i] = pick;
        GhostTargets[i] = (cx + pick.dx, cy + pick.dy);
    }

    private bool IsReleased(int i)
    {
        if (RespawnTimer[i] > 0f) return false;
        ref var entry = ref GhostEntries[i];
        if (entry.ReleaseAfter > 0 && ElapsedTime >= entry.ReleaseAfter) return true;
        if (entry.ReleaseAtCoinPercent > 0 && Player != null && Player.CoinsTotal > 0)
        {
            float coinPercent = (float)Player.CoinsCollected / Player.CoinsTotal;
            if (coinPercent >= entry.ReleaseAtCoinPercent) return true;
        }
        // Both zero = immediate release
        if (entry.ReleaseAfter <= 0 && entry.ReleaseAtCoinPercent <= 0) return true;
        return false;
    }

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
