using UnityEngine;

[DisallowMultipleComponent]
public class CharacterVisualGrounder : MonoBehaviour
{
    public Transform visualRoot;
    public float groundOffset = 0.015f;
    public bool onlyLowerWhenFloating = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttachLuigiGrounder()
    {
        var luigi = GameObject.Find("Luigi");
        if (luigi == null)
        {
            return;
        }

        var grounder = luigi.GetComponent<CharacterVisualGrounder>() ?? luigi.AddComponent<CharacterVisualGrounder>();
        if (grounder.visualRoot == null)
        {
            var model = luigi.transform.Find("LuigiModel");
            if (model != null)
            {
                grounder.visualRoot = model;
            }
        }

        grounder.groundOffset = 0.015f;
        grounder.onlyLowerWhenFloating = true;
    }

    private void LateUpdate()
    {
        if (visualRoot == null || !TryGetVisualBounds(visualRoot, out var bounds))
        {
            return;
        }

        var targetMinY = transform.position.y + groundOffset;
        var deltaY = targetMinY - bounds.min.y;
        if (onlyLowerWhenFloating && deltaY > 0f)
        {
            return;
        }

        if (Mathf.Abs(deltaY) > 0.001f)
        {
            visualRoot.position += Vector3.up * deltaY;
        }
    }

    private static bool TryGetVisualBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        var hasBounds = false;
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || IsEmptyRenderer(renderer))
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    private static bool IsEmptyRenderer(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinned)
        {
            return skinned.sharedMesh == null || skinned.sharedMesh.vertexCount == 0;
        }

        var filter = renderer.GetComponent<MeshFilter>();
        return filter != null && (filter.sharedMesh == null || filter.sharedMesh.vertexCount == 0);
    }
}
