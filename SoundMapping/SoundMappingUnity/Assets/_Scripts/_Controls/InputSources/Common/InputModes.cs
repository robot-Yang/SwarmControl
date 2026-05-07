/// <summary>
/// Shared enums describing how an input source emits its value.
/// Kept in one file so every spread/height source agrees on the same vocabulary.
/// </summary>
public enum SpreadMode
{
    /// <summary>Output is a rate (-1..+1) added to the swarm spread each frame.</summary>
    Rate,
    /// <summary>Output is a target separation distance (meters) for the swarm.</summary>
    Absolute,
}

public enum ResponseCurve
{
    /// <summary>Pass the rate through unchanged. Easier to predict, less precise near zero.</summary>
    Linear,
    /// <summary>Apply a power-curve to the rate magnitude. More precise near zero, faster at the extremes.</summary>
    Exponential,
}
