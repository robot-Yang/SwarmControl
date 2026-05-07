from __future__ import annotations

import cv2
import numpy as np

from .base import TwoHandPose
from .head_pose import estimate_head_yaw_deg

# Indices 0..16 of Wholebody match COCO-17 body keypoints exactly,
# so the wrist/elbow/shoulder indices are unchanged from when this used `Body`.
_COCO_LEFT_WRIST = 9
_COCO_RIGHT_WRIST = 10
_COCO_LEFT_ELBOW = 7
_COCO_RIGHT_ELBOW = 8
_COCO_LEFT_SHOULDER = 5
_COCO_RIGHT_SHOULDER = 6


class RTMPoseBackend:
    """RTMPose Wholebody backend (via rtmlib) → TwoHandPose with wrist keypoints
    and head yaw.

    Wholebody returns 133 keypoints: 0-16 body (COCO-17), 17-22 feet, 23-90 face
    (iBUG-68), 91-111 left hand, 112-132 right hand. We use:
      • 9, 10 (wrists) for two-hand spread/height
      • 7, 8 (elbows) for forearm size proxy (depth check)
      • 23 + {30, 8, 36, 45, 48, 54} (six face landmarks) for head yaw via PnP

    Args:
        model: 'lightweight' | 'balanced' | 'performance'  (rtmlib presets)
        device: 'cpu' | 'cuda' | 'mps'
        backend: onnxruntime backend, e.g. 'onnxruntime'
        min_confidence: per-keypoint confidence required to mark a wrist valid
    """

    name = "rtmpose"

    def __init__(
        self,
        model: str = "performance",
        device: str = "cuda",
        backend: str = "onnxruntime",
        min_confidence: float = 0.3,
    ):
        from rtmlib import Wholebody  # lazy import so mediapipe-only installs work
        self._body = Wholebody(mode=model, to_openpose=False, backend=backend, device=device)
        self._min_conf = float(min_confidence)
        self._last_keypoints: np.ndarray | None = None
        self._last_scores: np.ndarray | None = None

    def _pick_person(self, keypoints: np.ndarray, scores: np.ndarray) -> tuple[np.ndarray, np.ndarray] | None:
        """Pick the person with the most confident wrist visibility."""
        if keypoints is None or len(keypoints) == 0:
            return None
        best_idx = -1
        best_score = -1.0
        for i in range(len(keypoints)):
            s_lw = scores[i, _COCO_LEFT_WRIST]
            s_rw = scores[i, _COCO_RIGHT_WRIST]
            combined = s_lw + s_rw
            if combined > best_score:
                best_score = combined
                best_idx = i
        if best_idx < 0:
            return None
        return keypoints[best_idx], scores[best_idx]

    def detect(self, frame_bgr: np.ndarray) -> TwoHandPose:
        keypoints, scores = self._body(frame_bgr)
        keypoints = np.asarray(keypoints)
        scores = np.asarray(scores)

        picked = self._pick_person(keypoints, scores)
        self._last_keypoints = None
        self._last_scores = None
        if picked is None:
            return TwoHandPose(valid=False)

        kp, sc = picked
        self._last_keypoints, self._last_scores = kp, sc

        # Head yaw is independent of wrist availability, so compute it eagerly
        # and attach to whichever TwoHandPose we end up returning.
        head_yaw_deg = estimate_head_yaw_deg(kp, sc, frame_bgr.shape, min_confidence=self._min_conf)

        s_lw = float(sc[_COCO_LEFT_WRIST])
        s_rw = float(sc[_COCO_RIGHT_WRIST])
        if s_lw < self._min_conf or s_rw < self._min_conf:
            return TwoHandPose(valid=False, head_yaw_deg=head_yaw_deg, draw_payload=(kp, sc))

        left_xy = (float(kp[_COCO_LEFT_WRIST, 0]), float(kp[_COCO_LEFT_WRIST, 1]))
        right_xy = (float(kp[_COCO_RIGHT_WRIST, 0]), float(kp[_COCO_RIGHT_WRIST, 1]))

        # Forearm length as size proxy (elbow → wrist). Scales with distance from camera.
        def _forearm(elbow_idx: int, wrist_idx: int) -> float:
            if sc[elbow_idx] < self._min_conf:
                return 0.0
            dx = kp[wrist_idx, 0] - kp[elbow_idx, 0]
            dy = kp[wrist_idx, 1] - kp[elbow_idx, 1]
            return float(np.hypot(dx, dy))

        return TwoHandPose(
            valid=True,
            left_xy=left_xy,
            right_xy=right_xy,
            left_size=_forearm(_COCO_LEFT_ELBOW, _COCO_LEFT_WRIST),
            right_size=_forearm(_COCO_RIGHT_ELBOW, _COCO_RIGHT_WRIST),
            confidence=min(s_lw, s_rw),
            head_yaw_deg=head_yaw_deg,
            draw_payload=(kp, sc),
        )

    def draw(self, frame_bgr: np.ndarray, pose: TwoHandPose) -> None:
        if pose.draw_payload is None:
            return
        kp, sc = pose.draw_payload

        # Skeleton edges (COCO-17 upper-body subset is what we care about for two-hand control)
        edges = [
            (_COCO_LEFT_SHOULDER, _COCO_LEFT_ELBOW),
            (_COCO_LEFT_ELBOW, _COCO_LEFT_WRIST),
            (_COCO_RIGHT_SHOULDER, _COCO_RIGHT_ELBOW),
            (_COCO_RIGHT_ELBOW, _COCO_RIGHT_WRIST),
            (_COCO_LEFT_SHOULDER, _COCO_RIGHT_SHOULDER),
        ]
        for a, b in edges:
            if sc[a] >= self._min_conf and sc[b] >= self._min_conf:
                pa = (int(kp[a, 0]), int(kp[a, 1]))
                pb = (int(kp[b, 0]), int(kp[b, 1]))
                cv2.line(frame_bgr, pa, pb, (200, 200, 200), 2)

        for i in range(len(kp)):
            if sc[i] < self._min_conf:
                continue
            color = (0, 255, 0) if i in (_COCO_LEFT_WRIST, _COCO_RIGHT_WRIST) else (0, 200, 255)
            cv2.circle(frame_bgr, (int(kp[i, 0]), int(kp[i, 1])), 4, color, -1)

        if pose.valid:
            for xy in (pose.left_xy, pose.right_xy):
                if xy is not None:
                    cv2.circle(frame_bgr, (int(xy[0]), int(xy[1])), 8, (0, 255, 0), 2)

    def close(self) -> None:
        # rtmlib has no explicit close; sessions are released on GC
        self._body = None
