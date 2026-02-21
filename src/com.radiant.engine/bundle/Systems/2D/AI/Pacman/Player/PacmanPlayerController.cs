using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

[Pausable(PauseGroup.Gameplay | PauseGroup.Animation)]
[RunAfter(typeof(PacmanMazeBuilder))]
[RunBefore(typeof(PacmanGhostAI))]
[SystemTag("Pacman")]
public class PacmanPlayerController : core.System
{
    public (int x, int y) Cell { get; private set; }
    public Vector2 WorldPosition { get; private set; }
    public int CoinsCollected { get; private set; }
    public int CoinsTotal { get; private set; }
    public Color CoinColor { get; set; } = new Color(255, 220, 50);
    public bool PlayerCaught { get; set; }
    public bool HasMoved { get; private set; }
    public PacmanGhostAI GhostAI { get; set; }
    public float FrightenedDuration { get; set; } = 5f;
    public float HitboxRadius { get; set; } = 10f;
    public string LevelTag { get; set; } = "1-1";

    public int EntityId { get; private set; }
    public bool IsTracked { get; private set; }
    public Vector2 RenderPosition { get; private set; }
    public float WobbleX { get; private set; }
    public float WobbleY { get; private set; }
    public (int dx, int dy) FacingDir { get; private set; } = (1, 0);
    public bool MouthOpen { get; private set; }
    public float MouthRotation { get; private set; }
    public float BodyRadius { get; private set; }
    public float CollectFlash { get; private set; }

    const int RTSize = 128;
    static readonly Color PlayerEyeColor = new Color(30, 15, 5);

    RenderTarget2D PlayerRT;
    Texture2D CircleTex;
    Texture2D MouthTexture;
    Texture2D EyesTexture;
    Geometry Geometry;
    bool PlayerHidden;
    Color SavedAlbedo;
    Color SavedEmissive;

    PacmanMazeBuilder Maze;
    public float Speed { get; set; } = 200f;
    float Z;
    const string HudFontName = "PressStart2P";
    (int x, int y) PrevCell = (-1, -1);

    (int x, int y) TargetCell;
    (int dx, int dy) CurrentDir;
    (int dx, int dy) BufferedDir;

    float MouthTimer;
    Vector2 PrevRenderPos;
    float IdleTime;

    public void Track(int entityId, (int x, int y) startCell, float z)
    {
        EntityId = entityId;
        Cell = startCell;
        TargetCell = startCell;
        CurrentDir = (0, 0);
        BufferedDir = (0, 0);
        Z = z;
        WorldPosition = Maze.CellCenter(startCell.x, startCell.y);
        IsTracked = true;
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
        BodyRadius = Scene.ECS.GetComponent<Circle2D>(entityId).Radius;
        ref var material = ref Scene.ECS.GetComponent<Material>(entityId);
        material.Texture = PlayerRT;
    }

    public void Clear()
    {
        if (!IsTracked) return;
        Scene.ECS.DestroyEntity(EntityId);
        IsTracked = false;
        CoinsCollected = 0;
        CoinsTotal = 0;
    }

    public void HidePlayer()
    {
        if (PlayerHidden || !IsTracked || !Scene.ECS.IsAlive(EntityId)) return;
        ref var material = ref Scene.ECS.GetComponent<Material>(EntityId);
        SavedAlbedo = material.Albedo;
        SavedEmissive = material.Emissive;
        material.Albedo = Color.Transparent;
        material.Emissive = Color.Transparent;
        PlayerHidden = true;
    }

    public void ShowPlayer()
    {
        if (!PlayerHidden || !IsTracked || !Scene.ECS.IsAlive(EntityId)) return;
        ref var material = ref Scene.ECS.GetComponent<Material>(EntityId);
        material.Albedo = SavedAlbedo;
        material.Emissive = SavedEmissive;
        PlayerHidden = false;
    }

    public override void Initialize()
    {
        Maze = Scene.ECS.GetSystem<PacmanMazeBuilder>();
        Geometry = Scene.ECS.GetSystem<Geometry>();
        EyesTexture = Renderer.GetTexture("Eyes");
        MouthTexture = CreateMouthTexture(RTSize);
        CircleTex = Renderer.GetCircleTexture(RTSize);
        PlayerRT = Renderer.CreateRenderTarget(RTSize, RTSize);
    }

    public override void Update()
    {
        if (Maze == null || !IsTracked) return;
        if (Scene.ECS.IsDisabled(EntityId)) return;

        RenderPosition = PrevRenderPos;
        PrevRenderPos = WorldPosition;

        var kb = Keyboard.GetState();
        ReadInput(kb);

        float dt = (float)GameTime.ElapsedGameTime.TotalSeconds;
        float step = Speed * dt;

        if (!Scene.ECS.IsAlive(EntityId)) return;

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

        if (CollectFlash > 0f)
            CollectFlash = MathF.Max(0f, CollectFlash - dt * 4f);

        ref var circle = ref Scene.ECS.GetComponent<Circle2D>(EntityId);
        circle.Radius = BodyRadius * (1f + CollectFlash * 0.20f);

        if (CurrentDir != (0, 0))
        {
            FacingDir = CurrentDir;
            MouthTimer += dt * 24f;
        }

        float phase = (MathF.Sin(MouthTimer) + 1f) / 2f;
        MouthOpen = CurrentDir == (0, 0) || phase > 0.3f;
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

    void ReadInput(KeyboardState kb)
    {
        (int dx, int dy) desired = (0, 0);

        if (kb.IsKeyDown(Keys.Up)) desired = (0, -1);
        else if (kb.IsKeyDown(Keys.Left)) desired = (-1, 0);
        else if (kb.IsKeyDown(Keys.Down)) desired = (0, 1);
        else if (kb.IsKeyDown(Keys.Right)) desired = (1, 0);

        if (desired == (0, 0)) return;

        if (Maze.CanMove(Cell.x, Cell.y, desired.dx, desired.dy))
        {
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
            GhostAI?.SetRainbowFrightened(FrightenedDuration);
            CollectFlash = 1f;
        }
    }

    public override void LateRender()
    {
        if (Geometry.IsDebugHidingGameplay) return;
        if (Scene.ECS.IsDisabled(EntityId)) return;
        DrawPlayerEyes();
    }

    void RenderPlayerRT()
    {
        if (PlayerRT == null || !IsTracked) return;

        Renderer.PushTargets();
        Renderer.SetTarget(PlayerRT).Clear(Color.Transparent);

        Renderer.BeginDraw(SpriteSortMode.Immediate, BlendState.AlphaBlend);
        Renderer.DrawSprite(CircleTex, new Rectangle(0, 0, RTSize, RTSize), Color.White);
        Renderer.EndDraw();

        if (MouthOpen)
        {
            int half = RTSize / 2;
            var origin = new Vector2(half, half);
            Renderer.BlitMask(MouthTexture, new Rectangle(half, half, RTSize, RTSize), MouthRotation, origin);
        }

        Renderer.PopTargets();
    }

    void DrawPlayerEyes()
    {
        if (!IsTracked) return;
        if (!Scene.ECS.IsAlive(EntityId)) return;

        float radius = Scene.ECS.GetComponent<Circle2D>(EntityId).Radius;
        var facingDir = FacingDir;

        if (EyesTexture == null || facingDir.dy != 0) return;

        float sx = Renderer.ScreenWidth / Renderer.VirtualSize.X;
        float sy = Renderer.ScreenHeight / Renderer.VirtualSize.Y;
        float pcx = RenderPosition.X + WobbleX;
        float pcy = RenderPosition.Y + WobbleY;

        float eyeR = radius * 0.767f;
        float eyeD = eyeR * 2f;
        float eyeOff = radius * 0.133f;
        float ecx = pcx + facingDir.dx * eyeOff;
        float ecy = pcy + facingDir.dy * eyeOff - radius * 0.1f;
        int texW = EyesTexture.Width;
        int texH = EyesTexture.Height;

        Renderer.BeginDraw(SpriteSortMode.Deferred, BlendState.AlphaBlend);

        if (facingDir.dx == 1)
        {
            var src = new Rectangle(0, 0, texW / 2, texH);
            var dst = new Rectangle(
                (int)((ecx - eyeR) * sx),
                (int)((ecy - eyeR) * sy),
                (int)(eyeR * sx),
                (int)(eyeD * sy));
            Renderer.DrawSprite(EyesTexture, dst, src, PlayerEyeColor, 0f, Vector2.Zero);
        }
        else if (facingDir.dx == -1)
        {
            var src = new Rectangle(texW / 2, 0, texW / 2, texH);
            var dst = new Rectangle(
                (int)(ecx * sx),
                (int)((ecy - eyeR) * sy),
                (int)(eyeR * sx),
                (int)(eyeD * sy));
            Renderer.DrawSprite(EyesTexture, dst, src, PlayerEyeColor, 0f, Vector2.Zero);
        }

        Renderer.EndDraw();
    }

    Texture2D CreateMouthTexture(int size)
    {
        var tex = Renderer.CreateTexture(size, size, SurfaceFormat.Color);
        var pixels = new Color[size * size];
        float half = size / 2f;
        float originX = half - 4f;
        float slope = MathF.Tan(MathF.PI * 0.22f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - originX;
                float dy = y - half;

                if (dx > 0f && MathF.Abs(dy) < slope * dx)
                    pixels[y * size + x] = new Color((byte)0, (byte)0, (byte)0, (byte)255);
            }
        }

        tex.SetData(pixels);
        return tex;
    }
}
