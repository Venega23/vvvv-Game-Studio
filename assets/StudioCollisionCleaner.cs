using System.Collections.Generic;
using System.Collections;
using UnityEngine;

public class StudioCollisionCleaner : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (FindAnyObjectByType<StudioCollisionCleaner>() != null)
        {
            return;
        }

        var cleaner = new GameObject("StudioCollisionCleaner");
        DontDestroyOnLoad(cleaner);
        cleaner.AddComponent<StudioCollisionCleaner>();
    }

    private IEnumerator Start()
    {
        for (var i = 0; i < 20; i++)
        {
            MakeStudioDecorNonBlocking();
            yield return null;
        }
    }

    private void Update()
    {
        if (Time.frameCount % 30 == 0)
        {
            MakeStudioDecorNonBlocking();
        }
    }

    private static void MakeStudioDecorNonBlocking()
    {
        // Logic disabled to prevent walls and objects from becoming non-solid.
        /*
        var playerColliders = new List<Collider>();
        ...
        */
    }

    private static bool ShouldStaySolid(Collider collider)
    {
        if (!collider.enabled)
        {
            return true;
        }

        var go = collider.gameObject;
        if (go.CompareTag("Player"))
        {
            return true;
        }

        var name = go.name.ToLowerInvariant();
        if (name == "plane" || name.Contains("floor") || name.Contains("ground") || name.Contains("terrain"))
        {
            return true;
        }

        if (go.GetComponentInParent<PlayerMove>() != null)
        {
            return true;
        }

        return false;
    }
}



