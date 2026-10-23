using UnityEngine;
#if FPS_CINEMACHINE
using Unity.Cinemachine;
#endif

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Empty child at eye level. The CinemachineCamera is a child of this.")]
    [SerializeField]
    Transform cameraTarget;

    [Header("Look")]
    [Tooltip("Degrees per mouse pixel.")]
    [SerializeField]
    float sensitivity = 0.1f;

    [SerializeField, Range(10f, 89f)]
    float pitchLimit = 85f;

    [Tooltip("Head turn limit relative to body. Past this, the body rotates.")]
    [SerializeField, Range(30f, 180f)]
    float maxHeadYaw = 90f;

    [Tooltip("Body auto-aligns to view direction while moving (deg/s). 0 = off.")]
    [SerializeField]
    float bodyAlignSpeed = 360f;

    [SerializeField]
    bool invertY;

    [Header("Movement")]
    [SerializeField]
    float walkSpeed = 4f;

    [SerializeField]
    float crouchSpeed = 2f;

    [Tooltip("Ground acceleration (m/s²).")]
    [SerializeField]
    float acceleration = 30f;

    [Tooltip("Acceleration multiplier while airborne.")]
    [SerializeField, Range(0f, 1f)]
    float airControl = 0.3f;

    [Header("Sprint (Shift)")]
    [SerializeField]
    float sprintSpeed = 6.5f;

    [Tooltip("On = stamina-limited. Off = unlimited sprint.")]
    [SerializeField]
    bool limitedStamina = true;

    [SerializeField]
    float maxStamina = 100f;

    [Tooltip("Stamina spent per second while sprinting.")]
    [SerializeField]
    float staminaDrainRate = 20f;

    [Tooltip("Stamina regained per second when not sprinting.")]
    [SerializeField]
    float staminaRegenRate = 15f;

    [Tooltip("Seconds after sprinting before regen starts.")]
    [SerializeField]
    float regenDelay = 1f;

    [Tooltip("After running out, stamina fraction needed to sprint again.")]
    [SerializeField, Range(0f, 1f)]
    float exhaustRecovery = 0.25f;

    [Tooltip("Bob amplitude multiplier while sprinting.")]
    [SerializeField]
    float sprintBobMultiplier = 1.3f;

    [Tooltip("Runtime display (overwritten each frame when limited).")]
    [SerializeField]
    float stamina;

    [Header("Jump")]
    [SerializeField]
    float jumpHeight = 1.2f;

    [SerializeField]
    float gravity = 20f;

    [Header("Crouch")]
    [SerializeField]
    float crouchHeight = 1f;

    [Tooltip("Eye height above feet.")]
    [SerializeField]
    float standEyeHeight = 1.7f;

    [SerializeField]
    float crouchEyeHeight = 0.9f;

    [SerializeField]
    float crouchTime = 0.2f;

    [Tooltip("Sprinting while moving cancels toggled crouch.")]
    [SerializeField]
    bool sprintCancelsToggle = true;

    [SerializeField]
    LayerMask obstructionMask = ~0;

    [Header("Zoom")]
    [SerializeField]
    bool zoomEnabled = true;

    [Tooltip("Auto-found under CameraTarget if empty.")]
    [SerializeField]
    CinemachineCamera vcam;

    [Tooltip("Vertical FOV when fully zoomed (lower = closer).")]
    [SerializeField, Range(5f, 90f)]
    float zoomFov = 30f;

    [SerializeField]
    float zoomInTime = 0.25f;

    [SerializeField]
    float zoomOutTime = 0.2f;

    [Tooltip("Easing over 0..1. X = progress, Y = zoom amount.")]
    [SerializeField]
    AnimationCurve zoomCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Look sensitivity multiplier at full zoom.")]
    [SerializeField, Range(0.1f, 1f)]
    float zoomSensitivity = 0.5f;

    [Header("View Bob")]
    [SerializeField]
    bool bobEnabled = true;

    [Tooltip("Cycles/sec at walk speed.")]
    [SerializeField]
    float bobFrequency = 1.8f;

    [SerializeField]
    float bobAmplitudeY = 0.05f;

    [SerializeField]
    float bobAmplitudeX = 0.03f;

    [Tooltip("Amplitude multiplier while crouched.")]
    [SerializeField, Range(0f, 1f)]
    float crouchBobMultiplier = 0.6f;

    [Tooltip("Fade in/out speed of the bob.")]
    [SerializeField]
    float bobBlendSpeed = 8f;

    CharacterController _cc;
    float _standHeight,
        _feetOffset; // feet Y relative to transform
    float _pitch,
        _headYaw; // headYaw is world-space
    float _crouchT; // 0 stand .. 1 crouched
    bool _wantsCrouch,
        _crouchToggled; // C-key latch
    Vector3 _horizVel;
    float _vertVel;
    bool _grounded;
    float _bobPhase,
        _bobWeight;
    float _baseFov,
        _zoomT,
        _zoomEased; // _zoomT linear 0..1, _zoomEased after curve
    bool _zoomReady;
    bool _sprinting,
        _exhausted;
    float _regenTimer,
        _sprintBlend;
    readonly Collider[] _hits = new Collider[8];

    public bool IsGrounded => _grounded;
    public bool IsCrouching => _wantsCrouch;
    public bool IsCrouchToggled => _crouchToggled;
    public bool IsSprinting => _sprinting;
    public bool IsExhausted => _exhausted;
    public bool LimitedStamina => limitedStamina;
    public float Stamina => stamina;
    public float StaminaNormalized => maxStamina > 0f ? stamina / maxStamina : 1f;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        if (cameraTarget == null)
            cameraTarget = transform.Find("CameraTarget");
        if (cameraTarget == null)
        {
            Debug.LogError($"{name}: CameraTarget not assigned.", this);
            enabled = false;
            return;
        }
        _standHeight = _cc.height;
        _feetOffset = _cc.center.y - _cc.height * 0.5f;
        _headYaw = transform.eulerAngles.y;
        stamina = maxStamina;

        if (vcam == null)
            vcam = cameraTarget.GetComponentInChildren<CinemachineCamera>(true);
        _zoomReady = vcam != null;
        if (_zoomReady)
            _baseFov = vcam.Lens.FieldOfView;
        else
            Debug.LogWarning($"{name}: no CinemachineCamera found. Zoom disabled.", this);
    }

    void OnDisable()
    {
        // Restore FOV so the camera isn't left zoomed.
        _zoomT = _zoomEased = 0f;
        _crouchToggled = false;
        if (_zoomReady && vcam != null)
            vcam.Lens.FieldOfView = _baseFov;
    }

    void Update()
    {
        var input = InputManager.Instance;
        if (input == null)
            return; // quitting

        float dt = Time.deltaTime;
        Vector2 move = input.Move;

        UpdateZoom(input.ZoomHeld, dt);
        UpdateLook(input.Look, move.sqrMagnitude > 0.01f, dt);
        UpdateCrouch(
            input.CrouchHeld,
            input.CrouchTogglePressed,
            input.SprintHeld && move.sqrMagnitude > 0.01f,
            dt
        );
        UpdateSprint(input.SprintHeld, move, dt);
        UpdateMovement(move, input.JumpPressed, dt);
        UpdateCamera(dt);
    }

    void UpdateZoom(bool held, float dt)
    {
        if (!_zoomReady || vcam == null)
            return;

        bool zoomIn = zoomEnabled && held;
        float time = zoomIn ? zoomInTime : zoomOutTime;
        _zoomT = Mathf.MoveTowards(_zoomT, zoomIn ? 1f : 0f, dt / Mathf.Max(time, 0.01f));

        // Null/empty curve falls back to smoothstep.
        _zoomEased =
            (zoomCurve != null && zoomCurve.length > 0)
                ? Mathf.Clamp01(zoomCurve.Evaluate(_zoomT))
                : Mathf.SmoothStep(0f, 1f, _zoomT);

        float target = Mathf.Min(zoomFov, _baseFov); // never zoom "out"
        float fov = Mathf.Lerp(_baseFov, target, _zoomEased);
        if (!Mathf.Approximately(vcam.Lens.FieldOfView, fov))
            vcam.Lens.FieldOfView = fov;
    }

    void UpdateLook(Vector2 delta, bool moving, float dt)
    {
        delta *= sensitivity * Mathf.Lerp(1f, zoomSensitivity, _zoomEased); // slower aim when zoomed
        _pitch = Mathf.Clamp(_pitch + (invertY ? delta.y : -delta.y), -pitchLimit, pitchLimit);
        _headYaw = Mathf.Repeat(_headYaw + delta.x, 360f);

        float bodyYaw = transform.eulerAngles.y;
        float rel = Mathf.DeltaAngle(bodyYaw, _headYaw);

        if (Mathf.Abs(rel) > maxHeadYaw) // head past limit: drag body
            bodyYaw = _headYaw - Mathf.Sign(rel) * maxHeadYaw;
        else if (moving && bodyAlignSpeed > 0f) // walking: body follows view
            bodyYaw = Mathf.MoveTowardsAngle(bodyYaw, _headYaw, bodyAlignSpeed * dt);

        transform.rotation = Quaternion.Euler(0f, bodyYaw, 0f);
        cameraTarget.localRotation = Quaternion.Euler(
            _pitch,
            Mathf.DeltaAngle(bodyYaw, _headYaw),
            0f
        );
    }

    void UpdateCrouch(bool held, bool togglePressed, bool sprintIntent, float dt)
    {
        if (togglePressed)
            _crouchToggled = !_crouchToggled;
        else if (_crouchToggled && sprintCancelsToggle && sprintIntent && !held && CanStand())
            _crouchToggled = false; // sprint exits crouch

        if (held || _crouchToggled)
            _wantsCrouch = true;
        else if (_wantsCrouch && CanStand())
            _wantsCrouch = false;

        _crouchT = Mathf.MoveTowards(
            _crouchT,
            _wantsCrouch ? 1f : 0f,
            dt / Mathf.Max(crouchTime, 0.01f)
        );
        float s = Mathf.SmoothStep(0f, 1f, _crouchT);

        float h = Mathf.Lerp(_standHeight, crouchHeight, s);
        _cc.height = h;
        _cc.center = new Vector3(0f, _feetOffset + h * 0.5f, 0f); // keep feet planted
    }

    bool CanStand()
    {
        float r = _cc.radius * 0.95f;
        Vector3 p = transform.position + Vector3.up * (_feetOffset + _standHeight - _cc.radius);
        int n = Physics.OverlapSphereNonAlloc(
            p,
            r,
            _hits,
            obstructionMask,
            QueryTriggerInteraction.Ignore
        );
        for (int i = 0; i < n; i++)
            if (_hits[i] != null && _hits[i] != _cc)
                return false; // ignore self
        return true;
    }

    void UpdateSprint(bool held, Vector2 move, float dt)
    {
        maxStamina = Mathf.Max(0f, maxStamina);
        stamina = Mathf.Clamp(stamina, 0f, maxStamina);

        // Crouch has priority. Needs movement input.
        bool wants = held && move.sqrMagnitude > 0.01f && !_wantsCrouch;

        if (!limitedStamina)
        {
            _exhausted = false;
            stamina = maxStamina;
            _sprinting = wants;
        }
        else
        {
            if (_exhausted && stamina >= maxStamina * exhaustRecovery)
                _exhausted = false;
            _sprinting = wants && !_exhausted && stamina > 0f;

            if (_sprinting)
            {
                stamina -= staminaDrainRate * dt;
                _regenTimer = regenDelay;
                if (stamina <= 0f)
                {
                    stamina = 0f;
                    _exhausted = true;
                    _sprinting = false;
                }
            }
            else
            {
                _regenTimer -= dt;
                if (_regenTimer <= 0f)
                    stamina = Mathf.Min(maxStamina, stamina + staminaRegenRate * dt);
            }
        }
        _sprintBlend = Mathf.MoveTowards(_sprintBlend, _sprinting ? 1f : 0f, bobBlendSpeed * dt);
    }

    void UpdateMovement(Vector2 move, bool jump, float dt)
    {
        Vector3 wish = Quaternion.Euler(0f, _headYaw, 0f) * new Vector3(move.x, 0f, move.y);
        if (wish.sqrMagnitude > 1f)
            wish.Normalize();

        float s = Mathf.SmoothStep(0f, 1f, _crouchT);
        float maxSpeed = _sprinting ? sprintSpeed : Mathf.Lerp(walkSpeed, crouchSpeed, s);
        float accel = _grounded ? acceleration : acceleration * airControl;
        _horizVel = Vector3.MoveTowards(_horizVel, wish * maxSpeed, accel * dt);

        if (_grounded && _vertVel < 0f)
            _vertVel = -2f; // stick to ground
        if (_grounded && jump)
            _vertVel = Mathf.Sqrt(2f * gravity * jumpHeight);
        _vertVel -= gravity * dt;

        var flags = _cc.Move((_horizVel + Vector3.up * _vertVel) * dt);
        _grounded = _cc.isGrounded;
        if ((flags & CollisionFlags.Above) != 0 && _vertVel > 0f)
            _vertVel = 0f;
    }

    void UpdateCamera(float dt)
    {
        float s = Mathf.SmoothStep(0f, 1f, _crouchT);
        float eye = Mathf.Lerp(standEyeHeight, crouchEyeHeight, s);
        Vector3 basePos = new Vector3(0f, _feetOffset + eye, 0f);

        float speed = new Vector2(_horizVel.x, _horizVel.z).magnitude;
        float maxSpeed = _sprinting ? sprintSpeed : Mathf.Lerp(walkSpeed, crouchSpeed, s);

        bool active = bobEnabled && _grounded && speed > 0.1f;
        _bobWeight = Mathf.Lerp(_bobWeight, active ? 1f : 0f, 1f - Mathf.Exp(-bobBlendSpeed * dt));

        if (active)
        {
            _bobPhase += 2f * Mathf.PI * bobFrequency * (speed / Mathf.Max(walkSpeed, 0.01f)) * dt;
            _bobPhase = Mathf.Repeat(_bobPhase, 2f * Mathf.PI);
        }

        float amp =
            _bobWeight
            * Mathf.Clamp01(speed / Mathf.Max(maxSpeed, 0.01f))
            * Mathf.Lerp(1f, crouchBobMultiplier, s)
            * Mathf.Lerp(1f, sprintBobMultiplier, _sprintBlend);
        Vector3 bob =
            new Vector3(
                Mathf.Cos(_bobPhase) * bobAmplitudeX,
                Mathf.Sin(_bobPhase * 2f) * bobAmplitudeY,
                0f
            ) * amp;

        cameraTarget.localPosition = basePos + bob;
    }

    void OnValidate()
    {
        sprintSpeed = Mathf.Max(sprintSpeed, walkSpeed);
        maxStamina = Mathf.Max(0f, maxStamina);
        staminaDrainRate = Mathf.Max(0f, staminaDrainRate);
        staminaRegenRate = Mathf.Max(0f, staminaRegenRate);
        regenDelay = Mathf.Max(0f, regenDelay);
        crouchHeight = Mathf.Max(0.5f, crouchHeight);
        crouchEyeHeight = Mathf.Min(crouchEyeHeight, standEyeHeight);
        jumpHeight = Mathf.Max(0f, jumpHeight);
        gravity = Mathf.Max(0.1f, gravity);
    }
}
