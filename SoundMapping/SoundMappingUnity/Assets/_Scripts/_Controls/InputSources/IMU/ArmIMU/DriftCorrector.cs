using UnityEngine;

/// <summary>
/// Sits between ArmIMUSpreadHeightInput and InputFusionManager.
/// Applies drift correction to raw arm IMU outputs.
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

    [Tooltip("Angular rate threshold (deg/s) below which an arm is considered still")]
    public float zuptRateThreshold = 2f;

    [Tooltip("How fast the locked reference corrects the live reading (0 = off, 1 = instant snap)")]
    [Range(0f, 1f)]
    public float zuptCorrectionRate = 0.3f;

    // ZUPT runtime state
    private Vector3 _prevLeftEuler;
    private Vector3 _prevRightEuler;
    private float _stillTimer = 0f;
    private bool _zuptLocked = false;
    private float _lockedHeight;
    private float _lockedSpread;

    // ============================================
    // QUEST CORRECTION
    // ============================================
    [Header("Quest Hand Tracking Correction")]
    [Tooltip("Left OVRHand from OVRCameraRig/TrackingSpace/LeftHandAnchor")]
    public OVRHand questLeftHand;

    [Tooltip("Right OVRHand from OVRCameraRig/TrackingSpace/RightHandAnchor")]
    public OVRHand questRightHand;

    [Tooltip("Center Eye Anchor from OVRCameraRig — height reference point")]
    public Transform centerEyeAnchor;

    [Tooltip("How strongly Quest corrections pull the IMU estimate per second (0 = off, 1 = snap)")]
    [Range(0f, 1f)]
    public float questCorrectionStrength = 0.1f;

    [Tooltip("Only apply Quest correction when both hands report High confidence")]
    public bool questRequireHighConfidence = true;

    [Tooltip("Maximum hand height above eye level that maps to +1 (meters)")]
    public float questMaxHeightAbove = 0.5f;

    [Tooltip("Maximum hand height below eye level that maps to -1 (meters)")]
    public float questMaxHeightBelow = 0.5f;

    [Tooltip("Minimum hand distance in meters (maps to minSwarmSeparation)")]
    public float questMinHandDistance = 0.1f;

    [Tooltip("Maximum hand distance in meters (maps to maxSwarmSeparation)")]
    public float questMaxHandDistance = 0.8f;

    // ============================================
    // MEDIAPIPE CORRECTION
    // ============================================
    [Header("MediaPipe Correction (Fallback)")]
    [Tooltip("MediaPipe spread input — used as correction source when Quest unavailable")]
    public MediaPipeSpreadInput mediaPipeSpread;

    [Tooltip("MediaPipe height input — used as correction source when Quest unavailable")]
    public MediaPipeHeightInput mediaPipeHeight;

    [Tooltip("How strongly MediaPipe corrections pull the IMU estimate per second")]
    [Range(0f, 1f)]
    public float mediaPipeCorrectionStrength = 0.05f;

    // ============================================
    // OUTPUT PROPERTIES
    // ============================================

    /// <summary>Corrected height control (-1 to +1).</summary>
    public float CorrectedHeight { get; private set; }

    /// <summary>Corrected spread control (meters, absolute).</summary>
    public float CorrectedSpread { get; private set; }

    /// <summary>Returns true when the raw input source is available.</summary>
    public bool IsAvailable => armIMUInput != null && armIMUInput.IsAvailable;

    // Debug state (readable from OnGUI)
    private bool _questActive;
    private bool _zuptActive;

    // ============================================
    // INITIALIZATION
    // ============================================

    void Start()
    {
        if (armIMUInput != null && armIMUInput.leftArmIMU != null)
            _prevLeftEuler = armIMUInput.leftArmIMU.SensorEulerAnglesRaw;
        if (armIMUInput != null && armIMUInput.rightArmIMU != null)
            _prevRightEuler = armIMUInput.rightArmIMU.SensorEulerAnglesRaw;
    }

    // ============================================
    // UPDATE LOOP
    // ============================================

    void Update()
    {
        _questActive = false;
        _zuptActive = false;

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

        if (useQuestCorrection)
            ApplyQuestCorrection(ref height, ref spread);

        if (useMediaPipeCorrection)
            ApplyMediaPipeCorrection(ref height, ref spread);

        CorrectedHeight = height;
        CorrectedSpread = spread;
    }

    // ============================================
    // 1. KINEMATIC CLAMPING
    // ============================================

    void ApplyKinematicClamping(ref float height, ref float spread)
    {
        // Height is normalized -1..+1.
        // Map anatomical pitch limits to the same normalized range.
        float pitchMax = armIMUInput.pitchMaxAngle > 0 ? armIMUInput.pitchMaxAngle : 60f;
        float normUp   = Mathf.Clamp01(maxArmPitchUp   / pitchMax);
        float normDown = Mathf.Clamp01(maxArmPitchDown  / pitchMax);
        height = Mathf.Clamp(height, -normDown, normUp);

        // Spread is in meters. Map anatomical yaw limit to the separation range.
        float yawMax = armIMUInput.yawMaxSpreadAngle > 0 ? armIMUInput.yawMaxSpreadAngle : 120f;
        float normSpread = Mathf.Clamp01(maxArmYawSpread / yawMax);
        float maxSeparation = Mathf.Lerp(armIMUInput.minSwarmSeparation, armIMUInput.maxSwarmSeparation, normSpread);
        spread = Mathf.Clamp(spread, armIMUInput.minSwarmSeparation, maxSeparation);
    }

    // ============================================
    // 2. ZUPT (Zero-Velocity Update)
    // ============================================

    void ApplyZUPT(ref float height, ref float spread)
    {
        if (armIMUInput.leftArmIMU == null || armIMUInput.rightArmIMU == null) return;

        // Compute angular rates from Euler angle deltas
        Vector3 leftEuler = armIMUInput.leftArmIMU.SensorEulerAnglesRaw;
        Vector3 rightEuler = armIMUInput.rightArmIMU.SensorEulerAnglesRaw;

        float dt = Time.deltaTime;
        if (dt < 1e-6f) return;

        float leftRate = (leftEuler - _prevLeftEuler).magnitude / dt;
        float rightRate = (rightEuler - _prevRightEuler).magnitude / dt;

        _prevLeftEuler = leftEuler;
        _prevRightEuler = rightEuler;

        bool bothStill = leftRate < zuptRateThreshold && rightRate < zuptRateThreshold;

        if (bothStill)
        {
            _stillTimer += dt;

            if (_stillTimer >= zuptStillDuration)
            {
                if (!_zuptLocked)
                {
                    // Lock the current reading as the stable reference
                    _lockedHeight = height;
                    _lockedSpread = spread;
                    _zuptLocked = true;
                }

                // Nudge toward locked reference to cancel drift
                float blend = zuptCorrectionRate * dt * 5f;
                height = Mathf.Lerp(height, _lockedHeight, blend);
                spread = Mathf.Lerp(spread, _lockedSpread, blend);
                _zuptActive = true;
            }
        }
        else
        {
            // Arms moving — release the lock
            _stillTimer = 0f;
            _zuptLocked = false;
        }
    }

    // ============================================
    // 3. QUEST HAND TRACKING CORRECTION
    // ============================================

    void ApplyQuestCorrection(ref float height, ref float spread)
    {
        if (questLeftHand == null || questRightHand == null) return;

        // Check tracking availability
        bool leftTracked = questLeftHand.IsTracked;
        bool rightTracked = questRightHand.IsTracked;
        if (!leftTracked || !rightTracked) return;

        // Check confidence if required
        if (questRequireHighConfidence)
        {
            bool leftHigh = questLeftHand.HandConfidence == OVRHand.TrackingConfidence.High;
            bool rightHigh = questRightHand.HandConfidence == OVRHand.TrackingConfidence.High;
            if (!leftHigh || !rightHigh) return;
        }

        _questActive = true;
        float blend = questCorrectionStrength * Time.deltaTime;

        // --- Height correction ---
        // Average wrist Y relative to eye level → normalize to -1..+1
        float eyeY = centerEyeAnchor != null ? centerEyeAnchor.position.y : 0f;
        float avgHandY = (questLeftHand.transform.position.y + questRightHand.transform.position.y) * 0.5f;
        float deltaY = avgHandY - eyeY;

        float questHeight;
        if (deltaY >= 0)
            questHeight = Mathf.Clamp01(deltaY / questMaxHeightAbove);
        else
            questHeight = -Mathf.Clamp01(Mathf.Abs(deltaY) / questMaxHeightBelow);

        height = Mathf.Lerp(height, questHeight, blend);

        // --- Spread correction ---
        // World-space wrist distance → map to separation meters
        float handDist = Vector3.Distance(questLeftHand.transform.position, questRightHand.transform.position);
        float normalizedDist = Mathf.InverseLerp(questMinHandDistance, questMaxHandDistance, handDist);
        normalizedDist = Mathf.Clamp01(normalizedDist);
        float questSpread = Mathf.Lerp(armIMUInput.minSwarmSeparation, armIMUInput.maxSwarmSeparation, normalizedDist);

        spread = Mathf.Lerp(spread, questSpread, blend);
    }

    // ============================================
    // 4. MEDIAPIPE CORRECTION (Fallback)
    // ============================================

    void ApplyMediaPipeCorrection(ref float height, ref float spread)
    {
        float blend = mediaPipeCorrectionStrength * Time.deltaTime;

        // Height: MediaPipe outputs rate-based -1..+1, same as our height — blend directly
        if (mediaPipeHeight != null && mediaPipeHeight.IsAvailable)
        {
            height = Mathf.Lerp(height, mediaPipeHeight.HeightControl, blend);
        }

        // Spread: MediaPipe outputs absolute meters — blend directly
        if (mediaPipeSpread != null && mediaPipeSpread.IsAvailable)
        {
            spread = Mathf.Lerp(spread, mediaPipeSpread.SpreadControl, blend);
        }
    }

    // ============================================
    // PUBLIC API
    // ============================================

    public void CalibrateNeutral()
    {
        if (armIMUInput != null)
            armIMUInput.CalibrateNeutral();

        // Reset ZUPT state
        _stillTimer = 0f;
        _zuptLocked = false;
    }

    // ============================================
    // DEBUG
    // ============================================

    void OnGUI()
    {
        if (!Application.isPlaying) return;

        GUILayout.BeginArea(new Rect(10, 750, 380, 130));
        GUILayout.Label("<b>Drift Corrector</b>");

        string active = "";
        if (useKinematicClamping) active += "Clamp ";
        if (_zuptActive) active += "<color=cyan>ZUPT(locked)</color> ";
        else if (useZUPT) active += "ZUPT ";
        if (_questActive) active += "<color=lime>Quest</color> ";
        else if (useQuestCorrection) active += "Quest(waiting) ";
        if (useMediaPipeCorrection) active += "MPipe ";
        if (active.Length == 0) active = "None (passthrough)";

        GUILayout.Label($"Active: {active}");

        if (IsAvailable)
        {
            GUILayout.Label($"Height: raw={armIMUInput.HeightControl:F3}  corrected={CorrectedHeight:F3}");
            GUILayout.Label($"Spread: raw={armIMUInput.SpreadControl:F2}m  corrected={CorrectedSpread:F2}m");
            if (useZUPT)
                GUILayout.Label($"Still timer: {_stillTimer:F2}s  Locked: {_zuptLocked}");
        }
        GUILayout.EndArea();
    }
}
