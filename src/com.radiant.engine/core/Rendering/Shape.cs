using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.core;

/// <summary>
/// GPU instanced shape data. Use with Renderer.DrawShape() for efficient batched rendering.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Shape
{
    /// <summary>Top-left position in virtual coordinates.</summary>
    public Vector2 Position;

    /// <summary>Width and height in virtual coordinates.</summary>
    public Vector2 Size;

    /// <summary>Shape color (premultiplied alpha).</summary>
    public Color Color;

    /// <summary>Shape type: 0=rect, 1=circle, 2=triangle, 3=triangle_border.</summary>
    public float Type;

    internal static readonly VertexDeclaration Declaration = new VertexDeclaration(
        new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1),
        new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 2),
        new VertexElement(16, VertexElementFormat.Color, VertexElementUsage.Color, 1),
        new VertexElement(20, VertexElementFormat.Single, VertexElementUsage.TextureCoordinate, 3)
    );

    /// <summary>Creates a rectangle shape.</summary>
    public static Shape Rect(Vector2 position, Vector2 size, Color color) => new()
    {
        Position = position,
        Size = size,
        Color = color,
        Type = 0f
    };

    /// <summary>Creates a rectangle shape from individual coordinates.</summary>
    public static Shape Rect(float x, float y, float width, float height, Color color) => new()
    {
        Position = new Vector2(x, y),
        Size = new Vector2(width, height),
        Color = color,
        Type = 0f
    };

    /// <summary>Creates a circle shape centered at the given point.</summary>
    public static Shape Circle(Vector2 center, float radius, Color color) => new()
    {
        Position = new Vector2(center.X - radius, center.Y - radius),
        Size = new Vector2(radius * 2f, radius * 2f),
        Color = color,
        Type = 1f
    };

    /// <summary>Creates a circle shape from individual coordinates.</summary>
    public static Shape Circle(float x, float y, float radius, Color color) => new()
    {
        Position = new Vector2(x - radius, y - radius),
        Size = new Vector2(radius * 2f, radius * 2f),
        Color = color,
        Type = 1f
    };

    /// <summary>Creates a filled triangle shape.</summary>
    public static Shape Triangle(Vector2 position, Vector2 size, Color color) => new()
    {
        Position = position,
        Size = size,
        Color = color,
        Type = 2f
    };

    /// <summary>Creates a bordered (unfilled) triangle shape.</summary>
    public static Shape TriangleBorder(Vector2 position, Vector2 size, Color color) => new()
    {
        Position = position,
        Size = size,
        Color = color,
        Type = 3f
    };
}

/// <summary>
/// Vertex type for the instanced shape quad template (position + UV).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ShapeQuadVertex : IVertexType
{
    public Vector2 Position;
    public Vector2 UV;

    public static readonly VertexDeclaration Declaration = new VertexDeclaration(
        new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
        new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0)
    );

    VertexDeclaration IVertexType.VertexDeclaration => Declaration;

    public ShapeQuadVertex(Vector2 position, Vector2 uv)
    {
        Position = position;
        UV = uv;
    }
}
