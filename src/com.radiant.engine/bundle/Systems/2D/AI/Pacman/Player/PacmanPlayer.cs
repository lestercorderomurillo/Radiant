using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

[Pausable(PauseGroup.Gameplay | PauseGroup.Animation)]
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
    public bool HasMoved { get; private set; }
    public PacmanGhostAI GhostAI { get; set; }
    public RainbowGhostAI RainbowAI { get; set; }
    public float FrightenedDuration { get; set; } = 5f;
    public float HitboxRadius { get; set; } = 10f;
    public string LevelTag { get; set; } = "1-1";

    PacmanMazeBuilder Maze;
    Geometry Geometry;
    int EntityId;
    public float Speed { get; set; } = 200f;
    float Z;
    bool Tracked;
    const string HudFontName = "PressStart2P";
    const float HudFontSize = 24f;
    (int x, int y) PrevCell = (-1, -1);

    (int x, int y) TargetCell;
    (int dx, int dy) CurrentDir;
    (int dx, int dy) BufferedDir;

    // Mouth + eyes overlay
    const int RTSize = 128;
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
    static readonly Color EyeColor = new Color(30, 15, 5);
    float CollectFlash;
    float BaseRadius;
    float IdleTime;
    float WobbleX, WobbleY;

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
        HasMoved = false;
        PrevCell = startCell;
        CoinsCollected = 0;
        CoinsTotal = Maze.CoinCells.Count;
        RenderPosition = WorldPosition;
        PrevRenderPos = WorldPosition;
        FacingDir = (1, 0);
        MouthTimer = 0f;
        MouthOpen = true;
        MouthRotation = 0f;
        CollectFlash = 0f;
        BaseRadius = Scene.ECS.GetComponent<Circle2D>(entityId).Radius;

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
        Geometry = Scene.ECS.GetSystem<Geometry>();
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
        var pos = WorldPosition;
        var target = Maze.CellCenter(TargetCell.x, TargetCell.y);

        var diff = target - pos;
        float dist = diff.Length();

        if (dist <= step)
        {
            var raw = TargetCell;
            Cell = (Maze.WrapX(raw.x), raw.y);
            pos = Maze.CellCenter(Cell.x, Cell.y);

            if (Cell != PrevCell)
            {
                if (!HasMoved) HasMoved = true;
                CollectAt(Cell.x, Cell.y);
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
            var move = diff / dist * MathF.Min(step, dist);
            pos += move;
        }

        IdleTime += dt;
        float wobble = MathF.Sin(IdleTime * 3f) * 3f;
        WobbleX = CurrentDir.dy != 0 ? wobble : 0f;
        WobbleY = CurrentDir.dy != 0 ? 0f : wobble;
        transform.Position = new Vector3(pos.X + WobbleX, pos.Y + WobbleY, Z);
        WorldPosition = pos;

        // Collect ahead cell when within 0.2 * CellSize (~1.2 cell reach)
        if (CurrentDir != (0, 0))
        {
            int aheadX = Maze.WrapX(Cell.x + CurrentDir.dx);
            int aheadY = Cell.y + CurrentDir.dy;
            var aheadCenter = Maze.CellCenter(aheadX, aheadY);
            float adx = pos.X - aheadCenter.X;
            float ady = pos.Y - aheadCenter.Y;
            float reachThreshold = Maze.CellSize * 0.425f;
            if (adx * adx + ady * ady < reachThreshold * reachThreshold)
                CollectAt(aheadX, aheadY);
        }

        // Coin collect: radius bump
        if (CollectFlash > 0f)
            CollectFlash = MathF.Max(0f, CollectFlash - dt * 4f);
            
        ref var circle = ref Scene.ECS.GetComponent<Circle2D>(EntityId);

        circle.Radius = BaseRadius * (1f + CollectFlash * 0.20f);

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

        RenderPlayerRT();
    }

    private void RenderPlayerRT()
    {
        if (PlayerRT == null) return;

        // Render circle-with-mouth to the player RT (feeds into Geometry texture path → GI)
        Renderer.PushTargets();
        Renderer.SetTarget(PlayerRT).Clear(Color.Transparent);

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
        if (!Tracked || Geometry.IsDebugHidingGameplay) return;

        DrawEyes();
        DrawHUD();
    }

    void DrawEyes()
    {
        // No eyes when facing up/down
        if (PlayerEyesTexture == null || FacingDir.dy != 0) return;

        float Sx = Renderer.ScreenWidth / Renderer.VirtualSize.X;
        float Sy = Renderer.ScreenHeight / Renderer.VirtualSize.Y;
        float Pcx = RenderPosition.X + WobbleX;
        float Pcy = RenderPosition.Y + WobbleY;

        float Radius = Scene.ECS.GetComponent<Circle2D>(EntityId).Radius;
        float EyeR = Radius * 0.767f;
        float EyeD = EyeR * 2f;
        float EyeOff = Radius * 0.133f;
        float Ecx = Pcx + FacingDir.dx * EyeOff;
        float Ecy = Pcy + FacingDir.dy * EyeOff - Radius * 0.1f;
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
            Renderer.DrawSprite(PlayerEyesTexture, Dst, Src, EyeColor, 0f, Vector2.Zero);
        }
        else if (FacingDir.dx == -1) // Left → show right eye only
        {
            var Src = new Rectangle(TexW / 2, 0, TexW / 2, TexH);
            var Dst = new Rectangle(
                (int)(Ecx * Sx),
                (int)((Ecy - EyeR) * Sy),
                (int)(EyeR * Sx),
                (int)(EyeD * Sy));
            Renderer.DrawSprite(PlayerEyesTexture, Dst, Src, EyeColor, 0f, Vector2.Zero);
        }

        Renderer.EndDraw();
    }

    void DrawHUD()
    {
        var Scale = Matrix.CreateScale(
            Renderer.ScreenWidth / Renderer.VirtualWidth,
            Renderer.ScreenHeight / Renderer.VirtualHeight,
            1f);

        float Padding = 18f;
        float IconSize = 40f;
        float Gap = 14f;

        float MazeLeft = Maze.OffsetX + 40f;
        float MazeRight = Maze.OffsetX + Maze.Cols * Maze.CellSize - 40f;
        float Y = Maze.OffsetY - 45f;

        var TagSize = Renderer.MeasureString(HudFontName, HudFontSize, LevelTag);
        float TagBlockWidth = TagSize.X;
        float TextHeight = TagSize.Y;

        string Collected = CoinsCollected.ToString();
        string Separator = " / ";
        string Total = CoinsTotal.ToString();
        var CollectedSize = Renderer.MeasureString(HudFontName, HudFontSize, Collected);
        var SeparatorSize = Renderer.MeasureString(HudFontName, HudFontSize, Separator);
        var TotalSize = Renderer.MeasureString(HudFontName, HudFontSize, Total);
        float CoinTextWidth = CollectedSize.X + SeparatorSize.X + TotalSize.X;
        float CoinBlockWidth = IconSize + Gap + CoinTextWidth;

        Renderer.BeginDraw(SpriteSortMode.Deferred, BlendState.AlphaBlend, transform: Scale);

        float TagX = MazeLeft;
        var TagBg = new Rectangle(
            (int)(TagX - Padding), (int)(Y - Padding * 0.6f),
            (int)(TagBlockWidth + Padding * 2f), (int)(TextHeight + Padding * 1.2f));
        Renderer.DrawSprite(Renderer.GetSolidTexture(Color.White), TagBg, new Color(0, 0, 0, 180));
        Renderer.DrawString(HudFontName, HudFontSize, LevelTag, new Vector2(TagX, Y), new Color(255, 255, 255));

        float CoinX = MazeRight - CoinBlockWidth;
        var CoinBg = new Rectangle(
            (int)(CoinX - Padding), (int)(Y - Padding * 0.6f),
            (int)(CoinBlockWidth + Padding * 2f), (int)(TextHeight + Padding * 1.2f));
        Renderer.DrawSprite(Renderer.GetSolidTexture(Color.White), CoinBg, new Color(0, 0, 0, 180));

        var CoinTex = Renderer.GetCircleTexture((int)IconSize);
        var IconRect = new Rectangle(
            (int)CoinX, (int)(Y + (TextHeight - IconSize) / 2f),
            (int)IconSize, (int)IconSize);
        Renderer.DrawSprite(CoinTex, IconRect, CoinColor);

        float TextX = CoinX + IconSize + Gap;
        Renderer.DrawString(HudFontName, HudFontSize, Collected, new Vector2(TextX, Y), CoinColor);
        TextX += CollectedSize.X;
        Renderer.DrawString(HudFontName, HudFontSize, Separator, new Vector2(TextX, Y), new Color(200, 200, 200));
        TextX += SeparatorSize.X;
        Renderer.DrawString(HudFontName, HudFontSize, Total, new Vector2(TextX, Y), new Color(255, 255, 255));

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

    void CollectAt(int cx, int cy)
    {
        if (Maze.TryCollectCoin(cx, cy))
        {
            CoinsCollected++;
            CollectFlash = 1f;
        }

        if (Maze.TryCollectPowerPellet(cx, cy))
        {
            GhostAI?.SetFrightened(FrightenedDuration);
            RainbowAI?.SetFrightened(FrightenedDuration);
            CollectFlash = 1f;
        }
    }

    Texture2D CreateMouthTexture(int Size)
    {
        var Tex = Renderer.CreateTexture(Size, Size, SurfaceFormat.Color);
        var Pixels = new Color[Size * Size];
        float Half = Size / 2f;
        float OriginX = Half - 4f; // wedge starts a bit behind center
        float Slope = MathF.Tan(MathF.PI * 0.22f); // ~40° total opening

        for (int Y = 0; Y < Size; Y++)
        {
            for (int X = 0; X < Size; X++)
            {
                float Dx = X - OriginX;
                float Dy = Y - Half;

                // Pure linear test: straight edges guaranteed
                if (Dx > 0f && MathF.Abs(Dy) < Slope * Dx)
                    Pixels[Y * Size + X] = new Color((byte)0, (byte)0, (byte)0, (byte)255);
            }
        }

        Tex.SetData(Pixels);
        return Tex;
    }
}
