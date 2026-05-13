using UnityEngine;
using UnityEditor;
using System.IO;

[CustomEditor(typeof(SceneSelectorScript))]
public class SceneSelectorScriptEditor : Editor
{
    private const string SourceScenePath = "Assets/Scenes/SceneStudy/FPVObs_3d.unity";
    private const string MainScenePath = "Assets/Scenes/SceneStudy/Main.unity";

    public override void OnInspectorGUI()
    {
        // Draw the default inspector fields (any public fields, etc.)
        DrawDefaultInspector();

        // Reference to the actual script on the GameObject
        SceneSelectorScript myScript = (SceneSelectorScript)target;

        // Label for clarity

        //make  a text for Script.pid centered and in big font
        GUIStyle style = new GUIStyle();
        style.fontSize = 20;
        style.alignment = TextAnchor.MiddleCenter;
        style.normal.textColor = Color.white;
        EditorGUILayout.LabelField("ID: " + SceneSelectorScript.pid, style);
        //put in red the text Haptics if haptics is disabled
        if (!SceneSelectorScript._haptics)
        {
            style.normal.textColor = Color.red;
        }
        else
        {
            style.normal.textColor = Color.white;
        }
        EditorGUILayout.LabelField("Haptics: " + SceneSelectorScript._haptics, style);
        //same thing for order
        if (!SceneSelectorScript._order)
        {
            style.normal.textColor = Color.red;
        }
        else
        {
            style.normal.textColor = Color.white;
        }
        EditorGUILayout.LabelField("Main First : " + SceneSelectorScript._order, style);

        EditorGUILayout.BeginVertical();

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Pablo"))
        {
            myScript.SelectTrainingFromButton(myScript.ObstacleFPV);
        }

        if (GUILayout.Button("Main"))
        {
            myScript.SelectTrainingFromButton(myScript.ObstacleFPV2);
        }

        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Copy FPVObs_3d as Main"))
        {
            CopyFPVObs3dAsMain();
        }

        EditorGUILayout.EndVertical();





        //


    }

    private static void CopyFPVObs3dAsMain()
    {
        if (!File.Exists(SourceScenePath))
        {
            EditorUtility.DisplayDialog("Copy FPVObs_3d as Main", $"Source scene not found:\n{SourceScenePath}", "OK");
            return;
        }

        if (!File.Exists(MainScenePath))
        {
            EditorUtility.DisplayDialog("Copy FPVObs_3d as Main", $"Destination scene not found:\n{MainScenePath}", "OK");
            return;
        }

        bool shouldCopy = EditorUtility.DisplayDialog(
            "Copy FPVObs_3d as Main",
            $"Overwrite {MainScenePath} with {SourceScenePath}?",
            "Copy",
            "Cancel");

        if (!shouldCopy)
        {
            return;
        }

        File.Copy(SourceScenePath, MainScenePath, true);
        AssetDatabase.ImportAsset(MainScenePath);
        AssetDatabase.Refresh();

        Debug.Log($"Copied {SourceScenePath} to {MainScenePath}");
    }
}
