using UnityEngine;

using UnityEngine.Animations;
using UnityEngine.Playables;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class RumiStarterAssetsAnimatorDriver : MonoBehaviour
{
#if UNITY_EDITOR
    private const string DefaultCrouchAnimationPath = "Assets/DownloadedAnimations/CrouchIdle.FBX";
    private const string DefaultCrouchWalkAnimationPath = "Assets/DownloadedAnimations/CrouchWalk.FBX";
#endif

    public PlayerMove controller;
    public Animator animator;
    public float walkAnimationSpeed = 2f;
    public float runAnimationSpeed = 5.335f;
    public float strutAnimationSpeed = 3.5f;
    public float blendSpeed = 12f;

    [Header("Crouch Pose")]
    public float crouchBlendSpeed = 10f;
    public float crouchHipDrop = 0.22f;
    public float crouchSpinePitch = 10f;
    public float crouchThighPitch = -0.55f;
    public float crouchKneeBend = -0.75f;
    public float crouchFootPitch = -0.25f;
    public bool useHumanoidCrouchPose = true;
    public AnimationClip crouchAnimationClip;
    public AnimationClip crouchWalkAnimationClip;
    public bool useDownloadedCrouchAnimation = false;

    private int speedId;
    private int groundedId;
    private int jumpId;
    private int freeFallId;
    private int motionSpeedId;
    private int struttingId;
    private float blendedSpeed;
    private float crouchBlend;
    private bool initialized;
    private Transform pelvis;
    private Transform spine03;
    private Transform spine05;
    private Transform thighL;
    private Transform thighR;
    private Transform calfL;
    private Transform calfR;
    private Transform footL;
    private Transform footR;
    private PlayableGraph crouchGraph;
    private AnimationClipPlayable crouchPlayable;
    private AnimationClip activeCrouchClip;
    private HumanPoseHandler humanPoseHandler;
    private HumanPose humanPose;
    private int leftUpperLegFrontBack = -1;
    private int rightUpperLegFrontBack = -1;
    private int leftLowerLegStretch = -1;
    private int rightLowerLegStretch = -1;
    private int leftFootUpDown = -1;
    private int rightFootUpDown = -1;
    private int spineFrontBack = -1;

    private void Reset()
    {
        AutoWire();
    }

    private void OnValidate()
    {
        AutoWire();
    }

    private void Awake()
    {
        Initialize();
        AutoWire();
    }

    private void OnDisable()
    {
        DestroyCrouchGraph();
        humanPoseHandler?.Dispose();
        humanPoseHandler = null;
    }

    private void Update()
    {
        Initialize();
        if (controller == null || animator == null)
        {
            AutoWire();
        }

        if (animator == null)
        {
            return;
        }

        if (controller != null && !controller.enabled)
        {
            return;
        }

        var moving = controller != null && controller.IsMoving;
        var running = controller != null && controller.IsRunning;
        var crouching = controller != null && controller.IsCrouching;
        var strutting = controller != null && controller.IsStrutting;
        var grounded = controller == null || controller.IsGrounded;

        var targetSpeed = 0f;
        if (moving)
        {
            if (strutting) targetSpeed = strutAnimationSpeed;
            else if (running) targetSpeed = runAnimationSpeed;
            else targetSpeed = walkAnimationSpeed;
        }

        if (crouching)
        {
            targetSpeed *= 0.45f;
        }

        blendedSpeed = Mathf.Lerp(blendedSpeed, targetSpeed, Time.deltaTime * blendSpeed);
        if (blendedSpeed < 0.02f)
        {
            blendedSpeed = 0f;
        }

        animator.SetFloat(speedId, blendedSpeed);
        animator.SetFloat(motionSpeedId, moving ? 1f : 0f);
        animator.SetBool(struttingId, strutting);
        animator.SetBool(groundedId, grounded);
        animator.SetBool(jumpId, !grounded);
        animator.SetBool(freeFallId, !grounded);
    }

    private void LateUpdate()
    {
        if (controller != null && !controller.enabled)
        {
            return;
        }

        var target = controller != null && controller.IsCrouching && !controller.IsStrutting ? 1f : 0f;
        crouchBlend = Mathf.MoveTowards(crouchBlend, target, crouchBlendSpeed * Time.deltaTime);
        if (ApplyDownloadedCrouchAnimation(crouchBlend))
        {
            return;
        }

        if (crouchBlend <= 0.001f)
        {
            return;
        }

        if (ApplyHumanoidCrouchPose(crouchBlend))
        {
            return;
        }

        ApplyCrouchPose(crouchBlend);
    }

    public void AutoWire()
    {
        if (controller == null)
        {
            controller = GetComponentInParent<PlayerMove>();
        }

        if (animator == null)
        {
            animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
        }

#if UNITY_EDITOR
        if (crouchAnimationClip == null)
        {
            crouchAnimationClip = LoadFirstAnimationClip(DefaultCrouchAnimationPath);
        }

        if (crouchWalkAnimationClip == null)
        {
            crouchWalkAnimationClip = LoadFirstAnimationClip(DefaultCrouchWalkAnimationPath);
        }
#endif

        pelvis = FindBone("pelvis");
        spine03 = FindBone("spine_03");
        spine05 = FindBone("spine_05");
        thighL = FindBone("thigh_l");
        thighR = FindBone("thigh_r");
        calfL = FindBone("calf_l");
        calfR = FindBone("calf_r");
        footL = FindBone("foot_l");
        footR = FindBone("foot_r");
        CacheHumanoidPoseHandler();
    }

    public void OnFootstep()
    {
    }

    public void OnFootstep(AnimationEvent animationEvent)
    {
    }

    public void OnLand()
    {
    }

    public void OnLand(AnimationEvent animationEvent)
    {
    }

    private void Initialize()
    {
        if (initialized)
        {
            return;
        }

        speedId = Animator.StringToHash("Speed");
        groundedId = Animator.StringToHash("Grounded");
        jumpId = Animator.StringToHash("Jump");
        freeFallId = Animator.StringToHash("FreeFall");
        motionSpeedId = Animator.StringToHash("MotionSpeed");
        struttingId = Animator.StringToHash("Strutting");
        initialized = true;
    }

    private bool ApplyDownloadedCrouchAnimation(float amount)
    {
        if (!useDownloadedCrouchAnimation || crouchAnimationClip == null || animator == null)
        {
            DestroyCrouchGraph();
            return false;
        }

        if (amount <= 0.001f)
        {
            DestroyCrouchGraph();
            return false;
        }

        var selectedClip = controller != null && controller.IsMoving && crouchWalkAnimationClip != null
            ? crouchWalkAnimationClip
            : crouchAnimationClip;

        EnsureCrouchGraph(selectedClip);
        if (!crouchPlayable.IsValid())
        {
            return false;
        }
        return true;
    }

    private void EnsureCrouchGraph(AnimationClip selectedClip)
    {
        if (selectedClip == null)
        {
            DestroyCrouchGraph();
            return;
        }

        if (crouchGraph.IsValid() && activeCrouchClip == selectedClip)
        {
            return;
        }

        DestroyCrouchGraph();
        activeCrouchClip = selectedClip;
        crouchGraph = PlayableGraph.Create("Downloaded Crouch Animation");
        crouchGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        crouchPlayable = AnimationClipPlayable.Create(crouchGraph, selectedClip);
        crouchPlayable.SetApplyFootIK(true);
        crouchPlayable.SetApplyPlayableIK(false);
        crouchPlayable.SetSpeed(1d);

        var output = AnimationPlayableOutput.Create(crouchGraph, "DownloadedCrouchAnimation", animator);
        output.SetSourcePlayable(crouchPlayable);
        crouchGraph.Play();
    }

    private void DestroyCrouchGraph()
    {
        if (crouchGraph.IsValid())
        {
            crouchGraph.Destroy();
        }

        activeCrouchClip = null;
    }

    private bool ApplyHumanoidCrouchPose(float amount)
    {
        if (!useHumanoidCrouchPose || humanPoseHandler == null)
        {
            return false;
        }

        humanPoseHandler.GetHumanPose(ref humanPose);
        SetMuscle(leftUpperLegFrontBack, crouchThighPitch * amount);
        SetMuscle(rightUpperLegFrontBack, crouchThighPitch * amount);
        SetMuscle(leftLowerLegStretch, crouchKneeBend * amount);
        SetMuscle(rightLowerLegStretch, crouchKneeBend * amount);
        SetMuscle(leftFootUpDown, crouchFootPitch * amount);
        SetMuscle(rightFootUpDown, crouchFootPitch * amount);
        SetMuscle(spineFrontBack, -0.2f * amount);

        humanPoseHandler.SetHumanPose(ref humanPose);
        return true;
    }

    private void SetMuscle(int index, float value)
    {
        if (index < 0 || humanPose.muscles == null || index >= humanPose.muscles.Length)
        {
            return;
        }

        humanPose.muscles[index] = Mathf.Clamp(value, -1f, 1f);
    }

    private void CacheHumanoidPoseHandler()
    {
        if (!useHumanoidCrouchPose || animator == null || animator.avatar == null || !animator.avatar.isHuman)
        {
            return;
        }

        if (humanPoseHandler == null)
        {
            humanPoseHandler = new HumanPoseHandler(animator.avatar, animator.transform);
        }

        leftUpperLegFrontBack = FindMuscle("Left Upper Leg Front-Back");
        rightUpperLegFrontBack = FindMuscle("Right Upper Leg Front-Back");
        leftLowerLegStretch = FindMuscle("Left Lower Leg Stretch");
        rightLowerLegStretch = FindMuscle("Right Lower Leg Stretch");
        leftFootUpDown = FindMuscle("Left Foot Up-Down");
        rightFootUpDown = FindMuscle("Right Foot Up-Down");
        spineFrontBack = FindMuscle("Spine Front-Back");
    }

    private int FindMuscle(string muscleName)
    {
        for (var i = 0; i < HumanTrait.MuscleCount; i++)
        {
            if (string.Equals(HumanTrait.MuscleName[i], muscleName, System.StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private void ApplyCrouchPose(float amount)
    {
        if (pelvis != null)
        {
            pelvis.localPosition += Vector3.down * (crouchHipDrop * amount);
        }

        Rotate(spine03, crouchSpinePitch * amount, 0f, 0f);
        Rotate(spine05, crouchSpinePitch * 0.45f * amount, 0f, 0f);
        if (Mathf.Abs(crouchThighPitch) > 0.001f)
        {
            Rotate(thighL, crouchThighPitch * amount, 0f, 0f);
            Rotate(thighR, crouchThighPitch * amount, 0f, 0f);
        }

        if (Mathf.Abs(crouchKneeBend) > 0.001f)
        {
            Rotate(calfL, -crouchKneeBend * amount, 0f, 0f);
            Rotate(calfR, -crouchKneeBend * amount, 0f, 0f);
        }

        if (Mathf.Abs(crouchFootPitch) > 0.001f)
        {
            Rotate(footL, crouchFootPitch * amount, 0f, 0f);
            Rotate(footR, crouchFootPitch * amount, 0f, 0f);
        }
    }

    private void Rotate(Transform bone, float x, float y, float z)
    {
        if (bone == null)
        {
            return;
        }

        bone.localRotation *= Quaternion.Euler(x, y, z);
    }

    private Transform FindBone(string boneName)
    {
        foreach (var child in GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(child.name, boneName, System.StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }

#if UNITY_EDITOR
    private static AnimationClip LoadFirstAnimationClip(string path)
    {
        var clips = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
        for (var i = 0; i < clips.Length; i++)
        {
            if (clips[i] is AnimationClip clip && !clip.name.StartsWith("__preview__"))
            {
                return clip;
            }
        }

        return null;
    }
#endif
}

