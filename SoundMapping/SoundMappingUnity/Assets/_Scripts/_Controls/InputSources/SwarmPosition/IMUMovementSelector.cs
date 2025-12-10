using UnityEngine;

/// <summary>
/// Manages switching between different IMU movement control modes.
/// Only one mode is active at a time. Other modes are disabled to prevent conflicts.
/// </summary>
public class IMUMovementSelector : MonoBehaviour
{
    public enum IMUMovementMode
    {
        Linear,
        Exponential,
        RateBased
    }

    [Header("Mode Selection")]
    [Tooltip("Select which IMU movement control mode to use")]
    public IMUMovementMode selectedMode = IMUMovementMode.RateBased;

    [Header("Mode References")]
    [Tooltip("Linear mapping mode component")]
    public IMUMovementLinear linearMode;
    
    [Tooltip("Exponential mapping mode component")]
    public IMUMovementExponential exponentialMode;
    
    [Tooltip("Rate-based mode component (tilt = velocity)")]
    public IMUMovementRateBased rateBasedMode;

    // ============================================
    // PROPERTIES
    // ============================================

    /// <summary>
    /// Returns the currently active mode component
    /// </summary>
    public IMUMovementInputBase ActiveMode { get; private set; }

    // ============================================
    // INITIALIZATION
    // ============================================

    void Awake()
    {
        // Auto-detect mode components if not assigned
        if (linearMode == null) linearMode = GetComponent<IMUMovementLinear>();
        if (exponentialMode == null) exponentialMode = GetComponent<IMUMovementExponential>();
        if (rateBasedMode == null) rateBasedMode = GetComponent<IMUMovementRateBased>();

        // Validate references
        ValidateReferences();

        // Switch to the selected mode
        SwitchToMode(selectedMode);
    }

    void ValidateReferences()
    {
        if (linearMode == null)
        {
            Debug.LogError("IMUMovementSelector: Linear mode reference missing! Add IMUMovementLinear component.");
        }
        if (exponentialMode == null)
        {
            Debug.LogError("IMUMovementSelector: Exponential mode reference missing! Add IMUMovementExponential component.");
        }
        if (rateBasedMode == null)
        {
            Debug.LogError("IMUMovementSelector: RateBased mode reference missing! Add IMUMovementRateBased component.");
        }
    }

    // ============================================
    // MODE SWITCHING
    // ============================================

    /// <summary>
    /// Switches to the specified mode, disabling all others
    /// </summary>
    public void SwitchToMode(IMUMovementMode mode)
    {
        selectedMode = mode;

        // Disable all modes first
        if (linearMode != null) linearMode.enabled = false;
        if (exponentialMode != null) exponentialMode.enabled = false;
        if (rateBasedMode != null) rateBasedMode.enabled = false;

        // Enable the selected mode
        switch (mode)
        {
            case IMUMovementMode.Linear:
                if (linearMode != null)
                {
                    linearMode.enabled = true;
                    ActiveMode = linearMode;
                    Debug.Log("Switched to Linear IMU movement mode");
                }
                break;

            case IMUMovementMode.Exponential:
                if (exponentialMode != null)
                {
                    exponentialMode.enabled = true;
                    ActiveMode = exponentialMode;
                    Debug.Log("Switched to Exponential IMU movement mode");
                }
                break;

            case IMUMovementMode.RateBased:
                if (rateBasedMode != null)
                {
                    rateBasedMode.enabled = true;
                    ActiveMode = rateBasedMode;
                    Debug.Log("Switched to RateBased IMU movement mode");
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

    // ============================================
    // DEBUG DISPLAY
    // ============================================

    void OnGUI()
    {
        if (ActiveMode == null || !ActiveMode.IsAvailable) return;

        // Display IMU angles in top-right corner
        GUIStyle style = new GUIStyle();
        style.fontSize = 14;
        style.normal.textColor = Color.white;
        style.alignment = TextAnchor.UpperRight;

        float x = Screen.width - 200;
        float y = 10;
        float lineHeight = 20;

        //GUI.Label(new Rect(x, y, 190, 20), $"Pitch: {ActiveMode.GetPitchAngle():F1}°", style);
        //GUI.Label(new Rect(x, y + lineHeight, 190, 20), $"Roll: {ActiveMode.GetRollAngle():F1}°", style);
        //GUI.Label(new Rect(x, y + lineHeight * 2, 190, 20), $"Yaw: {ActiveMode.GetYawAngle():F1}° (display only)", style);
    }
}
