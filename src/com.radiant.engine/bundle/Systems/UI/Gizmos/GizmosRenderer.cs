using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using com.radiant.engine.runtime;
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

    private new bool Enabled = false;
    private const float TextPadding = 4f;
    private Color TextBackgroundColor = new(0, 0, 0, 180);

    public void ToggleGizmos() => Enabled = !Enabled;

    public override void Initialize()
    {
        BaseFont = Renderer.GetFont("fonts/BaseFont");
    }

    public override void Update()
    {
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
        // Scale from virtual coordinates to actual screen pixels so gizmos
        // resize proportionally with the window (resolution-independent).
        var Scale = Matrix.CreateScale(
            (float)Renderer.ScreenWidth / Renderer.VirtualWidth,
            (float)Renderer.ScreenHeight / Renderer.VirtualHeight,
            1f);

        if (Enabled)
        {
            Renderer.BeginDraw(SpriteSortMode.Deferred, BlendState.AlphaBlend, transform: Scale);
            RenderGizmos();
            Renderer.EndDraw();
        }

        // Always render build number in bottom-left corner
        var BuildText = Window.BuildTag;
        var TextSize = BaseFont.MeasureString(BuildText);
        var BuildPos = new Vector2(15, Renderer.VirtualHeight - TextSize.Y - 15);

        Renderer.BeginDraw(SpriteSortMode.Deferred, BlendState.AlphaBlend, transform: Scale);
        RenderTextWithBackground(new TextGizmo(BuildPos, BuildText, Color.Gray));
        Renderer.EndDraw();
    }

    private void RenderGizmos()
    {
        foreach (var line in LineQueue)
            RenderLine(line);

        foreach (var circle in CircleQueue)
            RenderCircle(circle);

        foreach (var arc in ArcQueue)
            RenderArc(arc);

        foreach (var rect in RectQueue)
            RenderRect(rect);

        foreach (var text in TextQueue)
            RenderTextWithBackground(text);
    }

    private void RenderTextWithBackground(TextGizmo text)
    {
        if (string.IsNullOrEmpty(text.Text)) return;

        Vector2 textSize = BaseFont.MeasureString(text.Text);

        Rectangle backgroundRect = new(
            (int)(text.Position.X - TextPadding),
            (int)(text.Position.Y - TextPadding),
            (int)(textSize.X + TextPadding * 2),
            (int)(textSize.Y + TextPadding * 2)
        );

        Renderer.DrawSprite(Renderer.GetSolidTexture(Color.White), backgroundRect, TextBackgroundColor);
        Renderer.DrawString(BaseFont, text.Text, text.Position, text.Color);
    }

    private void RenderLine(LineGizmo line)
    {
        Vector2 delta = line.End - line.Start;
        float length = delta.Length();

        if (length == 0) return;

        float rotation = (float)Math.Atan2(delta.Y, delta.X);

        Renderer.DrawSprite(Renderer.GetSolidTexture(Color.White), new Rectangle(
            (int)line.Start.X, (int)line.Start.Y,
            (int)length, (int)line.Thickness),
            null, line.Color, rotation,
            new Vector2(0, 0.5f));
    }

    private void RenderCircle(CircleGizmo circle)
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

            RenderLine(new LineGizmo(prev, next, circle.Color, 1f));
            prev = next;
        }
    }

    private void RenderArc(ArcGizmo arc)
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

            RenderLine(new LineGizmo(prev, next, arc.Color, 1f));
            prev = next;
        }
    }

    private void RenderRect(RectGizmo rect)
    {
        if (rect.Filled)
        {
            Renderer.DrawSprite(Renderer.GetSolidTexture(Color.White), rect.Rect, rect.Color);
        }
        else
        {
            Vector2 topLeft = new(rect.Rect.X, rect.Rect.Y);
            Vector2 topRight = new(rect.Rect.Right, rect.Rect.Y);
            Vector2 bottomLeft = new(rect.Rect.X, rect.Rect.Bottom);
            Vector2 bottomRight = new(rect.Rect.Right, rect.Rect.Bottom);

            RenderLine(new LineGizmo(topLeft, topRight, rect.Color, 1f));
            RenderLine(new LineGizmo(topRight, bottomRight, rect.Color, 1f));
            RenderLine(new LineGizmo(bottomRight, bottomLeft, rect.Color, 1f));
            RenderLine(new LineGizmo(bottomLeft, topLeft, rect.Color, 1f));
        }
    }

    public override void Dispose() { }
}
