using UnityEngine;

/// <summary>
/// Linear mapping for IMU movement control.
/// Output is directly proportional to tilt angle.
/// Provides smooth, predictable control.
/// </summary>
public class IMUMovementLinear : IMUMovementInputBase
{
    /// <summary>
    /// Linear mapping for pitch: output = input
    /// </summary>
    protected override float ApplyPitchMappingCurve(float normalizedInput)
    {
        return normalizedInput;
    }

    /// <summary>
    /// Linear mapping for roll: output = input
    /// </summary>
    protected override float ApplyRollMappingCurve(float normalizedInput)
    {
        return normalizedInput;
    }
}
