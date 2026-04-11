using UnityEngine;

/// <summary>
/// Temporary debug tool to identify Taranis axis numbers.
/// Add to any GameObject, move each stick/slider and watch which axis changes.
/// Delete this file once you've identified all axes.
/// </summary>
public class JoystickAxisFinder : MonoBehaviour
{
    // Named axes defined in InputManager — edit these if you add more
    readonly string[] axisNames = {
        "Horizontal", "Vertical",
        "JoystickRightHorizontal", "JoystickRightVertical",
        "LR", "Throttle",
        "TestAxis5", "TestAxis6", "TestAxis7", "TestAxis8"
    };

    void OnGUI()
    {
        if (!Application.isPlaying) return;

        GUILayout.BeginArea(new Rect(Screen.width - 240, 10, 230, 310));
        GUILayout.Label("<b>AXIS FINDER — move each stick</b>");
        GUILayout.Label("────────────────────");
        foreach (string name in axisNames)
        {
            float val = Input.GetAxisRaw(name);
            string color = Mathf.Abs(val) > 0.05f ? "lime" : "white";
            GUILayout.Label($"<color={color}>{name}: {val:F2}</color>");
        }
        GUILayout.Label("────────────────────");
        GUILayout.Label("<color=yellow>Move LEFT stick UP/DOWN</color>");
        GUILayout.Label("<color=yellow>and note which value changes</color>");
        GUILayout.EndArea();
    }
}
