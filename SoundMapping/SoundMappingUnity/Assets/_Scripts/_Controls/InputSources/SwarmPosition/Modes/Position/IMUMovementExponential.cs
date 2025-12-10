using UnityEngine;

/// <summary>
/// Exponential mapping for IMU movement control.
/// Small tilts produce fine control, large tilts ramp up quickly.
/// Good for precision tasks that occasionally need speed.
/// </summary>
public class IMUMovementExponential : IMUMovementInputBase
{
    [Header("Forward/Backward Settings")]
    [Tooltip("Exponent for pitch (1.5-4.0). Higher = more precision at small forward/back tilts")]
    [Range(1.5f, 4.0f)]
    public float pitchExponent = 2.0f;

    [Header("Left/Right Settings")]
    [Tooltip("Exponent for roll (1.5-4.0). Higher = more precision at small left/right tilts")]
    [Range(1.5f, 4.0f)]
    public float rollExponent = 2.0f;

    /// <summary>
    /// Exponential mapping for pitch: output = input^pitchExponent
    /// </summary>
    protected override float ApplyPitchMappingCurve(float normalizedInput)
    {
        return Mathf.Pow(normalizedInput, pitchExponent);
    }

    /// <summary>
    /// Exponential mapping for roll: output = input^rollExponent
    /// </summary>
    protected override float ApplyRollMappingCurve(float normalizedInput)
    {
        return Mathf.Pow(normalizedInput, rollExponent);
    }
}
