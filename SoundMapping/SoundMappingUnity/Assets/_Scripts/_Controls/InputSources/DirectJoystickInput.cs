using UnityEngine;

/// <summary>
/// Direct joystick control mode for swarm control.
/// Right stick: Swarm XZ movement (up/down = forward/back, left/right = left/right)
/// Left stick: Height and camera (up/down = height, left/right = camera rotation)
/// RT/LT: Spread control (RT = expand, LT = contract)
/// </summary>
public class DirectJoystickInput : MonoBehaviour
{
    // ============================================
    // PUBLIC PROPERTIES (Read by InputFusionManager)
    // ============================================
    
    /// <summary>
    /// Swarm horizontal movement (X=left/right, Z=forward/back) from RIGHT stick
    /// </summary>
    public Vector3 SwarmMovement { get; private set; }
    
    /// <summary>
    /// Swarm height control from LEFT stick vertical
    /// </summary>
    public float HeightControl { get; private set; }
    
    /// <summary>
    /// Camera rotation from LEFT stick horizontal
    /// </summary>
    public float CameraRotation { get; private set; }
    
    /// <summary>
    /// Swarm spread control from triggers (RT = positive, LT = negative)
    /// </summary>
    public float SpreadControl { get; private set; }

    // ============================================
    // INPUT AXIS CONFIGURATION
    // ============================================
    
    [Header("Right Stick - Swarm Movement")]
    [Tooltip("Right stick horizontal axis (swarm left/right) - Try '4th axis' or '5th axis' if not working")]
    public string rightStickHorizontal = "RightStickHorizontal";
    
    [Tooltip("Right stick vertical axis (swarm forward/back) - Try '4th axis' or '5th axis' if not working")]
    public string rightStickVertical = "RightStickVertical";
    
    [Header("Left Stick - Height & Camera")]
    [Tooltip("Left stick vertical axis (swarm height)")]
    public string leftStickVertical = "Vertical";
    
    [Tooltip("Left stick horizontal axis (camera rotation)")]
    public string leftStickHorizontal = "Horizontal";
    
    [Header("Triggers - Spread Control")]
    [Tooltip("Combined trigger axis (RT positive, LT negative) - Standard: 'LR' axis")]
    public string triggerAxis = "LR";
    
    [Tooltip("Use separate trigger axes instead of combined")]
    public bool useSeparateTriggers = false;
    
    [Tooltip("Right trigger axis (if using separate triggers)")]
    public string rightTrigger = "RightTrigger";
    
    [Tooltip("Left trigger axis (if using separate triggers)")]
    public string leftTrigger = "LeftTrigger";
    
    [Header("Sensitivity")]
    [Tooltip("Movement speed multiplier")]
    public float movementSensitivity = 1.0f;
    
    [Tooltip("Height control sensitivity")]
    public float heightSensitivity = 1.0f;
    
    [Tooltip("Camera rotation sensitivity")]
    public float rotationSensitivity = 1.0f;
    
    [Tooltip("Spread control sensitivity")]
    public float spreadSensitivity = 1.0f;
    
    [Header("Deadzones")]
    [Tooltip("Stick deadzone (0-1)")]
    public float stickDeadzone = 0.15f;
    
    [Tooltip("Camera rotation deadzone (higher to prevent drift)")]
    public float rotationDeadzone = 0.25f;
    
    [Tooltip("Trigger deadzone (0-1)")]
    public float triggerDeadzone = 0.1f;
    
    [Header("Debug")]
    [Tooltip("Show detailed axis values in console")]
    public bool debugMode = true;

    // ============================================
    // UPDATE LOOP
    // ============================================
    
    void Update()
    {
        ReadSwarmMovement();
        ReadHeightControl();
        ReadCameraRotation();
        ReadSpreadControl();
    }

    void ReadSwarmMovement()
    {
        // RIGHT STICK controls swarm XZ movement
        float horizontal = GetAxisWithDeadzone(rightStickHorizontal, stickDeadzone); // X (left/right)
        float vertical = GetAxisWithDeadzone(rightStickVertical, stickDeadzone);     // Z (forward/back)
        
        if (debugMode && (Mathf.Abs(horizontal) > 0.01f || Mathf.Abs(vertical) > 0.01f))
        {
            Debug.Log($"Right Stick - H:{horizontal:F2} ({rightStickHorizontal}), V:{vertical:F2} ({rightStickVertical})");
        }
        
        // Apply sensitivity
        horizontal *= movementSensitivity;
        vertical *= movementSensitivity;
        
        SwarmMovement = new Vector3(horizontal, 0f, vertical);
    }

    void ReadHeightControl()
    {
        // LEFT STICK VERTICAL controls height
        float rawHeight = GetAxisRaw(leftStickVertical);
        float height = GetAxisWithDeadzone(leftStickVertical, stickDeadzone);
        
        // Debug: Log the raw value at startup
        if (Time.frameCount < 10 && debugMode)
        {
            Debug.Log($"HEIGHT CHECK: Raw='{rawHeight:F2}' AfterDeadzone='{height:F2}' Axis='{leftStickVertical}'");
        }
        
        // Zero out if below threshold BEFORE applying sensitivity
        if (Mathf.Abs(height) < 0.05f)
        {
            height = 0f;
        }
        
        HeightControl = height * heightSensitivity;
        
        // Final safety: zero out small output values (catches sensitivity-amplified drift)
        if (Mathf.Abs(HeightControl) < 0.5f)
        {
            HeightControl = 0f;
        }
    }

    void ReadCameraRotation()
    {
        // LEFT STICK HORIZONTAL controls camera rotation
        float rotation = GetAxisWithDeadzone(leftStickHorizontal, rotationDeadzone);
        
        // Zero out if below threshold BEFORE applying sensitivity
        if (Mathf.Abs(rotation) < 0.05f)
        {
            rotation = 0f;
        }
        
        CameraRotation = rotation * rotationSensitivity;
        
        // Final safety: zero out small output values (catches sensitivity-amplified drift)
        if (Mathf.Abs(CameraRotation) < 0.5f)
        {
            CameraRotation = 0f;
        }
    }

    void ReadSpreadControl()
    {
        float spread = 0f;
        
        if (useSeparateTriggers)
        {
            // RT (Right Trigger) = spread out (positive)
            // LT (Left Trigger) = contract (negative)
            float rt = GetAxisWithDeadzone(rightTrigger, triggerDeadzone);
            float lt = GetAxisWithDeadzone(leftTrigger, triggerDeadzone);
            spread = rt - lt;
        }
        else
        {
            // Use combined trigger axis (standard "LR" axis)
            // Positive = RT (expand), Negative = LT (contract)
            spread = GetAxisWithDeadzone(triggerAxis, triggerDeadzone);
        }
        
        SpreadControl = spread * spreadSensitivity;
    }

    // ============================================
    // HELPER METHODS
    // ============================================
    
    /// <summary>
    /// Get axis value with deadzone applied
    /// </summary>
    float GetAxisWithDeadzone(string axisName, float deadzone)
    {
        try
        {
            float value = Input.GetAxis(axisName);
            
            // Apply deadzone
            if (Mathf.Abs(value) < deadzone)
                return 0f;
            
            // Rescale to smooth transition after deadzone
            float sign = Mathf.Sign(value);
            float magnitude = Mathf.Abs(value);
            float rescaled = (magnitude - deadzone) / (1f - deadzone);
            
            return sign * Mathf.Clamp01(rescaled);
        }
        catch
        {
            // Axis not configured in Input Manager
            Debug.LogWarning($"DirectJoystickInput: Axis '{axisName}' not found in Input Manager");
            return 0f;
        }
    }

    /// <summary>
    /// Returns true if any joystick input is active
    /// </summary>
    public bool IsActive()
    {
        return SwarmMovement.sqrMagnitude > 0.01f || 
               Mathf.Abs(HeightControl) > 0.01f || 
               Mathf.Abs(CameraRotation) > 0.01f || 
               Mathf.Abs(SpreadControl) > 0.01f;
    }

    // ============================================
    // DEBUG VISUALIZATION
    // ============================================
    
    void OnGUI()
    {
        if (!enabled) return;

        GUILayout.BeginArea(new Rect(Screen.width - 350, 10, 340, 280));
        GUILayout.Label("<b>=== DIRECT JOYSTICK CONTROL ===</b>");
        GUILayout.Label($"Swarm Movement: {SwarmMovement}");
        GUILayout.Label($"Height: {HeightControl:F2}");
        GUILayout.Label($"Camera Rotation: {CameraRotation:F2}");
        GUILayout.Label($"Spread: {SpreadControl:F2}");
        
        if (debugMode)
        {
            GUILayout.Label("---");
            GUILayout.Label($"<color=yellow>Right Stick H: {GetAxisRaw(rightStickHorizontal):F2}</color>");
            GUILayout.Label($"<color=yellow>Right Stick V: {GetAxisRaw(rightStickVertical):F2}</color>");
            GUILayout.Label($"Left Stick H: {GetAxisRaw(leftStickHorizontal):F2}");
            GUILayout.Label($"Left Stick V: {GetAxisRaw(leftStickVertical):F2}");
            GUILayout.Label($"Triggers: {GetAxisRaw(triggerAxis):F2}");
        }
        GUILayout.EndArea();
    }
    
    float GetAxisRaw(string axisName)
    {
        try { return Input.GetAxisRaw(axisName); }
        catch { return 0f; }
    }
}
