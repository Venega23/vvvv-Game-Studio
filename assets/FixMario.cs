using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public class FixMario
{
    static FixMario()
    {
        EditorApplication.delayCall += () => {
            if (!EditorPrefs.GetBool("MarioFixDone", false))
            {
                Go();
                EditorPrefs.SetBool("MarioFixDone", true);
            }
        };
    }

    [MenuItem("Tools/Fix Mario Size")]
    public static void Go()
    {
        var marioPath = "Assets/mario/Mario.fbx";
        var importer = AssetImporter.GetAtPath(marioPath) as ModelImporter;
        if (importer != null)
        {
            float oldScale = importer.globalScale;
            // Usually Mixamo models are 0.01 if they are small, we want to scale up by 100
            // Or if it's 1.0, maybe make it 100? Let's just multiply by 100 if it's smaller than 10.
            if (importer.globalScale < 10f)
            {
                importer.globalScale *= 100f;
            }
            importer.SaveAndReimport();
            Debug.Log($"Changed Mario scale from {oldScale} to {importer.globalScale}");
        }
        else
        {
            Debug.LogError("Could not find Mario.fbx");
        }
    }
}
