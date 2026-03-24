using UnityEngine;

/// <summary>
/// Rate-based mapping for hand height control.
/// Hand position relative to neutral controls vertical velocity.
/// Output is velocity rate (-1 to +1).
/// </summary>
public class HandHeightRateBased : HandHeightInputBase
{
    public override bool IsAbsoluteMode => false;

    /// <summary>
    /// Linear rate mapping: hand distance from neutral = velocity
    /// </summary>
    protected override float ApplyMappingCurve(float normalizedInput)
    {
        return normalizedInput;
    }
}
