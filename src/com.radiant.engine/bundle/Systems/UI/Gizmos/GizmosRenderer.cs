using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;

namespace com.radiant.engine.bundle;

public class GizmosRenderer : core.System
{
    private SpriteFont BaseFont;

    private List<LineGizmo> LineQueue = new();
    private List<CircleGizmo> CircleQueue = new();
    private List<ArcGizmo> ArcQueue = new();
    private List<TextGizmo> TextQueue = new();
    private List<RectGizmo> RectQueue = new();

    private Dictionary<string, List<string>> PendingStats = new();
    private bool ShowStats = true;
    private Vector2 StatsPosition = new(15, 15);
    private const float LineSpacing = 28f;
    private const float TextPadding = 4f;
    private Color TextBackgroundColor = new(0, 0, 0, 180);

    private static readonly Color[] CategoryColors = new[]
    {
        Color.Cyan, Color.LimeGreen, Color.Gold, Color.HotPink,
        Color.Orange, Color.LightBlue, Color.Violet, Color.Yellow
    };
    private Dictionary<string, Color> CategoryColorMap = new();

    private KeyboardState PrevKeyState;

    public override void Initialize()
    {
        BaseFont = Renderer.Window.Content.Load<SpriteFont>("fonts/BaseFont");

        PrevKeyState = Keyboard.GetState();
    }

    public override void Update()
    {
        var keyboard = Keyboard.GetState();

        if (keyboard.IsKeyDown(Keys.F1) && PrevKeyState.IsKeyUp(Keys.F1))
            ShowStats = !ShowStats;

        PrevKeyState = keyboard;

        ClearGizmoQueues();
    }

    private void ClearGizmoQueues()
    {
        LineQueue.Clear();
        CircleQueue.Clear();
        ArcQueue.Clear();
        TextQueue.Clear();
        RectQueue.Clear();
    }

    public void Set(string category, string text)
    {
        if (!PendingStats.TryGetValue(category, out var list))
        {
            list = new List<string>();
            PendingStats[category] = list;

            if (!CategoryColorMap.ContainsKey(category))
                CategoryColorMap[category] = CategoryColors[CategoryColorMap.Count % CategoryColors.Length];
        }
        list.Add(text);
    }

    private Color GetCategoryColor(string category)
    {
        return CategoryColorMap.TryGetValue(category, out var color) ? color : Color.White;
    }

    public void AddGizmoLine(Vector2 start, Vector2 end, Color color, float thickness = 1f)
    {
        LineQueue.Add(new LineGizmo(start, end, color, thickness));
    }

    public void AddGizmoCircle(Vector2 center, float radius, Color color)
    {
        CircleQueue.Add(new CircleGizmo(center, radius, color));
    }

    public void AddGizmoArc(Vector2 center, float radius, float startAngle, float endAngle, Color color)
    {
        ArcQueue.Add(new ArcGizmo(center, radius, startAngle, endAngle, color));
    }

    public void AddGizmoRect(Rectangle rect, Color color, bool filled = false)
    {
        RectQueue.Add(new RectGizmo(rect, color, filled));
    }

    public void AddGizmoText(Vector2 position, string text, Color color)
    {
        TextQueue.Add(new TextGizmo(position, text, color));
    }

    public override void LateRender()
    {
        Renderer.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

        RenderGizmos(Renderer.SpriteBatch);
        RenderStats(Renderer.SpriteBatch);

        Renderer.SpriteBatch.End();
    }

    private void RenderGizmos(SpriteBatch batch)
    {
        foreach (var line in LineQueue)
            RenderLine(batch, line);

        foreach (var circle in CircleQueue)
            RenderCircle(batch, circle);

        foreach (var arc in ArcQueue)
            RenderArc(batch, arc);

        foreach (var rect in RectQueue)
            RenderRect(batch, rect);

        foreach (var text in TextQueue)
            RenderTextWithBackground(batch, text);
    }

    private void RenderTextWithBackground(SpriteBatch batch, TextGizmo text)
    {
        if (string.IsNullOrEmpty(text.Text)) return;

        Vector2 textSize = BaseFont.MeasureString(text.Text);

        Rectangle backgroundRect = new(
            (int)(text.Position.X - TextPadding),
            (int)(text.Position.Y - TextPadding),
            (int)(textSize.X + TextPadding * 2),
            (int)(textSize.Y + TextPadding * 2)
        );

        batch.Draw(Renderer.GetSolidTexture(Color.White), backgroundRect, TextBackgroundColor);
        batch.DrawString(BaseFont, text.Text, text.Position, text.Color);
    }

    private void RenderLine(SpriteBatch batch, LineGizmo line)
    {
        Vector2 delta = line.End - line.Start;
        float length = delta.Length();

        if (length == 0) return;

        float rotation = (float)Math.Atan2(delta.Y, delta.X);

        batch.Draw(Renderer.GetSolidTexture(Color.White), new Rectangle(
            (int)line.Start.X, (int)line.Start.Y,
            (int)length, (int)line.Thickness),
            null, line.Color, rotation,
            new Vector2(0, 0.5f), SpriteEffects.None, 0);
    }

    private void RenderCircle(SpriteBatch batch, CircleGizmo circle)
    {
        const int segments = 32;
        Vector2 prev = circle.Center + new Vector2(circle.Radius, 0);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * MathHelper.TwoPi / segments;
            Vector2 next = circle.Center + new Vector2(
                (float)Math.Cos(angle) * circle.Radius,
                (float)Math.Sin(angle) * circle.Radius
            );

            RenderLine(batch, new LineGizmo(prev, next, circle.Color, 1f));
            prev = next;
        }
    }

    private void RenderArc(SpriteBatch batch, ArcGizmo arc)
    {
        const int segments = 32;
        float angleRange = arc.EndAngle - arc.StartAngle;
        int numSegments = Math.Max(1, (int)(segments * Math.Abs(angleRange) / MathHelper.TwoPi));

        Vector2 prev = arc.Center + new Vector2(
            (float)Math.Cos(arc.StartAngle) * arc.Radius,
            (float)Math.Sin(arc.StartAngle) * arc.Radius
        );

        for (int i = 1; i <= numSegments; i++)
        {
            float angle = arc.StartAngle + (angleRange * i / numSegments);
            Vector2 next = arc.Center + new Vector2(
                (float)Math.Cos(angle) * arc.Radius,
                (float)Math.Sin(angle) * arc.Radius
            );

            RenderLine(batch, new LineGizmo(prev, next, arc.Color, 1f));
            prev = next;
        }
    }

    private void RenderRect(SpriteBatch batch, RectGizmo rect)
    {
        if (rect.Filled)
        {
            batch.Draw(Renderer.GetSolidTexture(Color.White), rect.Rect, rect.Color);
        }
        else
        {
            Vector2 topLeft = new(rect.Rect.X, rect.Rect.Y);
            Vector2 topRight = new(rect.Rect.Right, rect.Rect.Y);
            Vector2 bottomLeft = new(rect.Rect.X, rect.Rect.Bottom);
            Vector2 bottomRight = new(rect.Rect.Right, rect.Rect.Bottom);

            RenderLine(batch, new LineGizmo(topLeft, topRight, rect.Color, 1f));
            RenderLine(batch, new LineGizmo(topRight, bottomRight, rect.Color, 1f));
            RenderLine(batch, new LineGizmo(bottomRight, bottomLeft, rect.Color, 1f));
            RenderLine(batch, new LineGizmo(bottomLeft, topLeft, rect.Color, 1f));
        }
    }

    private void RenderStats(SpriteBatch batch)
    {
        if (!ShowStats || BaseFont == null) return;

        float y = StatsPosition.Y;
        foreach (var kvp in PendingStats)
        {
            var category = kvp.Key;
            var lines = kvp.Value;
            if (lines.Count == 0) continue;

            var titleColor = GetCategoryColor(category);
            RenderTextWithBackground(batch, new TextGizmo(
                new Vector2(StatsPosition.X, y), category, titleColor));
            y += LineSpacing;

            foreach (var line in lines)
            {
                RenderTextWithBackground(batch, new TextGizmo(
                    new Vector2(StatsPosition.X, y), line, Color.White));
                y += LineSpacing;
            }

            y += 12;
        }

        foreach (var list in PendingStats.Values)
            list.Clear();
    }

    public override void Dispose() { }
}
