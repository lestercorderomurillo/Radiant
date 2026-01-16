using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public struct LineGizmo
{
    public Vector2 Start;
    public Vector2 End;
    public Color Color;
    public float Thickness;

    public LineGizmo(Vector2 start, Vector2 end, Color color, float thickness = 1f)
    {
        Start = start;
        End = end;
        Color = color;
        Thickness = thickness;
    }
}

public struct CircleGizmo
{
    public Vector2 Center;
    public float Radius;
    public Color Color;

    public CircleGizmo(Vector2 center, float radius, Color color)
    {
        Center = center;
        Radius = radius;
        Color = color;
    }
}

public struct ArcGizmo
{
    public Vector2 Center;
    public float Radius;
    public float StartAngle;
    public float EndAngle;
    public Color Color;

    public ArcGizmo(Vector2 center, float radius, float startAngle, float endAngle, Color color)
    {
        Center = center;
        Radius = radius;
        StartAngle = startAngle;
        EndAngle = endAngle;
        Color = color;
    }
}

public struct TextGizmo
{
    public Vector2 Position;
    public string Text;
    public Color Color;

    public TextGizmo(Vector2 position, string text, Color color)
    {
        Position = position;
        Text = text;
        Color = color;
    }
}

public struct RectGizmo
{
    public Rectangle Rect;
    public Color Color;
    public bool Filled;

    public RectGizmo(Rectangle rect, Color color, bool filled = false)
    {
        Rect = rect;
        Color = color;
        Filled = filled;
    }
}