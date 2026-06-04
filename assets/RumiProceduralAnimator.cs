using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class RumiProceduralAnimator : MonoBehaviour
{
    [Header("Driver")]
    public PlayerMove controller;
    public Rigidbody body;

    [Header("Motion")]
    public float walkCycleSpeed = 6f;
    public float runCycleSpeed = 9.5f;
    public float blendSpeed = 10f;
    public float walkAmount = 1f;
    public float runAmount = 1.35f;

    [Header("Pose")]
    public float legSwing = 28f;
    public float kneeBend = 24f;
    public float armSwing = 22f;
    public float footSwing = 14f;
    public float bodyBob = 0.035f;
    public float torsoSway = 4f;

    private readonly Dictionary<Transform, Quaternion> baseRotations = new();
    private Transform pelvis;
    private Transform spine03;
    private Transform spine05;
    private Transform thighL;
    private Transform thighR;
    private Transform calfL;
    private Transform calfR;
    private Transform footL;
    private Transform footR;
    private Transform upperArmL;
    private Transform upperArmR;
    private Transform neck;
    private Vector3 baseLocalPosition;
    private float cycle;
    private float motionBlend;

    private void Reset()
    {
        AutoWire();
    }

    private void OnEnable()
    {
        AutoWire();
        CaptureBasePose();
    }

    private void OnValidate()
    {
        AutoWire();
        CaptureBasePose();
    }

    private void LateUpdate()
    {
        if (controller != null && !controller.enabled)
        {
            return;
        }

        if (baseRotations.Count == 0)
        {
            CaptureBasePose();
        }

        if (controller != null && controller.IsStrutting)
        {
            return;
        }

        var deltaTime = Application.isPlaying ? Time.deltaTime : 1f / 60f;
        var moving = IsMoving();
        var running = controller != null && controller.IsRunning;
        var targetBlend = moving ? 1f : 0f;
        motionBlend = Mathf.MoveTowards(motionBlend, targetBlend, blendSpeed * deltaTime);

        var cycleSpeed = running ? runCycleSpeed : walkCycleSpeed;
        cycle += deltaTime * cycleSpeed * Mathf.Lerp(0.35f, 1f, motionBlend);

        ApplyBasePose();

        if (motionBlend <= 0.001f)
        {
            ApplyIdle(deltaTime);
            return;
        }

        var amount = motionBlend * (running ? runAmount : walkAmount);
        var sin = Mathf.Sin(cycle);
        var cos = Mathf.Cos(cycle);
        var absSin = Mathf.Abs(sin);

        Rotate(thighL, legSwing * sin * amount, 0f, 2f * cos * amount);
        Rotate(thighR, -legSwing * sin * amount, 0f, -2f * cos * amount);
        Rotate(calfL, kneeBend * Mathf.Max(0f, -sin) * amount, 0f, 0f);
        Rotate(calfR, kneeBend * Mathf.Max(0f, sin) * amount, 0f, 0f);
        Rotate(footL, -footSwing * Mathf.Max(0f, sin) * amount, 0f, 0f);
        Rotate(footR, -footSwing * Mathf.Max(0f, -sin) * amount, 0f, 0f);

        Rotate(upperArmL, -armSwing * sin * amount, 0f, 3f * cos * amount);
        Rotate(upperArmR, armSwing * sin * amount, 0f, -3f * cos * amount);
        Rotate(spine03, 0f, torsoSway * sin * amount, 0f);
        Rotate(spine05, 0f, -torsoSway * 0.55f * sin * amount, 0f);
        Rotate(neck, 2f * absSin * amount, 0f, 0f);

        if (pelvis != null)
        {
            pelvis.localPosition = baseLocalPosition + Vector3.up * (bodyBob * absSin * amount);
        }
    }

    public void AutoWire()
    {
        if (controller == null)
        {
            controller = GetComponentInParent<PlayerMove>();
        }

        if (body == null && controller != null)
        {
            body = controller.GetComponent<Rigidbody>();
        }

        pelvis = FindBone("pelvis");
        spine03 = FindBone("spine_03");
        spine05 = FindBone("spine_05");
        thighL = FindBone("thigh_l");
        thighR = FindBone("thigh_r");
        calfL = FindBone("calf_l");
        calfR = FindBone("calf_r");
        footL = FindBone("foot_l");
        footR = FindBone("foot_r");
        upperArmL = FindBone("upperarm_l");
        upperArmR = FindBone("upperarm_r");
        neck = FindBone("neck_02") ?? FindBone("neck_01");
    }

    public void CaptureBasePose()
    {
        baseRotations.Clear();
        foreach (var bone in new[] { pelvis, spine03, spine05, thighL, thighR, calfL, calfR, footL, footR, upperArmL, upperArmR, neck })
        {
            if (bone != null && !baseRotations.ContainsKey(bone))
            {
                baseRotations.Add(bone, bone.localRotation);
            }
        }

        if (pelvis != null)
        {
            baseLocalPosition = pelvis.localPosition;
        }
    }

    private bool IsMoving()
    {
        if (controller != null)
        {
            return controller.IsMoving;
        }

        if (body != null)
        {
            var velocity = body.linearVelocity;
            velocity.y = 0f;
            return velocity.sqrMagnitude > 0.05f;
        }

        return false;
    }

    private void ApplyIdle(float deltaTime)
    {
        var idle = Mathf.Sin(Time.time * 1.8f) * 0.6f;
        Rotate(spine05, idle, 0f, 0f);
        Rotate(neck, -idle * 0.5f, 0f, 0f);
        if (pelvis != null)
        {
            pelvis.localPosition = Vector3.Lerp(pelvis.localPosition, baseLocalPosition, deltaTime * blendSpeed);
        }
    }

    private void ApplyBasePose()
    {
        foreach (var pair in baseRotations)
        {
            if (pair.Key != null)
            {
                pair.Key.localRotation = pair.Value;
            }
        }
    }

    private void Rotate(Transform bone, float x, float y, float z)
    {
        if (bone == null || !baseRotations.TryGetValue(bone, out var baseRotation))
        {
            return;
        }

        bone.localRotation = baseRotation * Quaternion.Euler(x, y, z);
    }

    private Transform FindBone(string boneName)
    {
        foreach (var child in GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(child.name, boneName, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }
}
