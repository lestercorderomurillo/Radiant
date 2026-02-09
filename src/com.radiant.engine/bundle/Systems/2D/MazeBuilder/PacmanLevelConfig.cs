using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public class PacmanLevelConfig
{
    public string[] Layout { get; set; }
    public bool Procedural { get; set; }
    public int MazeSeed { get; set; }
    public Color WallColor { get; set; } = new Color((byte)0, (byte)0, (byte)40, (byte)255);
    public Color WallLight { get; set; } = new Color((byte)120, (byte)80, (byte)255, (byte)255);
    public float WallThickness { get; set; } = 4f;
    public (int left, int top, int right, int bottom) GhostHouse { get; set; } = (11, 13, 16, 15);
    public (int x, int y)[] NoUpTiles { get; set; } = [(12, 11), (15, 11)];

    public float CoinRadius { get; set; } = 6f;
    public Color CoinColor { get; set; } = new Color(165, 130, 15);
    public float CoinPulseSpeed { get; set; } = 1.5f;
    public float CoinPulseMin { get; set; } = 0.6f;
    public float CoinPulseMax { get; set; } = 1.0f;

    public PacmanGhostType[] Ghosts { get; set; } = [];
    public (int x, int y)[] GhostStartCells { get; set; }
    public float GhostSpeed { get; set; } = 200f;
    public float[] GhostReleaseTimes { get; set; }

    public float RainbowSpeed { get; set; } = 200f;

    public (int x, int y) PlayerStartCell { get; set; } = (14, 23);
}
