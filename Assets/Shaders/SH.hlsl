#define k01 0.2820947918f    // sqrt(  1/PI)/2
#define k02 0.4886025119f    // sqrt(  3/PI)/2
#define k03 1.0925484306f    // sqrt( 15/PI)/2
#define k04 0.3153915652f    // sqrt(  5/PI)/4
#define k05 0.5462742153f    // sqrt( 15/PI)/4

#define A0 3.1415f
#define A1 2.0943f
#define A2 0.7853f

float SH(in int l, in int m, in float3 dir)
{
	float x = dir.x;
	float y = dir.y;
	float z = dir.z;

	if(l == 0) return k01;

	if(l == 1 && m == -1) return k01 * y;
	if(l == 1 && m == 0) return k01 * z;
	if(l == 1 && m == 1) return k01 * x;

	if(l == 2 && m == -2) return k03 * x * y;
	if(l == 2 && m == -1) return k03 * y * z;
	if(l == 2 && m == 0) return k04 * (2*z*z - x*x - y*y);
	if(l == 2 && m == 1) return k03 * z * x;
	if(l == 2 && m == 2) return k05 * (x*x - y*y);

	return 0.0f;
}

//decode irradiance
float3 IrradianceSH9(in float3 c[9], in float3 dir)
{
	float3 irradiance = float3(0, 0, 0);
	irradiance += SH(0,  0, dir)*c[0]*A0;
	irradiance += SH(1, -1, dir)*c[1]*A1;
	irradiance += SH(1,  0, dir)*c[2]*A1;
	irradiance += SH(1,  1, dir)*c[3]*A1;
	irradiance += SH(2, -2, dir)*c[4]*A2;
	irradiance += SH(2, -1, dir)*c[5]*A2;
	irradiance += SH(2,  0, dir)*c[6]*A2;
	irradiance += SH(2,  1, dir)*c[7]*A2;
	irradiance += SH(2,  2, dir)*c[8]*A2;

	irradiance = max(float3(0,0,0), irradiance);
	return irradiance;
}
#define _SCALE 100000.0
//int and float 
int FloatToInt(float f)
{
	return int(f * _SCALE);
}
float IntToFloat(int i)
{
	return float(i) / _SCALE;
}

int3 GetProbeIndex3DFromWorldPos(float3 worldPos, float4 _coefficientVoxelSize, float _coefficientVoxelGridSize, float4 _coefficientVoxelCorner)
{
    float3 probeIndexF = floor((worldPos.xyz - _coefficientVoxelCorner.xyz) / _coefficientVoxelGridSize);
    int3 probeIndex3 = int3(probeIndexF.x, probeIndexF.y, probeIndexF.z);
    return probeIndex3;
}

int GetProbeIndex1DFromIndex3D(int3 probeIndex3, float4 _coefficientVoxelSize)
{
    int probeIndex = probeIndex3.x * _coefficientVoxelSize.y * _coefficientVoxelSize.z
                    + probeIndex3.y * _coefficientVoxelSize.z 
                    + probeIndex3.z;
    return probeIndex;
}

bool IsIndex3DInsideVoxel(int3 probeIndex3, float4 _coefficientVoxelSize)
{
    bool isInsideVoxelX = 0 <= probeIndex3.x && probeIndex3.x < _coefficientVoxelSize.x;
    bool isInsideVoxelY = 0 <= probeIndex3.y && probeIndex3.y < _coefficientVoxelSize.y;
    bool isInsideVoxelZ = 0 <= probeIndex3.z && probeIndex3.z < _coefficientVoxelSize.z;
    bool isInsideVoxel = isInsideVoxelX && isInsideVoxelY && isInsideVoxelZ;
    return isInsideVoxel;
}

void DecodeSHCoefficientFromVoxel(inout float3 c[9], in StructuredBuffer<int> _coefficientVoxel, int probeIndex)
{
    const int coefficientByteSize = 27; // 3x9 for SH9 RGB
    int offset = probeIndex * coefficientByteSize;   
    for(int i=0; i<9; i++)
    {
        c[i].x = IntToFloat(_coefficientVoxel[offset + i*3+0]);
        c[i].y = IntToFloat(_coefficientVoxel[offset + i*3+1]);
        c[i].z = IntToFloat(_coefficientVoxel[offset + i*3+2]);
    }
}

float3 GetProbePositionFromIndex3D(int3 probeIndex3, float _coefficientVoxelGridSize, float4 _coefficientVoxelCorner)
{
    float3 res = float3(probeIndex3.x, probeIndex3.y, probeIndex3.z) * _coefficientVoxelGridSize + _coefficientVoxelCorner.xyz;
    return res;
}

float3 TrilinearInterpolationFloat3(in float3 value[8], float3 rate)
{
    float3 a = lerp(value[0], value[4], rate.x);    // 000, 100
    float3 b = lerp(value[2], value[6], rate.x);    // 010, 110
    float3 c = lerp(value[1], value[5], rate.x);    // 001, 101
    float3 d = lerp(value[3], value[7], rate.x);    // 011, 111
    float3 e = lerp(a, b, rate.y);
    float3 f = lerp(c, d, rate.y);
    float3 g = lerp(e, f, rate.z); 
    return g;
}

float3 SampleSHVoxel(
    in float4 worldPos, 
    in float3 albedo, 
    in float3 normal,
    in StructuredBuffer<int> _coefficientVoxel,
    in float _coefficientVoxelGridSize,
    in float4 _coefficientVoxelCorner,
    in float4 _coefficientVoxelSize
    )
{
    // probe grid index for current fragment
    int3 probeIndex3 = GetProbeIndex3DFromWorldPos(worldPos, _coefficientVoxelSize, _coefficientVoxelGridSize, _coefficientVoxelCorner);
    int3 offset[8] = {
        int3(0, 0, 0), int3(0, 0, 1), int3(0, 1, 0), int3(0, 1, 1), 
        int3(1, 0, 0), int3(1, 0, 1), int3(1, 1, 0), int3(1, 1, 1), 
    };

    float3 c[9];
    float3 Lo[8] = { float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0)};
    float3 BRDF = albedo / 3.1415926;
    float weight = 0.0005;

    // near 8 probes
    for(int i=0; i<8; i++)
    {
        int3 idx3 = probeIndex3 + offset[i];
        bool isInsideVoxel = IsIndex3DInsideVoxel(idx3, _coefficientVoxelSize);
        if(!isInsideVoxel) 
        {
            Lo[i] = float3(0, 0, 0);
            continue;
        }

        // normal weight blend
        float3 probePos = GetProbePositionFromIndex3D(idx3, _coefficientVoxelGridSize, _coefficientVoxelCorner);
        float3 dir = normalize(probePos - worldPos.xyz);
        float normalWeight = saturate(dot(dir, normal));
        weight += normalWeight;

        // decode SH9
        int probeIndex = GetProbeIndex1DFromIndex3D(idx3, _coefficientVoxelSize);
        DecodeSHCoefficientFromVoxel(c, _coefficientVoxel, probeIndex);
        Lo[i] = IrradianceSH9(c, normal) * BRDF * normalWeight;      
    }

    // trilinear interpolation
    float3 minCorner = GetProbePositionFromIndex3D(probeIndex3, _coefficientVoxelGridSize, _coefficientVoxelCorner);
    float3 maxCorner = minCorner + float3(1, 1, 1) * _coefficientVoxelGridSize;
    float3 rate = (worldPos - minCorner) / _coefficientVoxelGridSize;
    float3 color = TrilinearInterpolationFloat3(Lo, rate) / weight;
    
    return color;
}
