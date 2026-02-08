using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public class MazeBuilder : core.System
{
    public string[] Layout { get; set; }
    public float CellSize { get; set; } = 70f;
    public float WallThickness { get; set; } = 14f;
    public float WallMargin { get; set; } = 20f;

    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public float OffsetX { get; private set; }
    public float OffsetY { get; private set; }

    private bool[,] Grid;

    public bool IsWall(int x, int y)
    {
        if (x < 0 || x >= Cols || y < 0 || y >= Rows) return true;
        return Grid[x, y];
    }

    public bool CanMove(int cx, int cy, int dx, int dy)
    {
        int nx = cx + dx, ny = cy + dy;
        if (nx < 0 || nx >= Cols || ny < 0 || ny >= Rows) return false;
        return !Grid[nx, ny];
    }

    public Vector2 CellCenter(int cx, int cy) => new(
        OffsetX + cx * CellSize + CellSize / 2f,
        OffsetY + cy * CellSize + CellSize / 2f);

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

        CreateCornerPosts(hw, ox, oy);
        CreateHorizontalSegments(hw, ox, oy);
        CreateVerticalSegments(hw, ox, oy);
    }

    private void CreateCornerPosts(float hw, float ox, float oy)
    {
        for (int gy = 0; gy <= Rows; gy++)
            for (int gx = 0; gx <= Cols; gx++)
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
    }

    private void CreateHorizontalSegments(float hw, float ox, float oy)
    {
        for (int gy = 0; gy <= Rows; gy++)
            for (int gx = 0; gx < Cols; gx++)
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
    }

    private void CreateVerticalSegments(float hw, float ox, float oy)
    {
        for (int gx = 0; gx <= Cols; gx++)
            for (int gy = 0; gy < Rows; gy++)
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

    private Vector2 PostPosition(int gx, int gy)
    {
        bool tl = IsWall(gx - 1, gy - 1), tr = IsWall(gx, gy - 1);
        bool bl = IsWall(gx - 1, gy), br = IsWall(gx, gy);

        int lw = (tl ? 1 : 0) + (bl ? 1 : 0);
        int rw = (tr ? 1 : 0) + (br ? 1 : 0);
        int tw = (tl ? 1 : 0) + (tr ? 1 : 0);
        int bw = (bl ? 1 : 0) + (br ? 1 : 0);

        float px = OffsetX + gx * CellSize;
        if (lw > rw) px -= WallMargin;
        else if (rw > lw) px += WallMargin;

        float py = OffsetY + gy * CellSize;
        if (tw > bw) py -= WallMargin;
        else if (bw > tw) py += WallMargin;

        return new Vector2(px, py);
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

        material.Albedo = new Color((byte)0, (byte)0, (byte)0, (byte)255);
        material.Emissive = Color.Transparent;
    }
}
