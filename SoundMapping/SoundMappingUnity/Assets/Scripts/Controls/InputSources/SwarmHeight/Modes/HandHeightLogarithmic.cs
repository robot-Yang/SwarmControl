using UnityEngine;

/// <summary>
/// Logarithmic mapping for hand height control.
/// Provides huge dynamic range - small hand movements control large height changes.
/// Useful when controlling height over very large vertical ranges.
/// Output is absolute height value.
/// </summary>
public class HandHeightLogarithmic : HandHeightInputBase
{
    public override bool IsAbsoluteMode => true;

    /// <summary>
    /// Logarithmic mapping: interpolates in log space
    /// Provides compression at high values
    /// </summary>
    protected override float ApplyMappingCurve(float normalizedInput)
    {
        if (normalizedInput <= 0f) return 0f;

        // Map 0-1 input to logarithmic curve
        // Using log10 for smooth compression
        return Mathf.Log10(1 + 9 * normalizedInput); // Maps [0,1] to [0,1] logarithmically
    }
}
