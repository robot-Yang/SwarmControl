from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Optional, Protocol

import numpy as np


@dataclass
class TwoHandPose:
    """Backend-agnostic two-hand pose result.

    All coordinates are in pixel space of the input frame.
    `draw_payload` is opaque: only the originating backend's `draw()` reads it.

    Optional fields populated when the backend supplies them:
      • `head_yaw_deg` — head pose in degrees (RTMPose Wholebody / MediaPipe Face)
      • `shoulder_mid_xy` + `shoulder_width_px` — body anchor for body-relative
        feature extraction. When present, `extract_two_hand_features` switches
        from image-relative to body-relative measurements: hand height becomes
        wrist_y − shoulder_mid_y, and spread becomes wrist_distance ÷ shoulder
        width (dimensionless). When absent, falls back to image-relative.
    """

    valid: bool
    left_xy: Optional[tuple[float, float]] = None
    right_xy: Optional[tuple[float, float]] = None
    left_size: float = 0.0
    right_size: float = 0.0
    confidence: float = 0.0
    head_yaw_deg: Optional[float] = None
    shoulder_mid_xy: Optional[tuple[float, float]] = None
    shoulder_width_px: float = 0.0
    draw_payload: Any = field(default=None, repr=False)


class PoseBackend(Protocol):
    """Common contract every detector backend must implement."""

    name: str

    def detect(self, frame_bgr: np.ndarray) -> TwoHandPose: ...

    def draw(self, frame_bgr: np.ndarray, pose: TwoHandPose) -> None: ...

    def close(self) -> None: ...


def build_backend(name: str, **kwargs) -> PoseBackend:
    """Factory: instantiate a backend by short name. Kwargs are passed through."""
    name = name.lower()
    if name in ("mediapipe", "mp", "mediapipe_hands"):
        from .mediapipe_hands import MediaPipeHandsBackend
        return MediaPipeHandsBackend(**kwargs)
    if name in ("rtmpose", "rtm"):
        from .rtmpose import RTMPoseBackend
        return RTMPoseBackend(**kwargs)
    raise ValueError(f"Unknown pose backend: {name!r}. Try 'mediapipe' or 'rtmpose'.")
