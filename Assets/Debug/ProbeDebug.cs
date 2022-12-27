using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;


[CustomEditor(typeof(Probe))]
public class ProbeDebug : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        if (GUILayout.Button("Probe Capture"))
        {
            Probe probe = (Probe)target;
            probe.CaptureGbufferCubemaps();
        }
    }
    void BatchSetShader(GameObject[] gameObjects, Shader shader)
    {
        foreach (var go in gameObjects)
        {
            MeshRenderer meshRenderer = go.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                foreach(var mat in meshRenderer.materials)
                {
                    mat.shader = shader;
                }
            }
        }
    }
}
