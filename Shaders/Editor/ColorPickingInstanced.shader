
Shader "JacksInstancing/ColorPickerShader" {
	Properties{
		[Enum(Off,0,Front,1,Back,2)] _CullMode("Cull Mode", Int) = 0
		_ObjectID("Object ID", Float) = 255
	}
		SubShader{
		Tags{ "RenderType" = "Opaque" }
		LOD 200
		ZWrite On
		Cull[_CullMode]
		ZTest LEqual

		Pass
	{
		CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 4.5


	float _ObjectID;

	struct appdata
	{
		float4 vertex : POSITION;
	};

	struct v2f
	{
		float4 pos : SV_POSITION;
		float3 instanceID : TEXCOORD0;
	};

	struct InstanceData
	{
		float4x4 objectToWorld;
		float4x4 worldToObject;
		float4 instanceColor;
	};

#if SHADER_TARGET >= 45
	StructuredBuffer<InstanceData> batchDataBuffer;
	StructuredBuffer<float> instanceIDBuffer;
#endif

	//returns 3 floats in [0,1] that can be written to 3 8 bit channels, max value is 2^24
	float3 packFloatToFloat3(float val) {
		float3 enc = floor(float3(1, 1 / 255.0, 1 / 65025.0) * val);
		enc = fmod(enc, 255);
		enc /= 255;
		return enc;

		//float firstEight = fmod(val, 255.0);
		//float secondEight = fmod(floor(val / 256.0), 255.0);
		//float thirdEight = fmod(floor((val / 65536.0), 255.0);

		//float3 packed = float3(firstEight, secondEight, thirdEight) / 255.0;

		//packed = val / 255;
		//return packed;
	}

	v2f vert(appdata v, uint instanceID : SV_InstanceID)
	{
#if SHADER_TARGET >= 45
		float4x4 objectToWorld = batchDataBuffer[instanceID].objectToWorld;
#else
		float4x4 objectToWorld = 0;
#endif

		v2f o;
		float4 worldPosition = float4(mul(objectToWorld, float4(v.vertex.xyz, 1)).xyz, 1);
		o.pos = mul(UNITY_MATRIX_VP, worldPosition);

		o.instanceID = packFloatToFloat3(instanceIDBuffer[instanceID]);
		return o;
	}

	float4 frag(v2f i) : SV_Target
	{
		float4 col;
		col.rgb = i.instanceID.rgb;
		col.a = _ObjectID * (1 / 255.0);
		return col;
	}

		ENDCG
	}
	}

		FallBack "Diffuse"
}
