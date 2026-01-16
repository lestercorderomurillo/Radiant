using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace com.radiant.engine.bundle;

public class GizmosRenderer : core.System
{
    #region Rendering Resources
    private Texture2D PixelTexture;

    private SpriteFont BaseFont;
    #endregion

    #region Gizmo Queues
    private List<LineGizmo> _lineQueue = new List<LineGizmo>();

    private List<CircleGizmo> _circleQueue = new List<CircleGizmo>();

    private List<ArcGizmo> _arcQueue = new List<ArcGizmo>();

    private List<TextGizmo> _textQueue = new List<TextGizmo>();

    private List<RectGizmo> _rectQueue = new List<RectGizmo>();
    #endregion

    #region Stats Display
    private Dictionary<string, StatsSection> _stats = new Dictionary<string, StatsSection>();

    private bool _showStats = true;

    private Vector2 _statsPosition = new Vector2(15, 15);

    private const float LINE_SPACING = 28f;


    private KeyboardState previousKeyState;
    #endregion

    // Text background settings
    private const float TEXT_PADDING = 4f;

    private Color TextBackgroundColor = new Color(0, 0, 0, 180); // Semi-transparent black

    public override void Initialize()
    {
        base.Initialize();
        
        PixelTexture = new Texture2D(RenderPipeline.GraphicsDevice, 1, 1);
        PixelTexture.SetData([Color.White]);

        BaseFont = RenderPipeline.Window.Content.Load<SpriteFont>("fonts/BaseFont");

        previousKeyState = Keyboard.GetState();
    }

    public override void Update()
    {
        var keyboard = Keyboard.GetState();

        if (keyboard.IsKeyDown(Keys.F1) && previousKeyState.IsKeyUp(Keys.F1))
            _showStats = !_showStats;

        previousKeyState = keyboard;

        ClearGizmoQueues();
    }

    private void ClearGizmoQueues()
    {
        _lineQueue.Clear();
        _circleQueue.Clear();
        _arcQueue.Clear();
        _textQueue.Clear();
        _rectQueue.Clear();
    }

    #region Public API
    public void AddGizmoLine(Vector2 start, Vector2 end, Color color, float thickness = 1f)
    {
        _lineQueue.Add(new LineGizmo(start, end, color, thickness));
    }

    public void AddGizmoCircle(Vector2 center, float radius, Color color)
    {
        _circleQueue.Add(new CircleGizmo(center, radius, color));
    }

    public void AddGizmoArc(Vector2 center, float radius, float startAngle, float endAngle, Color color)
    {
        _arcQueue.Add(new ArcGizmo(center, radius, startAngle, endAngle, color));
    }

    public void AddGizmoRect(Rectangle rect, Color color, bool filled = false)
    {
        _rectQueue.Add(new RectGizmo(rect, color, filled));
    }

    public void AddGizmoText(Vector2 position, string text, Color color)
    {
        _textQueue.Add(new TextGizmo(position, text, color));
    }

    public void AddSection(string key, string title, Color color)
    {
        if (!_stats.ContainsKey(key))
            _stats[key] = new StatsSection { 
                Title = title, TitleColor = color 
            };
    }

    public void AddSectionString(string key, string line)
    {
        if (!_stats.ContainsKey(key))
            AddSection(key, key, Color.White);
        
        _stats[key].Lines.Add(line);
    }

    public void ClearSection(string key)
    {
        if (_stats.TryGetValue(key, out StatsSection section))
            section.Lines.Clear();
    }
    #endregion

    public override void LateRender()
    {
        RenderPipeline.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

        RenderGizmos(RenderPipeline.SpriteBatch);
        RenderStats(RenderPipeline.SpriteBatch);
        
        RenderPipeline.SpriteBatch.End();
    }

    private void RenderGizmos(SpriteBatch batch)
    {
        foreach (var line in _lineQueue)
            RenderLine(batch, line);
            
        foreach (var circle in _circleQueue)
            RenderCircle(batch, circle);

        foreach (var arc in _arcQueue)
            RenderArc(batch, arc);

        foreach (var rect in _rectQueue)
            RenderRect(batch, rect);
            
        foreach (var text in _textQueue)
            RenderTextWithBackground(batch, text);
    }

    private void RenderTextWithBackground(SpriteBatch batch, TextGizmo text)
    {
        if (string.IsNullOrEmpty(text.Text)) return;

        // Measure the text to get its size
        Vector2 textSize = BaseFont.MeasureString(text.Text);
        
        // Create background rectangle with padding
        Rectangle backgroundRect = new Rectangle(
            (int)(text.Position.X - TEXT_PADDING),
            (int)(text.Position.Y - TEXT_PADDING),
            (int)(textSize.X + TEXT_PADDING * 2),
            (int)(textSize.Y + TEXT_PADDING * 2)
        );
        
        // Draw the background
        batch.Draw(PixelTexture, backgroundRect, TextBackgroundColor);
        
        // Draw the text on top
        batch.DrawString(BaseFont, text.Text, text.Position, text.Color);
    }

    private void RenderLine(SpriteBatch batch, LineGizmo line)
    {
        Vector2 delta = line.End - line.Start;
        float length = delta.Length();
        
        if (length == 0) return;
        
        float rotation = (float)Math.Atan2(delta.Y, delta.X);
        
        batch.Draw(PixelTexture, new Rectangle(
            (int)line.Start.X, (int)line.Start.Y,
            (int)length, (int)line.Thickness),
            null, line.Color, rotation, 
            new Vector2(0, 0.5f), SpriteEffects.None, 0);
    }

    private void RenderCircle(SpriteBatch batch, CircleGizmo circle)
    {
        const int SEGMENTS = 32;
        Vector2 prev = circle.Center + new Vector2(circle.Radius, 0);
        
        for (int i = 1; i <= SEGMENTS; i++)
        {
            float angle = i * MathHelper.TwoPi / SEGMENTS;
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
        const int SEGMENTS = 32;
        float angleRange = arc.EndAngle - arc.StartAngle;
        int numSegments = Math.Max(1, (int)(SEGMENTS * Math.Abs(angleRange) / MathHelper.TwoPi));
        
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
            batch.Draw(PixelTexture, rect.Rect, rect.Color);
        }
        else
        {
            Vector2 topLeft = new Vector2(rect.Rect.X, rect.Rect.Y);
            Vector2 topRight = new Vector2(rect.Rect.Right, rect.Rect.Y);
            Vector2 bottomLeft = new Vector2(rect.Rect.X, rect.Rect.Bottom);
            Vector2 bottomRight = new Vector2(rect.Rect.Right, rect.Rect.Bottom);
            
            RenderLine(batch, new LineGizmo(topLeft, topRight, rect.Color, 1f));
            RenderLine(batch, new LineGizmo(topRight, bottomRight, rect.Color, 1f));
            RenderLine(batch, new LineGizmo(bottomRight, bottomLeft, rect.Color, 1f));
            RenderLine(batch, new LineGizmo(bottomLeft, topLeft, rect.Color, 1f));
        }
    }

    private void RenderStats(SpriteBatch batch)
    {
        if (!_showStats || BaseFont == null) return;

        float y = _statsPosition.Y;
        foreach (var section in _stats.Values.Where(s => s.Enabled))
        {
            if (section.Lines.Count == 0) continue;
            
            // Render title with background
            RenderTextWithBackground(batch, new TextGizmo(
                new Vector2(_statsPosition.X, y), section.Title, section.TitleColor));
            y += LINE_SPACING;
            
            foreach (var line in section.Lines)
            {
                // Render stats lines with background
                RenderTextWithBackground(batch, new TextGizmo(
                    new Vector2(_statsPosition.X, y), line, Color.White));
                y += LINE_SPACING;
            }
            
            y += 12;
        }
    }

    public override void Dispose()
    {
        PixelTexture?.Dispose();
    }
}

class StatsSection
{
    public string Title { get; set; }
    public bool Enabled { get; set; } = true;
    public List<string> Lines { get; set; } = new List<string>();
    public Color TitleColor { get; set; } = Color.LightGreen;
}