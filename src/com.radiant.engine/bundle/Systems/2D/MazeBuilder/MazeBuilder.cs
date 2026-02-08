using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

public class MazeBuilder : core.System
{
    public string[] Layout { get; set; }
    public float CellSize { get; set; } = 70f;
    public float WallThickness { get; set; } = 14f;
    public float WallMargin { get; set; } = 20f;
    public Color WallColor { get; set; } = new Color((byte)0, (byte)0, (byte)0, (byte)255);
    public Color WallLight { get; set; } = new Color(100, 180, 255);
    public Color[] WallColors { get; set; }
    public string[] Sections { get; set; }

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

        float hw = WallThickness / 2f;
        float ox = OffsetX;
        float oy = OffsetY;

        FillWallCells(ox, oy);
        CreateEmissiveBorders(hw, ox, oy);
    }

    private void FillWallCells(float ox, float oy)
    {
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
                if (Grid[x, y])
                    CreateWall(
                        new Vector2(ox + x * CellSize, oy + y * CellSize),
                        new Vector2(CellSize, CellSize));
    }

    private void CreateEmissiveBorders(float hw, float ox, float oy)
    {
        // Horizontal edges — own the corners, no overlap
        for (int gy = 0; gy <= Rows; gy++)
            for (int gx = 0; gx < Cols; gx++)
            {
                bool above = IsWall(gx, gy - 1);
                bool below = IsWall(gx, gy);
                if (above == below) continue;
                int wx = gx, wy = above ? gy - 1 : gy;
                float sy = above
                    ? oy + gy * CellSize
                    : oy + gy * CellSize - WallThickness;
                CreateEmissiveBorder(
                    new Vector2(ox + gx * CellSize, sy),
                    new Vector2(CellSize, WallThickness),
                    GetWallColor(wx, wy));
            }

        // Vertical edges — trimmed at ends where horizontal borders exist
        for (int gx = 0; gx <= Cols; gx++)
            for (int gy = 0; gy < Rows; gy++)
            {
                bool left = IsWall(gx - 1, gy);
                bool right = IsWall(gx, gy);
                if (left == right) continue;
                int wx = left ? gx - 1 : gx, wy = gy;
                int cx = left ? gx : gx - 1;
                float sx = left
                    ? ox + gx * CellSize
                    : ox + gx * CellSize - WallThickness;

                float top = oy + gy * CellSize;
                float bot = oy + (gy + 1) * CellSize;
                if (IsWall(cx, gy - 1)) top += WallThickness;
                if (IsWall(cx, gy + 1)) bot -= WallThickness;
                if (bot <= top) continue;

                CreateEmissiveBorder(
                    new Vector2(sx, top),
                    new Vector2(WallThickness, bot - top),
                    GetWallColor(wx, wy));
            }

        // Outer corner posts — only where exactly 1 cell is wall (no overlap)
        for (int gy = 0; gy <= Rows; gy++)
            for (int gx = 0; gx <= Cols; gx++)
            {
                bool tl = IsWall(gx - 1, gy - 1), tr = IsWall(gx, gy - 1);
                bool bl = IsWall(gx - 1, gy), br = IsWall(gx, gy);
                int w = (tl ? 1 : 0) + (tr ? 1 : 0) + (bl ? 1 : 0) + (br ? 1 : 0);
                if (w != 1) continue;

                float px, py;
                int wx, wy;
                if (tl)      { px = ox + gx * CellSize;                py = oy + gy * CellSize;                wx = gx - 1; wy = gy - 1; }
                else if (tr) { px = ox + gx * CellSize - WallThickness; py = oy + gy * CellSize;                wx = gx;     wy = gy - 1; }
                else if (bl) { px = ox + gx * CellSize;                py = oy + gy * CellSize - WallThickness; wx = gx - 1; wy = gy;     }
                else         { px = ox + gx * CellSize - WallThickness; py = oy + gy * CellSize - WallThickness; wx = gx;     wy = gy;     }

                CreateEmissiveBorder(
                    new Vector2(px, py),
                    new Vector2(WallThickness, WallThickness),
                    GetWallColor(wx, wy));
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

        material.Albedo = Color.Transparent;
        material.Emissive = emissive;
    }
}
