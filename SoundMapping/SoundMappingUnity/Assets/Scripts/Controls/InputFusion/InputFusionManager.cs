using UnityEngine;

/// <summary>
/// Central hub that fuses all input sources (traditional, IMU, VR, etc.) 
/// and provides a unified API for swarm control.
/// This is the ONLY class that MigrationPointController and CameraMovement should read from.
/// </summary>
public class InputFusionManager : MonoBehaviour
{
    // ============================================
    // INPUT SOURCE REFERENCES
    // ============================================
    [Header("Input Sources")]
    [Tooltip("Keyboard and game controller input")]
    public TraditionalInput traditionalInput;

    [Tooltip("IMU movement mode selector - manages switching between Linear/Exponential modes")]
    public IMUMovementSelector imuMovementSelector;

    [Tooltip("Meta Quest headset for camera rotation (yaw)")]
    public MetaQuestInput metaQuestInput;

    [Tooltip("Hand tracking mode selector - manages switching between Rate/Absolute/Hybrid modes")]
    public HandTrackingModeSelector handTrackingSelector;

    [Tooltip("Hand height mode selector - manages switching between Linear/RateBased/Exponential/Logarithmic modes")]
    public HandHeightSelector handHeightSelector;

    // Future inputs will go here:
    // public HandIMUInput leftHandIMU;
    // public HandIMUInput rightHandIMU;

    // ============================================
    // INPUT PRIORITY TOGGLES
    // ============================================
    [Header("Input Priority Settings")]
    [Tooltip("Use IMU for swarm movement (if false, uses traditional input)")]
    public bool useIMUForMovement = false; // Start with false until IMU is connected

    [Tooltip("Use Meta Quest headset yaw for camera rotation (if false, uses traditional input)")]
    public bool useMetaQuestForRotation = false; // Will enable when MetaQuest is added

    [Tooltip("Use fused hand IMU + Quest arms for spread control (if false, uses traditional input)")]
    public bool useFusedHandsForSpread = false; // Will enable when hand IMUs are added

    [Tooltip("Use hand height for swarm height control (if false, uses traditional input)")]
    public bool useHandsForHeight = false; // Will enable when hand tracking is ready

    [Tooltip("Always allow traditional input as fallback when primary sources aren't moving")]
    public bool enableTraditionalFallback = true;

    // ============================================
    // FUSED OUTPUT PROPERTIES (Read by other systems)
    // ============================================
    // Fused Outputs (Read-Only) - these are computed from all input sources
    public Vector3 SwarmMovement { get; private set; }      // Final movement vector for swarm (XZ plane + Y height)
    public float SwarmSpread { get; private set; }          // Final spread control value (rate or target depending on mode)
    public float CameraRotation { get; private set; }       // Final camera rotation input
    
    /// <summary>
    /// Returns true if SwarmSpread is an absolute target (not a rate)
    /// MigrationPointController needs to know this to apply correctly
    /// </summary>
    public bool IsSpreadAbsolute => useFusedHandsForSpread && handTrackingSelector != null && 
                                     handTrackingSelector.ActiveMode != null && 
                                     handTrackingSelector.ActiveMode.IsAbsoluteMode;

    // Button states (pass-through from traditional for now)
    public bool SelectionNextPressed { get; private set; }
    public bool SelectionPrevPressed { get; private set; }
    public bool EmbodimentPressed { get; private set; }
    public bool DisembodimentPressed { get; private set; }
    public bool ToggleDummyForcesPressed { get; private set; }

    // ============================================
    // INITIALIZATION
    // ============================================
    void Start()
    {
        ValidateReferences();
    }

    void ValidateReferences()
    {
        if (traditionalInput == null)
        {
            Debug.LogError("InputFusionManager: TraditionalInput reference is missing! Assign it in the Inspector.");
        }

        if (imuMovementSelector == null && useIMUForMovement)
        {
            Debug.LogWarning("InputFusionManager: IMUMovementSelector is enabled but reference is missing. Falling back to traditional input.");
            useIMUForMovement = false;
        }
        else if (imuMovementSelector != null && imuMovementSelector.ActiveMode == null)
        {
            Debug.LogWarning("InputFusionManager: IMUMovementSelector has no active mode. Falling back to traditional input.");
            useIMUForMovement = false;
        }

        if (metaQuestInput == null && useMetaQuestForRotation)
        {
            Debug.LogWarning("InputFusionManager: MetaQuestInput is enabled but reference is missing. Falling back to traditional input.");
            useMetaQuestForRotation = false;
        }

        if (handTrackingSelector == null && useFusedHandsForSpread)
        {
            Debug.LogWarning("InputFusionManager: HandTrackingModeSelector is enabled but reference is missing. Falling back to traditional input.");
            useFusedHandsForSpread = false;
        }
        else if (handTrackingSelector != null && handTrackingSelector.ActiveMode == null)
        {
            Debug.LogWarning("InputFusionManager: HandTrackingModeSelector has no active mode. Falling back to traditional input.");
            useFusedHandsForSpread = false;
        }

        if (handHeightSelector == null && useHandsForHeight)
        {
            Debug.LogWarning("InputFusionManager: HandHeightSelector is enabled but reference is missing. Falling back to traditional input.");
            useHandsForHeight = false;
        }
        else if (handHeightSelector != null && handHeightSelector.ActiveMode == null)
        {
            Debug.LogWarning("InputFusionManager: HandHeightSelector has no active mode. Falling back to traditional input.");
            useHandsForHeight = false;
        }
    }

    // ============================================
    // UPDATE LOOP - FUSION LOGIC
    // ============================================
    void Update()
    {
        FuseMovementInputs();
        FuseHeightInputs();
        FuseSpreadInputs();
        FuseRotationInputs();
        FuseButtonInputs();
    }

    /// <summary>
    /// Combines horizontal movement (XZ plane) from IMU and/or traditional input
    /// Height (Y) is handled separately in FuseHeightInputs()
    /// </summary>
    void FuseMovementInputs()
    {
        Vector3 movement = Vector3.zero;

        // PRIMARY: Use IMU if enabled and available
        if (useIMUForMovement && imuMovementSelector != null && imuMovementSelector.ActiveMode != null)
        {
            IMUMovementInputBase activeMode = imuMovementSelector.ActiveMode;
            if (activeMode.IsAvailable)
            {
                movement = activeMode.MovementVector; // Already has Y=0
            }
        }
        // FALLBACK: Use traditional input (horizontal only)
        if (movement == Vector3.zero && traditionalInput != null)
        {
            Vector2 moveInput = traditionalInput.MovementInput;
            movement = new Vector3(moveInput.x, 0f, moveInput.y); // Y is handled in FuseHeightInputs
        }

        SwarmMovement = movement;
    }

    /// <summary>
    /// Combines height control from hand height and/or traditional input
    /// Updates the Y component of SwarmMovement
    /// </summary>
    void FuseHeightInputs()
    {
        float height = 0f;

        // PRIMARY: Use hand height if enabled and available
        if (useHandsForHeight && handHeightSelector != null && handHeightSelector.ActiveMode != null)
        {
            HandHeightInputBase activeMode = handHeightSelector.ActiveMode;
            if (activeMode.AreHandsAvailable)
            {
                height = activeMode.HeightControl;
            }
        }
        // FALLBACK: Use traditional input height
        if (height == 0f && traditionalInput != null)
        {
            height = traditionalInput.HeightInput;
        }

        // Update the Y component of SwarmMovement
        SwarmMovement = new Vector3(SwarmMovement.x, height, SwarmMovement.z);
    }

    /// <summary>
    /// Combines spread control from hand tracking and/or traditional input
    /// Note: Output meaning depends on hand tracking mode:
    /// - RateBased: SwarmSpread is a rate (-1 to +1)
    /// - Absolute/Hybrid: SwarmSpread is target separation distance (meters)
    /// </summary>
    void FuseSpreadInputs()
    {
        float spread = 0f;

        // PRIMARY: Use hand tracking if enabled and available
        if (useFusedHandsForSpread && handTrackingSelector != null && handTrackingSelector.ActiveMode != null)
        {
            HandTrackingInputBase activeMode = handTrackingSelector.ActiveMode;
            spread = activeMode.HandSpreadControl;
            
            // If using Hybrid mode, apply rate limiting here
            if (activeMode is HandTrackingHybrid hybrid)
            {
                spread = ApplyHybridSmoothing(spread, hybrid.maxChangeRate);
            }
        }
        // FALLBACK: Use traditional input (triggers/bumpers - always rate-based)
        else if (traditionalInput != null)
        {
            spread = traditionalInput.SpreadInput;
        }

        SwarmSpread = spread;
    }

    private float _lastHybridTarget = 2.5f; // Start at mid-range
    
    /// <summary>
    /// For Hybrid mode: smoothly approach target with rate limiting
    /// </summary>
    float ApplyHybridSmoothing(float targetSeparation, float maxRate)
    {
        float delta = targetSeparation - _lastHybridTarget;
        float maxChange = maxRate * Time.deltaTime;
        float change = Mathf.Clamp(delta, -maxChange, maxChange);
        
        _lastHybridTarget += change;
        return _lastHybridTarget;
    }

    /// <summary>
    /// Combines rotation from Meta Quest headset and/or traditional input
    /// </summary>
    void FuseRotationInputs()
    {
        float rotation = 0f;

        // PRIMARY: Use Meta Quest headset yaw if enabled and available
        if (useMetaQuestForRotation && metaQuestInput != null)
        {
            rotation = metaQuestInput.HeadsetYawRate;
        }
        // FALLBACK: Use traditional input (right stick / controller)
        else if (traditionalInput != null)
        {
            rotation = traditionalInput.RotationInput;
        }

        CameraRotation = rotation;
    }

    /// <summary>
    /// Pass through button states from traditional input
    /// (Buttons don't need fusion, they come from controller only)
    /// </summary>
    void FuseButtonInputs()
    {
        if (traditionalInput != null)
        {
            SelectionNextPressed = traditionalInput.SelectionNextPressed;
            SelectionPrevPressed = traditionalInput.SelectionPrevPressed;
            EmbodimentPressed = traditionalInput.EmbodimentPressed;
            DisembodimentPressed = traditionalInput.DisembodimentPressed;
            ToggleDummyForcesPressed = traditionalInput.ToggleDummyForcesPressed;
        }
    }

    // ============================================
    // HELPER METHODS
    // ============================================

    /// <summary>
    /// Returns true if any movement input is active (from any source)
    /// </summary>
    public bool IsMoving()
    {
        return SwarmMovement.sqrMagnitude > 0.01f;
    }

    /// <summary>
    /// Returns selection direction: +1 for next, -1 for previous, 0 for none
    /// </summary>
    public int GetSelectionDirection()
    {
        if (SelectionNextPressed) return 1;
        if (SelectionPrevPressed) return -1;
        return 0;
    }

    // ============================================
    // DEBUG VISUALIZATION
    // ============================================
    void OnGUI()
    {
        if (!Application.isPlaying) return;

        // Display current input state in top-left corner (for debugging)
        GUILayout.BeginArea(new Rect(10, 10, 300, 250));
        GUILayout.Label($"<b>Input Fusion Status</b>");
        GUILayout.Label($"Movement: {SwarmMovement}");
        GUILayout.Label($"Spread: {SwarmSpread:F2}");
        GUILayout.Label($"Rotation: {CameraRotation:F2}");
        GUILayout.Label($"---");
        GUILayout.Label($"IMU Active: {useIMUForMovement}");
        GUILayout.Label($"MetaQuest Active: {useMetaQuestForRotation}");
        GUILayout.Label($"HandTracking Spread Active: {useFusedHandsForSpread}");
        GUILayout.Label($"HandTracking Height Active: {useHandsForHeight}");
        GUILayout.Label($"Traditional Fallback: {enableTraditionalFallback}");
        GUILayout.EndArea();
    }
}
