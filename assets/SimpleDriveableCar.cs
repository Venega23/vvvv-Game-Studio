using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class SimpleDriveableCar : MonoBehaviour
{
    [Header("Interaction")]
    public float enterDistance = 3f;
    public Vector3 exitOffset = new Vector3(-2.2f, 0f, 0f);
    public CameraFollow cameraFollow;

    [Header("Driving")]
    public float maxForwardSpeed = 13f;
    public float maxReverseSpeed = 5f;
    public float acceleration = 18f;
    public float braking = 28f;
    public float steeringDegrees = 95f;
    public float groundY = 0f;
    public bool keepOnGround = true;
    public bool snapRendererBottomToGround = true;
    public float groundSkin = 0.02f;

    private PlayerMove driver;
    private GameObject driverObject;
    private float currentSpeed;
    private bool wasInteractPressed;

    public bool IsOccupied => driver != null;

    private void Awake()
    {
        if (cameraFollow == null && Camera.main != null)
        {
            cameraFollow = Camera.main.GetComponent<CameraFollow>();
        }
    }

    private void LateUpdate()
    {
        SnapToGround();
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

        var interactPressed = keyboard.eKey.wasPressedThisFrame;
        if (driver == null)
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, braking * Time.deltaTime);
            if (interactPressed)
            {
                TryEnterNearestDriver();
            }
            return;
        }

        Drive(keyboard);
        if (interactPressed && !wasInteractPressed)
        {
            ExitCar();
        }

        wasInteractPressed = interactPressed;
    }

    private void TryEnterNearestDriver()
    {
        var nearest = FindNearestEnabledDriver();
        if (nearest == null)
        {
            return;
        }

        driver = nearest;
        driverObject = nearest.gameObject;
        driver.ClearInputState();
        driver.enabled = false;
        driverObject.SetActive(false);

        if (cameraFollow == null && Camera.main != null)
        {
            cameraFollow = Camera.main.GetComponent<CameraFollow>();
        }

        if (cameraFollow != null)
        {
            cameraFollow.followControlledCharacter = false;
            cameraFollow.target = transform;
            cameraFollow.SnapToTarget();
        }
    }

    private PlayerMove FindNearestEnabledDriver()
    {
        PlayerMove nearest = null;
        var nearestSqrDistance = enterDistance * enterDistance;
        foreach (var move in FindObjectsByType<PlayerMove>(FindObjectsInactive.Exclude))
        {
            if (!move.enabled)
            {
                continue;
            }

            var sqrDistance = (move.transform.position - transform.position).sqrMagnitude;
            if (sqrDistance <= nearestSqrDistance)
            {
                nearest = move;
                nearestSqrDistance = sqrDistance;
            }
        }

        return nearest;
    }

    private void Drive(Keyboard keyboard)
    {
        var throttle = 0f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) throttle += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) throttle -= 1f;

        var targetSpeed = throttle >= 0f ? throttle * maxForwardSpeed : throttle * maxReverseSpeed;
        var speedChange = Mathf.Abs(throttle) > 0.01f ? acceleration : braking;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, speedChange * Time.deltaTime);

        var steer = 0f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) steer -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) steer += 1f;

        var steerScale = Mathf.InverseLerp(0.2f, maxForwardSpeed, Mathf.Abs(currentSpeed));
        transform.Rotate(Vector3.up, steer * steeringDegrees * steerScale * Time.deltaTime, Space.World);
        transform.position += transform.forward * (currentSpeed * Time.deltaTime);
        SnapToGround();
    }

    private void SnapToGround()
    {
        if (!keepOnGround)
        {
            return;
        }

        var position = transform.position;
        if (!snapRendererBottomToGround)
        {
            position.y = groundY;
            transform.position = position;
            return;
        }

        var bounds = GetRendererBounds();
        if (!bounds.HasValue)
        {
            position.y = groundY;
            transform.position = position;
            return;
        }

        position.y += (groundY + groundSkin) - bounds.Value.min.y;
        transform.position = position;
    }

    private Bounds? GetRendererBounds()
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
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

    private void ExitCar()
    {
        if (driverObject == null || driver == null)
        {
            driver = null;
            driverObject = null;
            return;
        }

        var exitPosition = transform.position + transform.TransformDirection(exitOffset);
        if (keepOnGround)
        {
            exitPosition.y = groundY;
        }

        driverObject.SetActive(true);
        driverObject.transform.position = exitPosition;
        driverObject.transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        driver.enabled = true;
        driver.ClearInputState();

        if (cameraFollow != null)
        {
            cameraFollow.followControlledCharacter = true;
            cameraFollow.SetControlledTarget(driverObject.transform, true);
        }

        driver = null;
        driverObject = null;
        currentSpeed = 0f;
        wasInteractPressed = true;
    }
}
