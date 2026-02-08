using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace com.radiant.engine.bundle;

public class GhostAI : core.System
{
    public int GhostCount { get; set; } = 8;
    public float GhostSpeed { get; set; } = 200f;
    public float GhostZ { get; set; } = 65530f;
    public (int x, int y)[] StartCells { get; set; }
    public Color[] GhostColors { get; set; }
    public Texture2D GhostTexture { get; set; }
    public Texture2D EyesTexture { get; set; }

    public int[] GhostIds { get; private set; }

    private MazeBuilder Maze;
    private (int x, int y)[] GhostCells;
    private (int x, int y)[] GhostTargets;
    private (int dx, int dy)[] GhostDirs;
    private Random Rng = new();

    public override void Initialize()
    {
        Maze = Scene.ECS.GetSystem<MazeBuilder>();
    }

    public void SpawnGhosts()
    {
        float radius = 25f;

        GhostIds = new int[GhostCount];
        GhostCells = new (int, int)[GhostCount];
        GhostTargets = new (int, int)[GhostCount];
        GhostDirs = new (int, int)[GhostCount];

        for (int i = 0; i < GhostCount; i++)
        {
            var cell = StartCells[i];
            var center = Maze.CellCenter(cell.x, cell.y);

            GhostIds[i] = LightFactory.CreateLight(
                Scene.ECS, center, radius, GhostColors[i], GhostColors[i], GhostZ, GhostTexture);
            Scene.ECS.AddComponent<MotionTrackable>(GhostIds[i]);

            GhostCells[i] = cell;
            GhostTargets[i] = cell;
            GhostDirs[i] = (0, 0);
        }
    }

    public override void Update()
    {
        if (GhostIds == null) return;

        float dt = (float)GameTime.ElapsedGameTime.TotalSeconds;
        float step = GhostSpeed * dt;

        for (int i = 0; i < GhostCount; i++)
        {
            ref var transform = ref Scene.ECS.GetComponent<Transform>(GhostIds[i]);
            var pos = new Vector2(transform.Position.X, transform.Position.Y);
            var target = Maze.CellCenter(GhostTargets[i].x, GhostTargets[i].y);

            var diff = target - pos;
            float dist = diff.Length();

            if (dist <= step)
            {
                GhostCells[i] = GhostTargets[i];
                PickDirection(i);

                target = Maze.CellCenter(GhostTargets[i].x, GhostTargets[i].y);
                diff = target - pos;
                dist = diff.Length();
            }

            if (dist > 0.01f)
            {
                var move = (diff / dist) * MathF.Min(step, dist);
                pos += move;
            }

            transform.Position = new Vector3(pos, GhostZ);
        }
    }

    public override void LateRender()
    {
        if (GhostIds == null || EyesTexture == null) return;

        float sx = Renderer.ScreenWidth / Renderer.VirtualSize.X;
        float sy = Renderer.ScreenHeight / Renderer.VirtualSize.Y;
        float radius = 20f;
        float diameter = radius * 2f;

        Renderer.Reset()
            .Configure(BlendState.AlphaBlend)
            .SetTarget(null);

        for (int i = 0; i < GhostCount; i++)
        {
            ref var transform = ref Scene.ECS.GetComponent<Transform>(GhostIds[i]);
            float cx = transform.Position.X;
            float cy = transform.Position.Y;

            Renderer.DrawTexture(EyesTexture,
                new Rectangle(
                    (int)((cx - radius) * sx),
                    (int)((cy - radius) * sy),
                    (int)(diameter * sx),
                    (int)(diameter * sy)),
                Color.White);
        }

        Renderer.Commit();
    }

    private void PickDirection(int i)
    {
        var (cx, cy) = GhostCells[i];
        var (pdx, pdy) = GhostDirs[i];

        Span<(int dx, int dy)> options = stackalloc (int, int)[4];
        int count = 0;
        int[] dxs = { 0, 1, 0, -1 };
        int[] dys = { -1, 0, 1, 0 };

        for (int d = 0; d < 4; d++)
            if (Maze.CanMove(cx, cy, dxs[d], dys[d]) && !(dxs[d] == -pdx && dys[d] == -pdy))
                options[count++] = (dxs[d], dys[d]);

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
}
