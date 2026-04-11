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

    [Tooltip("IMU yaw input for camera rotation from OpenZen sensor")]
    public IMUYawInput imuYawInput;

    [Tooltip("MediaPipe spread input from Python webcam tracking")]
    public MediaPipeSpreadInput mediaPipeSpreadInput;

    [Tooltip("MediaPipe height input from Python webcam tracking")]
    public MediaPipeHeightInput mediaPipeHeightInput;

    [Tooltip("Arm IMU input — forearm sensors for spread and height (primary wearable source)")]
    public ArmIMUSpreadHeightInput armIMUInput;

    [Tooltip("Optional drift corrector sitting in front of armIMUInput — leave empty to use raw IMU")]
    public DriftCorrector driftCorrector;

    // ============================================
    // INPUT PRIORITY TOGGLES
    // ============================================
    [Header("Input Priority Settings")]
    [Tooltip("Use IMU for swarm movement (if false, uses traditional input)")]
    public bool useIMUForMovement = false; // Start with false until IMU is connected

    [Tooltip("Use Meta Quest headset yaw for camera rotation (if false, uses traditional input)")]
    public bool useMetaQuestForRotation = false; // Will enable when MetaQuest is added

    [Tooltip("Use IMU yaw for camera rotation (if false, falls back to MetaQuest or traditional)")]
    public bool useIMUForRotation = false;

    [Tooltip("Use MediaPipe webcam tracking for spread control (if false, uses traditional input)")]
    public bool useMediaPipeForSpread = false;

    [Tooltip("Use MediaPipe webcam tracking for height control (if false, uses traditional input)")]
    public bool useMediaPipeForHeight = false;

    [Tooltip("Use forearm IMUs for spread and height (takes priority over MediaPipe when enabled)")]
    public bool useArmIMUForSpreadHeight = false;

    [Tooltip("Always allow traditional input as fallback when primary sources aren't moving")]
    public bool enableTraditionalFallback = true;

    // ============================================
    // CALIBRATION STATE
    // ============================================
    private bool isCalibrating = false;
    private float calibrationCountdown = 0f;

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
    public bool IsSpreadAbsolute
    {
        get
        {
            if (useArmIMUForSpreadHeight && ArmIMUAvailable())
                return armIMUInput.IsAbsoluteMode;
            if (useMediaPipeForSpread && mediaPipeSpreadInput != null && mediaPipeSpreadInput.IsAvailable)
                return mediaPipeSpreadInput.IsAbsoluteMode;
            return traditionalInput != null && traditionalInput.IsSpreadAbsolute;
        }
    }

    // Button states (pass-through from traditional for now)
    public bool SelectionNextPressed { get; private set; }
    public bool SelectionPrevPressed { get; private set; }
    public bool EmbodimentPressed { get; private set; }
    public bool DisembodimentPressed { get; private set; }
    public bool ToggleDummyForcesPressed { get; private set; }
    public bool CalibratePressed { get; private set; }

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

        if (mediaPipeSpreadInput == null && useMediaPipeForSpread)
        {
            Debug.LogWarning("InputFusionManager: MediaPipeSpreadInput is enabled but reference is missing. Falling back to traditional input.");
            useMediaPipeForSpread = false;
        }

        if (mediaPipeHeightInput == null && useMediaPipeForHeight)
        {
            Debug.LogWarning("InputFusionManager: MediaPipeHeightInput is enabled but reference is missing. Falling back to traditional input.");
            useMediaPipeForHeight = false;
        }

        if (armIMUInput == null && useArmIMUForSpreadHeight)
        {
            Debug.LogWarning("InputFusionManager: ArmIMUSpreadHeightInput is enabled but reference is missing. Falling back to MediaPipe/traditional.");
            useArmIMUForSpreadHeight = false;
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

        // Check for calibration button press - calibrate immediately
        if (CalibratePressed)
        {
            PerformCalibration();
        }
    }

    /// <summary>
    /// Combines horizontal movement (XZ plane) from IMU and/or traditional input
    /// Height (Y) is handled separately in FuseHeightInputs()
    /// </summary>
    void FuseMovementInputs()
    {
        Vector3 movement = Vector3.zero;

        // STABILIZE during calibration - provide minimal forward velocity to maintain swarm cohesion
        if (isCalibrating)
        {
            // Small forward velocity prevents alignmentVector from becoming zero (which causes disconnection)
            SwarmMovement = new Vector3(0f, 0f, 0.05f); // Gentle forward drift maintains formation
            return;
        }

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
    /// Combines height control from MediaPipe and/or traditional input
    /// Updates the Y component of SwarmMovement
    /// </summary>
    void FuseHeightInputs()
    {
        float height = 0f;

        // PRIMARY: Arm IMU forearm sensors (if enabled and available)
        if (useArmIMUForSpreadHeight && ArmIMUAvailable())
        {
            height = driftCorrector != null && driftCorrector.IsAvailable
                ? driftCorrector.CorrectedHeight
                : armIMUInput.HeightControl;
        }
        // SECONDARY: MediaPipe webcam tracking
        else if (useMediaPipeForHeight && mediaPipeHeightInput != null && mediaPipeHeightInput.IsAvailable)
        {
            height = mediaPipeHeightInput.HeightControl;
        }
        // FALLBACK: traditional input
        else if (traditionalInput != null)
        {
            height = traditionalInput.HeightInput;
        }

        SwarmMovement = new Vector3(SwarmMovement.x, height, SwarmMovement.z);
    }

    /// <summary>
    /// Combines spread control from MediaPipe and/or traditional input
    /// Note: Output meaning depends on mode:
    /// - Traditional: SwarmSpread is a rate (-1 to +1)
    /// - MediaPipe: SwarmSpread is target separation distance (meters)
    /// </summary>
    void FuseSpreadInputs()
    {
        float spread = 0f;

        // FREEZE SPREAD during calibration - no spreading/shrinking
        if (isCalibrating)
        {
            SwarmSpread = 0f; // Zero spread change during calibration
            return;
        }

        // PRIMARY: Arm IMU forearm sensors (if enabled and available)
        if (useArmIMUForSpreadHeight && ArmIMUAvailable())
        {
            spread = driftCorrector != null && driftCorrector.IsAvailable
                ? driftCorrector.CorrectedSpread
                : armIMUInput.SpreadControl;
        }
        // SECONDARY: MediaPipe webcam tracking
        else if (useMediaPipeForSpread && mediaPipeSpreadInput != null && mediaPipeSpreadInput.IsAvailable)
        {
            spread = mediaPipeSpreadInput.SpreadControl;
        }
        // FALLBACK: traditional input (rate-based)
        else if (traditionalInput != null)
        {
            spread = traditionalInput.SpreadInput;
        }

        SwarmSpread = spread;
    }

    /// <summary>
    /// Combines rotation from IMU, Meta Quest headset, and/or traditional input
    /// Priority: IMU > MetaQuest > Traditional
    /// Note: Meta Quest yaw is disabled when IMU pitch is actively moving the swarm left/right
    /// </summary>
    void FuseRotationInputs()
    {
        float rotation = 0f;

        // STOP rotation during calibration (but allow swarm to hover)
        if (isCalibrating)
        {
            CameraRotation = 0f;
            return;
        }

        // Check if IMU pitch is actively controlling left/right movement
        bool pitchIsActive = useIMUForMovement && 
                           imuMovementSelector != null && 
                           imuMovementSelector.ActiveMode != null && 
                           imuMovementSelector.ActiveMode.IsPitchActive;

        // PRIORITY 1: IMU yaw if enabled and available
        if (useIMUForRotation && imuYawInput != null && imuYawInput.IsAvailable)
        {
            rotation = imuYawInput.YawRotationRate;
        }
        // PRIORITY 2: Meta Quest headset yaw if enabled and available
        // BUT: Disable if pitch is actively moving the swarm left/right (prevents conflicting inputs)
        else if (useMetaQuestForRotation && metaQuestInput != null && !pitchIsActive)
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
            CalibratePressed = traditionalInput.CalibratePressed;
        }
    }

    // ============================================
    // HELPER METHODS
    // ============================================

    bool ArmIMUAvailable() => armIMUInput != null && armIMUInput.IsAvailable;

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
    // CALIBRATION METHODS
    // ============================================

    // Calibration is now instant - no countdown needed

    /// <summary>
    /// Performs the actual calibration of IMU and headset after countdown
    /// </summary>
    void PerformCalibration()
    {
        Debug.Log("=== PERFORMING CALIBRATION ===");

        // Calibrate IMU movement (all modes)
        if (imuMovementSelector != null && imuMovementSelector.ActiveMode != null)
        {
            imuMovementSelector.ActiveMode.CalibrateNeutral();
            Debug.Log("✓ IMU movement calibrated");
        }

        // Calibrate Meta Quest headset yaw
        if (metaQuestInput != null && metaQuestInput.IsHeadsetAvailable())
        {
            metaQuestInput.CalibrateNeutral();
            Debug.Log("✓ Meta Quest headset yaw calibrated");
        }

        // Calibrate IMU yaw input
        if (imuYawInput != null && imuYawInput.IsAvailable)
        {
            imuYawInput.CalibrateNeutral();
            Debug.Log("✓ IMU yaw calibrated");
        }

        // Calibrate arm IMU spread/height
        if (armIMUInput != null && armIMUInput.IsAvailable)
        {
            armIMUInput.CalibrateNeutral();
            Debug.Log("✓ Arm IMU spread/height calibrated");
        }

        Debug.Log("=== CALIBRATION COMPLETE ===");
        Debug.Log("Sensors calibrated. Input sources remain in their current state (not auto-activated).");
    }

    // ============================================
    // DEBUG VISUALIZATION
    // ============================================
    void OnGUI()
    {
        if (!Application.isPlaying) return;

        // Display current input state in top-left corner (for debugging)
        GUILayout.BeginArea(new Rect(10, 10, 400, 300));
        GUILayout.Label($"<b>=== INPUT FUSION STATUS ===</b>");
        
        // Show calibration status prominently
        if (isCalibrating)
        {
            GUILayout.Label($"<color=yellow><b>⏱ CALIBRATING in {calibrationCountdown:F1}s - HOLD STEADY!</b></color>");
        }
        
        GUILayout.Label($"Movement: {SwarmMovement}");
        GUILayout.Label($"Spread: {SwarmSpread:F2}  Height: {SwarmMovement.y:F2}");
        GUILayout.Label($"Camera Rotation OUTPUT: {CameraRotation:F2}");
        GUILayout.Label($"ArmIMU: {(useArmIMUForSpreadHeight ? (ArmIMUAvailable() ? "<color=lime>ON</color>" : "<color=red>MISSING</color>") : "off")}");
       // GUILayout.Label($"---");
       // GUILayout.Label($"IMU Movement Active: {useIMUForMovement}");
       // GUILayout.Label($"<color=cyan>MetaQuest Rotation Active: {useMetaQuestForRotation}</color>");
        //GUILayout.Label($"IMU Rotation Active: {useIMUForRotation}");
       // GUILayout.Label($"MediaPipe Spread Active: {useMediaPipeForSpread}");
        //GUILayout.Label($"MediaPipe Height Active: {useMediaPipeForHeight}");
        //GUILayout.Label($"Traditional Fallback: {enableTraditionalFallback}");
        
        // Show pitch active status (left/right movement)
        bool pitchActive = useIMUForMovement && 
                         imuMovementSelector != null && 
                         imuMovementSelector.ActiveMode != null && 
                         imuMovementSelector.ActiveMode.IsPitchActive;
        //GUILayout.Label($"<color=orange>Pitch Active (Yaw Locked): {pitchActive}</color>");
        
        if (useMetaQuestForRotation && metaQuestInput != null)
        {
            //GUILayout.Label($"<color=lime>MetaQuest Yaw Rate: {metaQuestInput.HeadsetYawRate:F2}</color>");
        }
        GUILayout.EndArea();
    }
}
