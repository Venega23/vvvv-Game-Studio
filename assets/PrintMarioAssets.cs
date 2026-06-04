using UnityEditor;
using UnityEngine;
using System.IO;

[InitializeOnLoad]
public class PrintMarioAssets
{
    static PrintMarioAssets()
    {
        EditorApplication.delayCall += () => {
            if (!EditorPrefs.GetBool("MarioPrintDone", false))
            {
                Go();
                EditorPrefs.SetBool("MarioPrintDone", true);
            }
        };
    }

    public static void Go()
    {
        var modelPath = "Assets/mario/Mario.fbx";
        var allAssets = AssetDatabase.LoadAllAssetsAtPath(modelPath);
        string output = $"Found {allAssets.Length} assets inside {modelPath}\n";
        foreach (var asset in allAssets)
        {
            if (asset is Material)
            {
                output += $"MATERIAL: {asset.name}\n";
            }
            else if (asset is GameObject go)
            {
                output += $"GAMEOBJECT: {go.name}\n";
            }
            else if (asset is Mesh mesh)
            {
                output += $"MESH: {mesh.name} submeshes: {mesh.subMeshCount}\n";
            }
        }
        File.WriteAllText("E:/Unity/vvvvv/Assets/mario/mats.txt", output);
        Debug.Log("Wrote mats.txt");
    }
}
