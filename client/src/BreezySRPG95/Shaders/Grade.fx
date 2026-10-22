// The palette's brightness and tone on the finished frame, on the GPU: the same sums as BreezyGame.Grade's CPU path
// (FUN_00405a18), so the frame can be graded at any render scale. Black stays black; the vividness mixes each colour with
// its grey (3R + 6G + B) / 10; the tone and brightness come in a 256 × 1 table (red, green, blue in its channels).
//
// Compiled with MonoGame's effect compiler (dotnet tool dotnet-mgfxc 3.8.2.1105), the output embedded in the client:
//   mgfxc Grade.fx Grade.ogl.mgfx /Profile:OpenGL

#if OPENGL
	#define SV_POSITION POSITION
	#define PS_SHADERMODEL ps_3_0
#else
	#define PS_SHADERMODEL ps_4_0_level_9_3
#endif

sampler2D Scene : register(s0);   // the sprite batch's texture

Texture2D Table;
sampler2D TableSampler : register(s1) = sampler_state
{
	Texture = <Table>;
	MinFilter = Point;
	MagFilter = Point;
	MipFilter = Point;
	AddressU = Clamp;
	AddressV = Clamp;
};

float Vivid;   // the vividness, 0–100; 100 and over leaves the colours as they are

struct VertexShaderOutput
{
	float4 Position : SV_POSITION;
	float4 Color : COLOR0;
	float2 Uv : TEXCOORD0;
};

float4 GradePS(VertexShaderOutput input) : COLOR
{
	float4 c = tex2D(Scene, input.Uv);
	float3 v = floor(c.rgb * 255 + 0.5);
	if (v.r + v.g + v.b == 0)
		return c;
	if (Vivid < 100)
	{
		// integer sums as the CPU path: the small bias keeps exact quotients from flooring one low
		float grey = floor((v.r * 3 + v.g * 6 + v.b) / 10 + 0.001);
		v = Vivid <= 0 ? float3(grey, grey, grey) : floor(((100 - Vivid) * grey + v * Vivid) / 100 + 0.001);
	}
	float3 u = (v + 0.5) / 256;
	return float4(tex2D(TableSampler, float2(u.r, 0.5)).r, tex2D(TableSampler, float2(u.g, 0.5)).g,
		tex2D(TableSampler, float2(u.b, 0.5)).b, c.a);
}

technique Grade
{
	pass P0
	{
		PixelShader = compile PS_SHADERMODEL GradePS();
	}
};
