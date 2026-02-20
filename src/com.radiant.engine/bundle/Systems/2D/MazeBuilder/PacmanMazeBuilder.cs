using System.Collections.Generic;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

[SystemTag("Pacman")]
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
    public bool HasGrid => Grid != null;

    private bool[,] Grid;

    private int GetSection(int x, int y)
    {
        if (Sections == null || y < 0 || y >= Rows || x < 0 || x >= Cols) return 0;
        char c = Sections[y][x];
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'z') return c - 'a' + 10;
        return 0;
    }

    public Color GetWallColor(int x, int y)
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

    public int[] SpawnCoins(float radius, Color emissive, float z, (int x, int y)? exclude = null)
    {
        var ecs = Scene.ECS;
        CoinCells.Clear();

        int count = 0;
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
                if (!Grid[x, y] && !InGhostHouse(x, y) && !IsGhostDoor(x, y)
                    && !(exclude.HasValue && exclude.Value.x == x && exclude.Value.y == y))
                    count++;

        var ids = new int[count];
        int idx = 0;
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
            {
                if (Grid[x, y] || InGhostHouse(x, y) || IsGhostDoor(x, y)) continue;
                if (exclude.HasValue && exclude.Value.x == x && exclude.Value.y == y) continue;
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
    }

    public void ClearMaze()
    {
        Grid = null;
    }
}
