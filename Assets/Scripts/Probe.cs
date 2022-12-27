using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.Rendering;

public struct Surfel
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector3 Albedo;
    public float skyMask;
}
public enum ProbeDebugMode
{
    None = 0,
    SphereDistribution = 1,
    SampleDirection = 2,
    Surfel = 3,
    SurfelRadiance = 4
}
[ExecuteAlways]
public class Probe : MonoBehaviour
{
    const int tX = 32;
    const int tY = 16;
    const int rayNum = tX * tY;
    const int surfelByteSize = 3 * 12 + 4; //sizeof(Surfel)

    MaterialPropertyBlock matPropBlock;

    public Surfel[] readBackBuffer;// CPU side surfel array, for debug
    public ComputeBuffer surfels; // GPU side surfel array

    Vector3[] radianceDebugBuffer;
    public ComputeBuffer surfelRadiance;

    const int coefficientSH9ByteSize = 9 * 3 * 4;
    int[] coefficientClearValue;
    public ComputeBuffer coefficientSH9;

    public RenderTexture RT_WorldPos;
    public RenderTexture RT_Normal;
    public RenderTexture RT_Albedo;

    public ComputeShader surfelSampleCS;
    public ComputeShader surfelRadianceCS;

    public ProbeDebugMode debugMode;

    [HideInInspector]
    public int indexInProbeVolume = -1; // set by parent
    ComputeBuffer tempBuffer;

    private void Start()
    {
        TryInit();
    }

    //for debug
    public void TryInit()
    {
        if (surfels == null)
            surfels = new ComputeBuffer(rayNum, surfelByteSize);

        if (readBackBuffer == null)
        {
            readBackBuffer = new Surfel[rayNum];
        }

        if (surfelRadiance == null)
        {
            surfelRadiance = new ComputeBuffer(rayNum, sizeof(float) * 3);
        }

        if (radianceDebugBuffer == null)
        {
            radianceDebugBuffer = new Vector3[rayNum];
        }

        if (matPropBlock == null)
        {
            matPropBlock = new MaterialPropertyBlock();
        }

        if(coefficientSH9 == null)
        {
            coefficientSH9 = new ComputeBuffer(27, sizeof(int));
            coefficientClearValue = new int[27];
            for(int i=0; i<27; i++) coefficientClearValue[i] = 0;
        }

        if (tempBuffer == null)
            tempBuffer = new ComputeBuffer(1, 4);
    }
    void OnDestroy()
    {
        if (surfels != null) surfels.Release();
        if (coefficientSH9 != null) coefficientSH9.Release();
        if (surfelRadiance != null) surfelRadiance.Release();
        if (tempBuffer != null) tempBuffer.Release();
    }
    // for DEBUG
    void OnDrawGizmos()
    {
        Vector3 probePos = gameObject.transform.position;

        //irradiance debug sphere
        gameObject.GetComponent<MeshRenderer>().enabled =! Application.isPlaying;
        MeshRenderer meshRenderer = gameObject.GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial.shader = Shader.Find("Custom/SHDebug");
        matPropBlock.SetBuffer("_coefficientSH9", coefficientSH9);
        meshRenderer.SetPropertyBlock(matPropBlock);

        if (debugMode == ProbeDebugMode.None) return;

        // read back the result from CS
        surfels.GetData(readBackBuffer);
        surfelRadiance.GetData(radianceDebugBuffer);

        for(int i = 0; i < rayNum; i++)
        {
            Surfel surfel = readBackBuffer[i];
            Vector3 radiance = radianceDebugBuffer[i];

            Vector3 surfelPos = surfel.Position;
            Vector3 surfelnormal = surfel.Normal;
            Vector3 surfelcolor = surfel.Albedo;

            Vector3 dir = surfelPos - probePos;
            dir = Vector3.Normalize(dir);

            bool isSky = surfel.skyMask >= 0.995;

            Gizmos.color = Color.white;

            if (debugMode == ProbeDebugMode.SphereDistribution)
            {
                if (isSky) Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(probePos + dir, 0.025f);
            }

            if(debugMode == ProbeDebugMode.SampleDirection)
            {
                if(isSky)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(probePos, probePos + dir * 25.0f);
                }
                else
                {
                    Gizmos.DrawLine(probePos, surfelPos);
                    Gizmos.DrawSphere(surfelPos, 0.05f);
                }
            }

            if(debugMode == ProbeDebugMode.Surfel)
            {
                if (isSky) continue;
                Gizmos.DrawSphere(surfelPos, 0.05f);
                Gizmos.DrawLine(surfelPos, surfelPos + surfelnormal * 0.25f);
            }

            if (debugMode == ProbeDebugMode.SurfelRadiance)
            {
                if (isSky) continue;
                Gizmos.color = new Color(radiance.x, radiance.y, radiance.z);
                Gizmos.DrawSphere(surfelPos, 0.05f);
            }
        }
    }

    void BatchSetShader(GameObject[] gameObjects, Shader shader)
    {
        foreach(var obj in gameObjects)
        {
            MeshRenderer mR = obj.GetComponent<MeshRenderer>();
            if(mR != null)
            {
                //mR.sharedMaterial.shader = shader;
                foreach (var mat in mR.sharedMaterials)
                {
                    mat.shader = shader;
                }
            }
        }
    }
 
    public void CaptureGbufferCubemaps()
    {
        TryInit();
       //creat camera
        GameObject cam = new GameObject("CubemapCamera");
        cam.transform.position = transform.position;
        cam.transform.rotation = Quaternion.identity;
        cam.AddComponent<Camera>();

        Camera camera = cam.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);

        //find all gameobj
        GameObject[] objs = FindObjectsOfType(typeof(GameObject)) as GameObject[];
        //objs = FindObjectsOfType<GameObject>();

        //capture gbuffer worldpos
        BatchSetShader(objs, Shader.Find("Custom/GbufferWorldPos"));
        camera.RenderToCubemap(RT_WorldPos);

        //capture gbuffer normal
        BatchSetShader(objs, Shader.Find("Custom/GbufferNormal"));
        camera.RenderToCubemap(RT_Normal);

        //capture gbuffer albedo
        BatchSetShader(objs, Shader.Find("Universal Render Pipeline/Unlit"));
        camera.RenderToCubemap(RT_Albedo);

        //reset shader
        BatchSetShader(objs, Shader.Find("Universal Render Pipeline/Lit"));

        SampleSurfels(RT_WorldPos, RT_Normal, RT_Albedo);

        DestroyImmediate(cam);
    }

    void SampleSurfels(RenderTexture worldPosCubemap, RenderTexture normalCubemap, RenderTexture albedoCubemap)
    {
        var kernel = surfelSampleCS.FindKernel("CSMain");
        Vector3 p = gameObject.transform.position;
        surfelSampleCS.SetVector("_probePos", new Vector4(p.x, p.y, p.z, 1.0f));
        surfelSampleCS.SetFloat("_randSeed", UnityEngine.Random.Range(0.0f, 1.0f));
        surfelSampleCS.SetTexture(kernel, "_worldPosCubemap", worldPosCubemap);
        surfelSampleCS.SetTexture(kernel, "_normalCubemap", normalCubemap);
        surfelSampleCS.SetTexture(kernel, "_albedoCubemap", albedoCubemap);
        surfelSampleCS.SetBuffer(kernel, "_surfels", surfels);

        surfelSampleCS.Dispatch(kernel, 1, 1, 1);

        surfels.GetData(readBackBuffer);
    }

    public void SurfelRadiance(CommandBuffer cmd)
    {
        var kernel = surfelRadianceCS.FindKernel("CSMain");

        Vector3 probePos = gameObject.transform.position;
        cmd.SetComputeVectorParam(surfelRadianceCS, "_probePos", new Vector4(probePos.x, probePos.y, probePos.z, 1.0f));
        cmd.SetComputeBufferParam(surfelRadianceCS, kernel, "_surfels", surfels);
        cmd.SetComputeBufferParam(surfelRadianceCS, kernel, "_surfelRadiance", surfelRadiance);
        cmd.SetComputeBufferParam(surfelRadianceCS, kernel, "_coefficientSH9", coefficientSH9);

        var parent = transform.parent;
        ProbeVolume probeVolume = parent==null?null:parent.gameObject.GetComponent<ProbeVolume>();
        ComputeBuffer coefficientVoxel = probeVolume==null?tempBuffer:probeVolume.coefficientVoxel;
        cmd.SetComputeBufferParam(surfelRadianceCS, kernel, "_coefficientVoxel", coefficientVoxel);
        cmd.SetComputeIntParam(surfelRadianceCS, "_indexInProbeVolume", indexInProbeVolume);
        //start CS
        cmd.SetBufferData(coefficientSH9, coefficientClearValue);
        cmd.DispatchCompute(surfelRadianceCS, kernel, 1, 1, 1);

        //int[] debug9 = new int[27];
        //coefficientSH9.GetData(debug9);
        //foreach (var item in debug9)
        //{
        //    Debug.Log(item);
        //}
        //Debug.Log("-------------------------------------------");
    }
}
