using System;
using System.Linq;
using UnityEngine;

[ExecuteAlways]
public class MiraHeadNpc : MonoBehaviour
{
    [Header("Scale")]
    [Min(0.05f)] public float size = 3.2f;
    public Transform modelRoot;

    [Header("Placement")]
    public float groundY = 0f;
    public bool keepHeadOnGround = true;
    public bool buryLowerHair = true;
    public float surfaceYOffset = 0f;

    [Header("Visibility")]
    public bool hideBody = true;
    public bool hideBackpackAndTools = true;

    [Header("Face")]
    [Range(0f, 100f)] public float mouthOpen = 0f;
    [Range(0f, 100f)] public float eyesWide = 0f;
    [Range(0f, 100f)] public float angryBrows = 0f;
    [Range(0f, 100f)] public float smile = 0f;

    [Header("Debug")]
    [SerializeField] private int affectedBlendShapes;

    private void Reset()
    {
        modelRoot = FindModelRoot();
    }

    private void OnValidate()
    {
        Apply();
    }

    private void OnEnable()
    {
        Apply();
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            Apply();
        }
    }

    public void Apply()
    {
        if (modelRoot == null)
        {
            modelRoot = FindModelRoot();
        }

        transform.localScale = Vector3.one * Mathf.Max(0.05f, size);

        RemoveGeneratedExpressionRig();
        ApplyVisibility();
        ApplyFaceBlendShapes();

        if (keepHeadOnGround)
        {
            AlignToGround();
        }
    }

    private Transform FindModelRoot()
    {
        for (var i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            if (child.name != "MiraExpressionRig")
            {
                return child;
            }
        }

        return null;
    }

    private void ApplyVisibility()
    {
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            var name = renderer.name.ToLowerInvariant();
            var isBody = name.Contains("body") || name.Contains("lod0") && !IsHeadPart(name);
            var isTool = name.Contains("backpack") || name.Contains("pack") || name.Contains("axe") || name.Contains("pickaxe");
            renderer.enabled = !(hideBody && isBody) && !(hideBackpackAndTools && isTool);
        }
    }

    private void ApplyFaceBlendShapes()
    {
        affectedBlendShapes = 0;

        foreach (var skinned in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = skinned.sharedMesh;
            if (mesh == null || mesh.blendShapeCount == 0)
            {
                continue;
            }

            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                var shape = mesh.GetBlendShapeName(i).ToLowerInvariant();

                if (Matches(shape, "mouth", "jaw", "open", "aa", "oh"))
                {
                    skinned.SetBlendShapeWeight(i, mouthOpen);
                    affectedBlendShapes++;
                }
                else if (Matches(shape, "eye", "wide", "surprise", "blink"))
                {
                    skinned.SetBlendShapeWeight(i, shape.Contains("blink") ? 100f - eyesWide : eyesWide);
                    affectedBlendShapes++;
                }
                else if (Matches(shape, "brow", "angry", "frown"))
                {
                    skinned.SetBlendShapeWeight(i, angryBrows);
                    affectedBlendShapes++;
                }
                else if (Matches(shape, "smile", "happy", "grin"))
                {
                    skinned.SetBlendShapeWeight(i, smile);
                    affectedBlendShapes++;
                }
            }
        }
    }

    private void AlignToGround()
    {
        if (modelRoot == null)
        {
            return;
        }

        var bounds = buryLowerHair ? SurfaceHeadBounds() : VisibleHeadBounds();
        if (!bounds.HasValue)
        {
            return;
        }

        var delta = new Vector3(
            transform.position.x - bounds.Value.center.x,
            groundY + surfaceYOffset - bounds.Value.min.y,
            transform.position.z - bounds.Value.center.z
        );
        modelRoot.position += delta;
    }

    private Bounds? VisibleHeadBounds()
    {
        var renderers = GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer.enabled && IsHeadPart(renderer.name.ToLowerInvariant()))
            .ToArray();

        if (renderers.Length == 0)
        {
            return null;
        }

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }

    private Bounds? SurfaceHeadBounds()
    {
        var renderers = GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer.enabled && IsSurfaceAnchorPart(renderer.name.ToLowerInvariant()))
            .ToArray();

        if (renderers.Length == 0)
        {
            return VisibleHeadBounds();
        }

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }

    private void RemoveGeneratedExpressionRig()
    {
        var rig = transform.Find("MiraExpressionRig");
        if (rig == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(rig.gameObject);
        }
        else
        {
            DestroyImmediate(rig.gameObject);
        }
    }

    private static bool IsHeadPart(string name)
    {
        return name.Contains("head") ||
               name.Contains("face") ||
               name.Contains("eye") ||
               name.Contains("hair") ||
               name.Contains("strand");
    }

    private static bool IsSurfaceAnchorPart(string name)
    {
        return name.Contains("head") ||
               name.Contains("face") ||
               name.Contains("eye");
    }

    private static bool Matches(string value, params string[] tokens)
    {
        return tokens.Any(value.Contains);
    }
}
