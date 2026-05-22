# Questionnaires (NASA-TLX + SUS) — SwarmControl integration

This folder is a vendored copy of [nasa-tlx-SUS](https://github.com/) (jQuery
static web app) plus a small prefill layer so Unity can launch it at the end of
a trial with the participant and task already populated.

## Files

- `index.html`, `setup/`, `scripts/`, `README.md` — original NASA-TLX app
- `setup/js/swarmcontrol-prefill.js` — SwarmControl prefill layer (new)

## Launching from Unity

At end-of-trial, open the page with the participant PID and task as query
parameters:

```csharp
// e.g. after a session completes
var url = $"file:///{Application.dataPath}/../../../WebPages/questionnaires/index.html"
        + $"?pid={UnityWebRequest.EscapeURL(pid)}"
        + $"&task={UnityWebRequest.EscapeURL(sceneName)}";
Application.OpenURL(url);
```

Supported query parameters:

| Param      | Meaning                            | Example  |
|------------|------------------------------------|----------|
| `pid`      | Participant id (your PID string)   | `HTP01`  |
| `task`     | Task / scene label                 | `Main`   |
| `camipro`  | Optional Camipro number (prefilled into the second field) | `123456` |

Behaviour:

- If a participant whose label contains `pid` already exists, that radio is
  selected. Otherwise `pid` is prefilled into the "Create new participant"
  name field and the experimenter clicks Create.
- Same logic for `task`.
- The page jumps from the overview to Step 1 automatically.
- A yellow note is shown at the top of Step 1 confirming what was prefilled.

The experimenter still has to choose the questionnaire (TLX / SUS / Additional)
and click Continue — that's intentional, so we don't silently start the wrong
questionnaire.

## Data

The app stores everything in browser `localStorage` under the key the original
project uses, and you export via the "Export CSV" button on Step 0. Open the
page in the same browser profile each session.
