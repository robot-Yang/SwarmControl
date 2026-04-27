using UnityEngine;

/// <summary>
/// Rate-based movement input from chest IMU.
/// Torso pitch tilt → forward/back speed; torso roll tilt → left/right speed.
/// Output is a velocity vector in world units/sec (already scaled by maxPitchSpeed
/// / maxRollSpeed) which InputFusionManager passes through to MigrationPointController.
///
/// Single concrete class. Replaces the old abstract base + selector + Linear /
/// Exponential / RateBased modes — all variants were rate-based with different
/// curves, which collapses cleanly to one class with a configurable response curve.
/// </summary>
public class IMUMovementInput : MonoBehaviour
{
    [Header("IMU Source")]
    [Tooltip("Reference to the OpenZen chest IMU sensor")]
    public OpenZenMoveObject openZenIMU;

    [Header("Angle Mapping (degrees)")]
    [Tooltip("Pitch angle that produces maximum forward/backward speed")]
    public float pitchMaxAngle = 30f;

    [Tooltip("Roll angle that produces maximum left/right speed")]
    public float rollMaxAngle = 30f;

    [Header("Speed (units/sec at full tilt)")]
    public float maxPitchSpeed = 4f;
    public float maxRollSpeed = 4f;

    [Header("Response Curve")]
    [Tooltip("Curve exponent. 1 = linear, 2 = squared (precise near center, fast at extremes), 3 = cubic.")]
    [Range(1f, 3f)]
    public float responseCurve = 2f;

    [Header("Deadzones (degrees)")]
    public float pitchDeadzone = 5f;
    public float rollDeadzone = 5f;

    [Header("Inversion")]
    public bool invertPitch = false;
    public bool invertRoll = true;

    [Header("Smoothing")]
    [Tooltip("0 = instant, 1 = max smoothing. Higher = less jitter, more lag.")]
    [Range(0f, 1f)]
    public float smoothingFactor = 0.3f;

    [Header("Local Test Calibration")]
    [Tooltip("Optional standalone calibrate key — official flow is via InputFusionManager.PerformCalibration()")]
    public KeyCode calibrateKey = KeyCode.C;

    [Tooltip("Calibrate neutral on Start (rarely needed if OpenZenMoveObject already calibrates)")]
    public bool autoCalibrateOnStart = false;

    [Header("Debug")]
    public bool showDebugInfo = false;

    // ============================================
    // PUBLIC OUTPUTS (read by InputFusionManager)
    // ============================================

    /// <summary>Movement vector in world units/sec. X = left/right, Y = 0, Z = forward/back.</summary>
    public Vector3 MovementVector { get; private set; }

    /// <summary>True when the chest IMU sensor reference is assigned.</summary>
    public bool IsAvailable => openZenIMU != null;

    /// <summary>True when pitch is actively producing left/right movement — used by InputFusionManager to disable headset yaw rotation when tilting.</summary>
    public bool IsPitchActive { get; private set; }

    // ============================================
    // PRIVATE STATE
    // ============================================

    private Vector3 _calibrationOffset = Vector3.zero;
    private bool _initialized = false;
    private Vector3 _smoothedMovementVector = Vector3.zero;

    void Update()
    {
        if (!_initialized && IsAvailable && autoCalibrateOnStart)
        {
            CalibrateNeutral();
            _initialized = true;
        }

        if (Input.GetKeyDown(calibrateKey)) CalibrateNeutral();

        if (!IsAvailable)
        {
            MovementVector = Vector3.zero;
            _smoothedMovementVector = Vector3.zero;
            return;
        }

        Vector3 angles = openZenIMU.SensorEulerAnglesDirect;
        if (autoCalibrateOnStart || _calibrationOffset != Vector3.zero)
            angles -= _calibrationOffset;

        Vector3 rawMovement = ConvertIMUToMovement(angles);

        float smoothSpeed = 1f - smoothingFactor;
        _smoothedMovementVector = Vector3.Lerp(_smoothedMovementVector, rawMovement, smoothSpeed * Time.deltaTime * 10f);
        MovementVector = _smoothedMovementVector;
    }

    Vector3 ConvertIMUToMovement(Vector3 eulerAngles)
    {
        // SWAPPED: Roll → forward/back; Pitch → left/right
        float pitch = NormalizeAngle(eulerAngles.x);
        float roll  = NormalizeAngle(eulerAngles.z);

        if (Mathf.Abs(pitch) < pitchDeadzone) pitch = 0f;
        if (Mathf.Abs(roll)  < rollDeadzone)  roll  = 0f;

        float pitchNormalized = Mathf.Clamp01(Mathf.Abs(pitch) / pitchMaxAngle);
        float rollNormalized  = Mathf.Clamp01(Mathf.Abs(roll)  / rollMaxAngle);

        // Rate-based curve: (normalized^curve) * maxSpeed
        float forwardMapped = Mathf.Pow(rollNormalized,  responseCurve) * maxRollSpeed;
        float rightMapped   = Mathf.Pow(pitchNormalized, responseCurve) * maxPitchSpeed;

        float forward = Mathf.Sign(roll)  * forwardMapped;
        float right   = Mathf.Sign(pitch) * rightMapped;

        if (invertPitch) forward = -forward;
        if (invertRoll)  right   = -right;

        IsPitchActive = Mathf.Abs(right) > 0.01f;

        return new Vector3(right, 0f, forward);
    }

    static float NormalizeAngle(float angle)
    {
        if (angle > 180f) return angle - 360f;
        return angle;
    }

    /// <summary>Set current chest IMU orientation as the new neutral.</summary>
    public void CalibrateNeutral()
    {
        if (!IsAvailable)
        {
            Debug.LogWarning("IMUMovementInput: Cannot calibrate — IMU not available.");
            return;
        }
        _calibrationOffset = openZenIMU.SensorEulerAnglesDirect;
        Debug.Log($"IMUMovementInput: calibrated. Offset = {_calibrationOffset}");
    }

    // ============================================
    // DEBUG HELPERS
    // ============================================

    public float GetPitchAngle() => IsAvailable
        ? AppliedAngle(openZenIMU.SensorEulerAnglesDirect.x, _calibrationOffset.x, pitchDeadzone)
        : 0f;

    public float GetRollAngle()  => IsAvailable
        ? AppliedAngle(openZenIMU.SensorEulerAnglesDirect.z, _calibrationOffset.z, rollDeadzone)
        : 0f;

    public float GetYawAngle()   => IsAvailable
        ? NormalizeAngle(openZenIMU.SensorEulerAnglesDirect.y - _calibrationOffset.y)
        : 0f;

    static float AppliedAngle(float raw, float offset, float deadzone)
    {
        float angle = NormalizeAngle(raw - offset);
        return Mathf.Abs(angle) < deadzone ? 0f : angle;
    }

    void OnGUI()
    {
        if (!showDebugInfo || !Application.isPlaying || !IsAvailable) return;
        GUILayout.BeginArea(new Rect(10, 840, 360, 110));
        GUILayout.Label("<b>=== IMU MOVEMENT (rate) ===</b>");
        GUILayout.Label($"Pitch: {GetPitchAngle():F1}°  Roll: {GetRollAngle():F1}°");
        GUILayout.Label($"Output: {MovementVector}");
        GUILayout.Label($"Pitch active (yaw lock): {IsPitchActive}");
        GUILayout.EndArea();
    }
}
