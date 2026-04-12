# SwarmControl

A study comparing two methods of controlling a drone swarm in VR:
- **Controller condition** — Taranis RC controller for all axes 
- **Upper body condition** — Chest IMU for movement, forearm IMUs for spread and height, meta headquest for camera control

---

## Project Structure

```
SwarmControl/
├── SoundMapping/SoundMappingUnity/   # Unity VR application (main project)
├── Control/                          # Python hand tracking system (MediaPipe)
└── WebPages/unity-plotter/           # Haptic feedback bridge (ESP32)
```

---

## Study Scenes

| Scene | Description |
|-------|-------------|
| `Main` | Primary study scene |
| `Pablo` | Legacy Scene |

---

## Input Conditions

### Controller (Taranis RC)
All axes mapped through Unity's Input Manager. No external setup required — plug in the Taranis and press Play.

| Axis | Control |
|------|---------|
| Right stick vertical|Forward/Backward|
| Right stick horizontal| Left/Right  |
| Left stick vertical (Throttle) | Height |
| Left stick horizontal | Camera rotation |
|Right Knob | Swarm spread |

### Upper Body (IMU + Hand Tracking)
Three OpenZen IMU sensors (+ MediaPipe webcam tracking)

| Input | Hardware | Controls |
|-------|----------|---------|
| Chest IMU Pitch | OpenZen sensor | Forward/Backward |
| Chest IMU Roll | OpenZen sensor | Left/Right |
| Forearm IMUs | 2× OpenZen sensors | Spread & height |
| Hand tracking | Webcam + MediaPipe | Spread & height (fallback) |

Enable the relevant toggles on the `InputFusionManager` component in the Setup scene Inspector:
- `useIMUForMovement`
- `useIMUForRotation`
- `useArmIMUForSpreadHeight`
---

## How to Run

### Controller Condition
1. Plug in the Taranis RC controller
2. Open Unity and press Play in the `Scene Selector` scene
3. Enter PID and start experiment

### Upper Body Condition
1. **Start hand tracking** (if using MediaPipe for spread/height):
   ```bash
   cd Control
   python tracker.py
   ```
   Set `CALIBRATION_PROFILE` in `tracker.py` to match the participant (see `calibrations/`).

2. **Power on IMU sensors** — chest + left arm + right arm — and wait for initialization

3. **Connect Meta Quest** via Oculus Link or Air Link

4. **Press Play** in Unity — sensors connect automatically

5. **Calibrate** — press the calibrate button on the controller (or `C` on keyboard) once everything is connected. Hold arms in neutral position for 3 seconds.

### Haptic Feedback (optional)
```bash
cd WebPages/unity-plotter
python serial_api_flexible.py
```
Requires a gateway ESP32 connected via USB serial.

---

## Running a Session

The PID string encodes session parameters:

```
Format: [H/N][T/F][participant_id]
```

| Character | Value | Meaning |
|-----------|-------|---------|
| 1st | `H` | Haptics ON |
| 1st | `N` | Haptics OFF |
| 2nd | `T` | Main scene first |
| 2nd | `F` | Pablo scene first |
| rest | any | Participant ID |

Example: `HTP01` → haptics on, Main first, participant P01.

---

## Calibration Profiles (Hand Tracking)

Stored in `Control/calibrations/` as JSON files, one per participant.

To create a new profile:
```bash
python Control/src/tools/calibration_tool.py
```
To fine-tune the response curve:
```bash
python Control/src/tools/linearization_tool.py
```

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Scene doesn't load | Check that `Pablo` and `Main` are in File → Build Settings |
| Drones don't appear | Check `needToSpawn` is enabled on `LevelConfiguration` in the scene; check `dronePrefab` is assigned on `swarmModel` |
| IMU not responding | Power cycle the sensor; restart Unity if needed |
| Hand tracking not connecting | Make sure `tracker.py` is running before pressing Play |
| No input response | Check `InputFusionManager` has `TraditionalInput` assigned in Inspector |
