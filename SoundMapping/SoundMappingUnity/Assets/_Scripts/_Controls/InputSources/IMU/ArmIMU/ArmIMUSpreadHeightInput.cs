using UnityEngine;

/// <summary>
/// Reads two forearm IMUs relative to the chest IMU to derive swarm spread and height.
/// Height  = average relative pitch of both arms (arms up = swarm rises)
/// Spread  = yaw angular difference between arms (arms apart = swarm expands)
///
/// Spread modes:
///   Absolute  — arm angle directly maps to target separation in meters (hold arm = hold spread)
///   RateBased — arm angle maps to rate of change (-1..+1), release to neutral = hold spread
/// </summary>
public enum SpreadControlMode { Absolute, RateBased }

public class ArmIMUSpreadHeightInput : MonoBehaviour
{
    // ============================================
    // IMU REFERENCES
    // ============================================
    [Header("IMU Sources")]
    [Tooltip("Chest IMU — used as the reference frame for both arm sensors")]
    public OpenZenMoveObject chestIMU;

    [Tooltip("Left forearm IMU")]
    public OpenZenMoveObject leftArmIMU;

    [Tooltip("Right forearm IMU")]
    public OpenZenMoveObject rightArmIMU;

    // ============================================
    // HEIGHT SETTINGS
    // ============================================
    [Header("Height Mapping")]
    [Tooltip("Arm pitch angle (degrees) that produces maximum height output (+1 or -1)")]
    public float pitchMaxAngle = 60f;

    [Tooltip("Ignore pitch angles smaller than this (degrees)")]
    public float pitchDeadzone = 5f;

    [Tooltip("Response curve exponent for height (1 = linear, 2 = squared)")]
    [Range(1f, 3f)]
    public float pitchResponseCurve = 1.5f;

    [Tooltip("Height smoothing factor (0 = instant, 1 = maximum smoothing)")]
    [Range(0f, 0.95f)]
    public float heightSmoothing = 0.3f;

    // ============================================
    // SPREAD SETTINGS
    // ============================================
    [Header("Spread Mapping")]
    [Tooltip("Absolute: arm angle maps to target meters. RateBased: arm angle maps to rate of change.")]
    public SpreadControlMode spreadMode = SpreadControlMode.Absolute;

    [Tooltip("Yaw angle difference (degrees) between arms that maps to maximum spread (1.0)")]
    public float yawMaxSpreadAngle = 120f;

    [Tooltip("Ignore yaw differences smaller than this (degrees)")]
    public float yawDeadzone = 5f;

    [Tooltip("Response curve exponent for spread (1 = linear, 2 = squared)")]
    [Range(1f, 3f)]
    public float spreadResponseCurve = 1.5f;

    [Tooltip("Spread smoothing factor (0 = instant, 1 = maximum smoothing)")]
    [Range(0f, 0.95f)]
    public float spreadSmoothing = 0.3f;

    [Header("Spread → Separation Mapping")]
    [Tooltip("Swarm separation (meters) when spread = 0")]
    public float minSwarmSeparation = 1.0f;

    [Tooltip("Swarm separation (meters) when spread = 1")]
    public float maxSwarmSeparation = 10.0f;

    // ============================================
    // CALIBRATION
    // ============================================
    [Header("Calibration")]
    [Tooltip("Press this key to set current arm pose as neutral")]
    public KeyCode calibrateKey = KeyCode.C;

    [Tooltip("Automatically calibrate neutral on start (after a few frames)")]
    public bool autoCalibrateOnStart = true;

    private float _heightCalibrationOffset = 0f;
    private float _spreadCalibrationOffset = 0f;
    private bool _initialized = false;

    // ============================================
    // SMOOTHING STATE
    // ============================================
    private float _smoothedHeight = 0f;
    private float _smoothedSpread = 0f;

    // ============================================
    // OUTPUT PROPERTIES
    // ============================================

    /// <summary>
    /// Height control rate: -1 = arms fully down, +1 = arms fully up.
    /// </summary>
    public float HeightControl { get; private set; }

    /// <summary>
    /// Absolute swarm separation target in meters, mapped from arm spread angle.
    /// </summary>
    public float SpreadControl { get; private set; }

    /// <summary>
    /// Returns true when all three IMU references are assigned.
    /// </summary>
    public bool IsAvailable => chestIMU != null && leftArmIMU != null && rightArmIMU != null;

    /// <summary>
    /// True when spread outputs absolute meters, false when rate-based (-1..+1).
    /// </summary>
    public bool IsAbsoluteMode => spreadMode == SpreadControlMode.Absolute;

    // ============================================
    // UPDATE LOOP
    // ============================================

    void Update()
    {
        if (!_initialized && autoCalibrateOnStart && Time.frameCount > 5)
        {
            CalibrateNeutral();
            _initialized = true;
        }

        if (Input.GetKeyDown(calibrateKey))
        {
            CalibrateNeutral();
        }

        if (!IsAvailable)
        {
            HeightControl = 0f;
            SpreadControl = minSwarmSeparation;
            return;
        }

        Vector3 chest = chestIMU.SensorEulerAnglesDirect;
        Vector3 left  = leftArmIMU.SensorEulerAnglesDirect;
        Vector3 right = rightArmIMU.SensorEulerAnglesDirect;

        // Relative angles (arm angle minus chest angle removes body movement)
        float leftRelPitch  = NormalizeAngle(left.x  - chest.x);
        float rightRelPitch = NormalizeAngle(right.x - chest.x);
        float leftRelYaw    = NormalizeAngle(left.y  - chest.y);
        float rightRelYaw   = NormalizeAngle(right.y - chest.y);

        ComputeHeight(leftRelPitch, rightRelPitch);
        ComputeSpread(leftRelYaw, rightRelYaw);
    }

    // ============================================
    // COMPUTATION
    // ============================================

    void ComputeHeight(float leftPitch, float rightPitch)
    {
        float avgPitch = (leftPitch + rightPitch) * 0.5f - _heightCalibrationOffset;

        if (Mathf.Abs(avgPitch) < pitchDeadzone)
            avgPitch = 0f;

        float normalized = Mathf.Clamp(avgPitch / pitchMaxAngle, -1f, 1f);
        float sign = Mathf.Sign(normalized);
        float curved = Mathf.Pow(Mathf.Abs(normalized), pitchResponseCurve) * sign;

        float smoothSpeed = 1f - heightSmoothing;
        _smoothedHeight = Mathf.Lerp(_smoothedHeight, curved, smoothSpeed * Time.deltaTime * 10f);
        HeightControl = _smoothedHeight;
    }

    void ComputeSpread(float leftYaw, float rightYaw)
    {
        float yawDiff = Mathf.Abs(leftYaw - rightYaw) - _spreadCalibrationOffset;
        yawDiff = Mathf.Max(0f, yawDiff);

        if (yawDiff < yawDeadzone)
            yawDiff = 0f;

        float normalized = Mathf.Clamp01(yawDiff / yawMaxSpreadAngle);
        float curved = Mathf.Pow(normalized, spreadResponseCurve);

        float smoothSpeed = 1f - spreadSmoothing;
        _smoothedSpread = Mathf.Lerp(_smoothedSpread, curved, smoothSpeed * Time.deltaTime * 10f);

        if (spreadMode == SpreadControlMode.Absolute)
        {
            // Direct mapping: arm angle → target separation in meters
            SpreadControl = Mathf.Lerp(minSwarmSeparation, maxSwarmSeparation, _smoothedSpread);
        }
        else
        {
            // Rate-based: arm angle → rate of change (-1..+1)
            // Arms at neutral (calibrated) = 0 rate = hold current spread
            // Arms spread wide = +1 = expanding fast
            // Arms closer than neutral = -1 = contracting
            float signedDiff = (leftYaw - rightYaw) - _spreadCalibrationOffset;
            float signedNorm = Mathf.Clamp(signedDiff / yawMaxSpreadAngle, -1f, 1f);
            float sign = Mathf.Sign(signedNorm);
            float rateCurved = Mathf.Pow(Mathf.Abs(signedNorm), spreadResponseCurve) * sign;
            _smoothedSpread = Mathf.Lerp(_smoothedSpread, rateCurved, smoothSpeed * Time.deltaTime * 10f);
            SpreadControl = _smoothedSpread; // -1..+1 rate
        }
    }

    // ============================================
    // CALIBRATION
    // ============================================

    /// <summary>
    /// Stores the current arm pose as neutral (zeroes height and spread offsets).
    /// </summary>
    public void CalibrateNeutral()
    {
        if (!IsAvailable)
        {
            Debug.LogWarning("ArmIMUSpreadHeightInput: Cannot calibrate — one or more IMUs not assigned.");
            return;
        }

        Vector3 chest = chestIMU.SensorEulerAnglesDirect;
        Vector3 left  = leftArmIMU.SensorEulerAnglesDirect;
        Vector3 right = rightArmIMU.SensorEulerAnglesDirect;

        float leftRelPitch  = NormalizeAngle(left.x  - chest.x);
        float rightRelPitch = NormalizeAngle(right.x - chest.x);
        float leftRelYaw    = NormalizeAngle(left.y  - chest.y);
        float rightRelYaw   = NormalizeAngle(right.y - chest.y);

        _heightCalibrationOffset = (leftRelPitch + rightRelPitch) * 0.5f;
        _spreadCalibrationOffset = Mathf.Abs(leftRelYaw - rightRelYaw);

        Debug.Log($"ArmIMU Calibrated. Height offset: {_heightCalibrationOffset:F2}°, Spread offset: {_spreadCalibrationOffset:F2}°");
    }

    // ============================================
    // HELPERS
    // ============================================

    float NormalizeAngle(float angle)
    {
        if (angle > 180f)  return angle - 360f;
        if (angle < -180f) return angle + 360f;
        return angle;
    }

    // ============================================
    // DEBUG
    // ============================================

    void OnGUI()
    {
        if (!Application.isPlaying) return;

        GUILayout.BeginArea(new Rect(10, 580, 340, 160));
        GUILayout.Label("<b>Arm IMU Spread/Height</b>");
        GUILayout.Label($"Available: {IsAvailable}");

        if (IsAvailable)
        {
            Vector3 chest = chestIMU.SensorEulerAnglesDirect;
            Vector3 left  = leftArmIMU.SensorEulerAnglesDirect;
            Vector3 right = rightArmIMU.SensorEulerAnglesDirect;

            float leftRelPitch = NormalizeAngle(left.x  - chest.x) - _heightCalibrationOffset;
            float rightRelPitch= NormalizeAngle(right.x - chest.x) - _heightCalibrationOffset;
            float leftRelYaw   = NormalizeAngle(left.y  - chest.y);
            float rightRelYaw  = NormalizeAngle(right.y - chest.y);
            float yawDiff      = Mathf.Abs(leftRelYaw - rightRelYaw) - _spreadCalibrationOffset;

            GUILayout.Label($"Rel Pitch  L:{leftRelPitch:F1}°  R:{rightRelPitch:F1}°");
            GUILayout.Label($"Rel Yaw Diff: {yawDiff:F1}°");
            GUILayout.Label($"Height Output: {HeightControl:F3}");
            GUILayout.Label($"Spread Output: {SpreadControl:F2}m");
        }

        GUILayout.EndArea();
    }
}
