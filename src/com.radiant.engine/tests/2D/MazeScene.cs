using com.radiant.engine.bundle;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;


namespace com.radiant.engine.core;

public class MazeScene : Scene
{
    private int MouseLightId;

    private MouseState PrevMouse;
    private KeyboardState PrevKeyboard;
    private Random Rng = new();

    private float RainbowHue = 0f;
    private const float HueSpeed = 0.008f;
    private const float PaintRadius = 8f;
    private const float PaintSpacing = 3f;

    private Vector2 LastPaintPos;
    private bool HasLastPaintPos = false;
    private Vector2 LastRightPaintPos;
    private bool HasLastRightPaintPos = false;
    private const float MouseLightZ = 65535f;

    private Texture2D GhostTexture;
    private Texture2D EyesTexture;

    private HRCGI HRCGISystem;
    private RCGI RCGISystem;
    private Bilinear Bilinear;
    private UDR1 UDR1System;
    private UDR2 UDR2System;
    private UDR3 UDR3System;
    private GizmosRenderer Gizmos;

    private bool UseHRCGI = true;
    private int UDRMode = 3;  // 0 = Bilinear, 1 = UDR1, 2 = UDR2, 3 = UDR3

    // Maze parameters
    private const float CellSize = 70f;
    private const float WallThickness = 14f;
    private const float WallMargin = 20f;
    private const int MazeCols = 28;
    private const int MazeRows = 31;
    private float MazeOffsetX, MazeOffsetY;

    // Pac-Man maze tile grid (true = wall)
    private bool[,] MazeGrid;

    private static readonly string[] PacmanLayout =
    {
        "1111111111111111111111111111", // 0
        "1000000000000110000000000001", // 1
        "1011110111110110111110111101", // 2
        "1011110111110110111110111101", // 3
        "1011110111110110111110111101", // 4
        "1000000000000000000000000001", // 5
        "1011110110111111110110111101", // 6
        "1011110110111111110110111101", // 7
        "1000000110000110000110000001", // 8
        "1111110111110110111110111111", // 9
        "1111110111110110111110111111", // 10
        "1111110110000000000110111111", // 11
        "1111110110111001110110111111", // 12
        "1111110110100000010110111111", // 13
        "0000000000100000010000000000", // 14
        "1111110110100000010110111111", // 15
        "1111110110111111110110111111", // 16
        "1111110110000000000110111111", // 17
        "1111110110111111110110111111", // 18
        "1111110110111111110110111111", // 19
        "1000000000000110000000000001", // 20
        "1011110111110110111110111101", // 21
        "1011110111110110111110111101", // 22
        "1000110000000000000000110001", // 23
        "1110110110111111110110110111", // 24
        "1110110110111111110110110111", // 25
        "1000000110000110000110000001", // 26
        "1011111111110110111111111101", // 27
        "1011111111110110111111111101", // 28
        "1000000000000000000000000001", // 29
        "1111111111111111111111111111", // 30
    };

    // Ghost wandering
    private const int GhostCount = 12;
    private const float GhostSpeed = 200f;
    private const float GhostZ = 65530f;
    private int[] GhostIds;
    private (int x, int y)[] GhostCells;
    private (int x, int y)[] GhostTargets;
    private (int dx, int dy)[] GhostDirs;

    public override void SetupECS()
    {
        ECS.AddSystem<PerformanceMonitor>();
        ECS.AddSystem<Geometry>();
        
        HRCGISystem = ECS.AddSystem<HRCGI>();
        RCGISystem = ECS.AddSystem<RCGI>(enabled: false);

        Bilinear = ECS.AddSystem<Bilinear>(enabled: false);
        UDR1System = ECS.AddSystem<UDR1>(enabled: false);
        UDR2System = ECS.AddSystem<UDR2>(enabled: false);
        UDR3System = ECS.AddSystem<UDR3>();
        
        Gizmos = ECS.AddSystem<GizmosRenderer>();

        base.SetupECS();
    }

    public override void SetupScene()
    {
        GhostTexture = Renderer.Window.Content.Load<Texture2D>("Ghost");
        EyesTexture = Renderer.Window.Content.Load<Texture2D>("Eyes");

        CreateMaze();
        CreateGhosts();
        CreateMouseLight();
        UpdateUDRInput();

        base.SetupScene();
    }

    private Vector2 CellCenter(int cx, int cy) => new(
        MazeOffsetX + cx * CellSize + CellSize / 2f,
        MazeOffsetY + cy * CellSize + CellSize / 2f);

    private void CreateGhosts()
    {
        float radius = 25f;

        var ghostColors = new Color[]
        {
            new(255, 0, 0),     // Blinky (red)
            new(255, 184, 255), // Pinky (pink)
            new(0, 255, 255),   // Inky (cyan)
            new(255, 184, 82),  // Clyde (orange)
            new(255, 255, 0),   // yellow
            new(0, 255, 0),     // green
            new(128, 0, 255),   // purple
            new(255, 100, 100), // light red
            new(100, 200, 255), // light blue
            new(255, 150, 0),   // dark orange
            new(200, 255, 200), // light green
            new(255, 100, 200), // hot pink
        };

        (int x, int y)[] startCells =
        {
            (13, 11),  // above ghost house
            (13, 14),  // center of ghost house
            (11, 14),  // left of ghost house
            (16, 14),  // right of ghost house
            (1, 1),    // top-left corner
            (26, 1),   // top-right corner
            (1, 29),   // bottom-left corner
            (26, 29),  // bottom-right corner
            (6, 5),    // upper-left corridor
            (21, 5),   // upper-right corridor
            (6, 23),   // lower-left corridor
            (21, 23),  // lower-right corridor
        };

        GhostIds = new int[GhostCount];
        GhostCells = new (int, int)[GhostCount];
        GhostTargets = new (int, int)[GhostCount];
        GhostDirs = new (int, int)[GhostCount];

        for (int i = 0; i < GhostCount; i++)
        {
            var cell = startCells[i];
            GhostIds[i] = CreateLight(CellCenter(cell.x, cell.y), radius, ghostColors[i], ghostColors[i], GhostZ);
            ref var mat = ref ECS.GetComponent<Material>(GhostIds[i]);
            mat.Texture = GhostTexture;
            ECS.AddComponent<MotionTrackable>(GhostIds[i]);
            GhostCells[i] = cell;
            GhostTargets[i] = cell;
            GhostDirs[i] = (0, 0);
        }
    }

    private bool CanMove(int cx, int cy, int dx, int dy)
    {
        int nx = cx + dx, ny = cy + dy;
        if (nx < 0 || nx >= MazeCols || ny < 0 || ny >= MazeRows) return false;
        return !MazeGrid[nx, ny];
    }

    private void PickGhostDirection(int i)
    {
        var (cx, cy) = GhostCells[i];
        var (pdx, pdy) = GhostDirs[i];

        // Collect open directions, prefer not reversing
        Span<(int dx, int dy)> options = stackalloc (int, int)[4];
        int count = 0;
        int[] dxs = { 0, 1, 0, -1 };
        int[] dys = { -1, 0, 1, 0 };

        for (int d = 0; d < 4; d++)
            if (CanMove(cx, cy, dxs[d], dys[d]) && !(dxs[d] == -pdx && dys[d] == -pdy))
                options[count++] = (dxs[d], dys[d]);

        // Dead end — reverse
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

    private void UpdateGhosts()
    {
        float step = GhostSpeed * DeltaTime;

        for (int i = 0; i < GhostCount; i++)
        {
            ref var transform = ref ECS.GetComponent<Transform>(GhostIds[i]);
            var pos = new Vector2(transform.Position.X, transform.Position.Y);
            var target = CellCenter(GhostTargets[i].x, GhostTargets[i].y);

            var diff = target - pos;
            float dist = diff.Length();

            if (dist <= step)
            {
                // Arrived at target cell
                GhostCells[i] = GhostTargets[i];
                PickGhostDirection(i);

                // Start moving toward new target
                target = CellCenter(GhostTargets[i].x, GhostTargets[i].y);
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

    private void CreateMaze()
    {
        float mazeW = MazeCols * CellSize;
        float mazeH = MazeRows * CellSize;
        var virt = Renderer.VirtualSize;
        MazeOffsetX = (virt.X - mazeW) / 2f;
        MazeOffsetY = (virt.Y - mazeH) / 2f;

        // Parse Pac-Man layout into tile grid
        MazeGrid = new bool[MazeCols, MazeRows];
        for (int y = 0; y < MazeRows; y++)
            for (int x = 0; x < MazeCols; x++)
                MazeGrid[x, y] = PacmanLayout[y][x] == '1';

        // Render walls as outline segments shifted into wall tiles by WallMargin
        float hw = WallThickness / 2f;
        float ox = MazeOffsetX;
        float oy = MazeOffsetY;

        // Corner posts at grid intersections on the boundary
        for (int gy = 0; gy <= MazeRows; gy++)
            for (int gx = 0; gx <= MazeCols; gx++)
            {
                bool tl = IsWall(gx - 1, gy - 1), tr = IsWall(gx, gy - 1);
                bool bl = IsWall(gx - 1, gy), br = IsWall(gx, gy);
                int w = (tl ? 1 : 0) + (tr ? 1 : 0) + (bl ? 1 : 0) + (br ? 1 : 0);
                if (w == 0 || w == 4) continue;

                int lw = (tl ? 1 : 0) + (bl ? 1 : 0);
                int rw = (tr ? 1 : 0) + (br ? 1 : 0);
                int tw = (tl ? 1 : 0) + (tr ? 1 : 0);
                int bw = (bl ? 1 : 0) + (br ? 1 : 0);

                float px = ox + gx * CellSize;
                if (lw > rw) px -= WallMargin;
                else if (rw > lw) px += WallMargin;

                float py = oy + gy * CellSize;
                if (tw > bw) py -= WallMargin;
                else if (bw > tw) py += WallMargin;

                CreateWall(new Vector2(px - hw, py - hw), new Vector2(WallThickness, WallThickness));
            }

        // Horizontal segments (between rows) — endpoints from corner posts
        for (int gy = 0; gy <= MazeRows; gy++)
            for (int gx = 0; gx < MazeCols; gx++)
            {
                bool above = IsWall(gx, gy - 1);
                bool below = IsWall(gx, gy);
                if (above == below) continue;

                float sy = oy + gy * CellSize + (above ? -WallMargin : WallMargin);
                var lp = PostPosition(gx, gy);
                var rp = PostPosition(gx + 1, gy);
                CreateWall(
                    new Vector2(lp.X - hw, sy - hw),
                    new Vector2(rp.X - lp.X + WallThickness, WallThickness));
            }

        // Vertical segments (between columns) — endpoints from corner posts
        for (int gx = 0; gx <= MazeCols; gx++)
            for (int gy = 0; gy < MazeRows; gy++)
            {
                bool left = IsWall(gx - 1, gy);
                bool right = IsWall(gx, gy);
                if (left == right) continue;

                float sx = ox + gx * CellSize + (left ? -WallMargin : WallMargin);
                var tp = PostPosition(gx, gy);
                var bp = PostPosition(gx, gy + 1);
                CreateWall(
                    new Vector2(sx - hw, tp.Y - hw),
                    new Vector2(WallThickness, bp.Y - tp.Y + WallThickness));
            }
    }

    private bool IsWall(int x, int y)
    {
        if (x < 0 || x >= MazeCols || y < 0 || y >= MazeRows) return true;
        return MazeGrid[x, y];
    }

    private Vector2 PostPosition(int gx, int gy)
    {
        bool tl = IsWall(gx - 1, gy - 1), tr = IsWall(gx, gy - 1);
        bool bl = IsWall(gx - 1, gy), br = IsWall(gx, gy);

        int lw = (tl ? 1 : 0) + (bl ? 1 : 0);
        int rw = (tr ? 1 : 0) + (br ? 1 : 0);
        int tw = (tl ? 1 : 0) + (tr ? 1 : 0);
        int bw = (bl ? 1 : 0) + (br ? 1 : 0);

        float px = MazeOffsetX + gx * CellSize;
        if (lw > rw) px -= WallMargin;
        else if (rw > lw) px += WallMargin;

        float py = MazeOffsetY + gy * CellSize;
        if (tw > bw) py -= WallMargin;
        else if (bw > tw) py += WallMargin;

        return new Vector2(px, py);
    }

    private void CreateWall(Vector2 position, Vector2 size)
    {
        int id = ECS.CreateEntity();
        ECS.AddComponent<Transform>(id);
        ECS.AddComponent<Rectangle2D>(id);
        ECS.AddComponent<Material>(id);

        ref var transform = ref ECS.GetComponent<Transform>(id);
        ref var rect = ref ECS.GetComponent<Rectangle2D>(id);
        ref var material = ref ECS.GetComponent<Material>(id);

        transform.Position = new Vector3(position, 0f);
        transform.Rotation = Vector3.UnitX;
        rect.Size = size;

        material.Albedo = new Color((byte)0, (byte)0, (byte)0, (byte)255);
        material.Emissive = Color.Transparent;
    }

    private void CreateMouseLight()
    {
        var mouse = Mouse.GetState();
        var worldPos = Renderer.ScreenToWorld(new Vector2(mouse.X, mouse.Y));
        MouseLightId = CreateLight(worldPos, 100f, new Color(0, 0, 0, 128), new Color(0, 0, 0, 128));

        // Add motion tracking for mouse-controlled light
        ECS.AddComponent<MotionTrackable>(MouseLightId);

        PrevMouse = mouse;
    }

    private int CreateLight(Vector2 position, float radius, Color color, Color emissive, float? z = null)
    {
        int id = ECS.CreateEntity();

        // Add all components first (each AddComponent moves entity to new archetype)
        ECS.AddComponent<Transform>(id);
        ECS.AddComponent<Circle2D>(id);
        ECS.AddComponent<Material>(id);

        // Now get fresh refs after entity is in final archetype
        ref var transform = ref ECS.GetComponent<Transform>(id);
        ref var circle = ref ECS.GetComponent<Circle2D>(id);
        ref var material = ref ECS.GetComponent<Material>(id);

        // Use entity ID as Z by default (unique, monotonic = newer always on top)
        transform.Position = new Vector3(position, z ?? id);
        transform.Rotation = Vector3.UnitX;

        material.Albedo = color;
        material.Emissive = emissive;

        circle.Radius = radius;

        return id;
    }

    public override void Update()
    {
        var keyboard = Keyboard.GetState();
        var mouse = Mouse.GetState();

        // Tab: toggle GI system
        if (keyboard.IsKeyDown(Keys.Tab) && PrevKeyboard.IsKeyUp(Keys.Tab))
            ToggleGISystem();

        UpdateGhosts();

        // Update mouse light (always on top) - convert screen coords to virtual world coords
        var mouseWorld = Renderer.ScreenToWorld(new Vector2(mouse.X, mouse.Y));
        ref var mouseTransform = ref ECS.GetComponent<Transform>(MouseLightId);
        mouseTransform.Position = new Vector3(mouseWorld, MouseLightZ);

        // Only allow spawning when window is focused
        if (Renderer.Window.IsActive)
        {
            var mousePos = mouseWorld;

            bool leftDown = mouse.LeftButton == ButtonState.Pressed;
            bool rightDown = mouse.RightButton == ButtonState.Pressed;

            // Left click: rainbow lights (blocked if right is also held)
            if (leftDown && !rightDown)
            {
                if (!HasLastPaintPos)
                {
                    PaintLightAt(mousePos);
                    LastPaintPos = mousePos;
                    HasLastPaintPos = true;
                }
                else
                {
                    float distance = Vector2.Distance(LastPaintPos, mousePos);
                    if (distance >= PaintSpacing)
                    {
                        Vector2 direction = Vector2.Normalize(mousePos - LastPaintPos);
                        float traveled = PaintSpacing;

                        while (traveled <= distance)
                        {
                            PaintLightAt(LastPaintPos + direction * traveled);
                            traveled += PaintSpacing;
                        }

                        LastPaintPos = mousePos;
                    }
                }
            }
            else
            {
                HasLastPaintPos = false;
            }

            // Right click: black dots (blocked if left is also held)
            if (rightDown && !leftDown)
            {
                if (!HasLastRightPaintPos)
                {
                    PaintBlackDotAt(mousePos);
                    LastRightPaintPos = mousePos;
                    HasLastRightPaintPos = true;
                }
                else
                {
                    float distance = Vector2.Distance(LastRightPaintPos, mousePos);
                    if (distance >= PaintSpacing)
                    {
                        Vector2 direction = Vector2.Normalize(mousePos - LastRightPaintPos);
                        float traveled = PaintSpacing;

                        while (traveled <= distance)
                        {
                            PaintBlackDotAt(LastRightPaintPos + direction * traveled);
                            traveled += PaintSpacing;
                        }

                        LastRightPaintPos = mousePos;
                    }
                }
            }
            else
            {
                HasLastRightPaintPos = false;
            }
        }

        // F11 to toggle UDR mode
        if (keyboard.IsKeyDown(Keys.F11) && PrevKeyboard.IsKeyUp(Keys.F11))
            ToggleUDRSystem();

        // X to spawn 10,000 random debug entities
        if (keyboard.IsKeyDown(Keys.X) && PrevKeyboard.IsKeyUp(Keys.X))
            SpawnDebugEntities(100_000);

        PrevKeyboard = keyboard;
        PrevMouse = mouse;

        Gizmos.Set("Scene", $"GI: {(UseHRCGI ? "HRCGI" : "RCGI")} [Tab]");
        Gizmos.Set("Scene", $"Upscaler: {GetUDRName()} [F11]");
        Gizmos.Set("Scene", "Left: Rainbow | Right: Black");
    }

    private void ToggleGISystem()
    {
        UseHRCGI = !UseHRCGI;

        if (UseHRCGI)
        {
            RCGISystem.Dispose();
            RCGISystem.Enabled = false;
            HRCGISystem.Initialize();
            HRCGISystem.Enabled = true;
        }
        else
        {
            HRCGISystem.Dispose();
            HRCGISystem.Enabled = false;
            RCGISystem.Initialize();
            RCGISystem.Enabled = true;
        }

        UpdateUDRInput();
    }

    private void UpdateUDRInput()
    {
        var inputSource = new Func<Texture2D>(() => UseHRCGI ? HRCGISystem.GetOutput() : RCGISystem.GetOutput());
        Bilinear.SetInputSource(inputSource);
        UDR1System.SetInputSource(inputSource);
        UDR2System.SetInputSource(inputSource);
        UDR3System.SetInputSource(inputSource);
    }

    private void ToggleUDRSystem()
    {
        // Disable current UDR system
        switch (UDRMode)
        {
            case 0:
                Bilinear.Dispose();
                Bilinear.Enabled = false;
                break;
            case 1:
                UDR1System.Dispose();
                UDR1System.Enabled = false;
                break;
            case 2:
                UDR2System.Dispose();
                UDR2System.Enabled = false;
                break;
            case 3:
                UDR3System.Dispose();
                UDR3System.Enabled = false;
                break;
        }

        // Cycle to next mode (0 = Bilinear, 1 = UDR1, 2 = UDR2, 3 = UDR3)
        UDRMode = (UDRMode + 1) % 4;

        // Enable new UDR system
        switch (UDRMode)
        {
            case 0:
                Bilinear.Initialize();
                Bilinear.Enabled = true;
                break;
            case 1:
                UDR1System.Initialize();
                UDR1System.Enabled = true;
                break;
            case 2:
                UDR2System.Initialize();
                UDR2System.Enabled = true;
                break;
            case 3:
                UDR3System.Initialize();
                UDR3System.Enabled = true;
                break;
        }

        UpdateUDRInput();
    }

    private string GetUDRName()
    {
        return UDRMode switch
        {
            0 => "Bilinear",
            1 => "UDR1 (Spatial)",
            2 => "UDR2 (Spatial + Temporal)",
            3 => "UDR3 (Lanczos + Temporal)",
            _ => "Unknown"
        };
    }

    private void PaintLightAt(Vector2 position)
    {
        var color = HueToRGB(RainbowHue);
        RainbowHue = (RainbowHue + HueSpeed) % 1f;

        var nearby = ECS.InRadius(new Vector3(position, 0), PaintRadius);
        foreach (int entityId in nearby)
        {
            if (ECS.HasComponent<Circle2D>(entityId) && entityId != MouseLightId)
            {
                ref var material = ref ECS.GetComponent<Material>(entityId);
                material.Albedo = color;
                material.Emissive = color;
                return;
            }
        }

        // Z defaults to entity ID (unique, newer = higher = on top)
        CreateLight(position, PaintRadius, color, color);
    }

    private void PaintBlackDotAt(Vector2 position)
    {
        var color = Color.Black;

        var nearby = ECS.InRadius(new Vector3(position, 0), PaintRadius);
        foreach (int entityId in nearby)
        {
            if (ECS.HasComponent<Circle2D>(entityId) && entityId != MouseLightId)
            {
                ref var material = ref ECS.GetComponent<Material>(entityId);
                material.Albedo = color;
                material.Emissive = color;
                return;
            }
        }

        CreateLight(position, PaintRadius, color, Color.Black);
    }

    private static Color HueToRGB(float hue)
    {
        float r = MathF.Abs(hue * 6f - 3f) - 1f;
        float g = 2f - MathF.Abs(hue * 6f - 2f);
        float b = 2f - MathF.Abs(hue * 6f - 4f);
        return new Color(
            (byte)(Math.Clamp(r, 0f, 1f) * 255),
            (byte)(Math.Clamp(g, 0f, 1f) * 255),
            (byte)(Math.Clamp(b, 0f, 1f) * 255)
        );
    }

    private void SpawnDebugEntities(int count)
    {
        var screen = Renderer.VirtualSize;

        for (int i = 0; i < count; i++)
        {
            float x = (float)Rng.NextDouble() * screen.X;
            float y = (float)Rng.NextDouble() * screen.Y;
            var color = HueToRGB((float)Rng.NextDouble());

            // Z defaults to entity ID (unique, newer = higher = on top)
            CreateLight(new Vector2(x, y), 3f, color, color);
        }
    }

    public override void Render()
    {
        base.Render();
    }

    public override void LateRender()
    {
        float sx = Renderer.ScreenWidth / Renderer.VirtualSize.X;
        float sy = Renderer.ScreenHeight / Renderer.VirtualSize.Y;
        float radius = 20f;
        float diameter = radius * 2f;

        Renderer.Reset()
            .Configure(BlendState.AlphaBlend)
            .SetTarget(null);

        for (int i = 0; i < GhostCount; i++)
        {
            ref var transform = ref ECS.GetComponent<Transform>(GhostIds[i]);
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
}
