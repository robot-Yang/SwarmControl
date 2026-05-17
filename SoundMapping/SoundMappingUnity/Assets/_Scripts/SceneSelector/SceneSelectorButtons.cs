using UnityEngine;
using UnityEditor;
using System.IO;

[CustomEditor(typeof(SceneSelectorScript))]
public class SceneSelectorScriptEditor : Editor
{
    private const string FpvObs3dScenePath = "Assets/Scenes/SceneStudy/FPVObs_3d.unity";
    private const string Trial1ScenePath = "Assets/Scenes/SceneStudy/trial_1.unity";
    private const string Trial2ScenePath = "Assets/Scenes/SceneStudy/trial_2.unity";
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
            CopySceneAsMain(FpvObs3dScenePath, "Copy FPVObs_3d as Main");
        }

        if (GUILayout.Button("Copy trial_1 as Main"))
        {
            CopySceneAsMain(Trial1ScenePath, "Copy trial_1 as Main");
        }

        if (GUILayout.Button("Copy trial_2 as Main"))
        {
            CopySceneAsMain(Trial2ScenePath, "Copy trial_2 as Main");
        }

        EditorGUILayout.EndVertical();





        //


    }

    // Overwrites Main.unity with the contents of the given source scene file.
    // dialogTitle is used as the title for the confirmation / error popups.
    private static void CopySceneAsMain(string sourceScenePath, string dialogTitle)
    {
        if (!File.Exists(sourceScenePath))
        {
            EditorUtility.DisplayDialog(dialogTitle, $"Source scene not found:\n{sourceScenePath}", "OK");
            return;
        }

        if (!File.Exists(MainScenePath))
        {
            EditorUtility.DisplayDialog(dialogTitle, $"Destination scene not found:\n{MainScenePath}", "OK");
            return;
        }

        bool shouldCopy = EditorUtility.DisplayDialog(
            dialogTitle,
            $"Overwrite {MainScenePath} with {sourceScenePath}?",
            "Copy",
            "Cancel");

        if (!shouldCopy)
        {
            return;
        }

        File.Copy(sourceScenePath, MainScenePath, true);
        AssetDatabase.ImportAsset(MainScenePath);
        AssetDatabase.Refresh();

        Debug.Log($"Copied {sourceScenePath} to {MainScenePath}");
    }
}
