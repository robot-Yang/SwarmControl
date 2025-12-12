using UnityEngine;

/// <summary>
/// Rate-based mapping for IMU movement control.
/// Tilt angle controls velocity (movement rate), not absolute position.
/// Similar to joystick control - hold tilt to keep moving.
/// </summary>
public class IMUMovementRateBased : IMUMovementInputBase
{
    [Header("Forward/Backward Speed")]
    [Tooltip("Maximum forward/backward speed (units/second) at full pitch tilt")]
    public float maxPitchSpeed = 4.0f;

    [Header("Left/Right Speed")]
    [Tooltip("Maximum left/right speed (units/second) at full roll tilt")]
    public float maxRollSpeed = 4.0f;

    [Header("Response Curve")]
    [Tooltip("Response curve exponent (1 = linear, 2 = squared, 3 = cubic). Higher = more precise at small tilts, faster at large tilts")]
    [Range(1f, 3f)]
    public float responseCurve = 2.0f;

    /// <summary>
    /// Curved mapping for pitch (forward/backward)
    /// </summary>
    protected override float ApplyPitchMappingCurve(float normalizedInput)
    {
        // Apply power curve for better control feel
        return Mathf.Pow(normalizedInput, responseCurve) * maxPitchSpeed;
    }

    /// <summary>
    /// Curved mapping for roll (left/right)
    /// </summary>
    protected override float ApplyRollMappingCurve(float normalizedInput)
    {
        // Apply power curve for better control feel
        return Mathf.Pow(normalizedInput, responseCurve) * maxRollSpeed;
    }
}
