using UnityEngine;

/// <summary>
/// Linear mapping for hand height control.
/// Hand position directly maps to target height.
/// Output is absolute height value.
/// </summary>
public class HandHeightLinear : HandHeightInputBase
{
    public override bool IsAbsoluteMode => true;

    /// <summary>
    /// Linear mapping: output = input
    /// Returns value in range based on max heights
    /// </summary>
    protected override float ApplyMappingCurve(float normalizedInput)
    {
        // For absolute modes, scale by the max range
        // Positive values use maxHeightAbove, negative use maxHeightBelow
        return normalizedInput;
    }
}
