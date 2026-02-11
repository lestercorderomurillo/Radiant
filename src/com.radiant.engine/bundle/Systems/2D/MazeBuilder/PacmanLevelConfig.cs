using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public struct GhostEntry
{
    public PacmanGhostType Type;
    public (int x, int y)? StartCell;
    public float ReleaseAfter;
    public float ReleaseAtCoinPercent;
}

public class PacmanLevelConfig
{
    public string[] Layout { get; set; }
    public bool Procedural { get; set; }
    public int MazeSeed { get; set; }
    public Color WallAlbedo { get; set; } = new Color((byte)0, (byte)0, (byte)40, (byte)255);
    public Color WallEmissive { get; set; } = new Color((byte)120, (byte)80, (byte)255, (byte)255);
    public (int left, int top, int right, int bottom) GhostHouse { get; set; } = (11, 13, 16, 15);
    public (int x, int y)[] NoUpTiles { get; set; } = [(12, 11), (15, 11)];

    public float CoinRadius { get; set; } = 6f;
    public Color CoinColor { get; set; } = new Color(165, 130, 15);

    public float PowerPelletRadius { get; set; } = 16f;
    public Color PowerPelletColor { get; set; } = new Color((byte)255, (byte)255, (byte)255, (byte)255);
    public float FrightenedDuration { get; set; } = 5f;
    public (int x, int y)[] PowerPelletCells { get; set; }

    public GhostEntry[] Ghosts { get; set; } = [];
    public float GhostSpeed { get; set; } = 200f;

    public float RainbowSpeed { get; set; } = 200f;

    public (int x, int y) PlayerStartCell { get; set; } = (14, 23);
}
