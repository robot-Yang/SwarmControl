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

    /// <summary>
    /// Linear mapping for pitch (forward/backward)
    /// </summary>
    protected override float ApplyPitchMappingCurve(float normalizedInput)
    {
        return normalizedInput * maxPitchSpeed;
    }

    /// <summary>
    /// Linear mapping for roll (left/right)
    /// </summary>
    protected override float ApplyRollMappingCurve(float normalizedInput)
    {
        return normalizedInput * maxRollSpeed;
    }
}
