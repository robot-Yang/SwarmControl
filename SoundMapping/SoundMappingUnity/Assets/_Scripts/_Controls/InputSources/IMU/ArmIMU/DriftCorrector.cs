using UnityEngine;

/// <summary>
/// Sits between ArmIMUSpreadHeightInput and InputFusionManager.
/// Applies drift correction to raw arm IMU outputs.
/// Currently a pass-through stub — correction methods are wired but not yet implemented.
///
/// Correction pipeline (applied in order when enabled):
///   1. Kinematic clamping   — anatomical angle limits prevent runaway values
///   2. ZUPT                 — zero-velocity updates during arm stillness
///   3. Quest hand tracking  — absolute correction from OVRHand wrist positions
///   4. MediaPipe            — fallback absolute correction from external camera
/// </summary>
public class DriftCorrector : MonoBehaviour
{
    // ============================================
    // SOURCE
    // ============================================
    [Header("Raw Input Source")]
    [Tooltip("The arm IMU input to correct")]
    public ArmIMUSpreadHeightInput armIMUInput;

    // ============================================
    // CORRECTION TOGGLES
    // ============================================
    [Header("Correction Methods")]
    [Tooltip("Clamp computed angles to anatomically plausible ranges")]
    public bool useKinematicClamping = true;

    [Tooltip("Lock spread/height when arms are stationary to prevent drift accumulation")]
    public bool useZUPT = false;

    [Tooltip("Use Meta Quest hand tracking as an absolute correction reference")]
    public bool useQuestCorrection = false;

    [Tooltip("Use MediaPipe as an absolute correction reference (fallback if Quest unavailable)")]
    public bool useMediaPipeCorrection = false;

    // ============================================
    // KINEMATIC CLAMPING
    // ============================================
    [Header("Kinematic Limits")]
    [Tooltip("Maximum pitch angle (degrees) arms can raise above neutral")]
    public float maxArmPitchUp = 150f;

    [Tooltip("Maximum pitch angle (degrees) arms can lower below neutral")]
    public float maxArmPitchDown = 60f;

    [Tooltip("Maximum yaw spread angle (degrees) between both arms")]
    public float maxArmYawSpread = 170f;

    // ============================================
    // ZUPT
    // ============================================
    [Header("Zero-Velocity Update (ZUPT)")]
    [Tooltip("Seconds both arms must be still before locking the reading as stable reference")]
    public float zuptStillDuration = 0.5f;

    [Tooltip("Gyro variance threshold (deg/s) below which arms are considered still")]
    public float zuptVarianceThreshold = 2f;

    // TODO: implement ZUPT detection using leftArmIMU/rightArmIMU gyro data

    // ============================================
    // QUEST CORRECTION
    // ============================================
    [Header("Quest Hand Tracking Correction")]
    [Tooltip("Left OVRHand from OVRCameraRig/TrackingSpace/LeftHandAnchor")]
    public OVRHand questLeftHand;

    [Tooltip("Right OVRHand from OVRCameraRig/TrackingSpace/RightHandAnchor")]
    public OVRHand questRightHand;

    [Tooltip("How strongly Quest corrections pull the IMU estimate (0 = no correction, 1 = snap to Quest)")]
    [Range(0f, 1f)]
    public float questCorrectionStrength = 0.1f;

    // TODO: implement Quest wrist position → relative arm angle mapping
    // TODO: blend Quest-derived angles with IMU estimates at questCorrectionStrength

    // ============================================
    // MEDIAPIPE CORRECTION
    // ============================================
    [Header("MediaPipe Correction (Fallback)")]
    [Tooltip("MediaPipe spread input — used as correction source when Quest unavailable")]
    public MediaPipeSpreadInput mediaPipeSpread;

    [Tooltip("MediaPipe height input — used as correction source when Quest unavailable")]
    public MediaPipeHeightInput mediaPipeHeight;

    [Tooltip("How strongly MediaPipe corrections pull the IMU estimate")]
    [Range(0f, 1f)]
    public float mediaPipeCorrectionStrength = 0.05f;

    // TODO: implement MediaPipe → IMU correction blending

    // ============================================
    // OUTPUT PROPERTIES
    // ============================================

    /// <summary>Corrected height control (-1 to +1).</summary>
    public float CorrectedHeight { get; private set; }

    /// <summary>Corrected spread control (meters, absolute).</summary>
    public float CorrectedSpread { get; private set; }

    /// <summary>Returns true when the raw input source is available.</summary>
    public bool IsAvailable => armIMUInput != null && armIMUInput.IsAvailable;

    // ============================================
    // UPDATE LOOP
    // ============================================

    void Update()
    {
        if (!IsAvailable)
        {
            CorrectedHeight = 0f;
            CorrectedSpread = armIMUInput != null ? armIMUInput.minSwarmSeparation : 0f;
            return;
        }

        float height = armIMUInput.HeightControl;
        float spread = armIMUInput.SpreadControl;

        if (useKinematicClamping)
            ApplyKinematicClamping(ref height, ref spread);

        if (useZUPT)
            ApplyZUPT(ref height, ref spread);
            // TODO: implement ZUPT

        if (useQuestCorrection)
            ApplyQuestCorrection(ref height, ref spread);
            // TODO: implement Quest correction

        if (useMediaPipeCorrection)
            ApplyMediaPipeCorrection(ref height, ref spread);
            // TODO: implement MediaPipe correction

        CorrectedHeight = height;
        CorrectedSpread = spread;
    }

    // ============================================
    // CORRECTION STUBS
    // ============================================

    void ApplyKinematicClamping(ref float height, ref float spread)
    {
        // Height: clamp to anatomical pitch limits
        // Positive height = arms raised, negative = arms lowered
        float maxUp   =  Mathf.Clamp01(maxArmPitchUp   / (armIMUInput.pitchMaxAngle > 0 ? armIMUInput.pitchMaxAngle : 60f));
        float maxDown =  Mathf.Clamp01(maxArmPitchDown  / (armIMUInput.pitchMaxAngle > 0 ? armIMUInput.pitchMaxAngle : 60f));
        height = Mathf.Clamp(height, -maxDown, maxUp);

        // Spread: clamp to max anatomical yaw spread
        float maxSpreadNorm = Mathf.Clamp01(maxArmYawSpread / (armIMUInput.yawMaxSpreadAngle > 0 ? armIMUInput.yawMaxSpreadAngle : 120f));
        float maxSeparation = Mathf.Lerp(armIMUInput.minSwarmSeparation, armIMUInput.maxSwarmSeparation, maxSpreadNorm);
        spread = Mathf.Min(spread, maxSeparation);
    }

    void ApplyZUPT(ref float height, ref float spread)
    {
        // TODO: monitor gyro variance on leftArmIMU and rightArmIMU
        // When both arms still for > zuptStillDuration, lock current values as stable reference
        // Slowly correct IMU estimates toward the locked reference
    }

    void ApplyQuestCorrection(ref float height, ref float spread)
    {
        // TODO: check questLeftHand.IsTracked && questLeftHand.HandConfidence == High
        // Compute wrist elevation relative to head → map to height correction
        // Compute wrist distance (world space) → map to spread correction
        // Blend: height = Lerp(height, questHeight, questCorrectionStrength * Time.deltaTime)
        //        spread = Lerp(spread, questSpread, questCorrectionStrength * Time.deltaTime)
    }

    void ApplyMediaPipeCorrection(ref float height, ref float spread)
    {
        // TODO: if Quest not available or low confidence, fall back to MediaPipe
        // mediaPipeHeight.HeightControl → rate-based, integrate to absolute position for correction
        // mediaPipeSpread.SpreadControl → already absolute meters, use directly
        // Blend at mediaPipeCorrectionStrength
    }

    // ============================================
    // DEBUG
    // ============================================

    void OnGUI()
    {
        if (!Application.isPlaying) return;

        GUILayout.BeginArea(new Rect(10, 750, 340, 100));
        GUILayout.Label("<b>Drift Corrector</b>");
        GUILayout.Label($"Available: {IsAvailable}  ZUPT:{useZUPT}  Quest:{useQuestCorrection}  MP:{useMediaPipeCorrection}");
        if (IsAvailable)
        {
            GUILayout.Label($"Height: raw={armIMUInput.HeightControl:F3}  corrected={CorrectedHeight:F3}");
            GUILayout.Label($"Spread: raw={armIMUInput.SpreadControl:F2}m  corrected={CorrectedSpread:F2}m");
        }
        GUILayout.EndArea();
    }
}
