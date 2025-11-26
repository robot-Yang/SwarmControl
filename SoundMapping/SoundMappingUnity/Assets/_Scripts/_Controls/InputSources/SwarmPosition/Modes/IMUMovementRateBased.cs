using UnityEngine;

/// <summary>
/// Rate-based mapping for IMU movement control.
/// Tilt angle controls velocity (movement rate), not absolute position.
/// Similar to joystick control - hold tilt to keep moving.
/// </summary>
public class IMUMovementRateBased : IMUMovementInputBase
{
    [Header("Rate-Based Settings")]
    [Tooltip("Maximum movement speed (units/second) at full tilt")]
    public float maxSpeed = 2.0f;

    /// <summary>
    /// Linear mapping, but output represents velocity rate
    /// The movement vector will be multiplied by Time.deltaTime by consumers
    /// </summary>
    protected override float ApplyMappingCurve(float normalizedInput)
    {
        // Linear rate: directly proportional to tilt angle
        return normalizedInput * maxSpeed;
    }
}
