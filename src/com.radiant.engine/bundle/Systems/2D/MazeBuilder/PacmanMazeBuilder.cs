using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace com.radiant.engine.bundle;

public class PacmanMazeBuilder : core.System
{
    public string[] Layout { get; set; }
    public float CellSize { get; set; } = 70f;
    public float WallThickness { get; set; } = 14f;
    public float WallMargin { get; set; } = 20f;
    public Color WallColor { get; set; } = new Color((byte)0, (byte)0, (byte)0, (byte)255);
    public Color WallLight { get; set; } = new Color(100, 180, 255);
    public Color[] WallColors { get; set; }
    public string[] Sections { get; set; }

    public List<int> BorderIds { get; private set; } = new();

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
                ids[idx++] = LightFactory.CreateLight(ecs, CellCenter(x, y), radius,
                    emissive, emissive, z);
            }

        return ids;
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

        float ox = OffsetX;
        float oy = OffsetY;

        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
            {
                if (!Grid[x, y]) continue;

                float fx = ox + x * CellSize;
                float fy = oy + y * CellSize;

                bool cL = !IsWall(x - 1, y);
                bool cR = !IsWall(x + 1, y);
                bool cU = !IsWall(x, y - 1);
                bool cD = !IsWall(x, y + 1);

                float l = fx + (cL ? WallMargin : 0);
                float t = fy + (cU ? WallMargin : 0);
                float r = fx + CellSize - (cR ? WallMargin : 0);
                float b = fy + CellSize - (cD ? WallMargin : 0);

                bool cutTL = !cL && !cU && !IsWall(x - 1, y - 1);
                bool cutTR = !cR && !cU && !IsWall(x + 1, y - 1);
                bool cutBL = !cL && !cD && !IsWall(x - 1, y + 1);
                bool cutBR = !cR && !cD && !IsWall(x + 1, y + 1);

                if (!cutTL && !cutTR && !cutBL && !cutBR)
                {
                    CreateWall(new Vector2(l, t), new Vector2(r - l, b - t));
                }
                else
                {
                    float cy1 = fy + WallMargin;
                    float cy2 = fy + CellSize - WallMargin;
                    if (t < cy1)
                    {
                        float sl = cutTL ? fx + WallMargin : l;
                        float sr = cutTR ? fx + CellSize - WallMargin : r;
                        CreateWall(new Vector2(sl, t), new Vector2(sr - sl, cy1 - t));
                    }
                    if (cy1 < cy2)
                        CreateWall(new Vector2(l, cy1), new Vector2(r - l, cy2 - cy1));
                    if (cy2 < b)
                    {
                        float sl = cutBL ? fx + WallMargin : l;
                        float sr = cutBR ? fx + CellSize - WallMargin : r;
                        CreateWall(new Vector2(sl, cy2), new Vector2(sr - sl, b - cy2));
                    }
                }

                if (!cL && !cR && !cU && !cD && !cutTL && !cutTR && !cutBL && !cutBR) continue;
                Color color = GetWallColor(x, y);

                if (cU)
                    CreateEmissiveBorder(new Vector2(l, t - WallThickness), new Vector2(r - l, WallThickness), color);
                if (cD)
                    CreateEmissiveBorder(new Vector2(l, b), new Vector2(r - l, WallThickness), color);
                if (cL)
                {
                    float vt = cU ? t - WallThickness : t;
                    float vb = cD ? b + WallThickness : b;
                    CreateEmissiveBorder(new Vector2(l - WallThickness, vt), new Vector2(WallThickness, vb - vt), color);
                }
                if (cR)
                {
                    float vt = cU ? t - WallThickness : t;
                    float vb = cD ? b + WallThickness : b;
                    CreateEmissiveBorder(new Vector2(r, vt), new Vector2(WallThickness, vb - vt), color);
                }

                if (cutTL)
                {
                    CreateEmissiveBorder(new Vector2(fx, fy + WallMargin - WallThickness), new Vector2(WallMargin, WallThickness), color);
                    CreateEmissiveBorder(new Vector2(fx + WallMargin - WallThickness, fy), new Vector2(WallThickness, WallMargin - WallThickness), color);
                }
                if (cutTR)
                {
                    CreateEmissiveBorder(new Vector2(fx + CellSize - WallMargin, fy + WallMargin - WallThickness), new Vector2(WallMargin, WallThickness), color);
                    CreateEmissiveBorder(new Vector2(fx + CellSize - WallMargin, fy), new Vector2(WallThickness, WallMargin - WallThickness), color);
                }
                if (cutBL)
                {
                    CreateEmissiveBorder(new Vector2(fx, fy + CellSize - WallMargin), new Vector2(WallMargin, WallThickness), color);
                    CreateEmissiveBorder(new Vector2(fx + WallMargin - WallThickness, fy + CellSize - WallMargin + WallThickness), new Vector2(WallThickness, WallMargin - WallThickness), color);
                }
                if (cutBR)
                {
                    CreateEmissiveBorder(new Vector2(fx + CellSize - WallMargin, fy + CellSize - WallMargin), new Vector2(WallMargin, WallThickness), color);
                    CreateEmissiveBorder(new Vector2(fx + CellSize - WallMargin, fy + CellSize - WallMargin + WallThickness), new Vector2(WallThickness, WallMargin - WallThickness), color);
                }
            }
    }

    private void CreateWall(Vector2 position, Vector2 size)
    {
        var ecs = Scene.ECS;
        int id = ecs.CreateEntity();
        ecs.AddComponent<Transform>(id);
        ecs.AddComponent<Rectangle2D>(id);
        ecs.AddComponent<Material>(id);

        ref var transform = ref ecs.GetComponent<Transform>(id);
        ref var rect = ref ecs.GetComponent<Rectangle2D>(id);
        ref var material = ref ecs.GetComponent<Material>(id);

        transform.Position = new Vector3(position, 0f);
        transform.Rotation = Vector3.UnitX;
        rect.Size = size;

        material.Albedo = WallColor;
        material.Emissive = Color.Transparent;
    }

    private void CreateEmissiveBorder(Vector2 position, Vector2 size, Color emissive)
    {
        var ecs = Scene.ECS;
        int id = ecs.CreateEntity();
        ecs.AddComponent<Transform>(id);
        ecs.AddComponent<Rectangle2D>(id);
        ecs.AddComponent<Material>(id);

        ref var transform = ref ecs.GetComponent<Transform>(id);
        ref var rect = ref ecs.GetComponent<Rectangle2D>(id);
        ref var material = ref ecs.GetComponent<Material>(id);

        transform.Position = new Vector3(position, 0f);
        transform.Rotation = Vector3.UnitX;
        rect.Size = size;

        material.Albedo = Color.Black;
        material.Emissive = emissive;

        BorderIds.Add(id);
    }
}
