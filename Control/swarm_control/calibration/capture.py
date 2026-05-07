"""Shared capture loop used by calibrate and linearize tools.

Decouples camera/UI/countdown plumbing from the choice of pose backend
and the choice of which feature is being sampled.
"""

from __future__ import annotations

import time
from typing import Callable, Optional

import cv2
import numpy as np

from ..features.two_hand import extract_two_hand_features
from ..pose.base import PoseBackend, TwoHandPose
from ..ui import overlay


def _selector_distance(features) -> float:
    return features.distance


def _selector_height(features) -> float:
    return features.height


SELECTORS = {"horizontal": _selector_distance, "vertical": _selector_height}


def capture_point(
    cap: cv2.VideoCapture,
    backend: PoseBackend,
    *,
    axis: str,
    title: str,
    instruction: str,
    samples: int = 30,
    countdown_seconds: float = 5.0,
    window_name: str = "Capture",
    progress_text: str = "",
) -> Optional[float]:
    """Run countdown → capture → return median raw value for one axis.

    Returns None if the user pressed 'q' to abort.
    """
    if axis not in SELECTORS:
        raise ValueError(f"axis must be one of {list(SELECTORS)}; got {axis!r}")
    select = SELECTORS[axis]

    countdown_start: Optional[float] = None
    capturing = False
    captured: list[float] = []

    while True:
        ok, frame = cap.read()
        if not ok:
            return None

        pose = backend.detect(frame)
        feats = extract_two_hand_features(pose)

        overlay.draw_status_lines(
            frame,
            [(title, (0, 255, 255), 1.0), (instruction, (255, 255, 255), 0.65)],
            origin=(20, 50),
        )
        if progress_text:
            overlay.draw_status_lines(
                frame,
                [(progress_text, (255, 255, 255), 0.7)],
                origin=(frame.shape[1] - 220, 30),
            )

        if feats is not None:
            backend.draw(frame, pose)
            current_raw = select(feats)

            overlay.draw_status_lines(
                frame,
                [(f"raw {axis}: {current_raw:.1f} px", (200, 200, 200), 0.6)],
                origin=(20, 130),
            )

            if not capturing:
                if countdown_start is None:
                    countdown_start = time.time()
                elapsed = time.time() - countdown_start
                remaining = countdown_seconds - elapsed
                if remaining <= 0:
                    capturing = True
                else:
                    overlay.draw_countdown(frame, remaining, countdown_seconds)
            else:
                captured.append(current_raw)
                overlay.draw_capturing(frame, len(captured), samples)
                if len(captured) >= samples:
                    cv2.imshow(window_name, frame)
                    cv2.waitKey(1)
                    return float(np.median(captured))
        else:
            countdown_start = None
            capturing = False
            captured.clear()
            overlay.draw_show_hands_warning(frame)

        overlay.draw_status_lines(
            frame,
            [("press 'q' to quit", (200, 200, 200), 0.55)],
            origin=(20, frame.shape[0] - 20),
        )

        cv2.imshow(window_name, frame)
        if (cv2.waitKey(1) & 0xFF) == ord("q"):
            return None


def capture_head_yaw(
    cap: cv2.VideoCapture,
    backend: PoseBackend,
    *,
    title: str = "NEUTRAL HEAD YAW",
    instruction: str = "look straight at the screen",
    samples: int = 30,
    countdown_seconds: float = 5.0,
    window_name: str = "Capture",
    progress_text: str = "",
) -> Optional[float]:
    """Run countdown → capture → return median head yaw in degrees.

    Mirrors `capture_point` but reads `TwoHandPose.head_yaw_deg`. Returns None
    if the user pressed 'q', or if the backend never reported a yaw value
    (e.g. MediaPipe Hands, which has no face landmarks).
    """
    countdown_start: Optional[float] = None
    capturing = False
    captured: list[float] = []

    while True:
        ok, frame = cap.read()
        if not ok:
            return None

        pose: TwoHandPose = backend.detect(frame)

        overlay.draw_status_lines(
            frame,
            [(title, (0, 255, 255), 1.0), (instruction, (255, 255, 255), 0.65)],
            origin=(20, 50),
        )
        if progress_text:
            overlay.draw_status_lines(
                frame,
                [(progress_text, (255, 255, 255), 0.7)],
                origin=(frame.shape[1] - 220, 30),
            )

        if pose.head_yaw_deg is not None:
            backend.draw(frame, pose)
            current_yaw = float(pose.head_yaw_deg)

            overlay.draw_status_lines(
                frame,
                [(f"raw yaw: {current_yaw:+.1f} deg", (200, 200, 200), 0.6)],
                origin=(20, 130),
            )

            if not capturing:
                if countdown_start is None:
                    countdown_start = time.time()
                elapsed = time.time() - countdown_start
                remaining = countdown_seconds - elapsed
                if remaining <= 0:
                    capturing = True
                else:
                    overlay.draw_countdown(frame, remaining, countdown_seconds)
            else:
                captured.append(current_yaw)
                overlay.draw_capturing(frame, len(captured), samples)
                if len(captured) >= samples:
                    cv2.imshow(window_name, frame)
                    cv2.waitKey(1)
                    return float(np.median(captured))
        else:
            countdown_start = None
            capturing = False
            captured.clear()
            overlay.draw_show_hands_warning(frame)

        overlay.draw_status_lines(
            frame,
            [("press 'q' to quit", (200, 200, 200), 0.55)],
            origin=(20, frame.shape[0] - 20),
        )

        cv2.imshow(window_name, frame)
        if (cv2.waitKey(1) & 0xFF) == ord("q"):
            return None
