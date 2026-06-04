using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class PlayerMove : MonoBehaviour
{
    [Header("Move")]
    public float speed = 5f;
    public float runSpeed = 8f;
    public float crouchSpeed = 2.5f;
    public Transform cameraTransform;
    public float turnSpeed = 12f;

    [Header("Controller Mode")]
    public bool useCharacterController = true;
    public bool preserveVerticalPosition;
    public bool canJump = true;
    public bool canCrouch = true;

    [Header("Studio Collision")]
    public bool ignoreWorldCollision = false;
    public float groundY = 0f;
    public Key togglePhaseKey = Key.N;
    public Key toggleFlyKey = Key.F;
    public Key toggleStrutKey = Key.X;
    private bool isPhasing = false;
    private bool isFlying = false;
    private bool strutModeEnabled = false;

    [Header("Jump")]
    public float jumpForce = 7f;
    public float gravity = -24f;
    public float groundedStickForce = -2f;
    public float flyVerticalSpeed = 5f;

    [Header("Crouch")]
    public float crouchHeight = 1.15f;
    public float crouchBlendSpeed = 12f;

    private CharacterController characterController;
    private Rigidbody rb;
    private CapsuleCollider capsule;
    private bool isGrounded;
    private bool jumpRequested;
    private float moveX;
    private float moveZ;
    private float moveY;
    private float verticalVelocity;
    private float standingHeight;
    private Vector3 standingCenter;
    private float lockedY;

    public Vector3 MoveDirection { get; private set; }
    public float CurrentSpeed { get; private set; }
    public bool IsMoving { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsCrouching { get; private set; }
    public bool IsStrutting { get; private set; }
    public bool IsFlying => isFlying;
    public bool IsGrounded => isGrounded;

    private void Awake()
    {
        lockedY = transform.position.y;
        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }

        if (!useCharacterController)
        {
            isGrounded = true;
            return;
        }

        characterController = GetComponent<CharacterController>();
        if (characterController == null)
        {
            characterController = gameObject.AddComponent<CharacterController>();
        }

        capsule = GetComponent<CapsuleCollider>();
        if (capsule != null)
        {
            characterController.radius = capsule.radius;
            characterController.height = capsule.height;
            characterController.center = capsule.center;
            capsule.enabled = false;
        }

        characterController.stepOffset = 0.35f;
        characterController.slopeLimit = 50f;
        characterController.skinWidth = 0.04f;
        characterController.minMoveDistance = 0f;
        characterController.detectCollisions = !ignoreWorldCollision;

        standingHeight = characterController.height;
        standingCenter = characterController.center;

        if (jumpForce < 5.5f)
        {
            jumpForce = 7f;
        }
    }

    private void OnEnable()
    {
        if (preserveVerticalPosition)
        {
            lockedY = transform.position.y;
        }
    }

    private void Start()
    {
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    private void Update()
    {
        if (MapSwitcher.IsMenuOpen || CharacterActionRecorder.IsMenuOpen || CharacterTuningMenu.IsOpen || SurfaceItemPlacer.BlocksGameplayInput)
        {
            ClearInputState();
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard[togglePhaseKey].wasPressedThisFrame)
            {
                isPhasing = !isPhasing;
            }

            if (keyboard[toggleFlyKey].wasPressedThisFrame)
            {
                isFlying = !isFlying;
                if (!isFlying && preserveVerticalPosition)
                {
                    lockedY = transform.position.y;
                }
            }
        }

        if (characterController != null)
        {
            // If phasing is active, we disable physics collisions entirely
            characterController.detectCollisions = !isPhasing && !ignoreWorldCollision;
        }
        
        ReadInput();
        MoveCharacter();
        ApplyCrouchController();
    }

    public void ClearInputState()
    {
        moveX = 0f;
        moveY = 0f;
        moveZ = 0f;
        jumpRequested = false;
        verticalVelocity = 0f;
        MoveDirection = Vector3.zero;
        CurrentSpeed = 0f;
        IsMoving = false;
        IsRunning = false;
        IsCrouching = false;
        IsStrutting = false;
        strutModeEnabled = false;
    }

    private void ReadInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        moveX = 0f;
        moveY = 0f;
        moveZ = 0f;

        if (keyboard.wKey.isPressed) moveZ = 1f;
        if (keyboard.sKey.isPressed) moveZ = -1f;
        if (keyboard.aKey.isPressed) moveX = -1f;
        if (keyboard.dKey.isPressed) moveX = 1f;

        if (isFlying)
        {
            if (keyboard.spaceKey.isPressed) moveY = 1f;
            if (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed) moveY = -1f;
        }

        if (keyboard[toggleStrutKey].wasPressedThisFrame)
        {
            strutModeEnabled = !strutModeEnabled;
        }

        IsCrouching = !isFlying && canCrouch && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
        IsStrutting = strutModeEnabled && !isFlying && !IsCrouching && (Mathf.Abs(moveX) > 0.001f || Mathf.Abs(moveZ) > 0.001f);
        IsRunning = !IsCrouching && !IsStrutting && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);

        if (!isFlying && canJump && keyboard.spaceKey.wasPressedThisFrame && isGrounded && !IsCrouching)
        {
            jumpRequested = true;
        }
    }

    private void MoveCharacter()
    {
        var move = GetCameraRelativeMove();
        MoveDirection = move;
        IsMoving = move.sqrMagnitude > 0.001f || Mathf.Abs(moveY) > 0.001f;
        CurrentSpeed = IsMoving ? (IsCrouching ? crouchSpeed : IsRunning ? runSpeed : speed) : 0f;

        if (IsStrutting && !isFlying && !IsCrouching)
        {
            CurrentSpeed = speed * 0.75f;
        }

        if (isFlying)
        {
            var flyVelocity = move * CurrentSpeed + Vector3.up * (moveY * flyVerticalSpeed);
            if (characterController != null && !isPhasing)
            {
                characterController.Move(flyVelocity * Time.deltaTime);
            }
            else
            {
                transform.position += flyVelocity * Time.deltaTime;
            }
            RotateTowardMove(move);
            isGrounded = false;
            verticalVelocity = 0f;
            return;
        }

        // If phasing, we bypass the CharacterController entirely and move the transform directly
        if (isPhasing)
        {
            MoveTransformPreservingHeight(move);
            return;
        }

        if (ignoreWorldCollision)
        {
            MoveTransformIgnoringWorld(move);
            return;
        }

        if (!useCharacterController || characterController == null)
        {
            MoveTransformPreservingHeight(move);
            return;
        }

        isGrounded = characterController.isGrounded;
        if (isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = groundedStickForce;
        }

        if (jumpRequested)
        {
            jumpRequested = false;
            verticalVelocity = jumpForce;
            isGrounded = false;
        }

        verticalVelocity += gravity * Time.deltaTime;

        var velocity = move * CurrentSpeed;
        velocity.y = preserveVerticalPosition ? 0f : verticalVelocity;
        characterController.Move(velocity * Time.deltaTime);

        if (preserveVerticalPosition)
        {
            var position = transform.position;
            position.y = lockedY;
            transform.position = position;
            verticalVelocity = 0f;
            isGrounded = true;
        }

        RotateTowardMove(move);
        isGrounded = characterController.isGrounded || preserveVerticalPosition;
    }

    private void MoveTransformIgnoringWorld(Vector3 move)
    {
        if (preserveVerticalPosition)
        {
            MoveTransformPreservingHeight(move);
            return;
        }

        if (transform.position.y <= groundY + 0.01f)
        {
            isGrounded = true;
            if (verticalVelocity < 0f)
            {
                verticalVelocity = groundedStickForce;
            }
        }

        if (jumpRequested)
        {
            jumpRequested = false;
            verticalVelocity = jumpForce;
            isGrounded = false;
        }

        verticalVelocity += gravity * Time.deltaTime;

        var position = transform.position;
        position += move * (CurrentSpeed * Time.deltaTime);
        position.y += verticalVelocity * Time.deltaTime;

        if (position.y <= groundY)
        {
            position.y = groundY;
            verticalVelocity = groundedStickForce;
            isGrounded = true;
        }
        else
        {
            isGrounded = false;
        }

        transform.position = position;
        RotateTowardMove(move);
    }

    private void MoveTransformPreservingHeight(Vector3 move)
    {
        var delta = move * (CurrentSpeed * Time.deltaTime);
        var position = transform.position + delta;
        if (preserveVerticalPosition)
        {
            position.y = lockedY;
        }

        transform.position = position;
        isGrounded = true;
        verticalVelocity = 0f;
        RotateTowardMove(move);
    }

    private void RotateTowardMove(Vector3 move)
    {
        var cameraFollow = cameraTransform != null ? cameraTransform.GetComponent<CameraFollow>() : null;
        var isFirstPerson = cameraFollow != null && cameraFollow.isFirstPerson;
        var isAiming = Mouse.current != null && Mouse.current.rightButton.isPressed && !SimpleInventoryWindow.IsOpen && !CharacterActionRecorder.IsMenuOpen && !CharacterTuningMenu.IsOpen && !SurfaceItemPlacer.BlocksToolInput;

        if (isFirstPerson || isAiming)
        {
            if (cameraTransform != null)
            {
                var targetRotation = Quaternion.Euler(0f, cameraTransform.eulerAngles.y, 0f);
                if (isFirstPerson)
                {
                    transform.rotation = targetRotation;
                }
                else
                {
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
                }
            }
            return;
        }

        if (move.sqrMagnitude <= 0.001f)
        {
            return;
        }

        var lookRotation = Quaternion.LookRotation(move, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, turnSpeed * Time.deltaTime);
    }


    private Vector3 GetCameraRelativeMove()
    {
        var input = new Vector3(moveX, 0f, moveZ);
        if (input.sqrMagnitude <= 0.001f)
        {
            return Vector3.zero;
        }

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (cameraTransform == null)
        {
            return input.normalized;
        }

        var forward = cameraTransform.forward;
        forward.y = 0f;
        forward.Normalize();

        var right = cameraTransform.right;
        right.y = 0f;
        right.Normalize();

        return (forward * moveZ + right * moveX).normalized;
    }

    private void ApplyCrouchController()
    {
        if (!canCrouch || characterController == null)
        {
            return;
        }

        var targetHeight = IsCrouching ? crouchHeight : standingHeight;
        var targetCenter = IsCrouching
            ? new Vector3(standingCenter.x, crouchHeight * 0.5f, standingCenter.z)
            : standingCenter;

        characterController.height = Mathf.Lerp(characterController.height, targetHeight, crouchBlendSpeed * Time.deltaTime);
        characterController.center = Vector3.Lerp(characterController.center, targetCenter, crouchBlendSpeed * Time.deltaTime);
    }
}
