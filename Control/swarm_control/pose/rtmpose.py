from __future__ import annotations

import os
import sys
from pathlib import Path
from typing import Optional

import cv2
import numpy as np

from .base import TwoHandPose
from .head_pose import estimate_head_yaw_deg


def _register_bundled_cuda_dlls() -> None:
    """Make pip-installed CUDA/cuDNN DLLs visible to onnxruntime on Windows.

    Packages like ``nvidia-cublas-cu12`` and ``nvidia-cudnn-cu12`` put DLLs in
    ``.venv/Lib/site-packages/nvidia/<pkg>/bin/``. Windows' default DLL loader
    doesn't search there, so onnxruntime fails to load the CUDA provider with
    a "cublasLt64_12.dll missing" error and silently falls back to CPU.

    We register each ``nvidia/*/bin`` directory two ways before importing
    rtmlib (which transitively imports onnxruntime):
      1. ``os.add_dll_directory`` — covers direct LoadLibrary calls.
      2. Prepend to ``PATH`` — covers transitive DLL imports
         (``onnxruntime_providers_cuda.dll`` → ``cublasLt64_12.dll``), which
         the user-directory mechanism doesn't reliably catch.
    """
    if sys.platform != "win32":
        return
    site_nvidia = Path(sys.prefix) / "Lib" / "site-packages" / "nvidia"
    if not site_nvidia.is_dir():
        return
    bin_dirs: list[str] = []
    for pkg in site_nvidia.iterdir():
        bin_dir = pkg / "bin"
        if bin_dir.is_dir():
            bin_dirs.append(str(bin_dir))
            try:
                os.add_dll_directory(str(bin_dir))
            except OSError:
                pass
    if bin_dirs:
        os.environ["PATH"] = os.pathsep.join(bin_dirs) + os.pathsep + os.environ.get("PATH", "")


_register_bundled_cuda_dlls()

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

    def _shoulder_anchor(self, kp: np.ndarray, sc: np.ndarray) -> tuple[Optional[tuple[float, float]], float]:
        """Return ((mid_x, mid_y), shoulder_width_px) or (None, 0.0) if shoulders
        aren't confidently detected. Used as the body anchor for body-relative
        feature extraction downstream.
        """
        if sc[_COCO_LEFT_SHOULDER] < self._min_conf or sc[_COCO_RIGHT_SHOULDER] < self._min_conf:
            return None, 0.0
        lx, ly = float(kp[_COCO_LEFT_SHOULDER, 0]), float(kp[_COCO_LEFT_SHOULDER, 1])
        rx, ry = float(kp[_COCO_RIGHT_SHOULDER, 0]), float(kp[_COCO_RIGHT_SHOULDER, 1])
        mid = ((lx + rx) / 2.0, (ly + ry) / 2.0)
        width = float(np.hypot(lx - rx, ly - ry))
        return mid, width

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

        # Head yaw and shoulder anchor are independent of wrist availability,
        # so compute them eagerly and attach to whichever TwoHandPose we return.
        head_yaw_deg = estimate_head_yaw_deg(kp, sc, frame_bgr.shape, min_confidence=self._min_conf)
        shoulder_mid_xy, shoulder_width_px = self._shoulder_anchor(kp, sc)

        s_lw = float(sc[_COCO_LEFT_WRIST])
        s_rw = float(sc[_COCO_RIGHT_WRIST])
        if s_lw < self._min_conf or s_rw < self._min_conf:
            return TwoHandPose(
                valid=False,
                head_yaw_deg=head_yaw_deg,
                shoulder_mid_xy=shoulder_mid_xy,
                shoulder_width_px=shoulder_width_px,
                draw_payload=(kp, sc),
            )

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
            shoulder_mid_xy=shoulder_mid_xy,
            shoulder_width_px=shoulder_width_px,
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
