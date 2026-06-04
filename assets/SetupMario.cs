using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public class SetupMario
{
    static SetupMario()
    {
        EditorApplication.delayCall += () => {
            if (!EditorPrefs.GetBool("MarioSetupDone", false))
            {
                Go();
                EditorPrefs.SetBool("MarioSetupDone", true);
            }
        };
    }

    [MenuItem("Tools/Setup Mario")]
    public static void Go()
    {
        var marioPath = "Assets/mario/Mario.fbx";
        
        // 1. Set to Humanoid
        var importer = AssetImporter.GetAtPath(marioPath) as ModelImporter;
        if (importer != null)
        {
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.SaveAndReimport();
                Debug.Log("Set Mario to Humanoid");
            }
        }

        // 2. Load the asset
        var marioAsset = AssetDatabase.LoadAssetAtPath<GameObject>(marioPath);
        if (marioAsset == null)
        {
            Debug.LogError("Could not load Mario.fbx");
            return;
        }

        // 3. Find Zoey to copy components from
        var zoey = GameObject.Find("Zoey");
        if (zoey == null)
        {
            Debug.LogError("Could not find Zoey in the scene to copy from.");
            return;
        }

        // 4. Instantiate Mario
        var marioInstance = Object.Instantiate(marioAsset);
        marioInstance.name = "Mario";
        marioInstance.transform.position = zoey.transform.position + Vector3.right * 1.5f;

        // 5. Copy components (PlayerMove, CharacterController, Animator settings, etc.)
        // CharacterController
        var ccSource = zoey.GetComponent<CharacterController>();
        var ccDest = marioInstance.AddComponent<CharacterController>();
        if (ccSource != null)
        {
            ccDest.center = ccSource.center;
            ccDest.radius = ccSource.radius;
            ccDest.height = ccSource.height;
        }
        else
        {
            ccDest.center = new Vector3(0, 0.9f, 0);
            ccDest.radius = 0.28f;
            ccDest.height = 1.8f;
        }

        // Animator
        var animSource = zoey.GetComponent<Animator>();
        var animDest = marioInstance.GetComponent<Animator>();
        if (animDest == null) animDest = marioInstance.AddComponent<Animator>();
        if (animSource != null)
        {
            animDest.runtimeAnimatorController = animSource.runtimeAnimatorController;
            animDest.applyRootMotion = animSource.applyRootMotion;
        }

        // PlayerMove
        var moveSource = zoey.GetComponent<PlayerMove>();
        var moveDest = marioInstance.AddComponent<PlayerMove>();
        // Just rely on PlayerMove default values for now, they are probably public fields.
        // We can do JsonUtility to deep copy if needed:
        if (moveSource != null)
        {
            var json = JsonUtility.ToJson(moveSource);
            JsonUtility.FromJsonOverwrite(json, moveDest);
        }

        // StarterAssetsAnimatorDriver or ProceduralAnimator
        var driverSource = zoey.GetComponent<RumiStarterAssetsAnimatorDriver>();
        if (driverSource != null)
        {
            var driverDest = marioInstance.AddComponent<RumiStarterAssetsAnimatorDriver>();
            var json = JsonUtility.ToJson(driverSource);
            JsonUtility.FromJsonOverwrite(json, driverDest);
            driverDest.controller = moveDest;
            driverDest.animator = animDest;
        }
        else
        {
            var procSource = zoey.GetComponent("RumiProceduralAnimator");
            if (procSource != null)
            {
                var procDest = marioInstance.AddComponent(procSource.GetType());
                var json = JsonUtility.ToJson(procSource);
                JsonUtility.FromJsonOverwrite(json, procDest);
            }
        }

        // Add RumiInteractionAnimationController
        marioInstance.AddComponent<RumiInteractionAnimationController>();

        // Select the new object
        Selection.activeGameObject = marioInstance;
        Debug.Log("Mario has been set up successfully!");
    }
}
