using com.radiant.engine.bundle;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;

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
    private const float CellSize = 150f;
    private const float WallThickness = 20f;
    private const int MazeCols = 22;
    private const int MazeRows = 12;
    private float MazeOffsetX, MazeOffsetY;

    // Maze connectivity (stored for ghost navigation)
    private bool[,] MazeHWalls; // horizontal walls [cols, rows+1]
    private bool[,] MazeVWalls; // vertical walls [cols+1, rows]

    // Ghost wandering
    private const int GhostCount = 4;
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

        CreateMaze();
        CreateGhosts();
        CreateMouseLight();
        UpdateUDRInput();

        base.SetupScene();
    }

    private Vector2 CellCenter(int cx, int cy) => new(
        MazeOffsetX + cx * (CellSize + WallThickness) + WallThickness + CellSize / 2f,
        MazeOffsetY + cy * (CellSize + WallThickness) + WallThickness + CellSize / 2f);

    private void CreateGhosts()
    {
        float radius = 40f;
        int qx = MazeCols / 4;
        int qy = MazeRows / 4;

        var ghostColors = new Color[]
        {
            new(255, 0, 0),     // Blinky (red)
            new(255, 184, 255), // Pinky (pink)
            new(0, 255, 255),   // Inky (cyan)
            new(255, 184, 82),  // Clyde (orange)
        };

        (int x, int y)[] startCells =
        {
            (qx, qy),
            (MazeCols - 1 - qx, qy),
            (qx, MazeRows - 1 - qy),
            (MazeCols - 1 - qx, MazeRows - 1 - qy),
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

        // Check wall between (cx,cy) and (nx,ny)
        if (dy == -1) return !MazeHWalls[cx, cy];       // up
        if (dx == 1)  return !MazeVWalls[cx + 1, cy];   // right
        if (dy == 1)  return !MazeHWalls[cx, cy + 1];   // down
        if (dx == -1) return !MazeVWalls[cx, cy];        // left
        return false;
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
        float mazeW = MazeCols * CellSize + (MazeCols + 1) * WallThickness;
        float mazeH = MazeRows * CellSize + (MazeRows + 1) * WallThickness;
        var virt = Renderer.VirtualSize;
        MazeOffsetX = (virt.X - mazeW) / 2f;
        MazeOffsetY = (virt.Y - mazeH) / 2f;
        float ox = MazeOffsetX;
        float oy = MazeOffsetY;

        // Generate maze with DFS (recursive backtracker)
        bool[,] visited = new bool[MazeCols, MazeRows];
        MazeHWalls = new bool[MazeCols, MazeRows + 1];
        MazeVWalls = new bool[MazeCols + 1, MazeRows];
        var hWalls = MazeHWalls;
        var vWalls = MazeVWalls;

        // All walls start present
        for (int x = 0; x < MazeCols; x++)
            for (int y = 0; y <= MazeRows; y++)
                hWalls[x, y] = true;
        for (int x = 0; x <= MazeCols; x++)
            for (int y = 0; y < MazeRows; y++)
                vWalls[x, y] = true;

        // DFS carve passages
        var stack = new Stack<(int x, int y)>();
        visited[0, 0] = true;
        stack.Push((0, 0));

        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { -1, 0, 1, 0 };

        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Peek();

            // Find unvisited neighbors
            int start = Rng.Next(4);
            bool found = false;
            for (int i = 0; i < 4; i++)
            {
                int d = (start + i) % 4;
                int nx = cx + dx[d];
                int ny = cy + dy[d];

                if (nx >= 0 && nx < MazeCols && ny >= 0 && ny < MazeRows && !visited[nx, ny])
                {
                    visited[nx, ny] = true;

                    // Remove wall between (cx,cy) and (nx,ny)
                    switch (d)
                    {
                        case 0: hWalls[cx, cy] = false; break;     // top
                        case 1: vWalls[cx + 1, cy] = false; break; // right
                        case 2: hWalls[cx, cy + 1] = false; break; // bottom
                        case 3: vWalls[cx, cy] = false; break;     // left
                    }

                    stack.Push((nx, ny));
                    found = true;
                    break;
                }
            }

            if (!found) stack.Pop();
        }

        // Corner posts at every grid intersection (always present)
        for (int gx = 0; gx <= MazeCols; gx++)
            for (int gy = 0; gy <= MazeRows; gy++)
                CreateWall(
                    new Vector2(ox + gx * (CellSize + WallThickness), oy + gy * (CellSize + WallThickness)),
                    new Vector2(WallThickness, WallThickness));

        // Horizontal wall segments (between adjacent posts, CellSize wide)
        for (int x = 0; x < MazeCols; x++)
            for (int y = 0; y <= MazeRows; y++)
                if (hWalls[x, y])
                    CreateWall(
                        new Vector2(ox + x * (CellSize + WallThickness) + WallThickness, oy + y * (CellSize + WallThickness)),
                        new Vector2(CellSize, WallThickness));

        // Vertical wall segments (between adjacent posts, CellSize tall)
        for (int x = 0; x <= MazeCols; x++)
            for (int y = 0; y < MazeRows; y++)
                if (vWalls[x, y])
                    CreateWall(
                        new Vector2(ox + x * (CellSize + WallThickness), oy + y * (CellSize + WallThickness) + WallThickness),
                        new Vector2(WallThickness, CellSize));
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
}
