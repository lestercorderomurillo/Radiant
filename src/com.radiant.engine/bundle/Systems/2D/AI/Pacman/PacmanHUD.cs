using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

[RunAfter(typeof(PacmanPlayerController))]
[SystemTag("Pacman")]
public class PacmanHUD : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.Gameplay;

    private PacmanPlayerController Player;
    private PacmanMazeBuilder Maze;
    private Geometry Geometry;

    const string HudFontName = "PressStart2P";
    const float HudFontSize = 24f;

    public override void Initialize()
    {
        Player = Scene.ECS.GetSystem<PacmanPlayerController>();
        Maze = Scene.ECS.GetSystem<PacmanMazeBuilder>();
        Geometry = Scene.ECS.GetSystem<Geometry>();
    }

    public override void LateRender()
    {
        if (Player == null || !Player.IsTracked || Geometry.IsDebugHidingGameplay) return;

        var scale = Matrix.CreateScale(
            Renderer.ScreenWidth / Renderer.VirtualWidth,
            Renderer.ScreenHeight / Renderer.VirtualHeight,
            1f);

        float padding = 18f;
        float iconSize = 40f;
        float gap = 14f;

        float mazeLeft = Maze.OffsetX + 40f;
        float mazeRight = Maze.OffsetX + Maze.Cols * Maze.CellSize - 40f;
        float y = Maze.OffsetY - 45f;

        var tagSize = Renderer.MeasureString(HudFontName, HudFontSize, Player.LevelTag);
        float tagBlockWidth = tagSize.X;
        float textHeight = tagSize.Y;

        string collected = Player.CoinsCollected.ToString();
        string separator = " / ";
        string total = Player.CoinsTotal.ToString();
        var collectedSize = Renderer.MeasureString(HudFontName, HudFontSize, collected);
        var separatorSize = Renderer.MeasureString(HudFontName, HudFontSize, separator);
        var totalSize = Renderer.MeasureString(HudFontName, HudFontSize, total);
        float coinTextWidth = collectedSize.X + separatorSize.X + totalSize.X;
        float coinBlockWidth = iconSize + gap + coinTextWidth;

        Renderer.BeginDraw(SpriteSortMode.Deferred, BlendState.AlphaBlend, transform: scale);

        float tagX = mazeLeft;
        var tagBg = new Rectangle(
            (int)(tagX - padding), (int)(y - padding * 0.6f),
            (int)(tagBlockWidth + padding * 2f), (int)(textHeight + padding * 1.2f));
        Renderer.DrawSprite(Renderer.GetSolidTexture(Color.White), tagBg, new Color(0, 0, 0, 180));
        Renderer.DrawString(HudFontName, HudFontSize, Player.LevelTag, new Vector2(tagX, y), new Color(255, 255, 255));

        float coinX = mazeRight - coinBlockWidth;
        var coinBg = new Rectangle(
            (int)(coinX - padding), (int)(y - padding * 0.6f),
            (int)(coinBlockWidth + padding * 2f), (int)(textHeight + padding * 1.2f));
        Renderer.DrawSprite(Renderer.GetSolidTexture(Color.White), coinBg, new Color(0, 0, 0, 180));

        var coinTex = Renderer.GetCircleTexture((int)iconSize);
        var iconRect = new Rectangle(
            (int)coinX, (int)(y + (textHeight - iconSize) / 2f),
            (int)iconSize, (int)iconSize);
        Renderer.DrawSprite(coinTex, iconRect, Player.CoinColor);

        float textX = coinX + iconSize + gap;
        Renderer.DrawString(HudFontName, HudFontSize, collected, new Vector2(textX, y), Player.CoinColor);
        textX += collectedSize.X;
        Renderer.DrawString(HudFontName, HudFontSize, separator, new Vector2(textX, y), new Color(200, 200, 200));
        textX += separatorSize.X;
        Renderer.DrawString(HudFontName, HudFontSize, total, new Vector2(textX, y), new Color(255, 255, 255));

        Renderer.EndDraw();
    }
}
