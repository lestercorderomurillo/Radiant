using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public class WorldGen : core.System
{
    private GizmosRenderer Gizmos;

    private PerlinNoise2D PerlinNoise;

    private Random Random;

    public int WorldWidth { get; private set; }

    public int WorldHeight { get; private set; }

    public int TileSize { get; private set; } = 48;

    private SurfaceBiomeType CurrentBiome;

    private int BiomeCounter;

    private int SurfaceLevelCounter;

    private int SurfaceLevel;

    public override void Initialize()
    {
        Gizmos = Scene.ECS.GetSystem<GizmosRenderer>();
        PerlinNoise = Scene.ECS.GetSystem<PerlinNoise2D>();
        Random = new Random();
    }

    enum SurfaceBiomeType { Greenfields, Desert, Snowy, Volcanic, Mountain }

    enum CaveBiomeType { Surface, Underground }

    public void GenerateWorld(int width, int height)
    {
        WorldWidth = width;
        WorldHeight = height;
        CurrentBiome = SurfaceBiomeType.Greenfields;
        SurfaceLevelCounter = 10;
        SurfaceLevel = 100;

        int biomeMinSize = 75;
        int biomeMaxSize = 175;
        int generalDirection = 1;

        BiomeCounter = Random.Next(biomeMinSize, biomeMaxSize);

        for (int x = 0; x < width; x++)
        {
            BiomeCounter--;
            if (BiomeCounter <= 0)
            {
                if (CurrentBiome == SurfaceBiomeType.Greenfields)
                {
                    biomeMinSize = 75;
                    biomeMaxSize = 125;

                    if (Random.Next(100) < 60)
                    {
                        CurrentBiome = SurfaceBiomeType.Greenfields;
                    }
                    else
                    {
                        CurrentBiome = SurfaceBiomeType.Mountain;
                    }
                }
                else if (CurrentBiome == SurfaceBiomeType.Mountain)
                {
                    biomeMinSize = 45;
                    biomeMaxSize = 60;

                    generalDirection *= -1;

                    if (Random.Next(100) < 85)
                    {
                        CurrentBiome = SurfaceBiomeType.Greenfields;
                    }
                    else
                    {
                        CurrentBiome = SurfaceBiomeType.Mountain;
                    }
                }

                BiomeCounter = Random.Next(biomeMinSize, biomeMaxSize);
            }

            SurfaceLevelCounter--;

            if (SurfaceLevelCounter <= 0)
            {
                int heightVariation = 1;
                int surfaceDownTendency = 50;
                int intervalMin = 3;
                int intervalMax = 8;

                if (CurrentBiome == SurfaceBiomeType.Greenfields)
                {
                    heightVariation = Random.Next(100) < 90 ? 1 : 2;
                    surfaceDownTendency = 50;
                    intervalMin = 5;
                    intervalMax = 8;
                }
                else if (CurrentBiome == SurfaceBiomeType.Mountain)
                {
                    heightVariation = Random.Next(100) < 65 ? 2 : 1;

                    if (SurfaceLevel < 20)
                    {
                        surfaceDownTendency = 100;
                    }
                    else if (SurfaceLevel > 120)
                    {
                        surfaceDownTendency = 0;
                    }
                    else
                    {
                        if (generalDirection > 0)
                            surfaceDownTendency = 90;
                        else
                            surfaceDownTendency = 10;
                    }

                    intervalMin = 1;
                    intervalMax = 2;
                }

                if (Random.Next(100) < surfaceDownTendency)
                {
                    SurfaceLevel += heightVariation;
                }
                else
                {
                    SurfaceLevel -= heightVariation;
                }

                SurfaceLevelCounter = Random.Next(intervalMin, intervalMax);
            }

            for (int y = 0; y < height; y++)
            {
                CreateTileEntity(x, y, 0, DetermineTileType(x, y, 0));
                CreateTileEntity(x, y, 1, DetermineTileType(x, y, 1));
            }
        }
    }

    float Scale(float input, float[] inputRange, float[] outputRange)
    {
        if (input <= inputRange[0]) return outputRange[0];
        if (input >= inputRange[inputRange.Length - 1]) return outputRange[outputRange.Length - 1];

        for (int i = 0; i < inputRange.Length - 1; i++)
        {
            if (input >= inputRange[i] && input <= inputRange[i + 1])
            {
                float t = (input - inputRange[i]) / (inputRange[i + 1] - inputRange[i]);
                return outputRange[i] + (outputRange[i + 1] - outputRange[i]) * t;
            }
        }

        return outputRange[0];
    }

    public static float Freq(float tilePeriod) => 1f / tilePeriod;

    private TileType DetermineTileType(int x, int y, int layer)
    {
        TileType surface = TileType.Grass;
        TileType subsurface = TileType.Dirt;

        int surfaceDepth = 5;
        int subSurfaceDepth = 5;

        if (CurrentBiome == SurfaceBiomeType.Greenfields)
        {
            surface = TileType.Grass;
            subsurface = TileType.Dirt;
            surfaceDepth = 1;
            subSurfaceDepth = 7;
        }
        else if (CurrentBiome == SurfaceBiomeType.Desert)
        {
            surface = TileType.Sand;
            subsurface = TileType.Sand;
            surfaceDepth = 1;
            subSurfaceDepth = 8;
        }
        else if (CurrentBiome == SurfaceBiomeType.Mountain)
        {
            surface = TileType.Stone;
            subsurface = TileType.Stone;
            surfaceDepth = 1;
            subSurfaceDepth = 1;
        }

        TileType tile;

        if (y < SurfaceLevel)
        {
            tile = TileType.Air;
        }
        else if (y < SurfaceLevel + surfaceDepth)
        {
            tile = surface;
        }
        else if (y < SurfaceLevel + surfaceDepth + subSurfaceDepth)
        {
            tile = subsurface;
        }
        else
        {
            tile = TileType.Stone;

            if (Random.Next(100) < Scale(y, [100, WorldHeight], [0, 10, 15, 0]))
            {
                tile = TileType.Dirt;
            }
        }

        if (y > SurfaceLevel + 15 && tile.Id != TileType.Air.Id && layer == 0)
        {
            int depth = y - SurfaceLevel;

            
            //var normalizedSin = (double frequency) => (Math.Sin(y / 10 * x * frequency) + 1) / 2;

           // var variance = (float)(0.050 * normalizedSin(0.025f));

            // Entrance caves, dissappear quickly
           // if (PerlinNoise.AbsSample(x * Freq(25), y * Freq(22)) < Scale(depth, [0, 45, 250], [0.0f, 0.058f + variance, 0.00f]))
             //   tile = TileType.Air;

            //if (PerlinNoise.AbsSample((x + 12) * Freq(130), y * Freq(90)) < Scale(depth, [175, 250, 512, 544], [0.015f, 0.055f, 0.035f, 0.005f]))
            //   tile = TileType.Air;

            // Medium tunnels (horizontal bias)
            // if (PerlinNoise.AbsSample(x * 0.04f, y * 0.025f) < Scale(depth, [15, 200], [0.10f, 0.18f]))
            //    tile = TileType.Air;

            // Small connecting passages
            // if (PerlinNoise.AbsSample(x * 0.08f, y * 0.06f) < Scale(depth, [30, 200], [0.0f, 0.07f]))
            //     tile = TileType.Air;
        }

        return tile;
    }

    private void CreateTileEntity(int tileX, int tileY, int layer, TileType tileType)
    {
        int entity = Scene.ECS.CreateEntity(new Vector3(tileX * TileSize, tileY * TileSize, layer));
        ref var rect = ref Scene.ECS.AddComponent<Rectangle2D>(entity);
        ref var tileData = ref Scene.ECS.AddComponent<TileData>(entity);
        Scene.ECS.AddComponent<Material>(entity);

        rect.Size = new Vector2(TileSize, TileSize);
        tileData.X = tileX;
        tileData.Y = tileY;
        tileData.Layer = layer;
        tileData.TileTypeId = tileType.Id;
    }

    public override void Update() { }

    public override void FixedUpdate() { }
    public override void Render() { }
    public override void LateRender() { }
    public override void Dispose() { }
}