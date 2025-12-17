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