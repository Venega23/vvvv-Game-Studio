using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    public Transform target;
    public Transform controlledTarget;
    public Transform[] selectableTargets;
    public bool followControlledCharacter = true;

    [Header("Orbit Camera")]
    public float distance = 4f;
    public float height = 1.45f;
    public float shoulderOffset = 0.35f;
    public float mouseSensitivity = 0.12f;
    public float minPitch = -25f;
    public float maxPitch = 65f;
    public float positionSmoothTime = 0.06f;
    public float rotationSmoothSpeed = 22f;
    public float zoomSpeed = 1.2f;
    public float zoomSmoothSpeed = 18f;
    public float minDistance = 0.8f;
    public float maxDistance = 14f;

    [Header("Developer Camera")]
    public Key toggleDeveloperCameraKey = Key.F1;
    public Key cycleCameraTargetKey = Key.C;
    public Key returnToControlledKey = Key.V;
    public bool developerCamera;
    public float developerMoveSpeed = 8f;
    public float developerFastMultiplier = 3f;
    public float developerVerticalSpeed = 6f;

    [Header("First Person")]
    public Key toggleFirstPersonKey = Key.V;
    public bool isFirstPerson;
    public float firstPersonHeight = 1.65f;

    [Header("Cursor")]
    public bool lockCursorOnPlay = true;

    private Camera cam;
    private Vector3 velocity;
    private float yaw;
    private float pitch = 18f;
    private float targetDistance;
    private int selectedTargetIndex;
    private float lastNonFPIDistance = 4f;
    private bool lastIsFirstPerson;
    private Renderer[] targetRenderers;

    public float Yaw => yaw;
    public Quaternion YawRotation => Quaternion.Euler(0f, yaw, 0f);
    public bool IsDeveloperCamera => developerCamera;

    private void Start()
    {
        cam = GetComponent<Camera>();
        if (target != null)
        {
            yaw = target.eulerAngles.y;
            targetRenderers = target.GetComponentsInChildren<Renderer>();
        }
        else
        {
            yaw = transform.eulerAngles.y;
        }

        distance = Mathf.Clamp(distance, minDistance, maxDistance);
        targetDistance = distance;
        lastNonFPIDistance = distance > 0.1f ? distance : 4f;
        lastIsFirstPerson = isFirstPerson;

        if (lockCursorOnPlay)
        {
            LockCursor();
        }
    }

    private void LateUpdate()
    {
        EnsureTarget();
        if (!CharacterActionRecorder.IsRenderingVideo)
        {
            HandleCameraHotkeys();
        }
        if (SimpleInventoryWindow.IsOpen || MapSwitcher.IsMenuOpen || CharacterActionRecorder.IsMenuOpen || CharacterTuningMenu.IsOpen || SurfaceItemPlacer.IsDragging || (SurfaceItemPlacer.IsPlacing && Keyboard.current != null && Keyboard.current.eKey.isPressed))
        {
            return;
        }

        if (!CharacterActionRecorder.IsRenderingVideo)
        {
            ReadMouseLook();
        }

        if (developerCamera)
        {
            MoveDeveloperCamera();
            return;
        }

        if (target == null)
        {
            return;
        }

        ApplyZoom();
        distance = Mathf.Lerp(distance, targetDistance, 1f - Mathf.Exp(-zoomSmoothSpeed * Time.deltaTime));

        if (cam != null)
        {
            cam.nearClipPlane = isFirstPerson ? 0.01f : 0.3f;
        }

        UpdateMeshVisibility();

        var pivot = GetPivot();
        var desiredPosition = GetDesiredPosition(pivot);

        if (isFirstPerson)
        {
            transform.position = desiredPosition;
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
        else
        {
            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, positionSmoothTime);

            var lookDirection = pivot - transform.position;
            if (lookDirection.sqrMagnitude > 0.001f)
            {
                var desiredRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationSmoothSpeed * Time.deltaTime);
            }
        }
    }

    private void UpdateMeshVisibility()
    {
        if (isFirstPerson == lastIsFirstPerson && targetRenderers != null)
        {
            return;
        }

        lastIsFirstPerson = isFirstPerson;
        if (target == null)
        {
            return;
        }

        targetRenderers = target.GetComponentsInChildren<Renderer>();
        foreach (var r in targetRenderers)
        {
            if (r != null)
            {
                r.enabled = !isFirstPerson;
            }
        }
    }

    public void SetControlledTarget(Transform newControlledTarget, bool immediate)
    {
        if (controlledTarget != null && targetRenderers != null)
        {
            foreach (var r in targetRenderers) if (r != null) r.enabled = true;
        }

        controlledTarget = newControlledTarget;
        targetRenderers = controlledTarget != null ? controlledTarget.GetComponentsInChildren<Renderer>() : null;
        
        if (followControlledCharacter && !developerCamera)
        {
            target = controlledTarget;
            if (immediate)
            {
                SnapToTarget();
            }
        }

        lastIsFirstPerson = !isFirstPerson; // Force visibility update
    }

    public void SetSelectableTargets(IEnumerable<Transform> targets)
    {
        selectableTargets = targets.Where(item => item != null).Distinct().ToArray();
        selectedTargetIndex = Mathf.Clamp(selectedTargetIndex, 0, Mathf.Max(0, selectableTargets.Length - 1));
    }

    public void SnapToTarget()
    {
        EnsureTarget();
        if (target == null)
        {
            return;
        }

        velocity = Vector3.zero;
        distance = Mathf.Clamp(distance, minDistance, maxDistance);
        targetDistance = distance;
        var pivot = GetPivot();
        transform.position = GetDesiredPosition(pivot);
        var lookDirection = pivot - transform.position;
        if (lookDirection.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        }
    }

    private void EnsureTarget()
    {
        if (target != null)
        {
            return;
        }

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            target = player.transform;
            controlledTarget = target;
            yaw = target.eulerAngles.y;
        }
    }

    private void HandleCameraHotkeys()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard[toggleDeveloperCameraKey].wasPressedThisFrame)
        {
            developerCamera = !developerCamera;
            followControlledCharacter = !developerCamera && followControlledCharacter;
            velocity = Vector3.zero;
            LockCursor();
        }

        if (keyboard[cycleCameraTargetKey].wasPressedThisFrame)
        {
            CycleCameraTarget();
        }

        if (keyboard[returnToControlledKey].wasPressedThisFrame && controlledTarget != null)
        {
            developerCamera = false;
            followControlledCharacter = true;
            target = controlledTarget;
            SnapToTarget();
        }

        if (keyboard[toggleFirstPersonKey].wasPressedThisFrame)
        {
            isFirstPerson = !isFirstPerson;
            if (isFirstPerson)
            {
                lastNonFPIDistance = targetDistance > 0.1f ? targetDistance : 4f;
                targetDistance = 0f;
                distance = 0f;
            }
            else
            {
                targetDistance = lastNonFPIDistance;
            }
        }
    }

    private void CycleCameraTarget()
    {
        if (selectableTargets == null || selectableTargets.Length == 0)
        {
            return;
        }

        developerCamera = false;
        followControlledCharacter = false;
        selectedTargetIndex = (selectedTargetIndex + 1) % selectableTargets.Length;
        target = selectableTargets[selectedTargetIndex];
        SnapToTarget();
    }

    private void ReadMouseLook()
    {
        var mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        var delta = mouse.delta.ReadValue();
        yaw += delta.x * mouseSensitivity;
        pitch = Mathf.Clamp(pitch - delta.y * mouseSensitivity, minPitch, maxPitch);

        if (developerCamera)
        {
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (lockCursorOnPlay && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.isPressed || SurfaceItemPlacer.IsPaletteOpen) && Cursor.lockState != CursorLockMode.Locked && !MapSwitcher.IsMenuOpen && !CharacterActionRecorder.IsMenuOpen && !CharacterTuningMenu.IsOpen && !SurfaceItemPlacer.IsDragging)
        {
            LockCursor();
        }
    }

    private void MoveDeveloperCamera()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        var move = Vector3.zero;
        if (keyboard.upArrowKey.isPressed) move += transform.forward;
        if (keyboard.downArrowKey.isPressed) move -= transform.forward;
        if (keyboard.rightArrowKey.isPressed) move += transform.right;
        if (keyboard.leftArrowKey.isPressed) move -= transform.right;
        if (keyboard.pageUpKey.isPressed || keyboard.eKey.isPressed) move += Vector3.up;
        if (keyboard.pageDownKey.isPressed || keyboard.fKey.isPressed) move -= Vector3.up;

        if (move.sqrMagnitude <= 0.001f)
        {
            return;
        }

        var speed = developerMoveSpeed;
        if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
        {
            speed *= developerFastMultiplier;
        }

        transform.position += move.normalized * (speed * Time.deltaTime);
    }

    private void ApplyZoom()
    {
        if (SurfaceItemPlacer.IsPlacing)
        {
            return;
        }

        var mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        var scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) <= 0.01f)
        {
            return;
        }

        if (targetDistance <= 0.001f)
        {
            targetDistance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        var wheelSteps = Mathf.Abs(scroll) > 10f ? scroll / 120f : scroll;
        targetDistance = Mathf.Clamp(targetDistance - wheelSteps * zoomSpeed, minDistance, maxDistance);
    }

    private Vector3 GetPivot()
    {
        return target.position + Vector3.up * (isFirstPerson ? firstPersonHeight : height);
    }

    private Vector3 GetDesiredPosition(Vector3 pivot)
    {
        var orbit = Quaternion.Euler(pitch, yaw, 0f);
        var offset = isFirstPerson ? Vector3.zero : new Vector3(shoulderOffset, 0f, -distance);
        return pivot + orbit * offset;
    }

    private void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
