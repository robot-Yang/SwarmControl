using UnityEngine;

/// <summary>
/// Activates additional displays in Unity at runtime.
/// Attach this to any GameObject in your scene to enable Display 2, 3, etc.
/// Required for multi-display setups where Display 1 (VR headset) and Display 2 (monitor) need to be active simultaneously.
/// </summary>
public class DisplayActivator : MonoBehaviour
{
    [Tooltip("Activate displays up to this number (e.g., 2 activates Display 1 and Display 2)")]
    [Range(1, 8)]
    public int numberOfDisplays = 2;

    void Awake()
    {
        // Activate all displays up to the specified number
        Debug.Log($"Total displays connected: {Display.displays.Length}");
        
        for (int i = 0; i < Mathf.Min(numberOfDisplays, Display.displays.Length); i++)
        {
            if (i > 0) // Display 0 (Display 1) is always active by default
            {
                Display.displays[i].Activate();
                Debug.Log($"Activated Display {i + 1} (index {i})");
            }
        }
    }
}
