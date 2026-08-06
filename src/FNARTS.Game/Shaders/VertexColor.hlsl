// VertexColor shader — pass-through vertex + flat color pixel.
// Vertices are pre-transformed in CPU space (NDC coordinates).
// Meets HLSL vertex conventions C1-C5 for FNA3D_HLSL.
//
// VS_INPUT: Position + Color (PC layout, matches VertexPositionColor)

struct VS_INPUT
{
    float3 Position : POSITION0;
    float4 Color    : COLOR0;
};

struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR0;
};

VS_OUTPUT VSMain(VS_INPUT input)
{
    VS_OUTPUT output;
    output.Position = float4(input.Position, 1.0);
    output.Color = input.Color;
    return output;
}

float4 PSMain(VS_OUTPUT input) : SV_TARGET0
{
    return input.Color;
}