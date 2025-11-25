using UnityEngine;

/// <summary>
/// Allows switching between different hand tracking control modes at runtime.
/// Add all three mode components to the same GameObject, this selector manages which one is active.
/// </summary>
public class HandTrackingModeSelector : MonoBehaviour
{
    public enum ControlMode
    {
        RateBased,      // Joystick-style: hand position controls rate of change
        Linear,       // Slider-style: hand distance directly sets swarm spread (linear)
        Hybrid,         // Thermostat-style: hand distance sets target, smoothly approaches it
        Exponential,    // Position-based with exponential curve (high precision at small spreads)
        Logarithmic     // Position-based with logarithmic curve (covers huge ranges)
    }

    [Header("Mode Selection")]
    [Tooltip("Select which hand tracking mode to use")]
    public ControlMode selectedMode = ControlMode.RateBased;

    [Header("Mode Components (Auto-detected)")]
    [Tooltip("Leave empty - will auto-find on Start")]
    public HandTrackingRateBased rateBasedMode;
    
    [Tooltip("Leave empty - will auto-find on Start")]
    public HandTrackingLinear linearMode;
    
    [Tooltip("Leave empty - will auto-find on Start")]
    public HandTrackingHybrid hybridMode;
    
    [Tooltip("Leave empty - will auto-find on Start")]
    public HandTrackingExponential exponentialMode;
    
    [Tooltip("Leave empty - will auto-find on Start")]
    public HandTrackingLogarithmic logarithmicMode;

    [Header("Runtime Info")]
    [SerializeField] private ControlMode _currentActiveMode;

    private HandTrackingInputBase _activeMode;

    // ============================================
    // PUBLIC API (For InputFusionManager)
    // ============================================

    /// <summary>
    /// Returns the currently active hand tracking mode component
    /// InputFusionManager should read from this instead of individual modes
    /// </summary>
    public HandTrackingInputBase ActiveMode => _activeMode;

    // ============================================
    // INITIALIZATION
    // ============================================

    void Awake()
    {
        // Auto-detect mode components on this GameObject
        if (rateBasedMode == null) rateBasedMode = GetComponent<HandTrackingRateBased>();
        if (linearMode == null) linearMode = GetComponent<HandTrackingLinear>();
        if (hybridMode == null) hybridMode = GetComponent<HandTrackingHybrid>();
        if (exponentialMode == null) exponentialMode = GetComponent<HandTrackingExponential>();
        if (logarithmicMode == null) logarithmicMode = GetComponent<HandTrackingLogarithmic>();

        ValidateComponents();
        SwitchToMode(selectedMode);
    }

    void ValidateComponents()
    {
        if (rateBasedMode == null)
        {
            Debug.LogWarning("HandTrackingModeSelector: HandTrackingRateBased component not found! Add it to use Rate-Based mode.");
        }

        if (linearMode == null)
        {
            Debug.LogWarning("HandTrackingModeSelector: HandTrackingLinear component not found! Add it to use Linear mode.");
        }

        if (hybridMode == null)
        {
            Debug.LogWarning("HandTrackingModeSelector: HandTrackingHybrid component not found! Add it to use Hybrid mode.");
        }

        if (exponentialMode == null)
        {
            Debug.LogWarning("HandTrackingModeSelector: HandTrackingExponential component not found! Add it to use Exponential mode.");
        }

        if (logarithmicMode == null)
        {
            Debug.LogWarning("HandTrackingModeSelector: HandTrackingLogarithmic component not found! Add it to use Logarithmic mode.");
        }

        int availableModes = (rateBasedMode != null ? 1 : 0) + 
                            (linearMode != null ? 1 : 0) + 
                            (hybridMode != null ? 1 : 0) +
                            (exponentialMode != null ? 1 : 0) +
                            (logarithmicMode != null ? 1 : 0);

        if (availableModes == 0)
        {
            Debug.LogError("HandTrackingModeSelector: No hand tracking mode components found! Please add at least one mode component.");
        }
        else
        {
            Debug.Log($"HandTrackingModeSelector: Found {availableModes} hand tracking mode(s) available.");
        }
    }

    // ============================================
    // MODE SWITCHING
    // ============================================

    void Update()
    {
        // Check if mode changed in Inspector
        if (selectedMode != _currentActiveMode)
        {
            SwitchToMode(selectedMode);
        }
    }

    /// <summary>
    /// Switch to a different control mode
    /// </summary>
    void SwitchToMode(ControlMode newMode)
    {
        // Disable all modes first
        if (rateBasedMode != null) rateBasedMode.enabled = false;
        if (linearMode != null) linearMode.enabled = false;
        if (hybridMode != null) hybridMode.enabled = false;
        if (exponentialMode != null) exponentialMode.enabled = false;
        if (logarithmicMode != null) logarithmicMode.enabled = false;

        // Enable selected mode
        switch (newMode)
        {
            case ControlMode.RateBased:
                if (rateBasedMode != null)
                {
                    rateBasedMode.enabled = true;
                    _activeMode = rateBasedMode;
                    _currentActiveMode = ControlMode.RateBased;
                    Debug.Log("Switched to Rate-Based hand tracking mode");
                }
                else
                {
                    Debug.LogError("Cannot switch to Rate-Based mode: component not found!");
                    TryFallbackMode();
                }
                break;

            case ControlMode.Linear:
                if (linearMode != null)
                {
                    linearMode.enabled = true;
                    _activeMode = linearMode;
                    _currentActiveMode = ControlMode.Linear;
                    Debug.Log("Switched to Linear hand tracking mode");
                }
                else
                {
                    Debug.LogError("Cannot switch to Linear mode: component not found!");
                    TryFallbackMode();
                }
                break;

            case ControlMode.Hybrid:
                if (hybridMode != null)
                {
                    hybridMode.enabled = true;
                    _activeMode = hybridMode;
                    _currentActiveMode = ControlMode.Hybrid;
                    Debug.Log("Switched to Hybrid hand tracking mode");
                }
                else
                {
                    Debug.LogError("Cannot switch to Hybrid mode: component not found!");
                    TryFallbackMode();
                }
                break;

            case ControlMode.Exponential:
                if (exponentialMode != null)
                {
                    exponentialMode.enabled = true;
                    _activeMode = exponentialMode;
                    _currentActiveMode = ControlMode.Exponential;
                    Debug.Log("Switched to Exponential hand tracking mode");
                }
                else
                {
                    Debug.LogError("Cannot switch to Exponential mode: component not found!");
                    TryFallbackMode();
                }
                break;

            case ControlMode.Logarithmic:
                if (logarithmicMode != null)
                {
                    logarithmicMode.enabled = true;
                    _activeMode = logarithmicMode;
                    _currentActiveMode = ControlMode.Logarithmic;
                    Debug.Log("Switched to Logarithmic hand tracking mode");
                }
                else
                {
                    Debug.LogError("Cannot switch to Logarithmic mode: component not found!");
                    TryFallbackMode();
                }
                break;
        }
    }

    /// <summary>
    /// If selected mode is unavailable, try to enable any available mode
    /// </summary>
    void TryFallbackMode()
    {
        if (rateBasedMode != null)
        {
            SwitchToMode(ControlMode.RateBased);
        }
        else if (linearMode != null)
        {
            SwitchToMode(ControlMode.Linear);
        }
        else if (hybridMode != null)
        {
            SwitchToMode(ControlMode.Hybrid);
        }
        else if (exponentialMode != null)
        {
            SwitchToMode(ControlMode.Exponential);
        }
        else if (logarithmicMode != null)
        {
            SwitchToMode(ControlMode.Logarithmic);
        }
        else
        {
            Debug.LogError("No hand tracking modes available!");
            _activeMode = null;
        }
    }

    // ============================================
    // PUBLIC METHODS (Runtime control)
    // ============================================

    /// <summary>
    /// Switch mode at runtime via code
    /// </summary>
    public void SetMode(ControlMode mode)
    {
        selectedMode = mode;
        SwitchToMode(mode);
    }

    /// <summary>
    /// Cycle to next available mode (useful for testing)
    /// </summary>
    public void CycleToNextMode()
    {
        ControlMode nextMode = selectedMode;
        
        switch (selectedMode)
        {
            case ControlMode.RateBased:
                nextMode = ControlMode.RateBased;
                break;
            case ControlMode.Linear:
                nextMode = ControlMode.Hybrid;
                break;
            case ControlMode.Hybrid:
                nextMode = ControlMode.Exponential;
                break;
            case ControlMode.Exponential:
                nextMode = ControlMode.Logarithmic;
                break;
            case ControlMode.Logarithmic:
                nextMode = ControlMode.RateBased;
                break;
        }

        SetMode(nextMode);
    }

    // ============================================
    // DEBUG
    // ============================================

    void OnGUI()
    {
        if (!Application.isPlaying) return;
        if (_activeMode == null || !_activeMode.showDebugInfo) return;

        // Add mode indicator at top of debug info
        GUILayout.BeginArea(new Rect(10, 340, 300, 30));
        GUILayout.Label($"<b>Active Mode: {_currentActiveMode}</b>");
        GUILayout.EndArea();
    }
}
