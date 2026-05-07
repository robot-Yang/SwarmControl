"""Capture min/max/neutral spread+height samples and write a calibration profile.

Examples:
    python -m apps.calibrate --profile Gabriel                       # rtmpose + cuda (default)
    python -m apps.calibrate --backend mediapipe --profile Gabriel   # CPU fallback
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

import cv2

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from swarm_control.calibration import CalibrationProfile, capture_point, capture_head_yaw
from swarm_control.calibration.profile import Linearization
from swarm_control.pose import build_backend


STEPS = [
    ("horizontal", "min_horizontal", "MIN HAND SPREAD",   "hands CLOSE together"),
    ("horizontal", "max_horizontal", "MAX HAND SPREAD",   "hands FAR apart"),
    ("vertical",   "min_vertical",   "MIN HEIGHT (LOW)",  "hands at LOWEST point"),
    ("vertical",   "max_vertical",   "MAX HEIGHT (HIGH)", "hands at HIGHEST point"),
    ("vertical",   "neutral_vertical", "NEUTRAL HEIGHT",  "hands at CENTER height"),
]


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Calibrate spread/height ranges for a user")
    p.add_argument("--backend", default="rtmpose", choices=["mediapipe", "rtmpose"])
    p.add_argument("--profile", required=True)
    p.add_argument("--description", default="")
    p.add_argument("--camera", type=int, default=0)
    p.add_argument("--samples", type=int, default=30)
    p.add_argument("--countdown", type=float, default=5.0)
    p.add_argument("--rtmpose-model", default="balanced",
                   choices=["lightweight", "balanced", "performance"])
    p.add_argument("--device", default="cuda", choices=["cpu", "cuda", "mps"])
    return p.parse_args()


def main() -> int:
    args = parse_args()

    if args.backend == "rtmpose":
        backend = build_backend("rtmpose", model=args.rtmpose_model, device=args.device)
    else:
        backend = build_backend("mediapipe")

    cap = cv2.VideoCapture(args.camera)
    if not cap.isOpened():
        print(f"error: cannot open camera {args.camera}")
        return 1

    # Match the tracker's lower-res / higher-FPS profile so calibration runs
    # at the same camera settings the live session will use.
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 360)
    cap.set(cv2.CAP_PROP_FPS, 60)
    print(f"[calibrate] camera negotiated: {int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))}x"
          f"{int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))} @ {cap.get(cv2.CAP_PROP_FPS):.0f} FPS")

    window = "Calibration"
    cv2.namedWindow(window, cv2.WINDOW_NORMAL)
    cv2.setWindowProperty(window, cv2.WND_PROP_FULLSCREEN, cv2.WINDOW_FULLSCREEN)

    captured: dict[str, float] = {}
    neutral_yaw_deg: float | None = None
    # Both backends now estimate head yaw via 6-point PnP — rtmpose from
    # Wholebody face landmarks, mediapipe from Face Mesh. Add the +1 capture
    # step for either. (If you add a backend without face landmarks later,
    # extend this check.)
    yaw_capture_enabled = backend.name in ("rtmpose", "mediapipe")
    total_steps = len(STEPS) + (1 if yaw_capture_enabled else 0)
    try:
        for i, (axis, key, title, instruction) in enumerate(STEPS, start=1):
            value = capture_point(
                cap, backend,
                axis=axis,
                title=title,
                instruction=instruction,
                samples=args.samples,
                countdown_seconds=args.countdown,
                window_name=window,
                progress_text=f"step {i}/{total_steps}",
            )
            if value is None:
                print("calibration cancelled")
                return 1
            captured[key] = value
            print(f"  {key} = {value:.1f} px")

        if yaw_capture_enabled:
            i = len(STEPS) + 1
            yaw = capture_head_yaw(
                cap, backend,
                title="NEUTRAL HEAD YAW",
                instruction="look straight at the screen",
                samples=args.samples,
                countdown_seconds=args.countdown,
                window_name=window,
                progress_text=f"step {i}/{total_steps}",
            )
            if yaw is None:
                print("yaw calibration cancelled — saving without yaw neutral")
            else:
                neutral_yaw_deg = yaw
                print(f"  neutral_yaw_deg = {yaw:+.2f} deg")
    finally:
        cap.release()
        cv2.destroyAllWindows()
        backend.close()

    # Auto-fix vertical inversion (screen Y=0 is the top)
    if captured["min_vertical"] > captured["max_vertical"]:
        print("auto-fix: swapping min/max vertical (screen coords)")
        captured["min_vertical"], captured["max_vertical"] = (
            captured["max_vertical"],
            captured["min_vertical"],
        )

    profile = CalibrationProfile(
        profile_name=args.profile,
        description=args.description,
        min_horizontal=captured["min_horizontal"],
        max_horizontal=captured["max_horizontal"],
        min_vertical=captured["min_vertical"],
        max_vertical=captured["max_vertical"],
        neutral_vertical=captured["neutral_vertical"],
        smooth_alpha=0.2,
        horizontal_linearization=Linearization(),
        vertical_linearization=Linearization(),
        backend=backend.name,
        neutral_yaw_deg=neutral_yaw_deg,
    )
    path = profile.save()
    print(f"saved → {path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
