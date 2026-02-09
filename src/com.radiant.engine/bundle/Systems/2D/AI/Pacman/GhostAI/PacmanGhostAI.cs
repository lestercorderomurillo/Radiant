using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace com.radiant.engine.bundle;

public enum PacmanGhostType : byte { Blinky, Pinky, Inky, Clyde, Dinky, Shadow, Rainbow }
public enum PacmanGhostMode : byte { Scatter, Chase, Frightened }

public class PacmanGhostAI : core.System
{
    public float GhostSpeed { get; set; } = 200f;
    public float GhostZ { get; set; } = 65530f;
    public Texture2D EyesTexture { get; set; }
    public Texture2D BodyTexture { get; set; }
    public float BodyRadius { get; set; } = 30f;
    public float DefaultReleaseInterval { get; set; } = 0.5f;
    public PacmanPlayer Player { get; set; }

    private int[] GhostIds;
    private PacmanMazeBuilder Maze;
    private Geometry Geometry;
    private (int x, int y)[] GhostCells;
    private (int x, int y)[] GhostTargets;
    private (int dx, int dy)[] GhostDirs;
    private PacmanGhostType[] PacmanGhostTypes;
    private (int x, int y)[] ChaseTargets;
    private bool[] ExitedHouse;
    private float[] ReleaseTimes;
    private Color[] GhostColors;
    private Vector2[] EyePositions;      // 2-frame delayed positions (matches Geometry's rendered body)
    private Vector2[] PrevPositions;     // 1-frame delayed (intermediate)
    private float ElapsedTime;
    private Random Rng = new();

    private PacmanGhostMode CurrentMode = PacmanGhostMode.Scatter;
    private PacmanGhostMode PreFrightenedMode;
    private float ModeTimer;
    private int ModePhase;
    private float FrightenedTimer;

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

    public void Track(int[] entityIds, (int x, int y)[] startCells, PacmanGhostType[] types = null, float[] releaseTimes = null)
    {
        int count = entityIds.Length;
        GhostIds = entityIds;
        GhostCells = new (int, int)[count];
        GhostTargets = new (int, int)[count];
        GhostDirs = new (int, int)[count];
        PacmanGhostTypes = new PacmanGhostType[count];
        ChaseTargets = new (int, int)[count];
        ExitedHouse = new bool[count];
        ReleaseTimes = new float[count];
        GhostColors = new Color[count];
        EyePositions = new Vector2[count];
        PrevPositions = new Vector2[count];

        for (int i = 0; i < count; i++)
        {
            GhostCells[i] = startCells[i];
            GhostTargets[i] = startCells[i];
            GhostDirs[i] = (1, 0);
            PacmanGhostTypes[i] = types != null ? types[i] : (PacmanGhostType)(i % 6);
            ChaseTargets[i] = PickRandomWalkable();
            ExitedHouse[i] = false;
            ReleaseTimes[i] = releaseTimes != null ? releaseTimes[i] : i * DefaultReleaseInterval;

            // GI emission via Geometry texture draw, eyes overlay via LateRender
            GhostColors[i] = PersonalityColor(PacmanGhostTypes[i]);
            ref var mat = ref Scene.ECS.GetComponent<Material>(GhostIds[i]);
            if (PacmanGhostTypes[i] == PacmanGhostType.Shadow)
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
            ref var t = ref Scene.ECS.GetComponent<Transform>(GhostIds[i]);
            var pos = new Vector2(t.Position.X, t.Position.Y);
            EyePositions[i] = pos;
            PrevPositions[i] = pos;
        }

        ElapsedTime = 0f;
        ModeTimer = 0f;
        ModePhase = 0;
        CurrentMode = PacmanGhostMode.Scatter;
        FrightenedTimer = 0f;
    }

    public void Clear()
    {
        if (GhostIds == null) return;
        for (int i = 0; i < GhostIds.Length; i++)
            Scene.ECS.DestroyEntity(GhostIds[i]);
        GhostIds = null;
    }

    /// <summary>Trigger frightened mode (all ghosts reverse and move randomly at half speed).</summary>
    public void SetFrightened(float duration)
    {
        if (CurrentMode != PacmanGhostMode.Frightened)
            PreFrightenedMode = CurrentMode;
        CurrentMode = PacmanGhostMode.Frightened;
        FrightenedTimer = duration;
        ReverseAll();
    }

    public override void Update()
    {
        if (GhostIds == null) return;

        // Shift position history: Geometry renders ReadBuffer (2 frames behind current)
        // so eyes must use 2-frame-delayed positions to match the body in EmissiveTexture
        for (int i = 0; i < GhostIds.Length; i++)
        {
            EyePositions[i] = PrevPositions[i];
            ref var t = ref Scene.ECS.GetComponent<Transform>(GhostIds[i]);
            PrevPositions[i] = new Vector2(t.Position.X, t.Position.Y);
        }

        float dt = (float)GameTime.ElapsedGameTime.TotalSeconds;
        ElapsedTime += dt;
        UpdateMode(dt);

        float step = GhostSpeed * dt;
        float frightenedStep = step * 0.5f;

        for (int i = 0; i < GhostIds.Length; i++)
        {
            float currentStep = !ExitedHouse[i] && ElapsedTime < ReleaseTimes[i]
                ? step * 0.5f
                : (CurrentMode == PacmanGhostMode.Frightened && ExitedHouse[i])
                    ? frightenedStep : step;

            // Shadow: 1.25x base, ramps to 1.5x when approaching, normal speed when very close
            if (PacmanGhostTypes[i] == PacmanGhostType.Shadow && ExitedHouse[i] && CurrentMode != PacmanGhostMode.Frightened)
            {
                var (scx, scy) = GhostCells[i];
                var (stx, sty) = Player != null ? Player.Cell : ChaseTargets[i];
                float sd = MathF.Sqrt((scx - stx) * (scx - stx) + (scy - sty) * (scy - sty));
                float mult = sd < 4f ? 1.0f : sd < 12f ? 1.5f : 1.25f;
                currentStep = step * mult;
            }

            ref var transform = ref Scene.ECS.GetComponent<Transform>(GhostIds[i]);
            var pos = new Vector2(transform.Position.X, transform.Position.Y);
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
                var move = (diff / dist) * MathF.Min(currentStep, dist);
                pos += move;
            }

            transform.Position = new Vector3(pos, GhostZ);
        }
    }

    public override void LateRender()
    {
        if (GhostIds == null || Geometry.IsDebugging) return;

        float sx = Renderer.ScreenWidth / Renderer.VirtualSize.X;
        float sy = Renderer.ScreenHeight / Renderer.VirtualSize.Y;
        float eyeR = 20f;
        float eyeD = eyeR * 2f;

        Renderer.Reset()
            .Configure(BlendState.AlphaBlend)
            .SetTarget(null);

        for (int i = 0; i < GhostIds.Length; i++)
        {
            var (dx, dy) = GhostDirs[i];
            float cx = EyePositions[i].X + dx * 4f;
            float cy = EyePositions[i].Y + dy * 4f;

            if (EyesTexture != null)
            {
                var eyeColor = PacmanGhostTypes[i] == PacmanGhostType.Shadow ? Color.White : Color.Black;
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

    private void UpdateMode(float dt)
    {
        if (CurrentMode == PacmanGhostMode.Frightened)
        {
            FrightenedTimer -= dt;
            if (FrightenedTimer <= 0f)
            {
                CurrentMode = PreFrightenedMode;
                ReverseAll();
            }
            return;
        }

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

            var prevMode = CurrentMode;
            CurrentMode = ModePhase < ModeCycle.Length
                ? ModeCycle[ModePhase].mode
                : PacmanGhostMode.Chase;

            // Mode change takes effect at next intersection (no mid-corridor reversal)
        }
    }

    private void ReverseAll()
    {
        for (int i = 0; i < GhostIds.Length; i++)
        {
            if (!ExitedHouse[i]) continue;

            var (pdx, pdy) = GhostDirs[i];
            if (pdx == 0 && pdy == 0) continue;

            var (cx, cy) = GhostCells[i];
            GhostDirs[i] = (-pdx, -pdy);

            if (CanGhostMove(i, cx, cy, -pdx, -pdy))
                GhostTargets[i] = (cx - pdx, cy - pdy);
        }
    }

    private bool CanGhostMove(int gi, int cx, int cy, int dx, int dy)
    {
        if (!Maze.CanMove(cx, cy, dx, dy)) return false;
        if (ExitedHouse[gi])
        {
            int nx = Maze.WrapX(cx + dx), ny = cy + dy;
            if (Maze.IsGhostDoor(nx, ny)) return false;
            // Shadow: won't use teleport tunnels
            if (PacmanGhostTypes[gi] == PacmanGhostType.Shadow && nx != cx + dx) return false;
        }
        return true;
    }

    private (int x, int y) GetTargetTile(int i)
    {
        if (CurrentMode == PacmanGhostMode.Scatter)
            return ScatterTarget(PacmanGhostTypes[i]);

        var (cx, cy) = GhostCells[i];

        // If player exists, all ghosts chase the player
        if (Player != null)
        {
            var (px, py) = Player.Cell;

            // Clyde: retreats to scatter corner when close to player (< 8 tiles)
            if (PacmanGhostTypes[i] == PacmanGhostType.Clyde)
            {
                float d = MathF.Sqrt((cx - px) * (cx - px) + (cy - py) * (cy - py));
                if (d < 8f)
                    return ScatterTarget(PacmanGhostType.Clyde);
            }

            // Dinky: charges at player but chickens out within 10 tiles — flees to a random corner
            if (PacmanGhostTypes[i] == PacmanGhostType.Dinky)
            {
                float d = MathF.Sqrt((cx - px) * (cx - px) + (cy - py) * (cy - py));
                if (d < 10f)
                    return RandomScatterCorner();
            }

            return (px, py);
        }

        // Fallback: random walkable targets when no player
        var (tx, ty) = ChaseTargets[i];

        if (PacmanGhostTypes[i] == PacmanGhostType.Clyde)
        {
            float d = MathF.Sqrt((cx - tx) * (cx - tx) + (cy - ty) * (cy - ty));
            if (d < 8f)
                return ScatterTarget(PacmanGhostType.Clyde);
        }

        if (PacmanGhostTypes[i] == PacmanGhostType.Dinky)
        {
            float d = MathF.Sqrt((cx - tx) * (cx - tx) + (cy - ty) * (cy - ty));
            if (d < 10f)
                return RandomScatterCorner();
        }

        if (PacmanGhostTypes[i] == PacmanGhostType.Shadow)
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
                ExitedHouse[i] = true;
            else
            {
                if (ElapsedTime >= ReleaseTimes[i])
                    PickHouseExit(i);
                else
                    PickHouseWander(i);
                return;
            }
        }

        // Frightened mode: random turns at intersections
        if (CurrentMode == PacmanGhostMode.Frightened)
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
