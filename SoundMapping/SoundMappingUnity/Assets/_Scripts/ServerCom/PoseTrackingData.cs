using System;

/// <summary>
/// Data structure for MediaPipe hand tracking data received from Python
/// </summary>
[Serializable]
public class MediaPipeHandData
{
    public float distance;  // Normalized hand distance (0..1)
    public float height;    // Normalized hand height (-1..+1, 0 = neutral)
}
