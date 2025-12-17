# Upper Body Based Swarm Control

## Overview

This project implements upper body-based swarm control using three input modalities:
- **IMU** - Forward/backward and left/right movement
- **VR Headset** - Visualization and camera control
- **Hand Pose** - Swarm spread and height control

---

## 1. Forward/Backward and Left/Right Movement

**Technology:** IMU sensor with OpenZen Unity plugin

**Location:** `Assets/_Scripts/_Controls/InputSources/SwarmPosition/`

### Key Components:
- **IMUMovementInputBase** - Abstract class that defines the input format
- **IMUMovementSelector** - Selects control type (rate-based, linear, etc.)
- **OpenZenMoveObject** - OpenZen-provided file that reads raw IMU values
- **IMUMovementRateBased** - Currently used mode for rate-based control (located in `Modes/` folder)

**Data Flow:** Raw IMU values → IMUMovementRateBased → MigrationPointController

---

## 2. Camera Control

**Technology:** Meta Quest headset yaw angle

**Location:** `Assets/_Scripts/_Controls/InputSources/SwarmCamera/`

### Key Components:
- **MetaQuestInput** - Reads raw yaw value from the headset
- **IMUYawInput** - Processes the yaw value
- **CameraMovement** - Uses the processed output value for camera control

---

## 3. Spread and Height Control

**Technology:** MediaPipe hand pose estimation

**Location:** `Control/`

### MediaPipe Files:
- **Calibration files** - Stored in `Control/calibrations/` folder
  - Contains min/max spread and height values per user
  - Generate using: `Control/src/tools/calibration_tool.py`
- **Tracker.py** - Main file that:
  - Uses calibration profile specified in `CALIBRATION_PROFILE = "..."`
  - Opens WebSocket connection
  - Automatically connects to Unity when game is running
  - Feeds values to Unity control files

### Unity Control Files:
- **Location:** `Assets/_Scripts/_Controls/InputSources/SwarmSpread/` and `Assets/_Scripts/_Controls/InputSources/SwarmHeight/`
- **MediaPipeSpreadInput** - Processes spread values from WebSocket
- **MediaPipeHeightInput** - Processes height values from WebSocket

**Data Flow:** MediaPipe hand tracking → WebSocket → Unity control files → MigrationPointController

---

## How to Run

### Startup Sequence:
1. **Start hand tracking:** 
   ```bash
   python Control/tracker.py
   ```
   - WebSocket server will start (default connection details in script)
   - Position yourself in front of the webcam

2. **Power on IMU sensor**
   - Wait for initialization

3. **Connect Meta Quest to laptop**
   - Use Oculus Link/Air Link
   - Verify you see the gray passthrough screen and Unity app is detected

4. **Launch Unity Scene**
   - Press Play in Unity Editor
   - All systems should automatically connect

### Tips:
- **Hand Tracking:** Use fist gestures instead of flat hands - MediaPipe detects them more reliably. Recognition parameters can be tuned in `tracker.py`
- **If the game doesn't load:** Close Unity and restart. If issues persist, restart your laptop to reset all connections.

---

## Project Structure

```
SwarmControl/
├── Control/                    # Python-based hand tracking system
│   ├── tracker.py             # Main hand tracking script (START HERE)
│   ├── calibrations/          # User calibration profiles
│   └── src/                   # Hand detection and WebSocket server
│
├── SoundMappingUnity/         # Unity VR application
│   └── Assets/_Scripts/       
│       └── _Controls/InputSources/
│           ├── SwarmPosition/ # IMU movement control
│           ├── SwarmCamera/   # VR headset camera control
│           ├── SwarmSpread/   # Hand spread control
│           └── SwarmHeight/   # Hand height control
│
├── Analysis/                  # Trajectory analysis tools
│   ├── Trajectories/         # Recorded trajectory data
│   └── generate_all_plots_v3.py
│
└── WebPages/                  # ESP32/haptic feedback system
    └── unity-plotter/        # Communication with haptic devices
```