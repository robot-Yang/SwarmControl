using UnityEngine;

/// <summary>
/// Manages switching between different hand height control modes.
/// Only one mode is active at a time. Other modes are disabled to prevent conflicts.
/// </summary>
public class HandHeightSelector : MonoBehaviour
{
    public enum HandHeightMode
    {
        Linear,
        RateBased,
        Exponential,
        Logarithmic
    }

    [Header("Mode Selection")]
    [Tooltip("Select which hand height control mode to use")]
    public HandHeightMode selectedMode = HandHeightMode.Linear;

    [Header("Mode References")]
    [Tooltip("Linear mapping mode component")]
    public HandHeightLinear linearMode;
    
    [Tooltip("Rate-based mode component (hand position = velocity)")]
    public HandHeightRateBased rateBasedMode;
    
    [Tooltip("Exponential mapping mode component")]
    public HandHeightExponential exponentialMode;
    
    [Tooltip("Logarithmic mapping mode component")]
    public HandHeightLogarithmic logarithmicMode;

    // ============================================
    // PROPERTIES
    // ============================================

    /// <summary>
    /// Returns the currently active mode component
    /// </summary>
    public HandHeightInputBase ActiveMode { get; private set; }

    // ============================================
    // INITIALIZATION
    // ============================================

    void Awake()
    {
        // Auto-detect mode components if not assigned
        if (linearMode == null) linearMode = GetComponent<HandHeightLinear>();
        if (rateBasedMode == null) rateBasedMode = GetComponent<HandHeightRateBased>();
        if (exponentialMode == null) exponentialMode = GetComponent<HandHeightExponential>();
        if (logarithmicMode == null) logarithmicMode = GetComponent<HandHeightLogarithmic>();

        // Validate references
        ValidateReferences();

        // Switch to the selected mode
        SwitchToMode(selectedMode);
    }

    void ValidateReferences()
    {
        if (linearMode == null)
        {
            Debug.LogError("HandHeightSelector: Linear mode reference missing! Add HandHeightLinear component.");
        }
        if (rateBasedMode == null)
        {
            Debug.LogError("HandHeightSelector: RateBased mode reference missing! Add HandHeightRateBased component.");
        }
        if (exponentialMode == null)
        {
            Debug.LogError("HandHeightSelector: Exponential mode reference missing! Add HandHeightExponential component.");
        }
        if (logarithmicMode == null)
        {
            Debug.LogError("HandHeightSelector: Logarithmic mode reference missing! Add HandHeightLogarithmic component.");
        }
    }

    // ============================================
    // MODE SWITCHING
    // ============================================

    /// <summary>
    /// Switches to the specified mode, disabling all others
    /// </summary>
    public void SwitchToMode(HandHeightMode mode)
    {
        selectedMode = mode;

        // Disable all modes first
        if (linearMode != null) linearMode.enabled = false;
        if (rateBasedMode != null) rateBasedMode.enabled = false;
        if (exponentialMode != null) exponentialMode.enabled = false;
        if (logarithmicMode != null) logarithmicMode.enabled = false;

        // Enable the selected mode
        switch (mode)
        {
            case HandHeightMode.Linear:
                if (linearMode != null)
                {
                    linearMode.enabled = true;
                    ActiveMode = linearMode;
                    Debug.Log("Switched to Linear hand height mode");
                }
                break;

            case HandHeightMode.RateBased:
                if (rateBasedMode != null)
                {
                    rateBasedMode.enabled = true;
                    ActiveMode = rateBasedMode;
                    Debug.Log("Switched to RateBased hand height mode");
                }
                break;

            case HandHeightMode.Exponential:
                if (exponentialMode != null)
                {
                    exponentialMode.enabled = true;
                    ActiveMode = exponentialMode;
                    Debug.Log("Switched to Exponential hand height mode");
                }
                break;

            case HandHeightMode.Logarithmic:
                if (logarithmicMode != null)
                {
                    logarithmicMode.enabled = true;
                    ActiveMode = logarithmicMode;
                    Debug.Log("Switched to Logarithmic hand height mode");
                }
                break;
        }
    }

    /// <summary>
    /// Called when Inspector values change - switches mode if changed
    /// </summary>
    void OnValidate()
    {
        if (Application.isPlaying)
        {
            SwitchToMode(selectedMode);
        }
    }
}
