using UnityEngine;

/// <summary>
/// Linear mapping for IMU movement control.
/// Output is directly proportional to tilt angle.
/// Provides smooth, predictable control.
/// </summary>
public class IMUMovementLinear : IMUMovementInputBase
{
    /// <summary>
    /// Linear mapping: output = input
    /// </summary>
    protected override float ApplyMappingCurve(float normalizedInput)
    {
        return normalizedInput;
    }
}
