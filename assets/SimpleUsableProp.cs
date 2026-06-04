using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class SimpleUsableProp : MonoBehaviour
{
    public enum UseMode
    {
        None,
        Sit,
        Lie,
        Bath
    }

    public UseMode mode = UseMode.None;
    public float useDistance = 2.4f;
    public Transform usePoint;
    public AnimationClip useAnimation;
    public float sitGroundOffset = 0.02f;
    public float lieSurfaceOffset = 0.04f;
    public float poseNudgeSpeed = 0.75f;
    public float poseVerticalNudgeSpeed = 0.45f;
    public float poseRotateSpeed = 70f;
    public float poseFastMultiplier = 3f;
    public CameraFollow cameraFollow;

    private PlayerMove user;
    private GameObject userObject;
    private Vector3 previousPosition;
    private Quaternion previousRotation;
    private RumiInteractionAnimationController interactionAnimation;
    private bool wasInteractPressed;
    private Vector3 manualOffset;
    private float manualYaw;

    private void Awake()
    {
        MakePropCollidersNonBlocking();
        AssignDefaultUseAnimationInEditor();

        if (cameraFollow == null && Camera.main != null)
        {
            cameraFollow = Camera.main.GetComponent<CameraFollow>();
        }
    }

    private void AssignDefaultUseAnimationInEditor()
    {
#if UNITY_EDITOR
        if (useAnimation != null)
        {
            return;
        }

        var path = string.Empty;
        if (mode == UseMode.Sit)
        {
            path = "Assets/DownloadedAnimations/Sitting Talking.fbx";
        }
        else if (mode == UseMode.Lie || mode == UseMode.Bath)
        {
            path = "Assets/DownloadedAnimations/Male Laying Pose.fbx";
        }

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var assets = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
        for (var i = 0; i < assets.Length; i++)
        {
            if (assets[i] is AnimationClip clip && !clip.name.StartsWith("__preview__", System.StringComparison.OrdinalIgnoreCase))
            {
                useAnimation = clip;
                return;
            }
        }
        
        if (useAnimation == null)
        {
            var allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
            for (var i = 0; i < allAssets.Length; i++)
            {
                if (allAssets[i] is AnimationClip clip && !clip.name.StartsWith("__preview__", System.StringComparison.OrdinalIgnoreCase) && !clip.name.StartsWith("Take 001", System.StringComparison.OrdinalIgnoreCase))
                {
                    useAnimation = clip;
                    break;
                }
            }
        }
#endif
    }

    private void MakePropCollidersNonBlocking()
    {
        var colliders = GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            var item = colliders[i];
            if (item != null)
            {
                item.isTrigger = true;
            }
        }
    }

    private void Update()
    {
        if (SimpleInventoryWindow.IsOpen || CharacterActionRecorder.IsMenuOpen || CharacterTuningMenu.IsOpen || SurfaceItemPlacer.BlocksToolInput)
        {
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (user != null)
        {
            HandlePoseAdjustment(keyboard);
        }

        var interactPressed = keyboard.eKey.wasPressedThisFrame;
        if (!interactPressed || wasInteractPressed)
        {
            wasInteractPressed = interactPressed;
            return;
        }

        if (user == null)
        {
            TryUse();
        }
        else
        {
            StopUse();
        }

        wasInteractPressed = true;
    }

    private void LateUpdate()
    {
        wasInteractPressed = false;

        if (userObject == null || usePoint == null || interactionAnimation == null)
        {
            return;
        }

        if (mode == UseMode.Sit)
        {
            var animator = userObject.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                var pelvis = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (pelvis != null)
                {
                    float seatHeight = usePoint.position.y;
                    if (TryGetPropBounds(out var bounds))
                    {
                        if (Mathf.Abs(usePoint.position.y - bounds.min.y) < 0.15f)
                        {
                            seatHeight = bounds.min.y + bounds.size.y * 0.45f;
                        }
                    }

                    Vector3 localToWorldOffset = usePoint.rotation * manualOffset;
                    float targetPelvisY = seatHeight + 0.1f + localToWorldOffset.y;
                    float diffY = targetPelvisY - pelvis.position.y;
                    
                    Vector3 newPos = userObject.transform.position;
                    newPos.y += diffY;
                    newPos.x = usePoint.position.x + localToWorldOffset.x;
                    newPos.z = usePoint.position.z + localToWorldOffset.z;

                    userObject.transform.position = newPos;
                    userObject.transform.rotation = usePoint.rotation * Quaternion.Euler(0f, manualYaw, 0f);
                }
            }
        }
        else if (mode == UseMode.Lie || mode == UseMode.Bath)
        {
            Vector3 targetPosition = usePoint.position;
            Quaternion targetRotation = usePoint.rotation;
            float surfaceY = targetPosition.y;
            
            if (mode == UseMode.Lie && TryGetCenteredLiePose(out var liePosition, out var lieRotation, out var lieSurfaceY))
            {
                targetPosition = liePosition;
                targetRotation = lieRotation;
                surfaceY = lieSurfaceY;
            }
            
            Vector3 localToWorldOffset = targetRotation * manualOffset;
            userObject.transform.position = targetPosition + localToWorldOffset;
            userObject.transform.rotation = targetRotation * Quaternion.Euler(0f, manualYaw, 0f);

            if (mode == UseMode.Lie)
            {
                AlignUserBottomToHeight(userObject, surfaceY + lieSurfaceOffset + localToWorldOffset.y);
                AlignUserHorizontalCenterToPosition(userObject, targetPosition + localToWorldOffset);
            }
        }
    }

    private void TryUse()
    {
        if (mode == UseMode.None || usePoint == null)
        {
            return;
        }

        AssignDefaultUseAnimationInEditor();

        var nearest = FindNearestEnabledUser();
        if (nearest == null)
        {
            return;
        }

        user = nearest;
        userObject = nearest.gameObject;
        previousPosition = userObject.transform.position;
        previousRotation = userObject.transform.rotation;
        
        LoadPoseOverride();

        user.ClearInputState();
        user.enabled = false;

        var targetPosition = usePoint.position;
        var targetRotation = usePoint.rotation;
        var surfaceY = targetPosition.y;

        if (mode == UseMode.Sit && TryGetPropBounds(out var sitBounds))
        {
            targetPosition.y = sitBounds.min.y + sitGroundOffset;
        }
        else if (mode == UseMode.Lie && TryGetCenteredLiePose(out var liePosition, out var lieRotation, out var lieSurfaceY))
        {
            targetPosition = liePosition;
            targetRotation = lieRotation;
            surfaceY = lieSurfaceY;
        }

        userObject.transform.SetPositionAndRotation(targetPosition, targetRotation);
        if (mode == UseMode.Lie)
        {
            AlignUserBottomToHeight(userObject, surfaceY + lieSurfaceOffset);
            AlignUserHorizontalCenterToPosition(userObject, targetPosition);
        }

        interactionAnimation = userObject.GetComponentInChildren<RumiInteractionAnimationController>(true);
        if (interactionAnimation == null)
        {
            interactionAnimation = userObject.AddComponent<RumiInteractionAnimationController>();
        }

        if (interactionAnimation != null && useAnimation != null)
        {
            interactionAnimation.PlayLoop(useAnimation);
        }

        if (cameraFollow == null && Camera.main != null)
        {
            cameraFollow = Camera.main.GetComponent<CameraFollow>();
        }

        if (cameraFollow != null)
        {
            cameraFollow.followControlledCharacter = false;
            cameraFollow.target = usePoint;
            cameraFollow.SnapToTarget();
        }
    }

    private void StopUse()
    {
        if (userObject == null || user == null)
        {
            interactionAnimation = null;
            user = null;
            userObject = null;
            return;
        }

        SavePoseOverride();

        if (interactionAnimation != null)
        {
            interactionAnimation.StopInteractionAnimation();
        }

        userObject.transform.SetPositionAndRotation(previousPosition, previousRotation);
        user.enabled = true;
        user.ClearInputState();

        if (cameraFollow != null)
        {
            cameraFollow.followControlledCharacter = true;
            cameraFollow.SetControlledTarget(userObject.transform, true);
        }

        interactionAnimation = null;
        user = null;
        userObject = null;
    }

    private string GetCleanName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Unknown";
        var idx = name.IndexOf("(Clone)", System.StringComparison.Ordinal);
        if (idx >= 0) name = name.Substring(0, idx).TrimEnd();
        idx = name.IndexOf("_Spawned_", System.StringComparison.Ordinal);
        if (idx >= 0) name = name.Substring(0, idx).TrimEnd();
        return name.Trim();
    }

    private string GetPosePrefsKey()
    {
        if (user == null) return string.Empty;
        string charName = GetCleanName(user.name);
        string propName = GetCleanName(gameObject.name);
        var version = mode == UseMode.Lie ? "v3" : "v1";
        return $"StudioPropPose_{version}_{charName}_{propName}_{mode}";
    }

    private void SavePoseOverride()
    {
        string key = GetPosePrefsKey();
        if (string.IsNullOrEmpty(key)) return;
        
        PlayerPrefs.SetFloat(key + "_offsetX", manualOffset.x);
        PlayerPrefs.SetFloat(key + "_offsetY", manualOffset.y);
        PlayerPrefs.SetFloat(key + "_offsetZ", manualOffset.z);
        PlayerPrefs.SetFloat(key + "_yaw", manualYaw);
        PlayerPrefs.Save();
    }

    private void LoadPoseOverride()
    {
        string key = GetPosePrefsKey();
        if (string.IsNullOrEmpty(key))
        {
            manualOffset = Vector3.zero;
            manualYaw = 0f;
            return;
        }

        if (PlayerPrefs.HasKey(key + "_offsetX"))
        {
            manualOffset.x = PlayerPrefs.GetFloat(key + "_offsetX");
            manualOffset.y = PlayerPrefs.GetFloat(key + "_offsetY");
            manualOffset.z = PlayerPrefs.GetFloat(key + "_offsetZ");
            manualYaw = PlayerPrefs.GetFloat(key + "_yaw");
        }
        else
        {
            manualOffset = Vector3.zero;
            manualYaw = 0f;
        }
    }

    private void HandlePoseAdjustment(Keyboard keyboard)
    {
        if (userObject == null || usePoint == null)
        {
            return;
        }

        if (keyboard.backspaceKey.wasPressedThisFrame)
        {
            manualOffset = Vector3.zero;
            manualYaw = 0f;
            SavePoseOverride();
            return;
        }

        var multiplier = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? poseFastMultiplier : 1f;
        var forward = GetCameraPlanarForward();
        var right = GetCameraPlanarRight();
        var worldMove = Vector3.zero;

        if (keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed) worldMove += forward * (poseNudgeSpeed * multiplier * Time.deltaTime);
        if (keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed) worldMove -= forward * (poseNudgeSpeed * multiplier * Time.deltaTime);
        if (keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed) worldMove += right * (poseNudgeSpeed * multiplier * Time.deltaTime);
        if (keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed) worldMove -= right * (poseNudgeSpeed * multiplier * Time.deltaTime);
        if (keyboard.pageUpKey.isPressed || keyboard.spaceKey.isPressed) worldMove += Vector3.up * (poseVerticalNudgeSpeed * multiplier * Time.deltaTime);
        if (keyboard.pageDownKey.isPressed || keyboard.cKey.isPressed) worldMove -= Vector3.up * (poseVerticalNudgeSpeed * multiplier * Time.deltaTime);

        if (worldMove.sqrMagnitude > 0.00001f)
        {
            var targetRotation = usePoint.rotation;
            if (mode == UseMode.Lie && TryGetCenteredLiePose(out var liePosition, out var lieRotation, out var lieSurfaceY))
            {
                targetRotation = lieRotation;
            }
            Vector3 localMove = Quaternion.Inverse(targetRotation) * worldMove;
            manualOffset += localMove;
        }

        var yaw = 0f;
        if (keyboard.leftBracketKey.isPressed) yaw -= 1f;
        if (keyboard.rightBracketKey.isPressed) yaw += 1f;
        if (Mathf.Abs(yaw) > 0.01f)
        {
            manualYaw += yaw * poseRotateSpeed * multiplier * Time.deltaTime;
        }
    }

    private Vector3 GetCameraPlanarForward()
    {
        if (Camera.main == null)
        {
            return transform.forward;
        }

        var forward = Camera.main.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.001f)
        {
            forward = transform.forward;
            forward.y = 0f;
        }

        return forward.normalized;
    }

    private Vector3 GetCameraPlanarRight()
    {
        if (Camera.main == null)
        {
            return transform.right;
        }

        var right = Camera.main.transform.right;
        right.y = 0f;
        if (right.sqrMagnitude <= 0.001f)
        {
            right = transform.right;
            right.y = 0f;
        }

        return right.normalized;
    }

    private bool TryGetCenteredLiePose(out Vector3 position, out Quaternion rotation, out float surfaceY)
    {
        position = usePoint != null ? usePoint.position : transform.position;
        rotation = usePoint != null ? usePoint.rotation : transform.rotation;
        surfaceY = position.y;

        if (!TryGetPropBounds(out var worldBounds) || !TryGetLocalRendererBounds(out var localBounds))
        {
            return false;
        }

        var worldCenter = worldBounds.center;
        surfaceY = GetLieSurfaceY(worldBounds);

        position = new Vector3(worldCenter.x, surfaceY, worldCenter.z);
        
        // Orient character along the bed and face the pillow/headboard side for this imported pose.
        var sizeX = localBounds.size.x * transform.lossyScale.x;
        var sizeZ = localBounds.size.z * transform.lossyScale.z;
        if (sizeX > sizeZ)
        {
            rotation = transform.rotation * Quaternion.Euler(0f, 270f, 0f);
        }
        else
        {
            rotation = transform.rotation * Quaternion.Euler(0f, 180f, 0f);
        }
        
        return true;
    }

    private float GetLieSurfaceY(Bounds worldBounds)
    {
        if (usePoint != null)
        {
            var usePointY = usePoint.position.y;
            if (usePointY >= worldBounds.min.y - 0.1f && usePointY <= worldBounds.max.y + 0.15f)
            {
                var maxReasonableMattressHeight = worldBounds.min.y + Mathf.Min(worldBounds.size.y * 0.45f, 0.55f);
                if (usePointY <= maxReasonableMattressHeight + 0.1f)
                {
                    return usePointY;
                }
            }
        }

        return worldBounds.min.y + Mathf.Min(worldBounds.size.y * 0.35f, 0.5f);
    }

    private void AlignUserHorizontalCenterToPosition(GameObject targetUser, Vector3 desiredCenter)
    {
        if (targetUser == null || !TryGetRendererBounds(targetUser, out var userBounds))
        {
            return;
        }

        var position = targetUser.transform.position;
        position.x += desiredCenter.x - userBounds.center.x;
        position.z += desiredCenter.z - userBounds.center.z;
        targetUser.transform.position = position;
    }

    private void AlignUserBottomToHeight(GameObject targetUser, float desiredBottomY)
    {
        if (targetUser == null || !TryGetRendererBounds(targetUser, out var userBounds))
        {
            return;
        }

        var position = targetUser.transform.position;
        position.y += desiredBottomY - userBounds.min.y;
        targetUser.transform.position = position;
    }

    private bool TryGetPropBounds(out Bounds bounds)
    {
        return TryGetRendererBounds(gameObject, out bounds);
    }

    private bool TryGetLocalRendererBounds(out Bounds bounds)
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        bounds = default;
        var found = false;

        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null || renderer is ParticleSystemRenderer)
            {
                continue;
            }

            var rendererBounds = renderer.localBounds;
            for (var corner = 0; corner < 8; corner++)
            {
                var localCorner = new Vector3(
                    (corner & 1) == 0 ? rendererBounds.min.x : rendererBounds.max.x,
                    (corner & 2) == 0 ? rendererBounds.min.y : rendererBounds.max.y,
                    (corner & 4) == 0 ? rendererBounds.min.z : rendererBounds.max.z);
                var rootLocalCorner = transform.InverseTransformPoint(renderer.transform.TransformPoint(localCorner));

                if (!found)
                {
                    bounds = new Bounds(rootLocalCorner, Vector3.zero);
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(rootLocalCorner);
                }
            }
        }

        return found;
    }

    private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        bounds = default;
        var found = false;

        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null || renderer is ParticleSystemRenderer)
            {
                continue;
            }

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }

    private PlayerMove FindNearestEnabledUser()
    {
        PlayerMove nearest = null;
        var nearestSqrDistance = useDistance * useDistance;
        foreach (var move in FindObjectsByType<PlayerMove>(FindObjectsInactive.Exclude))
        {
            if (!move.enabled)
            {
                continue;
            }

            var sqrDistance = GetUserSqrDistanceToUsableArea(move.transform.position);
            if (sqrDistance <= nearestSqrDistance)
            {
                nearest = move;
                nearestSqrDistance = sqrDistance;
            }
        }

        return nearest;
    }

    private float GetUserSqrDistanceToUsableArea(Vector3 userPosition)
    {
        var bestSqrDistance = (userPosition - transform.position).sqrMagnitude;

        if (usePoint != null)
        {
            bestSqrDistance = Mathf.Min(bestSqrDistance, (userPosition - usePoint.position).sqrMagnitude);
        }

        if (TryGetPropBounds(out var bounds))
        {
            var closest = bounds.ClosestPoint(userPosition);
            bestSqrDistance = Mathf.Min(bestSqrDistance, (userPosition - closest).sqrMagnitude);
        }

        return bestSqrDistance;
    }

    private void OnGUI()
    {
        if (user == null || userObject == null) return;

        // Init style
        var hudStyle = new GUIStyle(GUI.skin.box);
        hudStyle.normal.background = MakeTex(2, 2, new Color(0.06f, 0.07f, 0.08f, 0.9f));
        
        var labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.normal.textColor = new Color(0.9f, 0.95f, 0.97f);
        labelStyle.fontStyle = FontStyle.Bold;
        labelStyle.fontSize = 14;
        labelStyle.alignment = TextAnchor.MiddleCenter;

        var subLabelStyle = new GUIStyle(labelStyle);
        subLabelStyle.normal.textColor = new Color(0.6f, 0.65f, 0.7f);
        subLabelStyle.fontStyle = FontStyle.Normal;
        subLabelStyle.fontSize = 12;

        float width = 450f;
        float height = 56f;
        float x = (Screen.width - width) / 2f;
        float y = Screen.height - height - 20f;

        GUI.Box(new Rect(x, y, width, height), GUIContent.none, hudStyle);
        
        GUILayout.BeginArea(new Rect(x + 10f, y + 6f, width - 20f, height - 12f));
        string propName = GetCleanName(gameObject.name);
        GUILayout.Label($"USING {propName.ToUpperInvariant()} ({mode.ToString().ToUpperInvariant()})", labelStyle);
        GUILayout.Label("WASD / Arrows: Nudge | Space: Up | C: Down | [ ] : Rotate | Backspace: Reset pose | E: Exit", subLabelStyle);
        GUILayout.EndArea();
    }

    private Texture2D MakeTex(int w, int h, Color col)
    {
        var pix = new Color[w * h];
        for (var i = 0; i < pix.Length; i++) pix[i] = col;
        var tex = new Texture2D(w, h);
        tex.SetPixels(pix);
        tex.Apply();
        return tex;
    }
}
