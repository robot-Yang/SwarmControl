"""Sample 10 known target positions and fit per-axis linearization curves.

Examples:
    python -m apps.linearize --backend mediapipe --profile Gabriel
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

import cv2
import numpy as np
from sklearn.metrics import r2_score

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from swarm_control.calibration import CalibrationProfile, capture_point
from swarm_control.calibration.profile import Linearization
from swarm_control.pose import build_backend


POSITIONS = [
    ("horizontal", 0.00, "MIN SPREAD",     "hands CLOSE together"),
    ("horizontal", 0.25, "25% SPREAD",     "1/4 max spread"),
    ("horizontal", 0.50, "50% SPREAD",     "half max spread"),
    ("horizontal", 0.75, "75% SPREAD",     "3/4 max spread"),
    ("horizontal", 1.00, "MAX SPREAD",     "hands FAR apart"),
    ("vertical",   0.00, "MIN HEIGHT",     "hands at LOWEST"),
    ("vertical",   0.25, "25% HEIGHT",     "low-medium"),
    ("vertical",   0.50, "50% HEIGHT",     "center"),
    ("vertical",   0.75, "75% HEIGHT",     "medium-high"),
    ("vertical",   1.00, "MAX HEIGHT",     "hands at HIGHEST"),
]


def _linear_fit(samples: list[tuple[float, float]]) -> Linearization:
    X = np.array([s[0] for s in samples])
    y = np.array([s[1] for s in samples])
    A = np.vstack([X, np.ones_like(X)]).T
    slope, intercept = np.linalg.lstsq(A, y, rcond=None)[0]
    y_pred = slope * X + intercept
    return Linearization(
        type="linear",
        slope=float(slope),
        intercept=float(intercept),
        r_squared=float(r2_score(y, y_pred)),
        max_error=float(np.max(np.abs(y - y_pred))),
        mean_error=float(np.mean(np.abs(y - y_pred))),
    )


def _poly_fit(samples: list[tuple[float, float]], degree: int = 3) -> Linearization:
    if len(samples) < degree + 1:
        return _linear_fit(samples)
    X = np.array([s[0] for s in samples])
    y = np.array([s[1] for s in samples])
    coefs_high_first = np.polyfit(X, y, degree)
    poly = np.poly1d(coefs_high_first)
    y_pred = poly(X)
    return Linearization(
        type="polynomial",
        coefficients=[float(c) for c in reversed(coefs_high_first)],
        r_squared=float(r2_score(y, y_pred)),
        max_error=float(np.max(np.abs(y - y_pred))),
        mean_error=float(np.mean(np.abs(y - y_pred))),
    )


def _choose(samples: list[tuple[float, float]], label: str) -> Linearization:
    lin = _linear_fit(samples)
    pol = _poly_fit(samples)
    print(f"  {label} linear:     R²={lin.r_squared:.4f}  mean_err={lin.mean_error:.4f}")
    print(f"  {label} polynomial: R²={pol.r_squared:.4f}  mean_err={pol.mean_error:.4f}")
    if pol.r_squared is not None and lin.r_squared is not None and pol.r_squared - lin.r_squared > 0.02:
        print(f"  → polynomial wins")
        return pol
    print(f"  → keeping linear (simpler)")
    return lin


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Fit linearization curves on top of an existing profile")
    p.add_argument("--backend", default="mediapipe", choices=["mediapipe", "rtmpose"])
    p.add_argument("--profile", required=True)
    p.add_argument("--camera", type=int, default=0)
    p.add_argument("--samples", type=int, default=30)
    p.add_argument("--countdown", type=float, default=5.0)
    p.add_argument("--rtmpose-model", default="balanced",
                   choices=["lightweight", "balanced", "performance"])
    p.add_argument("--device", default="cpu", choices=["cpu", "cuda", "mps"])
    return p.parse_args()


def main() -> int:
    args = parse_args()

    profile = CalibrationProfile.load(args.profile)
    print(f"loaded profile {profile.profile_name} (backend={profile.backend})")

    if args.backend == "rtmpose":
        backend = build_backend("rtmpose", model=args.rtmpose_model, device=args.device)
    else:
        backend = build_backend("mediapipe")

    cap = cv2.VideoCapture(args.camera)
    if not cap.isOpened():
        print(f"error: cannot open camera {args.camera}")
        return 1

    window = "Linearization"
    cv2.namedWindow(window, cv2.WINDOW_NORMAL)

    horiz: list[tuple[float, float]] = []
    vert: list[tuple[float, float]] = []
    try:
        for i, (axis, target, title, instruction) in enumerate(POSITIONS, start=1):
            raw = capture_point(
                cap, backend,
                axis=axis,
                title=f"{title}  (target {target:.0%})",
                instruction=instruction,
                samples=args.samples,
                countdown_seconds=args.countdown,
                window_name=window,
                progress_text=f"step {i}/{len(POSITIONS)}",
            )
            if raw is None:
                print("linearization cancelled")
                return 1

            # Convert raw px → normalized 0..1 (or -1..+1) using the existing profile
            if axis == "horizontal":
                span = profile.max_horizontal - profile.min_horizontal
                norm = (raw - profile.min_horizontal) / span if span > 0 else 0.0
                norm = max(0.0, min(1.0, norm))
                horiz.append((norm, target))
            else:
                if raw < profile.neutral_vertical:
                    span = profile.neutral_vertical - profile.min_vertical
                    norm = (raw - profile.neutral_vertical) / span if span > 0 else 0.0
                else:
                    span = profile.max_vertical - profile.neutral_vertical
                    norm = (raw - profile.neutral_vertical) / span if span > 0 else 0.0
                norm = max(-1.0, min(1.0, -norm))
                # Linearization fits "post-clamp normalized" → "target normalized"
                target_norm = target * 2 - 1  # 0..1 target -> -1..+1
                vert.append((norm, target_norm))
            print(f"  step {i}: raw={raw:.1f}px  norm={norm:+.3f}")
    finally:
        cap.release()
        cv2.destroyAllWindows()
        backend.close()

    print("\n=== fitting ===")
    if len(horiz) >= 2:
        profile.horizontal_linearization = _choose(horiz, "horizontal")
    if len(vert) >= 2:
        profile.vertical_linearization = _choose(vert, "vertical")

    path = profile.save()
    print(f"\nsaved → {path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
