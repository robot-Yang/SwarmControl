"""Run the live tracker → WebSocket bridge.

Examples:
    python -m apps.tracker --profile Gabriel                       # rtmpose + cuda (default)
    python -m apps.tracker --backend mediapipe --profile Gabriel   # CPU fallback
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

import cv2

# Make `swarm_control` importable when run as a script (no install needed)
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from swarm_control.calibration import CalibrationProfile, Normalizer
from swarm_control.features import extract_two_hand_features
from swarm_control.pose import build_backend
from swarm_control.transport import WebSocketServer
from swarm_control.ui import overlay


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Hand/body → drone swarm WebSocket bridge")
    p.add_argument("--backend", default="rtmpose", choices=["mediapipe", "rtmpose"])
    p.add_argument("--profile", required=True, help="Calibration profile name (without .json)")
    p.add_argument("--camera", type=int, default=0)
    p.add_argument("--port", type=int, default=9052)
    p.add_argument("--host", default="0.0.0.0")
    # RTMPose-specific knobs
    p.add_argument("--rtmpose-model", default="balanced",
                   choices=["lightweight", "balanced", "performance"])
    p.add_argument("--device", default="cuda", choices=["cpu", "cuda", "mps"])
    p.add_argument("--no-debug-print", action="store_true")
    return p.parse_args()


def build_backend_from_args(args: argparse.Namespace):
    if args.backend == "rtmpose":
        return build_backend("rtmpose", model=args.rtmpose_model, device=args.device)
    return build_backend("mediapipe")


def main() -> int:
    args = parse_args()

    profile = CalibrationProfile.load(args.profile)
    normalizer = Normalizer(profile)
    backend = build_backend_from_args(args)
    ws = WebSocketServer(host=args.host, port=args.port)
    ws.start()

    cap = cv2.VideoCapture(args.camera)
    if not cap.isOpened():
        print(f"error: cannot open camera {args.camera}")
        return 1

    win = f"Tracker [{backend.name}] - {profile.profile_name}"
    cv2.namedWindow(win, cv2.WINDOW_NORMAL | cv2.WINDOW_KEEPRATIO)

    print(f"[tracker] backend={backend.name} profile={profile.profile_name}  press 'q' to quit")

    try:
        while True:
            ok, frame = cap.read()
            if not ok:
                break

            pose = backend.detect(frame)
            feats = extract_two_hand_features(pose)

            # Yaw is independent of hand validity. Subtract the captured neutral
            # so Unity receives a centered, degree-valued signal it can bound itself.
            yaw_relative_deg: float | None = None
            if pose.head_yaw_deg is not None:
                offset = profile.neutral_yaw_deg if profile.neutral_yaw_deg is not None else 0.0
                yaw_relative_deg = float(pose.head_yaw_deg) - float(offset)

            if feats is not None:
                backend.draw(frame, pose)
                d_norm, h_norm = normalizer.normalize(feats.distance, feats.height)

                ws.send_frame(
                    valid=True,
                    backend=backend.name,
                    left_xy=pose.left_xy,
                    right_xy=pose.right_xy,
                    distance=d_norm,
                    height=h_norm,
                    yaw=yaw_relative_deg,
                )

                yaw_label = f"yaw {yaw_relative_deg:+.1f}°" if yaw_relative_deg is not None else "yaw n/a"
                overlay.draw_status_lines(
                    frame,
                    [
                        (f"spread {d_norm:+.3f}", (255, 0, 0), 0.7),
                        (f"height {h_norm:+.3f}", (0, 255, 255), 0.7),
                        (yaw_label, (200, 100, 255), 0.7),
                        (f"raw {feats.distance:.1f}px / {feats.height:.1f}px", (200, 200, 200), 0.5),
                        (f"clients {ws.get_client_count()}", (0, 255, 0), 0.6),
                    ],
                    origin=(10, 30),
                    line_height=28,
                )

                if not args.no_debug_print:
                    yaw_str = f" yaw={yaw_relative_deg:+.1f}°" if yaw_relative_deg is not None else ""
                    print(
                        f"[{backend.name}] dist={d_norm:+.3f} height={h_norm:+.3f}{yaw_str} "
                        f"clients={ws.get_client_count()}"
                    )
            else:
                ws.send_frame(
                    valid=False, backend=backend.name,
                    left_xy=None, right_xy=None,
                    distance=None, height=None,
                    yaw=yaw_relative_deg,
                )
                overlay.draw_status_lines(
                    frame,
                    [("show both hands at similar distance", (0, 0, 255), 0.6)],
                    origin=(10, 30),
                )

            overlay.draw_status_lines(
                frame,
                [(f"profile={profile.profile_name} backend={backend.name} q=quit",
                  (200, 200, 200), 0.5)],
                origin=(10, frame.shape[0] - 12),
            )
            cv2.imshow(win, frame)
            if (cv2.waitKey(1) & 0xFF) == ord("q"):
                break
    finally:
        cap.release()
        cv2.destroyAllWindows()
        backend.close()
        ws.stop()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
