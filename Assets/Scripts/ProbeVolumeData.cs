using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;

[Serializable]
[CreateAssetMenu(fileName = "ProbeVolumeData", menuName = "ProbeVolumeData")]
public class ProbeVolumeData : ScriptableObject
{
    [SerializeField]
    public Vector3 volumePosition;

    [SerializeField]
    public float[] surfelStorageBuffer;

    public void StorageSurfelData(ProbeVolume volume)
    {
        int probeNum = volume.probeSizeX * volume.probeSizeY * volume.probeSizeZ;
        int surfelPerProbe = 512;
        int floatPerSurfel = 10;
        Array.Resize<float>(ref surfelStorageBuffer, probeNum * surfelPerProbe * floatPerSurfel);
        int j = 0;
        for (int i = 0; i < volume.probes.Length; i++)
        {
            Probe probe = volume.probes[i].GetComponent<Probe>();
            foreach (var surfel in probe.readBackBuffer)
            {
                surfelStorageBuffer[j++] = surfel.Position.x;
                surfelStorageBuffer[j++] = surfel.Position.y;
                surfelStorageBuffer[j++] = surfel.Position.z;
                surfelStorageBuffer[j++] = surfel.Normal.x;
                surfelStorageBuffer[j++] = surfel.Normal.y;
                surfelStorageBuffer[j++] = surfel.Normal.z;
                surfelStorageBuffer[j++] = surfel.Albedo.x;
                surfelStorageBuffer[j++] = surfel.Albedo.y;
                surfelStorageBuffer[j++] = surfel.Albedo.z;
                surfelStorageBuffer[j++] = surfel.skyMask;
            }
        }
        volumePosition = volume.gameObject.transform.position;

        EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();
    }

    // load surfel data from storage
    public void TryLoadSurfelData(ProbeVolume volume)
    {
        int probeNum = volume.probeSizeX * volume.probeSizeY * volume.probeSizeZ;
        int surfelPerProbe = 512;
        int floatPerSurfel = 10;
        bool dataDirty = surfelStorageBuffer.Length != probeNum * surfelPerProbe * floatPerSurfel;
        bool posDirty = volume.gameObject.transform.position != volumePosition;
        if (posDirty || dataDirty)
        {
            Debug.LogWarning("volume data is old! please re capture!");
            Debug.LogWarning("探针组数据需要重新捕获");
            return;
        }

        int j = 0;
        foreach (var go in volume.probes)
        {
            Probe probe = go.GetComponent<Probe>();
            for (int i = 0; i < probe.readBackBuffer.Length; i++)
            {
                probe.readBackBuffer[i].Position.x = surfelStorageBuffer[j++];
                probe.readBackBuffer[i].Position.y = surfelStorageBuffer[j++];
                probe.readBackBuffer[i].Position.z = surfelStorageBuffer[j++];
                probe.readBackBuffer[i].Normal.x = surfelStorageBuffer[j++];
                probe.readBackBuffer[i].Normal.y = surfelStorageBuffer[j++];
                probe.readBackBuffer[i].Normal.z = surfelStorageBuffer[j++];
                probe.readBackBuffer[i].Albedo.x = surfelStorageBuffer[j++];
                probe.readBackBuffer[i].Albedo.y = surfelStorageBuffer[j++];
                probe.readBackBuffer[i].Albedo.z = surfelStorageBuffer[j++];
                probe.readBackBuffer[i].skyMask = surfelStorageBuffer[j++];
            }
            probe.surfels.SetData(probe.readBackBuffer);
        }
    }
}
