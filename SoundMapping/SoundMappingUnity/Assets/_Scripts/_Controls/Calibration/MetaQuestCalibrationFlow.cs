using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 6-step calibration capture for Meta Quest spread + height inputs.
/// Triggered by the V key (configurable). Walks the participant through min/max/neutral
/// poses for spread (controllers/hands distance) and height (average hand world Y),
/// firing CaptureMin/Neutral/Max on whichever input source is currently available.
///
/// Design notes:
///   • Controller and hand sources are mutually exclusive at the hardware level
///     (OVRInput won't report both connected). Each step writes to whichever pair
///     is live at the moment of capture.
///   • Steps are advanced by a per-step countdown (default 3 s). Pressing the
///     advance key (Space) skips the remaining countdown.
///   • Pressing the cancel key (Escape) aborts mid-flow and rolls back the values
///     captured so far so the participant doesn't end up with a half-calibrated state.
/// </summary>
public class MetaQuestCalibrationFlow : MonoBehaviour
{
    [Header("Targets")]
    [Tooltip("Optional. Auto-found if left empty.")]
    public ControllerSpreadInput controllerSpread;
    [Tooltip("Optional. Auto-found if left empty.")]
    public HandSpreadInput        handSpread;
    [Tooltip("Optional. Auto-found if left empty.")]
    public ControllerHeightInput  controllerHeight;
    [Tooltip("Optional. Auto-found if left empty.")]
    public HandHeightInput        handHeight;

    [Header("Keybinds")]
    [Tooltip("Press to start the 6-step flow.")]
    public KeyCode startKey = KeyCode.V;

    [Tooltip("Press to skip the remaining countdown and capture immediately.")]
    public KeyCode advanceKey = KeyCode.Space;

    [Tooltip("Press to cancel mid-flow and roll back changes.")]
    public KeyCode cancelKey = KeyCode.Escape;

    [Header("Timing")]
    [Tooltip("Seconds the participant has to assume the pose (e.g. raise hands to MIN HEIGHT) before the capture countdown starts. Skippable with the advance key.")]
    [Range(0f, 15f)]
    public float getReadyPerStep = 4f;

    [Tooltip("Seconds to hold the pose steady while the capture countdown elapses. Skippable with the advance key.")]
    [Range(1f, 10f)]
    public float countdownPerStep = 3f;

    [Header("Debug")]
    public bool verboseLogging = true;

    // ============================================
    // STATE
    // ============================================

    private enum Step
    {
        Idle,
        SpreadMin,
        SpreadMax,
        SpreadNeutral,
        HeightMin,
        HeightMax,
        HeightNeutral,
        Done,
    }

    private static readonly Dictionary<Step, string> Prompts = new()
    {
        { Step.SpreadMin,     "MIN SPREAD — bring hands as close together as comfortable" },
        { Step.SpreadMax,     "MAX SPREAD — extend hands as wide as comfortable" },
        { Step.SpreadNeutral, "NEUTRAL SPREAD — relaxed, comfortable middle distance" },
        { Step.HeightMin,     "MIN HEIGHT — lower hands to the bottom of your range" },
        { Step.HeightMax,     "MAX HEIGHT — raise hands to the top of your range" },
        { Step.HeightNeutral, "NEUTRAL HEIGHT — relaxed, comfortable middle height" },
    };

    private Step _step = Step.Idle;
    private float _countdown = 0f;
    // Each step has two phases: get-ready (assume the pose) → capture countdown (hold steady).
    private bool _isGettingReady = false;

    // Snapshots of the prior calibration so we can roll back on cancel.
    private float _spreadMinBackup,    _spreadNeutralBackup,    _spreadMaxBackup;
    private float _heightMinBackup,    _heightNeutralBackup,    _heightMaxBackup;
    private bool _backupsTaken = false;

    public bool IsRunning => _step != Step.Idle && _step != Step.Done;

    // ============================================
    // LIFECYCLE
    // ============================================

    void Start()
    {
        if (controllerSpread  == null) controllerSpread  = FindObjectOfType<ControllerSpreadInput>();
        if (handSpread        == null) handSpread        = FindObjectOfType<HandSpreadInput>();
        if (controllerHeight  == null) controllerHeight  = FindObjectOfType<ControllerHeightInput>();
        if (handHeight        == null) handHeight        = FindObjectOfType<HandHeightInput>();
    }

    void Update()
    {
        if (!IsRunning)
        {
            if (Input.GetKeyDown(startKey)) BeginFlow();
            return;
        }

        if (Input.GetKeyDown(cancelKey)) { Cancel(); return; }

        _countdown -= Time.deltaTime;
        bool skip = Input.GetKeyDown(advanceKey);

        if (_countdown <= 0f || skip)
        {
            if (_isGettingReady)
            {
                // Get-ready phase done: start the capture countdown for the same step.
                _isGettingReady = false;
                _countdown = countdownPerStep;
                if (verboseLogging) Debug.Log($"  [{_step}] get-ready done, capturing in {countdownPerStep:F1}s");
            }
            else
            {
                CaptureCurrentStep();
                AdvanceStep();
            }
        }
    }

    // ============================================
    // FLOW CONTROL
    // ============================================

    public void BeginFlow()
    {
        TakeBackups();
        _step = Step.SpreadMin;
        _isGettingReady = true;
        _countdown = getReadyPerStep;
        if (verboseLogging) Debug.Log("=== Meta Quest calibration: starting (press Esc to cancel) ===");
    }

    private void AdvanceStep()
    {
        _step = _step switch
        {
            Step.SpreadMin     => Step.SpreadMax,
            Step.SpreadMax     => Step.SpreadNeutral,
            Step.SpreadNeutral => Step.HeightMin,
            Step.HeightMin     => Step.HeightMax,
            Step.HeightMax     => Step.HeightNeutral,
            Step.HeightNeutral => Step.Done,
            _                  => Step.Idle,
        };

        if (_step == Step.Done)
        {
            if (verboseLogging) Debug.Log("=== Meta Quest calibration: complete ===");
            _step = Step.Idle;
            _backupsTaken = false;
            _isGettingReady = false;
        }
        else
        {
            _isGettingReady = true;
            _countdown = getReadyPerStep;
        }
    }

    private void CaptureCurrentStep()
    {
        switch (_step)
        {
            case Step.SpreadMin:     CaptureSpread((s, h) => { s?.CaptureMin();     h?.CaptureMin();     }); break;
            case Step.SpreadMax:     CaptureSpread((s, h) => { s?.CaptureMax();     h?.CaptureMax();     }); break;
            case Step.SpreadNeutral: CaptureSpread((s, h) => { s?.CaptureNeutral(); h?.CaptureNeutral(); }); break;
            case Step.HeightMin:     CaptureHeight((s, h) => { s?.CaptureMin();     h?.CaptureMin();     }); break;
            case Step.HeightMax:     CaptureHeight((s, h) => { s?.CaptureMax();     h?.CaptureMax();     }); break;
            case Step.HeightNeutral: CaptureHeight((s, h) => { s?.CaptureNeutral(); h?.CaptureNeutral(); }); break;
        }
    }

    private void CaptureSpread(System.Action<ControllerSpreadInput, HandSpreadInput> apply)
    {
        apply(controllerSpread, handSpread);
        if (verboseLogging)
        {
            string ctlr = controllerSpread != null && controllerSpread.IsAvailable ? $"controller d={controllerSpread.GetCurrentDistance():F2}" : "controller n/a";
            string hand = handSpread != null && handSpread.IsAvailable ? $"hand d={handSpread.GetCurrentDistance():F2}" : "hand n/a";
            Debug.Log($"  [{_step}] {ctlr}  {hand}");
        }
    }

    private void CaptureHeight(System.Action<ControllerHeightInput, HandHeightInput> apply)
    {
        apply(controllerHeight, handHeight);
        if (verboseLogging)
        {
            string ctlr = controllerHeight != null && controllerHeight.IsAvailable ? $"controller y={controllerHeight.GetAverageControllerHeight():F2}" : "controller n/a";
            string hand = handHeight != null && handHeight.IsAvailable ? $"hand y={handHeight.GetAverageHandHeight():F2}" : "hand n/a";
            Debug.Log($"  [{_step}] {ctlr}  {hand}");
        }
    }

    public void Cancel()
    {
        if (!IsRunning) return;
        if (verboseLogging) Debug.LogWarning("=== Meta Quest calibration: cancelled, rolling back ===");
        RestoreBackups();
        _step = Step.Idle;
        _isGettingReady = false;
    }

    // ============================================
    // BACKUPS — single shared snapshot for whichever sibling is live.
    // We back up from controller* if connected, otherwise from hand*; on restore
    // we write the same snapshot back to whichever is still live. That's a tradeoff:
    // if the participant swaps controllers↔hands mid-flow, the rollback uses the
    // first source's values. In practice this never happens within a 20-second flow.
    // ============================================

    private void TakeBackups()
    {
        if (controllerSpread != null)
        {
            _spreadMinBackup     = controllerSpread.minDistance;
            _spreadNeutralBackup = controllerSpread.neutralDistance;
            _spreadMaxBackup     = controllerSpread.maxDistance;
        }
        else if (handSpread != null)
        {
            _spreadMinBackup     = handSpread.minDistance;
            _spreadNeutralBackup = handSpread.neutralDistance;
            _spreadMaxBackup     = handSpread.maxDistance;
        }

        if (controllerHeight != null)
        {
            _heightMinBackup     = controllerHeight.minHeight;
            _heightNeutralBackup = controllerHeight.neutralHeight;
            _heightMaxBackup     = controllerHeight.maxHeight;
        }
        else if (handHeight != null)
        {
            _heightMinBackup     = handHeight.minHeight;
            _heightNeutralBackup = handHeight.neutralHeight;
            _heightMaxBackup     = handHeight.maxHeight;
        }

        _backupsTaken = true;
    }

    private void RestoreBackups()
    {
        if (!_backupsTaken) return;

        if (controllerSpread != null)
        {
            controllerSpread.minDistance     = _spreadMinBackup;
            controllerSpread.neutralDistance = _spreadNeutralBackup;
            controllerSpread.maxDistance     = _spreadMaxBackup;
        }
        if (handSpread != null)
        {
            handSpread.minDistance     = _spreadMinBackup;
            handSpread.neutralDistance = _spreadNeutralBackup;
            handSpread.maxDistance     = _spreadMaxBackup;
        }
        if (controllerHeight != null)
        {
            controllerHeight.minHeight     = _heightMinBackup;
            controllerHeight.neutralHeight = _heightNeutralBackup;
            controllerHeight.maxHeight     = _heightMaxBackup;
        }
        if (handHeight != null)
        {
            handHeight.minHeight     = _heightMinBackup;
            handHeight.neutralHeight = _heightNeutralBackup;
            handHeight.maxHeight     = _heightMaxBackup;
        }

        _backupsTaken = false;
    }

    // ============================================
    // ON-SCREEN PROMPT
    // ============================================

    void OnGUI()
    {
        if (!Application.isPlaying || !IsRunning) return;

        GUI.color = Color.white;
        GUILayout.BeginArea(new Rect(Screen.width / 2 - 320, Screen.height / 2 - 80, 640, 160), GUI.skin.box);
        GUILayout.Label($"<size=22><b>Meta Quest Calibration</b></size>");
        GUILayout.Label($"<size=18>Step: {Prompts[_step]}</size>");
        if (_isGettingReady)
            GUILayout.Label($"<size=24><color=cyan>Get ready — capturing in {_countdown:F1}s</color></size>");
        else
            GUILayout.Label($"<size=24><color=yellow>Hold steady — capture in {_countdown:F1}s</color></size>");
        GUILayout.Label($"<size=14>{advanceKey} = skip phase    {cancelKey} = cancel</size>");
        GUILayout.EndArea();
    }
}
