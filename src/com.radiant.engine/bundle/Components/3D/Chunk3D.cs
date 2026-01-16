using com.radiant.engine.core;

public struct Chunk3D : Component
{
    public const int Size = 16;

    public Tile3D[] Tiles;

    public Chunk3D()
    {
        Tiles = new Tile3D[Size * Size * Size];
    }

    public Tile3D Get(int x, int y, int z) => Tiles[x + y * Size + z * Size * Size];
    
    public void Set(int x, int y, int z, Tile3D t) => Tiles[x + y * Size + z * Size * Size] = t;
}