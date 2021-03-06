﻿Shader "Hidden/Mixture/RidgedCellularNoise"
{	
	Properties
	{
		[InlineTexture(HideInNodeInspector)] _UV_2D("UVs", 2D) = "white" {}
		[InlineTexture(HideInNodeInspector)] _UV_3D("UVs", 3D) = "white" {}
		[InlineTexture(HideInNodeInspector)] _UV_Cube("UVs", Cube) = "white" {}

        [KeywordEnum(None, Tiled)] _TilingMode("Tiling Mode", Float) = 0
		[Enum(Euclidean, 0, Manhattan, 1, Minkowski, 2)] _DistanceMode("Distance Mode", Float) = 0
		[Enum(Gradient, 0, Cells, 1, Valleys, 2)] _CellsMode("Cells Mode", Float) = 0
		[MixtureVector2]_OutputRange("Output Range", Vector) = (-1, 1, 0, 0)
		_Lacunarity("Lacunarity", Float) = 1.5
		_Frequency("Frequency", Float) = 4
		_Persistance("Persistance", Float) = 0.9
		[IntRange]_Octaves("Octaves", Range(1, 12)) = 5
		_CellSize("Cell Size", Float) = 1
		_Seed("Seed", Int) = 42
	}
	SubShader
	{
		Tags { "RenderType"="Opaque" }
		LOD 100

		Pass
		{
			CGPROGRAM
			float _DistanceMode;
			float _CellSize;

			#include "Packages/com.alelievr.mixture/Runtime/Shaders/MixtureFixed.cginc"
			#define CUSTOM_DISTANCE _DistanceMode
			#define CUSTOM_DISTANCE_MULTIPLIER _CellSize
			#include "Packages/com.alelievr.mixture/Runtime/Shaders/Noises.hlsl"
            #pragma vertex CustomRenderTextureVertexShader
			#pragma fragment MixtureFragment
			#pragma target 3.0

			// The list of defines that will be active when processing the node with a certain dimension
            #pragma shader_feature CRT_2D CRT_3D CRT_CUBE
			#pragma shader_feature _ USE_CUSTOM_UV
			#pragma shader_feature _TILINGMODE_NONE _TILINGMODE_TILED

			// This macro will declare a version for each dimention (2D, 3D and Cube)
			TEXTURE_SAMPLER_X(_UV);
			float _Octaves;
			float2 _OutputRange;
			float _Lacunarity;
			float _Frequency;
			float _Persistance;
			float _CellsMode;
			int _Seed;

			float4 mixture (v2f_customrendertexture i) : SV_Target
			{
				float3 uvs = GetNoiseUVs(i, SAMPLE_X(_UV, i.localTexcoord.xyz, i.direction), _Seed);

#ifdef CRT_2D
				float4 noise = GenerateRidgedCellular2DNoise(uvs, _Frequency, _Octaves, _Persistance, _Lacunarity).rgbr;
#else
				float4 noise = GenerateRidgedCellular3DNoise(uvs, _Frequency, _Octaves, _Persistance, _Lacunarity).rgbr;
#endif

				switch (_CellsMode)
				{
					default:
					case 0: noise = noise.xxxx; break;
					case 1: noise = noise.yyyy; break;
					case 2: noise = noise.zzzz; break;
				}

				return RemapClamp(noise, 0, 1, _OutputRange.x, _OutputRange.y);
			}
			ENDCG
		}
	}
}
