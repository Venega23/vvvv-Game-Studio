using UnityEditor;
using UnityEngine;
using System.IO;

[InitializeOnLoad]
public class AutoTextureMario
{
    static AutoTextureMario()
    {
        EditorApplication.delayCall += () => {
            if (!EditorPrefs.GetBool("MarioTextureDone", false))
            {
                Go();
                EditorPrefs.SetBool("MarioTextureDone", true);
            }
        };
    }

    [MenuItem("Tools/Auto Texture Mario")]
    public static void Go()
    {
        var modelPath = "Assets/mario/Mario.fbx";
        var textureFolder = "Assets/mario/textures";
        
        var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (importer != null)
        {
            // Set material extraction
            if (importer.materialLocation != ModelImporterMaterialLocation.InPrefab)
            {
                importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
                importer.materialSearch = ModelImporterMaterialSearch.Local;
                importer.SaveAndReimport();
            }

            if (!AssetDatabase.IsValidFolder("Assets/mario/Materials"))
            {
                AssetDatabase.CreateFolder("Assets/mario", "Materials");
            }

            // Extract materials manually
            var allAssets = AssetDatabase.LoadAllAssetsAtPath(modelPath);
            foreach (var asset in allAssets)
            {
                if (asset is Material mat && !AssetDatabase.IsMainAsset(mat))
                {
                    string newMatPath = "Assets/mario/Materials/" + mat.name + ".mat";
                    newMatPath = AssetDatabase.GenerateUniqueAssetPath(newMatPath);
                    AssetDatabase.ExtractAsset(mat, newMatPath);
                }
            }
            AssetDatabase.Refresh();

            Debug.Log("Materials extracted. Now searching for matching textures...");

            // Try to map extracted materials
            string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/mario/Materials" });
            string[] texGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { textureFolder });

            foreach (var matGuid in matGuids)
            {
                var matPath = AssetDatabase.GUIDToAssetPath(matGuid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null) continue;

                // Strip suffix like (Instance) or _mat if any, just check if tex path contains mat name
                string matName = mat.name.ToLower().Replace("_mat", "").Replace(" material", "");
                
                foreach (var texGuid in texGuids)
                {
                    var texPath = AssetDatabase.GUIDToAssetPath(texGuid);
                    var texName = Path.GetFileNameWithoutExtension(texPath).ToLower();
                    
                    if (texName.Contains(matName) || matName.Contains(texName))
                    {
                        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
                        Debug.Log($"Assigned texture {tex.name} to material {mat.name}");
                        EditorUtility.SetDirty(mat);
                        break;
                    }
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Finished mapping textures to Mario!");
        }
    }
}
