using System;
using System.Diagnostics;
using System.Threading;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

public class Tileset : core.System
{
    private int WorldWidth;
    private int WorldHeight;
    private int TileSize;

    private float LightFalloff = 1.0f / 10;
    private Vector3 MinLight = new Vector3(0.0f, 0.0f, 0.0f);
    private Vector3 SunColor = new Vector3(1.0f, 1.0f, 1.00f);

    private Vector2 CameraPosition = Vector2.Zero;
    private float CameraSpeed = 1000f;
    private int Padding = -18;

    private MouseState CurrentMouseState;
    private int BrushSize = 1;
    private Point LastModifiedTile = new Point(-999, -999);
    private bool LastActionWasPlace = false;

    private RenderTarget2D TileRenderTarget;
    private Texture2D LightTexture;
    private Color[] LightData;

    private int GridWidth;
    private int GridHeight;
    private int ViewportTilesX;
    private int ViewportTilesY;
    private int ViewportWidth;
    private int ViewportHeight;

    private BlendState MultiplyBlend;
    private Color SkyColor = new Color(135, 206, 235);
    private int[] SkyLevels;
    private int[] SkyLevelsLighting;
    private readonly object SkyLock = new object();

    private Thread LightingThread;
    private volatile bool IsRunning = true;
    private readonly object SwapLock = new object();

    private Vector3[] WorkingBuffer;
    private Vector3[] ReadyBuffer;
    private Vector3[] DisplayBuffer;

    private int WorkingTileX, WorkingTileY;
    private int ReadyTileX, ReadyTileY;
    private int DisplayTileX, DisplayTileY;

    private volatile bool NewBufferReady = false;
    private volatile bool IsWorldLoaded = false;

    private const int LightingIntervalMs = 50;

    private int WorldToTile(float worldCoord)
    {
        return (int)Math.Floor(worldCoord / TileSize);
    }

    private float TileToWorld(int tile)
    {
        return tile * TileSize;
    }

    public override void Initialize()
    {
        base.Initialize();

        MultiplyBlend = new BlendState
        {
            ColorBlendFunction = BlendFunction.Add,
            ColorSourceBlend = Blend.DestinationColor,
            ColorDestinationBlend = Blend.Zero,
            AlphaBlendFunction = BlendFunction.Add,
            AlphaSourceBlend = Blend.DestinationAlpha,
            AlphaDestinationBlend = Blend.Zero
        };
    }

    public void LoadWorld(WorldGen worldGen)
    {
        WorldWidth = worldGen.WorldWidth;
        WorldHeight = worldGen.WorldHeight;
        TileSize = worldGen.TileSize;

        SkyLevels = new int[WorldWidth];
        SkyLevelsLighting = new int[WorldWidth];

        CalculateSkyLevels();

        ViewportWidth = Renderer.Device.Viewport.Width;
        ViewportHeight = Renderer.Device.Viewport.Height;

        // Viewport size in tiles (ceiling + 1 for partial tiles)
        ViewportTilesX = (ViewportWidth + TileSize - 1) / TileSize + 1;
        ViewportTilesY = (ViewportHeight + TileSize - 1) / TileSize + 1;

        // Grid = viewport + padding on all sides
        GridWidth = ViewportTilesX + (Padding * 2);
        GridHeight = ViewportTilesY + (Padding * 2);

        // Render target sized to fit entire grid (including padding)
        int renderTargetWidth = GridWidth * TileSize;
        int renderTargetHeight = GridHeight * TileSize;
        TileRenderTarget = new RenderTarget2D(Renderer.Device, renderTargetWidth, renderTargetHeight);

        LightTexture = new Texture2D(Renderer.Device, GridWidth, GridHeight);
        LightData = new Color[GridWidth * GridHeight];
        WorkingBuffer = new Vector3[GridWidth * GridHeight];
        ReadyBuffer = new Vector3[GridWidth * GridHeight];
        DisplayBuffer = new Vector3[GridWidth * GridHeight];

        IsWorldLoaded = true;

        LightingThread = new Thread(ProcessLighting) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
        LightingThread.Start();
    }

    private void ProcessLighting()
    {
        Stopwatch timer = Stopwatch.StartNew();

        while (IsRunning)
        {
            if (!IsWorldLoaded)
            {
                Thread.Sleep(10);
                continue;
            }

            long startTick = timer.ElapsedMilliseconds;

            Vector2 cam;
            lock (SwapLock) { cam = CameraPosition; }

            lock (SkyLock) { Array.Copy(SkyLevels, SkyLevelsLighting, WorldWidth); }

            int camTileX = WorldToTile(cam.X);
            int camTileY = WorldToTile(cam.Y);
            int startX = camTileX - Padding;
            int startY = camTileY - Padding;

            for (int gx = 0; gx < GridWidth; gx++)
            {
                int worldTileX = startX + gx;
                for (int gy = 0; gy < GridHeight; gy++)
                {
                    int worldTileY = startY + gy;
                    int idx = gy * GridWidth + gx;

                    if (worldTileX < 0 || worldTileX >= WorldWidth || worldTileY < 0 || worldTileY >= WorldHeight)
                    {
                        WorkingBuffer[idx] = MinLight;
                        continue;
                    }

                    bool isSky = worldTileY < SkyLevelsLighting[worldTileX];
                    Vector3 initialLight = isSky ? SunColor : MinLight;

                    var (isSrc, sCol, sIns) = GetLightSource(worldTileX, worldTileY);
                    if (isSrc) initialLight = Vector3.Max(initialLight, sCol * sIns);
                    if (isSky || isSrc)
                        WorkingBuffer[idx] = initialLight.Add(LightFalloff);
                    else
                        WorkingBuffer[idx] = initialLight;
                }
            }

            for (int p = 0; p < 2; p++)
            {
                for (int gy = 0; gy < GridHeight; gy++)
                    for (int gx = 0; gx < GridWidth; gx++)
                        Propagate(gx, gy, startX, startY);

                for (int gy = GridHeight - 1; gy >= 0; gy--)
                    for (int gx = GridWidth - 1; gx >= 0; gx--)
                        Propagate(gx, gy, startX, startY);
            }

            WorkingTileX = startX;
            WorkingTileY = startY;

            lock (SwapLock)
            {
                var temp = ReadyBuffer;
                ReadyBuffer = WorkingBuffer;
                WorkingBuffer = temp;

                ReadyTileX = WorkingTileX;
                ReadyTileY = WorkingTileY;
                NewBufferReady = true;
            }

            long elapsed = timer.ElapsedMilliseconds - startTick;
            int sleepTime = LightingIntervalMs - (int)elapsed;
            if (sleepTime > 0) Thread.Sleep(sleepTime);
        }
    }

    private void Propagate(int gx, int gy, int startX, int startY)
    {
        int idx = gy * GridWidth + gx;

        int worldTileX = startX + gx;
        int worldTileY = startY + gy;

        bool isAir = GetTileType(worldTileX, worldTileY, 0).Id == TileType.Air.Id;
        float decay = isAir ? LightFalloff * 0.25f : LightFalloff;

        Vector3 decayVec = new Vector3(decay);
        Vector3 current = WorkingBuffer[idx];

        if (gx > 0) current = Vector3.Max(current, WorkingBuffer[idx - 1] - decayVec);
        if (gx < GridWidth - 1) current = Vector3.Max(current, WorkingBuffer[idx + 1] - decayVec);
        if (gy > 0) current = Vector3.Max(current, WorkingBuffer[idx - GridWidth] - decayVec);
        if (gy < GridHeight - 1) current = Vector3.Max(current, WorkingBuffer[idx + GridWidth] - decayVec);

        WorkingBuffer[idx] = Vector3.Max(current, MinLight);
    }

    private void UpdateLightTexture()
    {
        if (!NewBufferReady) return;

        lock (SwapLock)
        {
            var temp = DisplayBuffer;
            DisplayBuffer = ReadyBuffer;
            ReadyBuffer = temp;

            DisplayTileX = ReadyTileX;
            DisplayTileY = ReadyTileY;
            NewBufferReady = false;
        }

        for (int i = 0; i < DisplayBuffer.Length; i++)
        {
            Vector3 l = DisplayBuffer[i];
            LightData[i] = new Color(l.X, l.Y, l.Z);
        }
        LightTexture.SetData(LightData);
    }

    public override void Update()
    {
        if (!IsWorldLoaded) return;

        HandleCameraInput();
        HandleMouseInput();
        UpdateLightTexture();
        UpdateMaterials();
    }

    public override void Render()
    {
        if (!IsWorldLoaded) return;

        // === GET LIGHT GRID POSITION (this is our authoritative grid origin) ===
        int gridStartX, gridStartY;
        lock (SwapLock)
        {
            gridStartX = DisplayTileX;
            gridStartY = DisplayTileY;
        }

        // === CAMERA CALCULATIONS ===
        // Grid origin in world space
        float gridOriginX = TileToWorld(gridStartX);
        float gridOriginY = TileToWorld(gridStartY);

        // Camera offset from grid origin (for smooth scrolling)
        float cameraOffsetX = CameraPosition.X - gridOriginX;
        float cameraOffsetY = CameraPosition.Y - gridOriginY;

        // === RENDER TO TARGET (full grid size) ===
        Renderer.Device.SetRenderTarget(TileRenderTarget);
        Renderer.Device.Clear(SkyColor);

        // === RENDER TILES ===
        Vector3 bMin = new Vector3(gridOriginX, gridOriginY, 0);
        Vector3 bMax = new Vector3(gridOriginX + (GridWidth * TileSize), gridOriginY + (GridHeight * TileSize), 1);

        Renderer.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

        foreach (int id in Scene.ECS.InBox(bMin, bMax))
        {
            if (!Scene.ECS.HasComponent<TileData>(id)) continue;
            ref var m = ref Scene.ECS.GetComponent<Material>(id);
            if (m.Albedo.A == 0) continue;

            ref var t = ref Scene.ECS.GetComponent<Transform>(id);
            ref var r = ref Scene.ECS.GetComponent<Rectangle2D>(id);

            // World to render target: subtract grid origin
            int rtX = (int)(t.Position.X - gridOriginX);
            int rtY = (int)(t.Position.Y - gridOriginY);

            Rectangle rect = new Rectangle(rtX, rtY, (int)r.Size.X, (int)r.Size.Y);
            Renderer.SpriteBatch.Draw(Renderer.GetSolidTexture(Color.White), rect, m.Albedo);
        }
        
        Renderer.SpriteBatch.End();

        // === RENDER LIGHT (always at 0,0 since grid origin matches light origin) ===
        Renderer.SpriteBatch.Begin(SpriteSortMode.Deferred, MultiplyBlend, SamplerState.LinearClamp);

        Rectangle destRect = new Rectangle(0, 0, GridWidth * TileSize, GridHeight * TileSize);
        Renderer.SpriteBatch.Draw(LightTexture, destRect, Color.White);

        Renderer.SpriteBatch.End();

        // === FINAL OUTPUT TO SCREEN ===
        Renderer.Device.SetRenderTarget(null);
        Renderer.Device.Clear(Color.Red);

        Renderer.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp);

        int rtWidth = GridWidth * TileSize;
        int rtHeight = GridHeight * TileSize;

        // Camera offset from grid origin (NO padding added - it's already included)
        int srcX = (int)Math.Floor(cameraOffsetX);
        int srcY = (int)Math.Floor(cameraOffsetY);
        int srcW = ViewportWidth;
        int srcH = ViewportHeight;

        int dstX = 0;
        int dstY = 0;

        // Clamp left edge
        if (srcX < 0)
        {
            dstX = -srcX;
            srcW += srcX;
            srcX = 0;
        }

        // Clamp top edge
        if (srcY < 0)
        {
            dstY = -srcY;
            srcH += srcY;
            srcY = 0;
        }

        // Clamp right edge
        if (srcX + srcW > rtWidth)
        {
            srcW = rtWidth - srcX;
        }

        // Clamp bottom edge
        if (srcY + srcH > rtHeight)
        {
            srcH = rtHeight - srcY;
        }

        // Only draw if there's valid area
        if (srcW > 0 && srcH > 0)
        {
            Rectangle sourceRect = new Rectangle(srcX, srcY, srcW, srcH);
            Rectangle screenRect = new Rectangle(dstX, dstY, srcW, srcH);
            Renderer.SpriteBatch.Draw(TileRenderTarget, screenRect, sourceRect, Color.White);
        }

        Renderer.SpriteBatch.End();
    }
    private void CalculateSkyLevels()
    {
        lock (SkyLock)
        {
            for (int x = 0; x < WorldWidth; x++)
            {
                SkyLevels[x] = WorldHeight;
                for (int y = 0; y < WorldHeight; y++)
                {
                    if (GetTileType(x, y, 0).Id != TileType.Air.Id) { SkyLevels[x] = y; break; }
                }
            }
        }
    }

    private void RecalculateSkyColumns(Point center)
    {
        int h = BrushSize / 2;
        int minX = Math.Max(0, center.X - h);
        int maxX = Math.Min(WorldWidth - 1, center.X + h);

        lock (SkyLock)
        {
            for (int x = minX; x <= maxX; x++)
            {
                SkyLevels[x] = WorldHeight;
                for (int y = 0; y < WorldHeight; y++)
                {
                    if (GetTileType(x, y, 0).Id != TileType.Air.Id) { SkyLevels[x] = y; break; }
                }
            }
        }
    }

    private TileType GetTileType(int tileX, int tileY, int layer)
    {
        if (tileX < 0 || tileX >= WorldWidth || tileY < 0 || tileY >= WorldHeight) return TileType.Air;
        var e = Scene.ECS.AtExact(TileToWorld(tileX), TileToWorld(tileY), layer);
        return e.HasValue ? TileTypeRegistry.GetTileType(Scene.ECS.GetComponent<TileData>(e.Value).TileTypeId) : TileType.Air;
    }

    private (bool IsSource, Vector3 Color, float Intensity) GetLightSource(int tileX, int tileY)
    {
        var fg = GetTileType(tileX, tileY, 0);
        if (fg.IsLightSource) return (true, fg.LightColor, fg.LightIntensity);
        var bg = GetTileType(tileX, tileY, 1);
        if (bg.IsLightSource && fg.Id == TileType.Air.Id) return (true, bg.LightColor, bg.LightIntensity);
        return (false, Vector3.Zero, 0f);
    }

    private void UpdateMaterials()
    {
        int camTileX = WorldToTile(CameraPosition.X);
        int camTileY = WorldToTile(CameraPosition.Y);
        int startTileX = camTileX - Padding;
        int startTileY = camTileY - Padding;

        float renderOriginX = TileToWorld(startTileX);
        float renderOriginY = TileToWorld(startTileY);

        Vector3 bMin = new Vector3(renderOriginX, renderOriginY, 0);
        Vector3 bMax = new Vector3(renderOriginX + (GridWidth * TileSize), renderOriginY + (GridHeight * TileSize), 1);

        foreach (var id in Scene.ECS.InBox(bMin, bMax))
        {
            if (!Scene.ECS.HasComponent<TileData>(id)) continue;
            ref var d = ref Scene.ECS.GetComponent<TileData>(id);
            ref var m = ref Scene.ECS.GetComponent<Material>(id);
            TileType t = TileTypeRegistry.GetTileType(d.TileTypeId);

            if (d.Layer == 1)
            {
                bool fgAir = GetTileType(d.X, d.Y, 0).Id == TileType.Air.Id;
                m.Albedo = (fgAir && t.Id != TileType.Air.Id) ? new Color(t.Color.R / 2, t.Color.G / 2, t.Color.B / 2) : Color.Transparent;
            }
            else
            {
                m.Albedo = (t.Id == TileType.Air.Id) ? Color.Transparent : t.Color;
            }
        }
    }

    private void HandleCameraInput()
    {
        var ks = Keyboard.GetState();
        Vector2 dir = Vector2.Zero;

        if (ks.IsKeyDown(Keys.W)) dir.Y -= 1;
        if (ks.IsKeyDown(Keys.S)) dir.Y += 1;
        if (ks.IsKeyDown(Keys.A)) dir.X -= 1;
        if (ks.IsKeyDown(Keys.D)) dir.X += 1;

        if (dir != Vector2.Zero)
        {
            dir.Normalize();
            CameraPosition += dir * CameraSpeed * Scene.DeltaTime;
        }

        // Clamp camera to world bounds
        float maxX = (WorldWidth * TileSize) - ViewportWidth;
        float maxY = (WorldHeight * TileSize) - ViewportHeight;

        CameraPosition.X = MathHelper.Clamp(CameraPosition.X, 0, maxX);
        CameraPosition.Y = MathHelper.Clamp(CameraPosition.Y, 0, maxY);
    }

    private void HandleMouseInput()
    {
        CurrentMouseState = Mouse.GetState();
        Point tp = new Point(
            WorldToTile(CurrentMouseState.X + CameraPosition.X),
            WorldToTile(CurrentMouseState.Y + CameraPosition.Y));

        bool leftPressed = CurrentMouseState.LeftButton == ButtonState.Pressed;
        bool rightPressed = CurrentMouseState.RightButton == ButtonState.Pressed;

        if (leftPressed)
        {
            if (tp == LastModifiedTile && LastActionWasPlace) return;
            ModifyTiles(tp, TileType.Torch);
            RecalculateSkyColumns(tp);
            LastModifiedTile = tp;
            LastActionWasPlace = true;
        }
        else if (rightPressed)
        {
            if (tp == LastModifiedTile && !LastActionWasPlace) return;
            ModifyTiles(tp, TileType.Air);
            RecalculateSkyColumns(tp);
            LastModifiedTile = tp;
            LastActionWasPlace = false;
        }
        else
        {
            LastModifiedTile = new Point(-999, -999);
        }
    }

    private void ModifyTiles(Point c, TileType t)
    {
        int h = BrushSize / 2;
        for (int tx = c.X - h; tx <= c.X + h; tx++)
            for (int ty = c.Y - h; ty <= c.Y + h; ty++)
            {
                var e = Scene.ECS.AtExact(TileToWorld(tx), TileToWorld(ty), 0);
                if (e.HasValue) Scene.ECS.GetComponent<TileData>(e.Value).TileTypeId = t.Id;
            }
    }

    public override void Dispose()
    {
        IsRunning = false;
        LightingThread?.Join(200);
        LightTexture?.Dispose();
        TileRenderTarget?.Dispose();
    }
}