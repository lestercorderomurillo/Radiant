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

    // Mouth + eyes overlay
    const int RTSize = 64;
    RenderTarget2D PlayerRT;
    Texture2D CircleTex;
    Texture2D MouthTexture;
    Texture2D PlayerEyesTexture;
    float MouthTimer;
    Vector2 RenderPosition;
    Vector2 PrevRenderPos;
    (int dx, int dy) FacingDir = (1, 0);
    bool MouthOpen;
    float MouthRotation;

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
        RenderPosition = WorldPosition;
        PrevRenderPos = WorldPosition;
        FacingDir = (1, 0);
        MouthTimer = 0f;
        MouthOpen = true;
        MouthRotation = 0f;

        // Set the RT as entity texture — Geometry texture path handles emissive/absorption
        ref var Mat = ref Scene.ECS.GetComponent<Material>(EntityId);
        Mat.Texture = PlayerRT;
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
        PlayerEyesTexture = Renderer.GetTexture("Eyes");
        MouthTexture = CreateMouthTexture(RTSize);
        CircleTex = Renderer.GetCircleTexture(RTSize);
        PlayerRT = Renderer.CreateRenderTarget(RTSize, RTSize);
    }

    public override void Update()
    {
        if (Maze == null || !Tracked) return;

        // Shift position history (2-frame delay to sync with Geometry double-buffer)
        RenderPosition = PrevRenderPos;
        PrevRenderPos = WorldPosition;

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

        // Mouth animation and facing direction
        if (CurrentDir != (0, 0))
        {
            FacingDir = CurrentDir;
            MouthTimer += dt * 24f;
        }

        float Phase = (MathF.Sin(MouthTimer) + 1f) / 2f;
        MouthOpen = CurrentDir == (0, 0) || Phase > 0.3f;
        MouthRotation = (FacingDir.dx, FacingDir.dy) switch
        {
            (1, 0) => 0f,
            (0, 1) => MathF.PI / 2f,
            (-1, 0) => MathF.PI,
            (0, -1) => -MathF.PI / 2f,
            _ => 0f
        };
    }

    public override void Render()
    {
        if (!Tracked || PlayerRT == null) return;

        // Render circle-with-mouth to the player RT (feeds into Geometry texture path → GI)
        Renderer.PushTargets();
        Renderer.SetTarget(PlayerRT);
        Renderer.ClearBackBuffer(Color.Transparent);

        // White circle — Material's EmissiveScaled/Absorption provide the color tint
        Renderer.BeginDraw(SpriteSortMode.Immediate, BlendState.AlphaBlend);
        Renderer.DrawSprite(CircleTex, new Rectangle(0, 0, RTSize, RTSize), Color.White);
        Renderer.EndDraw();

        // Cut out mouth wedge (origin = center of source, so dest must be offset to center pivot in RT)
        if (MouthOpen)
        {
            int Half = RTSize / 2;
            var Origin = new Vector2(Half, Half);
            Renderer.BlitMask(MouthTexture, new Rectangle(Half, Half, RTSize, RTSize), MouthRotation, Origin);
        }

        Renderer.PopTargets();
    }

    public override void LateRender()
    {
        if (!Tracked) return;

        DrawEyes();
        DrawHUD();
    }

    void DrawEyes()
    {
        if (PlayerEyesTexture == null) return;

        float Sx = Renderer.ScreenWidth / Renderer.VirtualSize.X;
        float Sy = Renderer.ScreenHeight / Renderer.VirtualSize.Y;
        float Pcx = RenderPosition.X;
        float Pcy = RenderPosition.Y;

        float EyeR = 20f;
        float EyeD = EyeR * 2f;
        float EyeUpNudge = FacingDir.dy == -1 ? 12f : 0f;
        float Ecx = Pcx + FacingDir.dx * 4f;
        float Ecy = Pcy + FacingDir.dy * 4f + EyeUpNudge;
        int TexW = PlayerEyesTexture.Width;
        int TexH = PlayerEyesTexture.Height;

        Renderer.BeginDraw(SpriteSortMode.Deferred, BlendState.AlphaBlend);

        if (FacingDir.dx == 1) // Right → show left eye only
        {
            var Src = new Rectangle(0, 0, TexW / 2, TexH);
            var Dst = new Rectangle(
                (int)((Ecx - EyeR) * Sx),
                (int)((Ecy - EyeR) * Sy),
                (int)(EyeR * Sx),
                (int)(EyeD * Sy));
            Renderer.DrawSprite(PlayerEyesTexture, Dst, Src, Color.Black, 0f, Vector2.Zero);
        }
        else if (FacingDir.dx == -1) // Left → show right eye only
        {
            var Src = new Rectangle(TexW / 2, 0, TexW / 2, TexH);
            var Dst = new Rectangle(
                (int)(Ecx * Sx),
                (int)((Ecy - EyeR) * Sy),
                (int)(EyeR * Sx),
                (int)(EyeD * Sy));
            Renderer.DrawSprite(PlayerEyesTexture, Dst, Src, Color.Black, 0f, Vector2.Zero);
        }
        else // Up/Down → show both eyes
        {
            Renderer.DrawSprite(PlayerEyesTexture,
                new Rectangle(
                    (int)((Ecx - EyeR) * Sx),
                    (int)((Ecy - EyeR) * Sy),
                    (int)(EyeD * Sx),
                    (int)(EyeD * Sy)),
                Color.Black);
        }

        Renderer.EndDraw();
    }

    void DrawHUD()
    {
        if (HudFont == null) return;

        var Scale = Matrix.CreateScale(
            (float)Renderer.ScreenWidth / Renderer.VirtualWidth,
            (float)Renderer.ScreenHeight / Renderer.VirtualHeight,
            1f);

        string Collected = CoinsCollected.ToString();
        string Separator = " / ";
        string Total = CoinsTotal.ToString();

        var CollectedSize = HudFont.MeasureString(Collected);
        var SeparatorSize = HudFont.MeasureString(Separator);
        var TotalSize = HudFont.MeasureString(Total);

        float IconSize = 20f;
        float Gap = 10f;
        float TextWidth = CollectedSize.X + SeparatorSize.X + TotalSize.X;
        float FullWidth = IconSize + Gap + TextWidth;
        float TextHeight = CollectedSize.Y;

        float Padding = 12f;
        float X = Renderer.VirtualWidth - FullWidth - 25f;
        float Y = 20f;

        var BgRect = new Rectangle(
            (int)(X - Padding), (int)(Y - Padding * 0.6f),
            (int)(FullWidth + Padding * 2f), (int)(TextHeight + Padding * 1.2f));

        Renderer.BeginDraw(SpriteSortMode.Deferred, BlendState.AlphaBlend, transform: Scale);

        Renderer.DrawSprite(Renderer.GetSolidTexture(Color.White), BgRect, new Color(0, 0, 0, 160));

        // Coin icon
        var CoinTex = Renderer.GetCircleTexture((int)IconSize);
        var IconRect = new Rectangle(
            (int)X, (int)(Y + (TextHeight - IconSize) / 2f),
            (int)IconSize, (int)IconSize);
        Renderer.DrawSprite(CoinTex, IconRect, CoinColor);

        // Text
        float TextX = X + IconSize + Gap;
        Renderer.DrawString(HudFont, Collected, new Vector2(TextX, Y), CoinColor);
        TextX += CollectedSize.X;
        Renderer.DrawString(HudFont, Separator, new Vector2(TextX, Y), new Color(150, 150, 150));
        TextX += SeparatorSize.X;
        Renderer.DrawString(HudFont, Total, new Vector2(TextX, Y), new Color(200, 200, 200));

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

    Texture2D CreateMouthTexture(int Size)
    {
        var Tex = Renderer.CreateTexture(Size, Size, SurfaceFormat.Color);
        var Pixels = new Color[Size * Size];
        float Half = Size / 2f;
        float MaxAngle = MathF.PI * 0.28f; // ~50° total opening

        for (int Y = 0; Y < Size; Y++)
        {
            for (int X = 0; X < Size; X++)
            {
                float Dx = X - Half;
                float Dy = Y - Half;
                float Dist = MathF.Sqrt(Dx * Dx + Dy * Dy);

                if (Dist < Half && Dist > 0.5f)
                {
                    float Angle = MathF.Abs(MathF.Atan2(Dy, Dx));
                    if (Angle < MaxAngle)
                    {
                        float EdgeDist = Half - Dist;
                        float CircleFade = MathF.Min(EdgeDist / 1.5f, 1f);
                        float AngleDist = MaxAngle - Angle;
                        float AngleFade = MathF.Min(AngleDist / 0.05f, 1f);
                        byte A = (byte)(CircleFade * AngleFade * 255);
                        Pixels[Y * Size + X] = new Color((byte)0, (byte)0, (byte)0, A);
                    }
                }
            }
        }

        Tex.SetData(Pixels);
        return Tex;
    }
}
