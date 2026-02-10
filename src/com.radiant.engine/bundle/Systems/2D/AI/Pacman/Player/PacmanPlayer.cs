using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;

namespace com.radiant.engine.bundle;

[RunAfter(typeof(PacmanMazeBuilder))]
[RunBefore(typeof(PacmanGhostAI))]
public class PacmanPlayer : core.System
{
    public (int x, int y) Cell { get; private set; }
    public Vector2 WorldPosition { get; private set; }
    public int CoinsCollected { get; private set; }
    public int CoinsTotal { get; private set; }
    public Color CoinColor { get; set; } = new Color(255, 220, 50);
    public bool PlayerCaught { get; set; }

    PacmanMazeBuilder Maze;
    int EntityId;
    float Speed = 200f;
    float Z;
    bool Tracked;
    SpriteFont HudFont;
    (int x, int y) PrevCell = (-1, -1);

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
        Tracked = true;
        PlayerCaught = false;
        PrevCell = (-1, -1);
        CoinsCollected = 0;
        CoinsTotal = Maze.CoinCells.Count;
    }

    public void Clear()
    {
        if (!Tracked) return;
        Scene.ECS.DestroyEntity(EntityId);
        Tracked = false;
        CoinsCollected = 0;
        CoinsTotal = 0;
    }

    public override void Initialize()
    {
        Maze = Scene.ECS.GetSystem<PacmanMazeBuilder>();
        HudFont = Renderer.GetFont("fonts/BaseFont");
    }

    public override void Update()
    {
        if (Maze == null || !Tracked) return;

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

            // Collect coin at new cell
            if (Cell != PrevCell)
            {
                if (Maze.TryCollectCoin(Cell.x, Cell.y))
                    CoinsCollected++;
                PrevCell = Cell;
            }

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

    public override void LateRender()
    {
        if (!Tracked || HudFont == null) return;

        var scale = Matrix.CreateScale(
            (float)Renderer.ScreenWidth / Renderer.VirtualWidth,
            (float)Renderer.ScreenHeight / Renderer.VirtualHeight,
            1f);

        string collected = CoinsCollected.ToString();
        string separator = " / ";
        string total = CoinsTotal.ToString();

        var collectedSize = HudFont.MeasureString(collected);
        var separatorSize = HudFont.MeasureString(separator);
        var totalSize = HudFont.MeasureString(total);

        float iconSize = 20f;
        float gap = 10f;
        float textWidth = collectedSize.X + separatorSize.X + totalSize.X;
        float fullWidth = iconSize + gap + textWidth;
        float textHeight = collectedSize.Y;

        float padding = 12f;
        float x = Renderer.VirtualWidth - fullWidth - 25f;
        float y = 20f;

        var bgRect = new Rectangle(
            (int)(x - padding), (int)(y - padding * 0.6f),
            (int)(fullWidth + padding * 2f), (int)(textHeight + padding * 1.2f));

        Renderer.BeginDraw(SpriteSortMode.Deferred, BlendState.AlphaBlend, transform: scale);

        Renderer.DrawSprite(Renderer.GetSolidTexture(Color.White), bgRect, new Color(0, 0, 0, 160));

        // Coin icon
        var coinTex = Renderer.GetCircleTexture((int)iconSize);
        var iconRect = new Rectangle(
            (int)x, (int)(y + (textHeight - iconSize) / 2f),
            (int)iconSize, (int)iconSize);
        Renderer.DrawSprite(coinTex, iconRect, CoinColor);

        // Text
        float textX = x + iconSize + gap;
        Renderer.DrawString(HudFont, collected, new Vector2(textX, y), CoinColor);
        textX += collectedSize.X;
        Renderer.DrawString(HudFont, separator, new Vector2(textX, y), new Color(150, 150, 150));
        textX += separatorSize.X;
        Renderer.DrawString(HudFont, total, new Vector2(textX, y), new Color(200, 200, 200));

        Renderer.EndDraw();
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
