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
    public Vector2 Position;
    public Vector2 Size;
    public Color Color;
    public float Type;

    internal static readonly VertexDeclaration Declaration = new VertexDeclaration(
        new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1),
        new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 2),
        new VertexElement(16, VertexElementFormat.Color, VertexElementUsage.Color, 1),
        new VertexElement(20, VertexElementFormat.Single, VertexElementUsage.TextureCoordinate, 3)
    );

    public static Shape Rect(Vector2 position, Vector2 size, Color color) => new()
    {
        Position = position,
        Size = size,
        Color = color,
        Type = 0f
    };

    public static Shape Rect(float x, float y, float width, float height, Color color) => new()
    {
        Position = new Vector2(x, y),
        Size = new Vector2(width, height),
        Color = color,
        Type = 0f
    };

    public static Shape Circle(Vector2 center, float radius, Color color) => new()
    {
        Position = new Vector2(center.X - radius, center.Y - radius),
        Size = new Vector2(radius * 2f, radius * 2f),
        Color = color,
        Type = 1f
    };

    public static Shape Circle(float x, float y, float radius, Color color) => new()
    {
        Position = new Vector2(x - radius, y - radius),
        Size = new Vector2(radius * 2f, radius * 2f),
        Color = color,
        Type = 1f
    };

    public static Shape Triangle(Vector2 position, Vector2 size, Color color) => new()
    {
        Position = position,
        Size = size,
        Color = color,
        Type = 2f
    };

    public static Shape TriangleBorder(Vector2 position, Vector2 size, Color color) => new()
    {
        Position = position,
        Size = size,
        Color = color,
        Type = 3f
    };
}

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
