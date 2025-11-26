using UnityEngine;

/// <summary>
/// Exponential mapping for IMU movement control.
/// Small tilts produce fine control, large tilts ramp up quickly.
/// Good for precision tasks that occasionally need speed.
/// </summary>
public class IMUMovementExponential : IMUMovementInputBase
{
    [Header("Exponential Settings")]
    [Tooltip("Exponent value (1.5-4.0). Higher = more aggressive curve, more precision at small tilts")]
    [Range(1.5f, 4.0f)]
    public float exponent = 2.0f;

    /// <summary>
    /// Exponential mapping: output = input^exponent
    /// </summary>
    protected override float ApplyMappingCurve(float normalizedInput)
    {
        return Mathf.Pow(normalizedInput, exponent);
    }
}
