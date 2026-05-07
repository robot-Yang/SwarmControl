# Control

Hand/body tracking → drone swarm control bridge.

A small Python pipeline that reads a webcam, infers two-hand keypoints with a
swappable pose backend (MediaPipe Hands or RTMPose), normalizes the spread and
height with a per-user calibration profile, and broadcasts the result as JSON
over a WebSocket so Unity (or `apps/simulator.py`) can consume it.

## Layout

```
Control/
├── pyproject.toml
├── requirements.txt
├── calibrations/                  # per-user JSON profiles
├── swarm_control/                 # importable package
│   ├── pose/                      #   PoseBackend: mediapipe_hands, rtmpose
│   ├── features/                  #   TwoHandPose -> (distance, height)
│   ├── calibration/               #   profile dataclass, normalizer, capture loop
│   ├── ui/                        #   shared cv2 overlay helpers
│   └── transport/                 #   WebSocket broadcast server
└── apps/                          # thin entrypoints (argparse)
    ├── tracker.py                 #   live tracker → WebSocket
    ├── calibrate.py               #   capture min/max/neutral
    ├── linearize.py               #   fit polynomial/linear correction
    └── simulator.py               #   pygame test client
```

## Install

```bash
pip install -r requirements.txt
# or, in editable mode with backend extras:
pip install -e ".[mediapipe]"        # MediaPipe only
pip install -e ".[rtmpose]"          # RTMPose only (CPU)
pip install -e ".[rtmpose-gpu]"      # RTMPose with CUDA onnxruntime
pip install -e ".[all]"              # both
```

## Daily use

Defaults are RTMPose Body on CUDA. Pass `--backend mediapipe` for the CPU fallback.

```bash
# 1) Calibrate once per user (and once per backend you plan to use)
python -m apps.calibrate --profile alex

# 2) Optional: improve linearity with 10 sample points
python -m apps.linearize --profile alex

# 3) Run the tracker
python -m apps.tracker --profile alex

# 4) Sanity-check from another terminal (no Unity needed)
python -m apps.simulator
```

All `apps/*` scripts add the repo root to `sys.path`, so they work without
`pip install` if you'd rather just clone and run.

## Wire format

The WebSocket broadcasts one JSON message per processed frame:

```json
{
  "t":        1714838400.123,
  "seq":      42,
  "valid":    true,
  "backend":  "rtmpose",
  "left":     [320.5, 240.1],
  "right":    [480.2, 245.7],
  "distance": 0.61,
  "height":  -0.12
}
```

`t` and `seq` let consumers detect dropped/stale frames and measure end-to-end
latency. `left`/`right` are raw 2D pixel keypoints (independent of backend) so
you can record sessions and re-derive features later.

## Adding a new pose backend

1. Drop a new module in `swarm_control/pose/` that exposes a class with
   `detect(frame_bgr) -> TwoHandPose`, `draw(frame_bgr, pose)`, and `close()`.
2. Add it to the factory in `swarm_control/pose/base.py`.
3. Add a `--backend yours` choice in the apps you want it in.

Nothing else changes — calibration, normalization, transport, and Unity stay
identical.
