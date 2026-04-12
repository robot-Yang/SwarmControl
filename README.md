# SwarmControl

SwarmControl is a VR research project comparing two methods of controlling a drone swarm. The study investigates how the input modality affects **task performance** and the **sense of embodiment** experienced by the operator.

---

## Conditions

| | Controller | Upper Body |
|---|---|---|
| **Hardware** | Taranis RC transmitter | Chest IMU + forearm IMUs + Meta Quest |
| **Movement** | Right stick (XZ) | Chest pitch/roll |
| **Height** | Left stick (throttle) | Forearm IMU |
| **Spread** | Right knob | Forearm IMU |
| **Camera** | Left stick (yaw) | Meta Quest headset yaw |

> Meta Quest hand tracking is used to correct IMU drift in the Upper Body condition.

![Taranis RC controller](images/taranis.jpg)
![IMU](images/imu.png)

---

## Task Path

The path is a linear obstacle course (~400 m) that systematically tests all control axes. Obstacles are ordered to isolate and then combine input modalities:

1. **Left / Right** — lateral navigation gates
2. **Spread / Contract** — obstacles requiring swarm radius adjustment
3. **Height** — vertical clearance obstacles
![IMU](images/up.png)
4. **Combined** — multi-axis obstacles requiring simultaneous control of movement, spread, and height
![IMU](images/imu.png)

---

## Measured Outcomes

**Performance**

- Task completion time
- Drones lost (obstacle collisions)
- Swarm network connectivity (proportion of drones in main connected cluster, 0–1)
- Swarm isolation events (drones disconnected per frame)

**Usability & Embodiment**

- SUS (System Usability Scale)
- Embodiment feeling (1–5 self-report)
- Likeness rating (1–5 self-report)

**Haptics** (counterbalanced factor)

- Wrist-worn ESP32 actuator nodes deliver feedback on obstacle, network, force field, and crash events
- All haptic events are time-stamped in the data log

---

## Previous Pilot Study (N = 3)

Three participants across experience levels completed both conditions. Results suggest the Upper Body condition consistently improves embodiment, while performance trends vary with expertise.

| Participant | Condition | Time (s) | Crashes | SUS | Embodiment | Likeness |
|---|---|---|---|---|---|---|
| P1 — Novice | Controller | 182.6 | 2 | 32.5 | 2/5 | 3/5 |
| | Upper Body | 147.3 | 2 | 80 | 5/5 | 5/5 |
| P2 — Intermediate | Controller | 122.5 | 3 | 65 | 1/5 | 3/5 |
| | Upper Body | 112.5 | 1 | 75 | 5/5 | 4/5 |
| P3 — Expert | Controller | 120.6 | 1 | 87.5 | 5/5 | 5/5 |
| | Upper Body | 122.5 | 2 | 32.5 | 3/5 | 1/5 |

**Key observations:**

- Upper Body yields higher embodiment scores for novice and intermediate users
- Expert users showed higher usability with the Controller, suggesting a learning curve for the body-based interface
- Completion time and crash count are comparable or better with Upper Body for non-expert users

![P1 - Novice](images/p1.png)
![P2 - Intermediate](images/p2.png)
![P3 - Expert](images/p3.png)

---

## System Architecture

```
SwarmControl/
├── SoundMapping/SoundMappingUnity/   # Unity VR application — simulation, input fusion, data logging
├── Control/                          # Python hand-tracking server (MediaPipe, WebSocket → Unity)
└── WebPages/unity-plotter/           # Haptic bridge (Unity → Python → USB → ESP32 → actuators)
```

**Data flow**

```
IMU sensors (OpenZen) ──────────────────┐
Meta Quest headset ─────────────────────┤
Taranis RC controller ──────────────────┼──▶ InputFusionManager (Unity) ──▶ SwarmModel ──▶ JSON logs
Webcam + MediaPipe ──▶ WebSocket :9052 ─┘                                              ──▶ Trajectories

Unity ──▶ Python ──▶ USB ──▶ Gateway ESP32 ──▶ ESP-NOW ──▶ Haptic nodes
```

Two parallel data streams are recorded per session:

| Stream | Rate | Contents |
|--------|------|----------|
| Swarm state | 10 Hz | Per-drone position, velocity, forces, network topology, connectivity, isolation, haptic events |
| Trajectories | 15 Hz | Per-drone position + velocity, connectivity flag, embodied flag, trial timestamps |

---

## Study Design

Sessions are counterbalanced across haptics (on/off) and scene order, encoded in the participant ID:

```
Format: [H/N][T/F][participant_id]
Example: HTP01  →  haptics on, Main scene first, participant P01
```

---

## Next Steps

Integrate full upper-body haptic feedback via a wearable haptic jacket, combining IMU-based swarm control with distributed haptic actuation across the torso — enabling closed-loop sensorimotor control of the swarm.
