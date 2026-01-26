// Instanced 2D Shape Renderer
// Renders rectangles, circles, and triangles using SDF in a single draw call

float4x4 ViewProjection;

struct VSInput
{
    // Per-vertex (quad mesh)
    float2 Position : POSITION0;
    float2 UV : TEXCOORD0;

    // Per-instance
    float2 InstancePos : TEXCOORD1;
    float2 InstanceSize : TEXCOORD2;
    float4 InstanceColor : COLOR1;
    float InstanceType : TEXCOORD3;
};

struct PSInput
{
    float4 Position : SV_POSITION;
    float2 UV : TEXCOORD0;
    float4 Color : COLOR0;
    float Type : TEXCOORD1;
};

PSInput MainVS(VSInput input)
{
    PSInput output;

    // Transform quad vertex by instance position and size
    float2 worldPos = input.InstancePos + input.Position * input.InstanceSize;

    output.Position = mul(float4(worldPos, 0, 1), ViewProjection);
    output.UV = input.UV;
    output.Color = input.InstanceColor;
    output.Type = input.InstanceType;

    return output;
}

float4 MainPS(PSInput input) : SV_Target
{
    float2 uv = input.UV;
    float2 center = uv - 0.5;
    int type = (int)(input.Type + 0.5);

    float alpha = 1.0;

    if (type == 1) // Circle
    {
        float dist = length(center);
        // Screen-space anti-aliasing using derivatives
        float pixelWidth = fwidth(dist);
        alpha = 1.0 - smoothstep(0.5 - pixelWidth, 0.5, dist);
        if (alpha < 0.001)
            discard;
    }
    else if (type == 2) // Triangle (equilateral, pointing up)
    {
        // Equilateral triangle: apex at TOP, base at BOTTOM
        float2 p = float2(abs(center.x), center.y);

        // Base edge at bottom (y = 0.35)
        float d_base = p.y - 0.35;

        // Slant edges from apex (0, -0.35) to corners (±0.4, 0.35)
        float d_slant = p.x * 0.868 - (p.y + 0.35) * 0.496;

        float d = max(d_base, d_slant);

        float pixelWidth = fwidth(d);
        alpha = 1.0 - smoothstep(-pixelWidth, pixelWidth, d);
        if (alpha < 0.001)
            discard;
    }
    else if (type == 3) // Triangle Border (unfilled, outline only)
    {
        // Equilateral triangle border
        float borderThickness = 0.03;
        float2 p = float2(abs(center.x), center.y);

        // Same SDF as filled
        float d_base = p.y - 0.35;
        float d_slant = p.x * 0.868 - (p.y + 0.35) * 0.496;
        float d = max(d_base, d_slant);

        // Border ring
        float d_outer = d;
        float d_inner = d + borderThickness;
        float d_border = max(d_outer, -d_inner);

        float pixelWidth = fwidth(d_border);
        alpha = 1.0 - smoothstep(-pixelWidth, pixelWidth, d_border);
        if (alpha < 0.001)
            discard;
    }
    // type == 0: Rectangle - no SDF needed, full quad

    float4 color = input.Color;
    color.a *= alpha;

    // Premultiplied alpha
    color.rgb *= color.a;

    return color;
}

// Sharp version (no anti-aliasing, pixel perfect)
float4 SharpPS(PSInput input) : SV_Target
{
    float2 uv = input.UV;
    float2 center = uv - 0.5;
    int type = (int)(input.Type + 0.5);

    if (type == 1) // Circle
    {
        float dist = length(center);
        if (dist > 0.5)
            discard;
    }
    else if (type == 2) // Triangle
    {
        float2 p = float2(abs(center.x), center.y);
        float d_base = p.y - 0.35;
        float d_slant = p.x * 0.868 - (p.y + 0.35) * 0.496;
        if (max(d_base, d_slant) > 0)
            discard;
    }
    else if (type == 3) // Triangle Border
    {
        float borderThickness = 0.03;
        float2 p = float2(abs(center.x), center.y);
        float d_base = p.y - 0.35;
        float d_slant = p.x * 0.868 - (p.y + 0.35) * 0.496;
        float d = max(d_base, d_slant);
        float d_border = max(d, -(d + borderThickness));
        if (d_border > 0)
            discard;
    }

    float4 color = input.Color;
    color.rgb *= color.a;
    return color;
}

technique Default
{
    pass P0
    {
        VertexShader = compile vs_4_0 MainVS();
        PixelShader = compile ps_4_0 MainPS();
    }
}

technique Sharp
{
    pass P0
    {
        VertexShader = compile vs_4_0 MainVS();
        PixelShader = compile ps_4_0 SharpPS();
    }
}

