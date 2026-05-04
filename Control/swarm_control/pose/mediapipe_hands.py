from __future__ import annotations

import cv2
import numpy as np

from .base import TwoHandPose


class MediaPipeHandsBackend:
    """MediaPipe Hands → TwoHandPose. Hand center = mean of 21 landmarks."""

    name = "mediapipe"

    def __init__(self, min_detection_confidence: float = 0.6, min_tracking_confidence: float = 0.6):
        import mediapipe as mp  # imported lazily so rtmpose-only installs work
        self._mp_hands = mp.solutions.hands
        self._mp_drawing = mp.solutions.drawing_utils
        self._hands = self._mp_hands.Hands(
            static_image_mode=False,
            max_num_hands=2,
            min_detection_confidence=min_detection_confidence,
            min_tracking_confidence=min_tracking_confidence,
        )

    @staticmethod
    def _hand_center_and_area(image: np.ndarray, landmarks) -> tuple[float, float, float]:
        h, w, _ = image.shape
        xs = np.array([lm.x * w for lm in landmarks.landmark])
        ys = np.array([lm.y * h for lm in landmarks.landmark])
        cx = float(xs.mean())
        cy = float(ys.mean())
        area = float((xs.max() - xs.min()) * (ys.max() - ys.min()))
        return cx, cy, area

    def detect(self, frame_bgr: np.ndarray) -> TwoHandPose:
        rgb = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2RGB)
        results = self._hands.process(rgb)

        if not results.multi_hand_landmarks or len(results.multi_hand_landmarks) < 2:
            return TwoHandPose(valid=False)

        h1, h2 = results.multi_hand_landmarks[0], results.multi_hand_landmarks[1]
        x1, y1, s1 = self._hand_center_and_area(frame_bgr, h1)
        x2, y2, s2 = self._hand_center_and_area(frame_bgr, h2)

        # Order so left_xy is the smaller-x hand (camera is mirrored upstream if desired)
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
            draw_payload=payload,
        )

    def draw(self, frame_bgr: np.ndarray, pose: TwoHandPose) -> None:
        if not pose.valid or pose.draw_payload is None:
            return
        for hand in pose.draw_payload:
            self._mp_drawing.draw_landmarks(frame_bgr, hand, self._mp_hands.HAND_CONNECTIONS)
        for xy in (pose.left_xy, pose.right_xy):
            if xy is not None:
                cv2.circle(frame_bgr, (int(xy[0]), int(xy[1])), 6, (0, 255, 0), -1)

    def close(self) -> None:
        self._hands.close()
