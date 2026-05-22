using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class textInfo : MonoBehaviour
{
    public TextMeshProUGUI connexionText;
    public TextMeshProUGUI SpreadnessText;
    public TextMeshProUGUI IsolationText;
    public TextMeshProUGUI DroneCrashText;
    public TextMeshProUGUI SpreadnessSwarmScore;
    public TextMeshProUGUI textTutorial;
    public TextMeshProUGUI errorText;

    public Image deathImage;

    public TextMeshProUGUI CollectibleText;
    // Start is called before the first frame update
    // Update is called once per frame

    public bool showConnexion = false;
    public bool showSpreadness = false;
    public bool showIsolation = false;
    public bool showDroneCrash = false;


    void Start()
    {
        // On-screen tutorial text suppressed — keep the screen clean during runs.
        // To re-enable, restore: textTutorial.text = LevelConfiguration._textTutorial;
        if (textTutorial != null) textTutorial.text = "";
        refresh();

        TutorialPlayer.playTuto(LevelConfiguration.sceneNumber);


    }

    void refresh()
    {
        LevelConfiguration config = GameObject.FindGameObjectWithTag("Config").GetComponent<LevelConfiguration>();
        if (config == null)
        {
            Debug.LogError("No config found");
            return;
        }

        showConnexion = config.hapticsNetwork;
        showSpreadness = config.audioSpreadness;
        showIsolation = config.audioIsolation;
        showDroneCrash = config.hapticsCrash;

    }
    void Update()
    {
        // Hide all UI text
        connexionText.text = "";
        SpreadnessText.text = "";
        IsolationText.text = "";
        DroneCrashText.text = "";
        SpreadnessSwarmScore.text = "";
        
        /* Original code - uncomment to re-enable
        if(LevelConfiguration._ShowText)
        {
            connexionText.text = showConnexion ? "Connection: " + getOneDecimal(swarmModel.swarmConnectionScore) : "";
            SpreadnessText.text = showSpreadness ? "Spreadness: " + getOneDecimal(swarmModel.desiredSeparation) : "";
            IsolationText.text = showIsolation ? "Isolation : " + swarmModel.numberOfDroneDiscionnected.ToString() : "";
            DroneCrashText.text = showDroneCrash ? "Drone Crash : " + swarmModel.numberOfDroneCrashed.ToString() : "";
            //SpreadnessSwarmScore.text = showSpreadness ? "Swarm spreadness : " + getOneDecimal(swarmModel.swarmAskingSpreadness) : "";
            SpreadnessSwarmScore.text = "";
        }else
        {
            connexionText.text = "";
            SpreadnessText.text = "";
            IsolationText.text = "";
            DroneCrashText.text = "";
            SpreadnessSwarmScore.text = "";
        }
        */
        
        // Collectible HUD suppressed — count is still tracked internally and
        // saved to the trajectory JSON via SwarmTrajectoryRecorder.
        if (CollectibleText != null) CollectibleText.text = "";
    }


    public static void setDeathImageStatic(float value)
    {
        Image img = GameObject.FindGameObjectWithTag("GameManager").GetComponent<textInfo>().deathImage;
        if(value > 0.9f)
        {
            GameObject.FindGameObjectWithTag("GameManager").GetComponent<textInfo>().deathImage.gameObject.SetActive(false);
            return;
        }

        img.gameObject.SetActive(true);
        Color color = img.color;
        color.a = (1 - value/2);

        img.color = color;


    }
    public static void setTextErrorStatic(string text, float time)
    {
        GameObject.FindGameObjectWithTag("GameManager").GetComponent<textInfo>().setTextError(text, time);
    }

    public void setTextError(string text, float time)
    {
        StartCoroutine(setTextErrorCoroutine(text, time));
    }

    public IEnumerator setTextErrorCoroutine(string text, float time)
    {
        errorText.text = text;
        yield return new WaitForSeconds(time);
        errorText.text = "";
    }

    string getOneDecimal(float value)
    {
        return value.ToString("F1");
    }
}
