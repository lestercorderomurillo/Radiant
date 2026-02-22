using System;
using System.Collections.Generic;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public struct TileType
{
    public string Id;
    public Color Color;
    public bool IsSolid;
    public string Name;
    public float LightTransmission;
    public bool IsLightSource;
    public Vector3 LightColor;
    public float LightIntensity;

    public TileType(string id, Color color, bool isSolid, string name, 
        float lightTransmission = 0f, bool isLightSource = false, 
        Vector3? lightColor = null, float lightIntensity = 0f)
    {
        Id = id;
        Color = color;
        IsSolid = isSolid;
        Name = name;
        LightTransmission = lightTransmission;
        IsLightSource = isLightSource;
        LightColor = lightColor ?? Vector3.Zero;
        LightIntensity = lightIntensity;
    }

    public static TileType Air => new TileType("main:air", Color.Transparent, false, "Air", 1.0f);
    public static TileType Grass => new TileType("main:grass", new Color(34, 139, 34), true, "Grass", 0f);
    public static TileType Dirt => new TileType("main:dirt", new Color(139, 69, 19), true, "Dirt", 0f);
    public static TileType Stone => new TileType("main:stone", new Color(105, 105, 105), true, "Stone", 0f);


    public static TileType Basite => new TileType("main:basite", new Color(255, 255, 255), true, "Basite", 0f);
    public static TileType Ash => new TileType("main:ash", new Color(255, 255, 255), true, "Ash", 0f);
    public static TileType Snow => new TileType("main:snow", new Color(255, 255, 255), true, "Snow", 0f);
    public static TileType Sandstone => new TileType("main:sandstone", new Color(255, 200, 100), true, "Sandstone", 0f);
    public static TileType Sand => new TileType("main:sand", new Color(255, 200, 100), true, "Sand", 0f);
    public static TileType Torch => new TileType("main:torch", new Color(255, 200, 100), false, "Torch", 
        1.0f, true, new Vector3(1.0f, 0.8f, 0.4f), 1.0f);
}

public static class TileTypeRegistry
{
    private static Dictionary<string, TileType> _tileTypes = new Dictionary<string, TileType>();
    private static TileType _defaultTileType = TileType.Air;

    static TileTypeRegistry()
    {
        RegisterTileType(TileType.Air);
        RegisterTileType(TileType.Grass);
        RegisterTileType(TileType.Dirt);
        RegisterTileType(TileType.Stone);
        RegisterTileType(TileType.Torch);
    }

    public static void RegisterTileType(TileType tileType)
    {
        _tileTypes[tileType.Id] = tileType;
    }

    public static TileType GetTileType(string id)
    {
        return _tileTypes.TryGetValue(id, out var tileType) ? tileType : _defaultTileType;
    }
}

[ComponentDescription("Tile position, layer and type for 2D tilesets.")]
public struct TileData : Component
{
    public int X;
    public int Y;
    public int Layer;
    public string TileTypeId;

    public Vector3 LightFromTop;
    public Vector3 LightFromBottom;
    public Vector3 LightFromLeft;
    public Vector3 LightFromRight;
    public Vector3 FinalLight;
}