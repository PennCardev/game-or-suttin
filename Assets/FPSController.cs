using UnityEngine;

[DisallowMultipleComponent]
public class FPSController : MonoBehaviour
{
    [Header("References")]
    public CharacterController controller;
    public Transform cameraRoot;  
    public Camera cam;

    [Header("Look Settings")]
    [Range(0.05f, 10f)] public float mouseSensitivity = 2.2f;
    [Range(30f, 120f)] public float pitchClamp = 85f;
    public bool invertY = false;
    public float lookSmoothing = 18f; // Higher = tighter smoothing

    [Header("Movement Speeds")]
    public float walkSpeed = 1.75f;     
    public float sprintSpeed = 3.2f;
    public float crouchSpeed = 1.15f;
    public float airControlMultiplier = 0.35f;

    [Header("Acceleration (heavier = more horror weight)")]
    public float accel = 10f;
    public float decel = 14f;

    [Header("Gravity / Jump (optional)")]
    public bool allowJump = false;
    public float gravity = -18f;
    public float jumpHeight = 1.1f;

    [Header("Crouch Settings")]
    public float standHeight = 1.8f;
    public float crouchHeight = 1.15f;
    public float crouchTransitionSpeed = 12f;
    public float cameraStandLocalY = 0.78f;   // Camera local Y when standing
    public float cameraCrouchLocalY = 0.48f;  // Camera local Y when crouching

    [Header("Stamina (Sprint)")]
    public float staminaMax = 6.0f;
    public float staminaDrainPerSec = 1.25f;
    public float staminaRegenPerSec = 0.9f;
    public float staminaRegenDelay = 0.75f;
    public float sprintFov = 72f;
    public float baseFov = 66f;
    public float fovBlendSpeed = 10f;

    [Header("Headbob / Sway")]
    public float bobWalkFrequency = 1.65f;
    public float bobSprintFrequency = 2.35f;
    public float bobCrouchFrequency = 1.35f;

    public float bobWalkAmplitude = 0.035f;
    public float bobSprintAmplitude = 0.055f;
    public float bobCrouchAmplitude = 0.025f;

    public float bobSideAmplitude = 0.020f;
    public float bobReturnSpeed = 14f;

    [Header("Breathing Sway (idle tension)")]
    public float breathAmplitude = 0.018f;
    public float breathFrequency = 0.9f;

    [Header("Lean")]
    public float leanAngle = 10f;
    public float leanOffset = 0.12f;
    public float leanSpeed = 10f;

    [Header("Footstep Audio (optional)")]
    public AudioSource footstepSource;
    public AudioClip[] footstepClips;
    public float stepDistanceWalk = 1.65f;
    public float stepDistanceSprint = 1.15f;
    public float stepDistanceCrouch = 2.0f;

    [Header("Landing Thump (optional)")]
    public AudioSource landSource;
    public AudioClip landClip;
    public float landMinFallSpeed = 6.5f;

    // Internal state
    private float _yaw;
    private float _pitch;
    private Vector2 _lookVel;
    private Vector2 _lookSmooth;

    private Vector3 _moveVel;       // Smoothed horizontal velocity
    private float _verticalVel;     // Gravity and jump velocity
    private bool _wasGrounded;
    private float _fallSpeed;

    private bool _isCrouching;
    private bool _isSprinting;
    private float _stamina;
    private float _lastSprintTime;

    private float _bobT;
    private Vector3 _camRootBaseLocalPos;
    private float _stepAccum;

    private float _leanTarget;
    private float _leanCurrent;

    void Reset()
    {
        controller = GetComponent<CharacterController>();
        cam = GetComponentInChildren<Camera>();
    }

    void Awake()
    {
        if (!controller) controller = GetComponent<CharacterController>();
        if (!cam) cam = GetComponentInChildren<Camera>();
        if (!cameraRoot && cam) cameraRoot = cam.transform.parent;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        _stamina = staminaMax;

        if (cam) cam.fieldOfView = baseFov;

        if (cameraRoot)
            _camRootBaseLocalPos = cameraRoot.localPosition;

        // Ensure controller height matches standing height
        if (controller)
        {
            controller.height = standHeight;
            var c = controller.center;
            c.y = standHeight * 0.5f;
            controller.center = c;
        }
    }

    void Update()
    {
        if (!controller || !cameraRoot || !cam) return;

        HandleLook();
        HandleCrouch();
        HandleSprintAndStamina();
        HandleMovement();
        HandleCameraEffects();
        HandleFov();
    }

    void HandleLook()
    {
        float mx = Input.GetAxisRaw("Mouse X");
        float my = Input.GetAxisRaw("Mouse Y") * (invertY ? 1f : -1f);

        Vector2 target = new Vector2(mx, my) * mouseSensitivity;
        _lookSmooth = Vector2.SmoothDamp(_lookSmooth, target, ref _lookVel, 1f / lookSmoothing);

        _yaw += _lookSmooth.x;
        _pitch = Mathf.Clamp(_pitch + _lookSmooth.y, -pitchClamp, pitchClamp);

        transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);

        // Lean input Q/E
        float qLean = 0f;
        if (Input.GetKey(KeyCode.Q)) qLean -= 1f;
        if (Input.GetKey(KeyCode.E)) qLean += 1f;
        _leanTarget = qLean;

        _leanCurrent = Mathf.Lerp(_leanCurrent, _leanTarget, 1f - Mathf.Exp(-leanSpeed * Time.deltaTime));

        float roll = -_leanCurrent * leanAngle;
        cameraRoot.localRotation = Quaternion.Euler(_pitch, 0f, roll);
    }

    void HandleCrouch()
    {
        bool wantsCrouch = Input.GetKey(KeyCode.LeftControl);

        // Disallow crouch while sprinting
        if (_isSprinting) wantsCrouch = false;

        _isCrouching = wantsCrouch;

        float targetHeight = _isCrouching ? crouchHeight : standHeight;

        // Prevent standing up into ceiling
        if (!_isCrouching)
        {
            float radius = controller.radius * 0.95f;
            float castDist = (standHeight - controller.height) + 0.05f;
            Vector3 castOrigin = transform.position + Vector3.up * (controller.height * 0.5f);
            if (Physics.SphereCast(castOrigin, radius, Vector3.up, out _, castDist, ~0, QueryTriggerInteraction.Ignore))
            {
                targetHeight = crouchHeight;
                _isCrouching = true;
            }
        }

        controller.height = Mathf.Lerp(controller.height, targetHeight, 1f - Mathf.Exp(-crouchTransitionSpeed * Time.deltaTime));
        Vector3 center = controller.center;
        center.y = controller.height * 0.5f;
        controller.center = center;

        // Camera height blend
        Vector3 camPos = cameraRoot.localPosition;
        float targetCamY = _isCrouching ? cameraCrouchLocalY : cameraStandLocalY;
        camPos.y = Mathf.Lerp(camPos.y, targetCamY, 1f - Mathf.Exp(-crouchTransitionSpeed * Time.deltaTime));
        cameraRoot.localPosition = camPos;
    }

    void HandleSprintAndStamina()
    {
        bool sprintHeld = Input.GetKey(KeyCode.LeftShift);
        bool movingForwardish = Input.GetAxisRaw("Vertical") > 0.1f;
        bool hasMoveInput = Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.1f;

        bool canSprint = !_isCrouching && hasMoveInput && movingForwardish && _stamina > 0.05f;

        _isSprinting = sprintHeld && canSprint;

        if (_isSprinting)
        {
            _stamina = Mathf.Max(0f, _stamina - staminaDrainPerSec * Time.deltaTime);
            _lastSprintTime = Time.time;
        }
        else
        {
            if (Time.time - _lastSprintTime >= staminaRegenDelay)
                _stamina = Mathf.Min(staminaMax, _stamina + staminaRegenPerSec * Time.deltaTime);
        }
    }

    void HandleMovement()
    {
        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");

        Vector3 input = new Vector3(x, 0f, z);
        input = Vector3.ClampMagnitude(input, 1f);

        float targetSpeed = walkSpeed;
        if (_isCrouching) targetSpeed = crouchSpeed;
        else if (_isSprinting) targetSpeed = sprintSpeed;

        Vector3 wishDir = transform.TransformDirection(input);

        Vector3 targetVel = wishDir * targetSpeed;

        float a = (input.sqrMagnitude > 0.0001f) ? accel : decel;

        float airMul = controller.isGrounded ? 1f : airControlMultiplier;

        _moveVel = Vector3.Lerp(_moveVel, targetVel, (1f - Mathf.Exp(-a * Time.deltaTime)) * airMul);

        // Gravity & jump lowk bun this
        if (controller.isGrounded)
        {
            if (!_wasGrounded)
            {
                // Landing sound if falling fast
                if (landSource != null && landClip != null && _fallSpeed > landMinFallSpeed)
                {
                    landSource.pitch = Random.Range(0.95f, 1.05f);
                    landSource.PlayOneShot(landClip, Mathf.InverseLerp(landMinFallSpeed, landMinFallSpeed + 8f, _fallSpeed));
                }
            }

            _verticalVel = -2f; // keep grounded
            _fallSpeed = 0f;

            if (allowJump && Input.GetButtonDown("Jump") && !_isCrouching)
            {
                _verticalVel = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
        }
        else
        {
            _verticalVel += gravity * Time.deltaTime;
            _fallSpeed = Mathf.Max(_fallSpeed, -_verticalVel);
        }

        Vector3 velocity = _moveVel + Vector3.up * _verticalVel;
        controller.Move(velocity * Time.deltaTime);

        HandleFootsteps(targetSpeed, input.magnitude);

        _wasGrounded = controller.isGrounded;
    }

    void HandleFootsteps(float targetSpeed, float inputMag)
    {
        if (footstepSource == null || footstepClips == null || footstepClips.Length == 0) return;
        if (!controller.isGrounded) return;
        if (inputMag < 0.1f) return;

        float stepDist = stepDistanceWalk;
        if (_isSprinting) stepDist = stepDistanceSprint;
        if (_isCrouching) stepDist = stepDistanceCrouch;

        _stepAccum += (_moveVel.magnitude) * Time.deltaTime;

        if (_stepAccum >= stepDist)
        {
            _stepAccum = 0f;
            var clip = footstepClips[Random.Range(0, footstepClips.Length)];
            footstepSource.pitch = Random.Range(0.92f, 1.08f);
            float vol = _isSprinting ? 1.0f : (_isCrouching ? 0.55f : 0.8f);
            footstepSource.PlayOneShot(clip, vol);
        }
    }

    void HandleCameraEffects()
    {
        Vector3 basePos = cameraRoot.localPosition;
        float targetCamY = _isCrouching ? cameraCrouchLocalY : cameraStandLocalY;

        float leanX = _leanCurrent * leanOffset;

        bool hasMove = _moveVel.magnitude > 0.15f && controller.isGrounded;

        float freq = bobWalkFrequency;
        float amp = bobWalkAmplitude;

        if (_isSprinting) { freq = bobSprintFrequency; amp = bobSprintAmplitude; }
        else if (_isCrouching) { freq = bobCrouchFrequency; amp = bobCrouchAmplitude; }

        if (hasMove)
        {
            _bobT += Time.deltaTime * freq * (0.85f + _moveVel.magnitude * 0.15f);
        }
        else
        {
            _bobT = Mathf.Lerp(_bobT, 0f, 1f - Mathf.Exp(-4f * Time.deltaTime));
        }

        float bobY = hasMove ? Mathf.Sin(_bobT * Mathf.PI * 2f) * amp : 0f;
        float bobX = hasMove ? Mathf.Cos(_bobT * Mathf.PI * 2f) * bobSideAmplitude : 0f;

        float breath = Mathf.Sin(Time.time * breathFrequency * Mathf.PI * 2f) * breathAmplitude;

        Vector3 target = new Vector3(_camRootBaseLocalPos.x + leanX + bobX,
                                     targetCamY + bobY + breath,
                                     _camRootBaseLocalPos.z);

        cameraRoot.localPosition = Vector3.Lerp(cameraRoot.localPosition, target, 1f - Mathf.Exp(-bobReturnSpeed * Time.deltaTime));
    }

    void HandleFov()
    {
        float target = _isSprinting ? sprintFov : baseFov;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, target, 1f - Mathf.Exp(-fovBlendSpeed * Time.deltaTime));
    }

    // ill do it maybe (expose stamina normalized for UI)
    public float Stamina01 => staminaMax <= 0f ? 0f : Mathf.Clamp01(_stamina / staminaMax);
}
