using System;
using System.Collections.Generic;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

public class PacmanMazeBuilder : core.System
{
    public string[] Layout { get; set; }
    public float CellSize { get; set; } = 70f;
    public float WallThickness { get; set; } = 6f;
    public float WallMargin { get; set; } = 20f;
    public Color WallColor { get; set; } = new Color((byte)0, (byte)0, (byte)0, (byte)255);
    public Color WallLight { get; set; } = new Color(100, 180, 255);
    public Color[] WallColors { get; set; }
    public string[] Sections { get; set; }

    private RenderTarget2D MazeEmissiveRT;
    private RenderTarget2D MazeAbsorptionRT;

    public Dictionary<(int, int), int> CoinCells { get; private set; } = new();
    public Dictionary<(int, int), int> PowerPelletCells { get; private set; } = new();

    // Ghost house bounds (grid coordinates, inclusive). (-1,-1,-1,-1) = none.
    public (int left, int top, int right, int bottom) GhostHouse { get; set; } = (-1, -1, -1, -1);
    // Tiles where ghosts cannot turn upward in scatter/chase mode
    public (int x, int y)[] NoUpTiles { get; set; }

    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public float OffsetX { get; private set; }
    public float OffsetY { get; private set; }

    private bool[,] Grid;

    private int GetSection(int x, int y)
    {
        if (Sections == null || y < 0 || y >= Rows || x < 0 || x >= Cols) return 0;
        char c = Sections[y][x];
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'z') return c - 'a' + 10;
        return 0;
    }

    private Color GetWallColor(int x, int y)
    {
        if (WallColors == null || WallColors.Length == 0) return WallLight;
        int section = GetSection(x, y);
        return WallColors[section % WallColors.Length];
    }

    public bool IsWall(int x, int y)
    {
        if (x < 0 || x >= Cols || y < 0 || y >= Rows) return true;
        return Grid[x, y];
    }

    public bool CanMove(int cx, int cy, int dx, int dy)
    {
        int nx = cx + dx, ny = cy + dy;
        if (ny < 0 || ny >= Rows) return false;
        // Horizontal tunnel wrapping
        if (nx < 0) nx += Cols;
        else if (nx >= Cols) nx -= Cols;
        return !Grid[nx, ny];
    }

    public int WrapX(int x)
    {
        if (x < 0) return x + Cols;
        if (x >= Cols) return x - Cols;
        return x;
    }

    // Ghost house derived properties
    public int HouseDoorY => GhostHouse.top - 1;
    public int HouseDoorLeft => (GhostHouse.left + GhostHouse.right) / 2;
    public int HouseDoorRight => HouseDoorLeft + 1;

    public bool InGhostHouse(int x, int y)
    {
        var (l, t, r, b) = GhostHouse;
        return l >= 0 && x >= l && x <= r && y >= t && y <= b;
    }

    public bool IsGhostDoor(int x, int y) =>
        GhostHouse.left >= 0 && y == HouseDoorY && x >= HouseDoorLeft && x <= HouseDoorRight;

    public bool IsNoUpTile(int x, int y)
    {
        if (NoUpTiles == null) return false;
        for (int i = 0; i < NoUpTiles.Length; i++)
            if (NoUpTiles[i].x == x && NoUpTiles[i].y == y)
                return true;
        return false;
    }

    public Vector2 CellCenter(int cx, int cy) => new(
        OffsetX + cx * CellSize + CellSize / 2f,
        OffsetY + cy * CellSize + CellSize / 2f);

    public int[] SpawnCoins(float radius, Color emissive, float z)
    {
        var ecs = Scene.ECS;
        CoinCells.Clear();

        int count = 0;
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
                if (!Grid[x, y] && !InGhostHouse(x, y) && !IsGhostDoor(x, y))
                    count++;

        var ids = new int[count];
        int idx = 0;
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
            {
                if (Grid[x, y] || InGhostHouse(x, y) || IsGhostDoor(x, y)) continue;
                int id = LightFactory.CreateLight(ecs, CellCenter(x, y), radius,
                    emissive, emissive, z);
                ids[idx++] = id;
                CoinCells[(x, y)] = id;
            }

        return ids;
    }

    public bool TryCollectCoin(int cx, int cy)
    {
        if (!CoinCells.Remove((cx, cy), out int entityId)) return false;
        Scene.ECS.DestroyEntity(entityId);
        return true;
    }

    public void ClearCoins()
    {
        var ecs = Scene.ECS;
        foreach (var id in CoinCells.Values)
            ecs.DestroyEntity(id);
        CoinCells.Clear();
        foreach (var id in PowerPelletCells.Values)
            ecs.DestroyEntity(id);
        PowerPelletCells.Clear();
    }

    public void SpawnPowerPellets((int x, int y)[] positions, float radius, Color color, float z)
    {
        var ecs = Scene.ECS;
        PowerPelletCells.Clear();

        for (int i = 0; i < positions.Length; i++)
        {
            var (x, y) = positions[i];

            // Remove coin at this position if any
            if (CoinCells.Remove((x, y), out int coinId))
                ecs.DestroyEntity(coinId);

            int pelletId = LightFactory.CreateLight(ecs, CellCenter(x, y), radius, color, color, z);
            PowerPelletCells[(x, y)] = pelletId;
        }
    }

    public bool TryCollectPowerPellet(int cx, int cy)
    {
        if (!PowerPelletCells.Remove((cx, cy), out int entityId)) return false;
        Scene.ECS.DestroyEntity(entityId);
        return true;
    }

    public (int x, int y)[] FindCornerPelletPositions()
    {
        var corners = new (int tx, int ty)[]
        {
            (1, 1),
            (Cols - 2, 1),
            (1, Rows - 2),
            (Cols - 2, Rows - 2)
        };

        var result = new (int x, int y)[4];
        for (int c = 0; c < 4; c++)
        {
            float bestDist = float.MaxValue;
            result[c] = (Cols / 2, Rows / 2);

            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Cols; x++)
                {
                    if (IsWall(x, y) || InGhostHouse(x, y) || IsGhostDoor(x, y)) continue;
                    float dx = x - corners[c].tx;
                    float dy = y - corners[c].ty;
                    float dist = dx * dx + dy * dy;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        result[c] = (x, y);
                    }
                }
        }

        return result;
    }

    public int[] SpawnAtCells((int x, int y)[] cells, Color[] colors, float radius, float z,
        Texture2D texture = null)
    {
        var ecs = Scene.ECS;
        var ids = new int[cells.Length];

        for (int i = 0; i < cells.Length; i++)
        {
            var center = CellCenter(cells[i].x, cells[i].y);
            ids[i] = LightFactory.CreateLight(ecs, center, radius, colors[i], colors[i], z, texture);
            ecs.AddComponent<MotionTrackable>(ids[i]);
        }

        return ids;
    }

    public void BuildMaze()
    {
        Cols = Layout[0].Length;
        Rows = Layout.Length;

        float mazeW = Cols * CellSize;
        float mazeH = Rows * CellSize;
        var virt = Renderer.VirtualSize;
        OffsetX = (virt.X - mazeW) / 2f;
        OffsetY = (virt.Y - mazeH) / 2f;

        Grid = new bool[Cols, Rows];
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
                Grid[x, y] = Layout[y][x] == '1';

        DrawMazeToRenderTargets();
    }

    public void ClearMaze()
    {
        MazeEmissiveRT?.Dispose();
        MazeEmissiveRT = null;
        MazeAbsorptionRT?.Dispose();
        MazeAbsorptionRT = null;

        var geometry = Scene.ECS.GetSystem<Geometry>();
        if (geometry != null)
        {
            geometry.BackgroundEmissive = null;
            geometry.BackgroundAbsorption = null;
        }

        Grid = null;
    }

    public override void OnResize()
    {
        if (Grid != null) DrawMazeToRenderTargets();
    }

    public override void Dispose()
    {
        MazeEmissiveRT?.Dispose();
        MazeAbsorptionRT?.Dispose();
    }

    private void DrawMazeToRenderTargets()
    {
        float sx = Renderer.ScreenWidth / Renderer.VirtualSize.X;
        float sy = Renderer.ScreenHeight / Renderer.VirtualSize.Y;

        MazeEmissiveRT?.Dispose();
        MazeAbsorptionRT?.Dispose();
        MazeEmissiveRT = Renderer.CreateRenderTarget(Renderer.ScreenWidth, Renderer.ScreenHeight);
        MazeAbsorptionRT = Renderer.CreateRenderTarget(Renderer.ScreenWidth, Renderer.ScreenHeight);

        var pixel = Renderer.GetSolidTexture(Color.White);

        float[] gridX = new float[Cols + 1];
        float[] gridY = new float[Rows + 1];
        for (int i = 0; i <= Cols; i++) gridX[i] = OffsetX + i * CellSize;
        for (int i = 0; i <= Rows; i++) gridY[i] = OffsetY + i * CellSize;

        var fills = new List<Rectangle>();
        var borders = new List<(Rectangle rect, Color color)>();

        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
            {
                if (!Grid[x, y]) continue;

                float fx = gridX[x];
                float fy = gridY[y];
                float fxr = gridX[x + 1];
                float fyb = gridY[y + 1];

                bool cL = !IsWall(x - 1, y);
                bool cR = !IsWall(x + 1, y);
                bool cU = !IsWall(x, y - 1);
                bool cD = !IsWall(x, y + 1);

                float l = fx + (cL ? WallMargin : 0);
                float t = fy + (cU ? WallMargin : 0);
                float r = fxr - (cR ? WallMargin : 0);
                float b = fyb - (cD ? WallMargin : 0);

                bool cutTL = !cL && !cU && !IsWall(x - 1, y - 1);
                bool cutTR = !cR && !cU && !IsWall(x + 1, y - 1);
                bool cutBL = !cL && !cD && !IsWall(x - 1, y + 1);
                bool cutBR = !cR && !cD && !IsWall(x + 1, y + 1);

                if (!cutTL && !cutTR && !cutBL && !cutBR)
                {
                    fills.Add(ToScreen(l, t, r - l, b - t, sx, sy));
                }
                else
                {
                    float cy1 = fy + WallMargin;
                    float cy2 = fyb - WallMargin;
                    if (t < cy1)
                    {
                        float sl = cutTL ? fx + WallMargin : l;
                        float sr = cutTR ? fxr - WallMargin : r;
                        fills.Add(ToScreen(sl, t, sr - sl, cy1 - t, sx, sy));
                    }
                    if (cy1 < cy2)
                        fills.Add(ToScreen(l, cy1, r - l, cy2 - cy1, sx, sy));
                    if (cy2 < b)
                    {
                        float sl = cutBL ? fx + WallMargin : l;
                        float sr = cutBR ? fxr - WallMargin : r;
                        fills.Add(ToScreen(sl, cy2, sr - sl, b - cy2, sx, sy));
                    }
                }

                if (!cL && !cR && !cU && !cD && !cutTL && !cutTR && !cutBL && !cutBR) continue;
                Color color = GetWallColor(x, y);

                if (cU)
                    borders.Add((ToScreen(l, t - WallThickness, r - l, WallThickness, sx, sy), color));
                if (cD)
                    borders.Add((ToScreen(l, b, r - l, WallThickness, sx, sy), color));
                if (cL)
                {
                    float vt = cU ? t - WallThickness : t;
                    float vb = cD ? b + WallThickness : b;
                    borders.Add((ToScreen(l - WallThickness, vt, WallThickness, vb - vt, sx, sy), color));
                }
                if (cR)
                {
                    float vt = cU ? t - WallThickness : t;
                    float vb = cD ? b + WallThickness : b;
                    borders.Add((ToScreen(r, vt, WallThickness, vb - vt, sx, sy), color));
                }

                if (cutTL)
                {
                    borders.Add((ToScreen(fx, fy + WallMargin - WallThickness, WallMargin, WallThickness, sx, sy), color));
                    borders.Add((ToScreen(fx + WallMargin - WallThickness, fy, WallThickness, WallMargin - WallThickness, sx, sy), color));
                }
                if (cutTR)
                {
                    borders.Add((ToScreen(fxr - WallMargin, fy + WallMargin - WallThickness, WallMargin, WallThickness, sx, sy), color));
                    borders.Add((ToScreen(fxr - WallMargin, fy, WallThickness, WallMargin - WallThickness, sx, sy), color));
                }
                if (cutBL)
                {
                    borders.Add((ToScreen(fx, fyb - WallMargin, WallMargin, WallThickness, sx, sy), color));
                    borders.Add((ToScreen(fx + WallMargin - WallThickness, fyb - WallMargin + WallThickness, WallThickness, WallMargin - WallThickness, sx, sy), color));
                }
                if (cutBR)
                {
                    borders.Add((ToScreen(fxr - WallMargin, fyb - WallMargin, WallMargin, WallThickness, sx, sy), color));
                    borders.Add((ToScreen(fxr - WallMargin, fyb - WallMargin + WallThickness, WallThickness, WallMargin - WallThickness, sx, sy), color));
                }
            }

        // Draw absorption RT: fills (white = inverted black albedo) + borders (emissive color)
        Renderer.Reset().Configure(BlendState.Opaque).SetTarget(MazeAbsorptionRT).Clear(Color.Transparent);
        for (int i = 0; i < fills.Count; i++)
            Renderer.DrawTexture(pixel, fills[i], Color.White);
        for (int i = 0; i < borders.Count; i++)
            Renderer.DrawTexture(pixel, borders[i].rect, borders[i].color);
        Renderer.Commit();

        // Draw emissive RT: borders only
        Renderer.Reset().Configure(BlendState.Opaque).SetTarget(MazeEmissiveRT).Clear(Color.Transparent);
        for (int i = 0; i < borders.Count; i++)
            Renderer.DrawTexture(pixel, borders[i].rect, borders[i].color);
        Renderer.Commit();

        Renderer.SetTarget(null);

        var geometry = Scene.ECS.GetSystem<Geometry>();
        if (geometry != null)
        {
            geometry.BackgroundEmissive = MazeEmissiveRT;
            geometry.BackgroundAbsorption = MazeAbsorptionRT;
        }
    }

    private static Rectangle ToScreen(float vx, float vy, float vw, float vh, float sx, float sy)
    {
        int px = (int)MathF.Round(vx * sx);
        int py = (int)MathF.Round(vy * sy);
        int pr = (int)MathF.Round((vx + vw) * sx);
        int pb = (int)MathF.Round((vy + vh) * sy);
        return new Rectangle(px, py, pr - px, pb - py);
    }
}
