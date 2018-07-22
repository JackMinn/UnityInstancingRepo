// Unity built-in shader source. Copyright (c) 2016 Unity Technologies. MIT license (see license.txt)

Shader "JacksInstancing/BlitCopyDepth" {
	SubShader
	{
		Tags{ "RenderType" = "Opaque" }

		Pass
	{
		ZTest Always Cull Off Zwrite On

		CGPROGRAM
		// Required to compile gles 2.0 with standard srp library
#pragma vertex vert
#pragma fragment frag

#include "UnityCG.cginc"

		sampler2D _CameraDepthTexture;

	struct VertexInput
	{
		float4 vertex   : POSITION;
		float2 uv       : TEXCOORD0;
	};

	struct VertexOutput
	{
		float4 position : SV_POSITION;
		float2 uv       : TEXCOORD0;
	};

	VertexOutput vert(VertexInput i)
	{
		VertexOutput o;
		o.uv = i.uv;
		o.position = UnityObjectToClipPos(i.vertex.xyz);
		return o;
	}

	float frag(VertexOutput i) : SV_Depth
	{
		float depth = tex2D(_CameraDepthTexture, UnityStereoTransformScreenSpaceTex(i.uv)).r;
		return depth;
	}
		ENDCG
	}
	}
}
