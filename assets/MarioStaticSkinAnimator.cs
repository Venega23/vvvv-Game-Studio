using UnityEngine;

[DisallowMultipleComponent]
public class MarioStaticSkinAnimator : MonoBehaviour
{
    public PlayerMove controller;
    public float walkBob = 0.045f;
    public float runBob = 0.075f;
    public float walkTilt = 3.5f;
    public float runTilt = 6f;
    public float walkFrequency = 7f;
    public float runFrequency = 10.5f;
    public float crouchDrop = 0.18f;
    public float smoothing = 14f;

    private Vector3 restLocalPosition;
    private Quaternion restLocalRotation;
    private Vector3 restLocalScale;
    private float cycle;
    private bool captured;

    private void Awake()
    {
        CaptureRestPose();
    }

    private void OnEnable()
    {
        CaptureRestPose();
    }

    public void Bind(PlayerMove move)
    {
        controller = move;
        CaptureRestPose();
    }

    private void LateUpdate()
    {
        if (!captured)
        {
            CaptureRestPose();
        }

        if (controller == null)
        {
            transform.localPosition = Vector3.Lerp(transform.localPosition, restLocalPosition, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
            transform.localRotation = Quaternion.Slerp(transform.localRotation, restLocalRotation, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
            transform.localScale = Vector3.Lerp(transform.localScale, restLocalScale, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
            return;
        }

        var moving = controller.IsMoving && controller.MoveDirection.sqrMagnitude > 0.001f;
        var running = controller.IsRunning;
        var crouching = controller.IsCrouching;
        var frequency = running ? runFrequency : walkFrequency;
        var bobAmount = running ? runBob : walkBob;
        var tiltAmount = running ? runTilt : walkTilt;

        if (moving)
        {
            cycle += Time.deltaTime * frequency;
        }
        else
        {
            cycle = Mathf.Lerp(cycle, 0f, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
        }

        var stride = moving ? Mathf.Sin(cycle) : 0f;
        var bob = moving ? Mathf.Abs(Mathf.Sin(cycle * 2f)) * bobAmount : 0f;
        var crouch = crouching ? crouchDrop : 0f;
        var targetPosition = restLocalPosition + new Vector3(0f, bob - crouch, 0f);
        var targetRotation = restLocalRotation * Quaternion.Euler(0f, 0f, -stride * tiltAmount);
        var squash = moving ? Mathf.Abs(stride) * 0.025f : 0f;
        var targetScale = new Vector3(restLocalScale.x * (1f + squash), restLocalScale.y * (1f - squash * 0.35f), restLocalScale.z * (1f + squash));

        var t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        transform.localPosition = Vector3.Lerp(transform.localPosition, targetPosition, t);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRotation, t);
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, t);
    }

    private void CaptureRestPose()
    {
        if (captured)
        {
            return;
        }

        restLocalPosition = transform.localPosition;
        restLocalRotation = transform.localRotation;
        restLocalScale = transform.localScale;
        captured = true;
    }
}
