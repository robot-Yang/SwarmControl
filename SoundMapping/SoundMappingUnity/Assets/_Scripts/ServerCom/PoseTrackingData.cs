using System;

/// <summary>
/// Data structure for pose-tracking data received from the Python tracker
/// (MediaPipe Hands or RTMPose Wholebody backend; wire format is identical).
/// </summary>
[Serializable]
public class PoseTrackingData
{
    public float distance;  // Normalized hand spread (0..1)
    public float height;    // Normalized hand height (-1..+1, 0 = neutral)
    public float yaw;       // Head yaw in degrees, neutral-subtracted. 0 if backend has no face landmarks.
}
