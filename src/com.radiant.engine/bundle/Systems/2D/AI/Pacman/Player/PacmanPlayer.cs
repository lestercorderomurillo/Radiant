using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;

namespace com.radiant.engine.bundle;

[RunAfter(typeof(PacmanMazeBuilder))]
[RunBefore(typeof(PacmanGhostAI))]
public class PacmanPlayer : core.System
{
    public (int x, int y) Cell { get; private set; }
    public Vector2 WorldPosition { get; private set; }

    PacmanMazeBuilder Maze;
    int EntityId;
    float Speed = 200f;
    float Z;

    (int x, int y) TargetCell;
    (int dx, int dy) CurrentDir;
    (int dx, int dy) BufferedDir;

    public void Track(int entityId, (int x, int y) startCell, float z)
    {
        EntityId = entityId;
        Cell = startCell;
        TargetCell = startCell;
        CurrentDir = (0, 0);
        BufferedDir = (0, 0);
        Z = z;
        WorldPosition = Maze.CellCenter(startCell.x, startCell.y);
    }

    public override void Initialize()
    {
        Maze = Scene.ECS.GetSystem<PacmanMazeBuilder>();
    }

    public override void Update()
    {
        if (Maze == null) return;

        var kb = Keyboard.GetState();
        ReadInput(kb);

        float dt = (float)GameTime.ElapsedGameTime.TotalSeconds;
        float step = Speed * dt;

        ref var transform = ref Scene.ECS.GetComponent<Transform>(EntityId);
        var pos = new Vector2(transform.Position.X, transform.Position.Y);
        var target = Maze.CellCenter(TargetCell.x, TargetCell.y);

        var diff = target - pos;
        float dist = diff.Length();

        if (dist <= step)
        {
            var raw = TargetCell;
            Cell = (Maze.WrapX(raw.x), raw.y);
            pos = Maze.CellCenter(Cell.x, Cell.y);

            // Try buffered direction first, then current direction
            if (BufferedDir != (0, 0) && Maze.CanMove(Cell.x, Cell.y, BufferedDir.dx, BufferedDir.dy))
            {
                CurrentDir = BufferedDir;
                BufferedDir = (0, 0);
            }

            if (CurrentDir != (0, 0) && Maze.CanMove(Cell.x, Cell.y, CurrentDir.dx, CurrentDir.dy))
            {
                TargetCell = (Cell.x + CurrentDir.dx, Cell.y + CurrentDir.dy);
            }
            else
            {
                CurrentDir = (0, 0);
                TargetCell = Cell;
            }

            target = Maze.CellCenter(TargetCell.x, TargetCell.y);
            diff = target - pos;
            dist = diff.Length();
        }

        if (dist > 0.01f)
        {
            var move = (diff / dist) * MathF.Min(step, dist);
            pos += move;
        }

        transform.Position = new Vector3(pos, Z);
        WorldPosition = pos;
    }

    void ReadInput(KeyboardState kb)
    {
        (int dx, int dy) desired = (0, 0);

        if (kb.IsKeyDown(Keys.Up)) desired = (0, -1);
        else if (kb.IsKeyDown(Keys.Left)) desired = (-1, 0);
        else if (kb.IsKeyDown(Keys.Down)) desired = (0, 1);
        else if (kb.IsKeyDown(Keys.Right)) desired = (1, 0);

        if (desired == (0, 0)) return;

        // If we can turn immediately from current cell, do it
        if (Maze.CanMove(Cell.x, Cell.y, desired.dx, desired.dy))
        {
            // Reverse is always allowed instantly
            if (desired.dx == -CurrentDir.dx && desired.dy == -CurrentDir.dy)
            {
                CurrentDir = desired;
                TargetCell = (Cell.x + desired.dx, Cell.y + desired.dy);
                BufferedDir = (0, 0);
                return;
            }

            BufferedDir = desired;
        }
        else
        {
            BufferedDir = desired;
        }
    }
}
