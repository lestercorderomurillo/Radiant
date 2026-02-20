using System.Collections.Generic;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

[RunAfter(typeof(PacmanMazeBuilder))]
[RunBefore(typeof(Geometry))]
[SystemTag("Pacman")]
public class PacmanMazeRenderer : core.System
{
    private RenderTarget2D MazeEmissiveRT;
    private RenderTarget2D MazeAbsorptionRT;
    private PacmanMazeBuilder Maze;
    private Geometry Geometry;

    public override void Initialize()
    {
        Maze = Scene.ECS.GetSystem<PacmanMazeBuilder>();
        Geometry = Scene.ECS.GetSystem<Geometry>();
    }

    public void RenderMaze()
    {
        if (Maze == null || !Maze.HasGrid) return;
        DrawMazeToRenderTargets();
    }

    public void ClearRenderTargets()
    {
        MazeEmissiveRT?.Dispose();
        MazeEmissiveRT = null;
        MazeAbsorptionRT?.Dispose();
        MazeAbsorptionRT = null;
    }

    public override void Render()
    {
        if (Geometry == null) return;
        Geometry.BackgroundEmissive = MazeEmissiveRT;
        Geometry.BackgroundAbsorption = MazeAbsorptionRT;
    }

    public override void OnResize()
    {
        if (Maze != null && Maze.HasGrid) DrawMazeToRenderTargets();
    }

    public override void Dispose()
    {
        ClearRenderTargets();
    }

    private void DrawMazeToRenderTargets()
    {
        MazeEmissiveRT?.Dispose();
        MazeAbsorptionRT?.Dispose();
        MazeEmissiveRT = Renderer.CreateRenderTarget(Renderer.ScreenWidth, Renderer.ScreenHeight);
        MazeAbsorptionRT = Renderer.CreateRenderTarget(Renderer.ScreenWidth, Renderer.ScreenHeight);

        var pixel = Renderer.GetSolidTexture(Color.White);
        int cols = Maze.Cols;
        int rows = Maze.Rows;
        float cellSize = Maze.CellSize;
        float offsetX = Maze.OffsetX;
        float offsetY = Maze.OffsetY;
        float wallThickness = Maze.WallThickness;
        float wallMargin = Maze.WallMargin;

        float[] gridX = new float[cols + 1];
        float[] gridY = new float[rows + 1];
        for (int i = 0; i <= cols; i++) gridX[i] = offsetX + i * cellSize;
        for (int i = 0; i <= rows; i++) gridY[i] = offsetY + i * cellSize;

        var fills = new List<Rectangle>();
        var borders = new List<(Rectangle rect, Color color)>();

        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                if (!Maze.IsWall(x, y)) continue;

                float fx = gridX[x];
                float fy = gridY[y];
                float fxr = gridX[x + 1];
                float fyb = gridY[y + 1];

                bool cL = !Maze.IsWall(x - 1, y);
                bool cR = !Maze.IsWall(x + 1, y);
                bool cU = !Maze.IsWall(x, y - 1);
                bool cD = !Maze.IsWall(x, y + 1);

                float l = fx + (cL ? wallMargin : 0);
                float t = fy + (cU ? wallMargin : 0);
                float r = fxr - (cR ? wallMargin : 0);
                float b = fyb - (cD ? wallMargin : 0);

                bool cutTL = !cL && !cU && !Maze.IsWall(x - 1, y - 1);
                bool cutTR = !cR && !cU && !Maze.IsWall(x + 1, y - 1);
                bool cutBL = !cL && !cD && !Maze.IsWall(x - 1, y + 1);
                bool cutBR = !cR && !cD && !Maze.IsWall(x + 1, y + 1);

                if (!cutTL && !cutTR && !cutBL && !cutBR)
                {
                    fills.Add(Renderer.VirtualToScreenRect(l, t, r - l, b - t));
                }
                else
                {
                    float cy1 = fy + wallMargin;
                    float cy2 = fyb - wallMargin;
                    if (t < cy1)
                    {
                        float sl = cutTL ? fx + wallMargin : l;
                        float sr = cutTR ? fxr - wallMargin : r;
                        fills.Add(Renderer.VirtualToScreenRect(sl, t, sr - sl, cy1 - t));
                    }
                    if (cy1 < cy2)
                        fills.Add(Renderer.VirtualToScreenRect(l, cy1, r - l, cy2 - cy1));
                    if (cy2 < b)
                    {
                        float sl = cutBL ? fx + wallMargin : l;
                        float sr = cutBR ? fxr - wallMargin : r;
                        fills.Add(Renderer.VirtualToScreenRect(sl, cy2, sr - sl, b - cy2));
                    }
                }

                if (!cL && !cR && !cU && !cD && !cutTL && !cutTR && !cutBL && !cutBR) continue;
                Color color = Maze.GetWallColor(x, y);

                if (cU)
                    borders.Add((Renderer.VirtualToScreenRect(l, t - wallThickness, r - l, wallThickness), color));
                if (cD)
                    borders.Add((Renderer.VirtualToScreenRect(l, b, r - l, wallThickness), color));
                if (cL)
                {
                    float vt = cU ? t - wallThickness : t;
                    float vb = cD ? b + wallThickness : b;
                    borders.Add((Renderer.VirtualToScreenRect(l - wallThickness, vt, wallThickness, vb - vt), color));
                }
                if (cR)
                {
                    float vt = cU ? t - wallThickness : t;
                    float vb = cD ? b + wallThickness : b;
                    borders.Add((Renderer.VirtualToScreenRect(r, vt, wallThickness, vb - vt), color));
                }

                if (cutTL)
                {
                    borders.Add((Renderer.VirtualToScreenRect(fx, fy + wallMargin - wallThickness, wallMargin, wallThickness), color));
                    borders.Add((Renderer.VirtualToScreenRect(fx + wallMargin - wallThickness, fy, wallThickness, wallMargin - wallThickness), color));
                }
                if (cutTR)
                {
                    borders.Add((Renderer.VirtualToScreenRect(fxr - wallMargin, fy + wallMargin - wallThickness, wallMargin, wallThickness), color));
                    borders.Add((Renderer.VirtualToScreenRect(fxr - wallMargin, fy, wallThickness, wallMargin - wallThickness), color));
                }
                if (cutBL)
                {
                    borders.Add((Renderer.VirtualToScreenRect(fx, fyb - wallMargin, wallMargin, wallThickness), color));
                    borders.Add((Renderer.VirtualToScreenRect(fx + wallMargin - wallThickness, fyb - wallMargin + wallThickness, wallThickness, wallMargin - wallThickness), color));
                }
                if (cutBR)
                {
                    borders.Add((Renderer.VirtualToScreenRect(fxr - wallMargin, fyb - wallMargin, wallMargin, wallThickness), color));
                    borders.Add((Renderer.VirtualToScreenRect(fxr - wallMargin, fyb - wallMargin + wallThickness, wallThickness, wallMargin - wallThickness), color));
                }
            }

        Renderer.Reset().Configure(BlendState.Opaque).SetTarget(MazeAbsorptionRT).Clear(Color.Transparent);
        for (int i = 0; i < fills.Count; i++)
            Renderer.DrawTexture(pixel, fills[i], Color.White);
        for (int i = 0; i < borders.Count; i++)
            Renderer.DrawTexture(pixel, borders[i].rect, borders[i].color);
        Renderer.Commit();

        Renderer.Reset().Configure(BlendState.Opaque).SetTarget(MazeEmissiveRT).Clear(Color.Transparent);
        for (int i = 0; i < borders.Count; i++)
            Renderer.DrawTexture(pixel, borders[i].rect, borders[i].color);
        Renderer.Commit();

        Renderer.SetTarget(null);
    }
}
