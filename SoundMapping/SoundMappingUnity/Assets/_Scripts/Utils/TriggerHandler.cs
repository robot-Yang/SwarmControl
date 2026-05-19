using UnityEngine;
using UnityEngine.Events; // For UnityEvent
using UnityEditor;
using System; // For PropertyDrawer
using System.Collections;

public class TriggerHandlerWithCallback : MonoBehaviour
{
    [TagSelector] // Custom attribute for tag selection
    public string targetTag; // Tag to filter the objects

    public bool allCollectiblesCollected
    {
        get
        {
            try
            {
                return GameObject.FindGameObjectsWithTag("Collectibles").Length == 0;
            }
            catch (Exception e)
            {
                return true;
            }
            //return GameObject.FindGameObjectsWithTag("Collectibles").Length == 0;
        }
    }

    public bool allDronesConnected
    {
        get
        {
            try
            {
                return swarmModel.network.IsFullyConnected();
            }
            catch (Exception e)
            {
                return true;
            }
            //return GameObject.FindGameObjectsWithTag("Collectibles").Length == 0;
        }
    }

    [SerializeField] bool useUnityEvent = true;
    [SerializeField] bool isStart = true;
    [SerializeField] float postRunTailSeconds = 1.0f;
    private bool runStopped = false;


    public UnityEvent onTriggerEnter; // Callback to assign in the Inspector

    private static GameObject gm;


    public static void setGM(GameObject gameManager)
    {
        gm = gameManager;
    }


    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(targetTag))
        {
            if (useUnityEvent)
            {
                if (isStart)
                {
                //    XboxScreenRecorder.StartRecording();
                    TutorialPlayer.stopVideo();
                    //         print(gm.name);
                    SwarmTrajectoryRecorder.MarkTrialStart("Run");
                    gm.GetComponent<Timer>().StartTimer();
                    runStopped = false;
                }
                else
                {
                    if (!runStopped)
                    {
                        SwarmTrajectoryRecorder.MarkTrialStop("Run");
                        gm.GetComponent<Timer>().StopTimer();
                        runStopped = true;
                    }

                    if(!allDronesConnected)
                    {
                        textInfo.setTextErrorStatic("No drones must be left behind", 2);
                        return;
                    }
                    if (allCollectiblesCollected)
                    {
                        if(Timer.isValidTime())
                        {
                       //     XboxScreenRecorder.StopRecordingAndSave();
                            print("Level Finished from trigger");
                            StartCoroutine(ExportAfterPostRunTail());
                        }else{
                            swarmModel.restart();
                        }

                    }else{
                        textInfo.setTextErrorStatic("Collect all the collectibles", 2);
                        return;
                    }
                }
            }
            else
            {
                onTriggerEnter?.Invoke(); // Call the assigned callback
            }
            
        }
    }

    private IEnumerator ExportAfterPostRunTail()
    {
        if (postRunTailSeconds > 0f)
            yield return new WaitForSeconds(postRunTailSeconds);
        saveInfoToJSON.exportData(false);
    }
}

public class TagSelectorAttribute : PropertyAttribute { }

#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(TagSelectorAttribute))]
public class TagSelectorPropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType == SerializedPropertyType.String)
        {
            EditorGUI.BeginProperty(position, label, property);
            property.stringValue = EditorGUI.TagField(position, label, property.stringValue);
            EditorGUI.EndProperty();
        }
        else
        {
            EditorGUI.PropertyField(position, property, label);
            Debug.LogWarning("TagSelector can only be used with string properties.");
        }
    }
}
#endif
