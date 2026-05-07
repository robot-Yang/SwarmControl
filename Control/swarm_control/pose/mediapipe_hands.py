from __future__ import annotations

import time
import urllib.request
from pathlib import Path
from typing import Optional

import cv2
import numpy as np

from .base import TwoHandPose
from .head_pose import solve_yaw_from_pts2d


# MediaPipe Face Mesh canonical-468 indices for our 6 PnP landmarks.
# Order matches `solve_yaw_from_pts2d` exactly:
#   nose tip, chin, right-eye outer, left-eye outer,
#   right-mouth corner, left-mouth corner
# "right"/"left" are from the *subject's* perspective.
MEDIAPIPE_FACE_INDICES = (
      1,  # nose tip
    152,  # chin
     33,  # right eye outer corner (subject's right)
    263,  # left eye outer corner  (subject's left)
     61,  # right mouth corner
    291,  # left mouth corner
)


# Model URLs (Google's official MediaPipe Tasks model zoo). These are stable
# float16 versions; if they ever move, update the URLs and bump the cache.
_HAND_MODEL_URL = (
    "https://storage.googleapis.com/mediapipe-models/hand_landmarker/"
    "hand_landmarker/float16/1/hand_landmarker.task"
)
_FACE_MODEL_URL = (
    "https://storage.googleapis.com/mediapipe-models/face_landmarker/"
    "face_landmarker/float16/1/face_landmarker.task"
)
# Pose Landmarker (BlazePose 33-keypoint, lite variant — ~9 MB, fast).
# Used only for the shoulder anchor; we don't need full-body kinematic accuracy.
_POSE_MODEL_URL = (
    "https://storage.googleapis.com/mediapipe-models/pose_landmarker/"
    "pose_landmarker_lite/float16/1/pose_landmarker_lite.task"
)
_MODEL_CACHE_DIR = Path.home() / ".cache" / "swarm_control" / "mediapipe"

# BlazePose landmark indices for shoulders. Same in both the lite and full
# variants of pose_landmarker.task.
_BLAZEPOSE_LEFT_SHOULDER = 11
_BLAZEPOSE_RIGHT_SHOULDER = 12

# Shoulder visibility floor — BlazePose returns a `visibility` float per
# landmark. Below this we treat the shoulder as not detected and the
# downstream pose result falls back to image-relative measurements.
_SHOULDER_MIN_VISIBILITY = 0.5

# MediaPipe Hands landmark indices for a stable "palm center" — wrist and the
# 4 metacarpal-phalangeal knuckles. Averaging these gives a point that doesn't
# drift when fingers flex (unlike mean-of-21, which moves with finger motion).
_PALM_CENTER_INDICES = (0, 5, 9, 13, 17)


def _ensure_model(url: str, filename: str) -> str:
    """Download a MediaPipe `.task` file to the user cache on first use, then
    reuse the cached copy. Returns the absolute path."""
    _MODEL_CACHE_DIR.mkdir(parents=True, exist_ok=True)
    target = _MODEL_CACHE_DIR / filename
    if not target.exists():
        print(f"[mediapipe] downloading {filename} → {target} ...")
        urllib.request.urlretrieve(url, str(target))
        print(f"[mediapipe] downloaded {target.stat().st_size / 1e6:.1f} MB")
    return str(target)


# Hand connection list (the 21-landmark skeleton). Used by draw().
# Same topology as the legacy mp.solutions.hands.HAND_CONNECTIONS.
_HAND_CONNECTIONS: tuple[tuple[int, int], ...] = (
    (0, 1), (1, 2), (2, 3), (3, 4),           # thumb
    (0, 5), (5, 6), (6, 7), (7, 8),           # index
    (5, 9), (9, 10), (10, 11), (11, 12),      # middle
    (9, 13), (13, 14), (14, 15), (15, 16),    # ring
    (13, 17), (17, 18), (18, 19), (19, 20),   # pinky
    (0, 17),                                   # palm closer
)


class MediaPipeHandsBackend:
    """MediaPipe Tasks API → TwoHandPose. Hand center = mean of 21 landmarks.

    Uses MediaPipe Tasks (the post-0.11 API) for both hands and face. Face Mesh
    runs in parallel to populate `head_yaw_deg` via the same 6-point PnP solver
    used by RTMPose. Wire format matches across backends — Unity's PoseYawInput
    works identically with either Python tracker.
    """

    name = "mediapipe"

    def __init__(
        self,
        min_detection_confidence: float = 0.6,
        min_tracking_confidence: float = 0.6,
        enable_head_pose: bool = True,
    ):
        # Lazy import so rtmpose-only installs work.
        import mediapipe as mp
        from mediapipe.tasks import python as mp_python
        from mediapipe.tasks.python import vision as mp_vision

        self._mp = mp
        self._mp_image = mp.Image  # convenience handle for detect()

        # Hand Landmarker — VIDEO mode produces tracking continuity across frames.
        hand_path = _ensure_model(_HAND_MODEL_URL, "hand_landmarker.task")
        hand_options = mp_vision.HandLandmarkerOptions(
            base_options=mp_python.BaseOptions(model_asset_path=hand_path),
            num_hands=2,
            min_hand_detection_confidence=min_detection_confidence,
            min_hand_presence_confidence=min_detection_confidence,
            min_tracking_confidence=min_tracking_confidence,
            running_mode=mp_vision.RunningMode.VIDEO,
        )
        self._hands = mp_vision.HandLandmarker.create_from_options(hand_options)

        # Face Landmarker — only created when head pose is enabled.
        self._face = None
        if enable_head_pose:
            face_path = _ensure_model(_FACE_MODEL_URL, "face_landmarker.task")
            face_options = mp_vision.FaceLandmarkerOptions(
                base_options=mp_python.BaseOptions(model_asset_path=face_path),
                num_faces=1,
                min_face_detection_confidence=min_detection_confidence,
                min_face_presence_confidence=min_detection_confidence,
                min_tracking_confidence=min_tracking_confidence,
                output_face_blendshapes=False,
                output_facial_transformation_matrixes=False,
                running_mode=mp_vision.RunningMode.VIDEO,
            )
            self._face = mp_vision.FaceLandmarker.create_from_options(face_options)

        # Pose Landmarker — provides the shoulder anchor for body-relative
        # feature extraction. Lite preset is plenty for shoulders only.
        pose_path = _ensure_model(_POSE_MODEL_URL, "pose_landmarker_lite.task")
        pose_options = mp_vision.PoseLandmarkerOptions(
            base_options=mp_python.BaseOptions(model_asset_path=pose_path),
            num_poses=1,
            min_pose_detection_confidence=min_detection_confidence,
            min_pose_presence_confidence=min_detection_confidence,
            min_tracking_confidence=min_tracking_confidence,
            output_segmentation_masks=False,
            running_mode=mp_vision.RunningMode.VIDEO,
        )
        self._pose = mp_vision.PoseLandmarker.create_from_options(pose_options)

        # MediaPipe Tasks VIDEO mode requires monotonically increasing timestamps
        # in milliseconds. Time.monotonic gives us a stable clock.
        self._t0 = time.monotonic()

    def _now_ms(self) -> int:
        return int((time.monotonic() - self._t0) * 1000)

    @staticmethod
    def _hand_center_and_area(
        image_shape: tuple[int, int, int],
        landmarks,
    ) -> tuple[float, float, float]:
        """Center = mean of wrist + 4 MCP knuckles (palm-stable, ignores fingers).
        Area = bbox of all 21 landmarks (still used as the depth proxy).
        """
        h, w, _ = image_shape
        # All-21 bbox for area (depth proxy).
        all_x = np.array([lm.x * w for lm in landmarks])
        all_y = np.array([lm.y * h for lm in landmarks])
        area = float((all_x.max() - all_x.min()) * (all_y.max() - all_y.min()))
        # Palm-center for position.
        palm_x = np.array([landmarks[i].x * w for i in _PALM_CENTER_INDICES])
        palm_y = np.array([landmarks[i].y * h for i in _PALM_CENTER_INDICES])
        cx = float(palm_x.mean())
        cy = float(palm_y.mean())
        return cx, cy, area

    def _estimate_head_yaw(self, mp_image, frame_shape: tuple[int, int, int]) -> Optional[float]:
        """Run Face Landmarker → 6-point PnP → yaw degrees. None if no face."""
        if self._face is None:
            return None
        result = self._face.detect_for_video(mp_image, self._now_ms())
        if not result.face_landmarks:
            return None

        h, w, _ = frame_shape
        face = result.face_landmarks[0]
        pts2d = np.array(
            [(face[i].x * w, face[i].y * h) for i in MEDIAPIPE_FACE_INDICES],
            dtype=np.float64,
        )
        return solve_yaw_from_pts2d(pts2d, frame_shape)

    def _shoulder_anchor(
        self, mp_image, frame_shape: tuple[int, int, int]
    ) -> tuple[Optional[tuple[float, float]], float]:
        """Run Pose Landmarker → return (shoulder midpoint pixels, shoulder
        width pixels). Returns (None, 0.0) if no pose or shoulders are below
        the visibility threshold.
        """
        result = self._pose.detect_for_video(mp_image, self._now_ms())
        if not result.pose_landmarks:
            return None, 0.0
        landmarks = result.pose_landmarks[0]
        l = landmarks[_BLAZEPOSE_LEFT_SHOULDER]
        r = landmarks[_BLAZEPOSE_RIGHT_SHOULDER]
        if l.visibility < _SHOULDER_MIN_VISIBILITY or r.visibility < _SHOULDER_MIN_VISIBILITY:
            return None, 0.0

        h, w, _ = frame_shape
        lx, ly = l.x * w, l.y * h
        rx, ry = r.x * w, r.y * h
        mid = ((lx + rx) / 2.0, (ly + ry) / 2.0)
        width = float(np.hypot(lx - rx, ly - ry))
        return mid, width

    def detect(self, frame_bgr: np.ndarray) -> TwoHandPose:
        rgb = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2RGB)
        mp_image = self._mp_image(image_format=self._mp.ImageFormat.SRGB, data=rgb)

        # Head yaw and shoulder anchor are independent of hand availability —
        # compute them eagerly so they're attached to whichever pose we return.
        head_yaw_deg = self._estimate_head_yaw(mp_image, frame_bgr.shape)
        shoulder_mid_xy, shoulder_width_px = self._shoulder_anchor(mp_image, frame_bgr.shape)

        result = self._hands.detect_for_video(mp_image, self._now_ms())
        hands = result.hand_landmarks  # list of lists of NormalizedLandmark

        if len(hands) < 2:
            return TwoHandPose(
                valid=False,
                head_yaw_deg=head_yaw_deg,
                shoulder_mid_xy=shoulder_mid_xy,
                shoulder_width_px=shoulder_width_px,
            )

        h1, h2 = hands[0], hands[1]
        x1, y1, s1 = self._hand_center_and_area(frame_bgr.shape, h1)
        x2, y2, s2 = self._hand_center_and_area(frame_bgr.shape, h2)

        # Order so left_xy is the smaller-x hand.
        if x1 <= x2:
            left, right = (x1, y1), (x2, y2)
            left_size, right_size = s1, s2
            payload = (h1, h2)
        else:
            left, right = (x2, y2), (x1, y1)
            left_size, right_size = s2, s1
            payload = (h2, h1)

        return TwoHandPose(
            valid=True,
            left_xy=left,
            right_xy=right,
            left_size=left_size,
            right_size=right_size,
            confidence=1.0,
            head_yaw_deg=head_yaw_deg,
            shoulder_mid_xy=shoulder_mid_xy,
            shoulder_width_px=shoulder_width_px,
            draw_payload=payload,
        )

    def draw(self, frame_bgr: np.ndarray, pose: TwoHandPose) -> None:
        if not pose.valid or pose.draw_payload is None:
            return
        h, w, _ = frame_bgr.shape
        # Tasks API result has no built-in drawing helper that's binary-compatible
        # with the legacy mp.solutions.drawing_utils — render manually.
        for hand in pose.draw_payload:
            pts = [(int(lm.x * w), int(lm.y * h)) for lm in hand]
            for a, b in _HAND_CONNECTIONS:
                cv2.line(frame_bgr, pts[a], pts[b], (200, 200, 200), 1)
            for p in pts:
                cv2.circle(frame_bgr, p, 3, (0, 200, 255), -1)
        for xy in (pose.left_xy, pose.right_xy):
            if xy is not None:
                cv2.circle(frame_bgr, (int(xy[0]), int(xy[1])), 6, (0, 255, 0), -1)

    def close(self) -> None:
        self._hands.close()
        if self._face is not None:
            self._face.close()
        self._pose.close()
