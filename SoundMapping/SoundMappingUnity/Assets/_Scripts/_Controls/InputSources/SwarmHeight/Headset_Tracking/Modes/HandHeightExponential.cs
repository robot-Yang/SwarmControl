using UnityEngine;

/// <summary>
/// Exponential mapping for hand height control.
/// Small movements from neutral = fine control, large movements = fast response.
/// Output is absolute height value.
/// </summary>
public class HandHeightExponential : HandHeightInputBase
{
    [Header("Exponential Settings")]
    [Tooltip("Exponent value (1.5-4.0). Higher = more precision near neutral")]
    [Range(1.5f, 4.0f)]
    public float exponent = 2.0f;

    public override bool IsAbsoluteMode => true;

    /// <summary>
    /// Exponential mapping: output = input^exponent
    /// </summary>
    protected override float ApplyMappingCurve(float normalizedInput)
    {
        return Mathf.Pow(normalizedInput, exponent);
    }
}
