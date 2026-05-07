"""Head pose estimation from RTMPose Wholebody face landmarks.

Uses the classic 6-point PnP solve (cv2.solvePnP) on a canonical 3D face model.
Six landmarks were chosen because they are rigid (don't deform with expression),
span the face well (non-coplanar — a requirement for a stable PnP), and are reliably
detected by RTMPose Wholebody.

Returned yaw is in degrees:
    0  = facing the camera
    +  = head turned to the participant's left  (image's right side leading)
    -  = head turned to the participant's right
The sign convention is verified empirically — see SIGN_FLIP_YAW below if you find
your camera produces the opposite mapping.
"""

from __future__ import annotations

from typing import Optional

import cv2
import numpy as np


# ---------------------------------------------------------------------------
# Canonical 3D model (millimeters, face-centered).
# Standard values used in OpenCV head-pose tutorials and most academic papers.
# ---------------------------------------------------------------------------
_FACE_3D_MODEL = np.array(
    [
        (   0.0,    0.0,    0.0),  # nose tip
        (   0.0,  -63.6,  -12.5),  # chin
        ( -43.3,   32.7,  -26.0),  # right eye outer corner (subject's right)
        (  43.3,   32.7,  -26.0),  # left eye outer corner  (subject's left)
        ( -28.9,  -28.9,  -24.1),  # right mouth corner
        (  28.9,  -28.9,  -24.1),  # left mouth corner
    ],
    dtype=np.float64,
)


# ---------------------------------------------------------------------------
# RTMPose Wholebody keypoint indices for our 6 PnP landmarks.
# Wholebody layout: 0-16 body, 17-22 feet, 23-90 face (iBUG-68), 91-... hands.
# So face_idx_in_wholebody = 23 + iBUG_idx.
# iBUG-68 indices for the 6 chosen points:
#     30 nose tip,  8 chin,
#     36 right-eye outer,  45 left-eye outer,
#     48 right-mouth corner,  54 left-mouth corner
# Verify on your install with `print(keypoints[WHOLEBODY_FACE_INDICES])` if the
# yaw output looks wrong — different rtmlib versions could shift these.
# ---------------------------------------------------------------------------
WHOLEBODY_FACE_INDICES = (
    23 + 30,  # nose tip            → 53
    23 +  8,  # chin                → 31
    23 + 36,  # right eye outer     → 59
    23 + 45,  # left eye outer      → 68
    23 + 48,  # right mouth corner  → 71
    23 + 54,  # left mouth corner   → 77
)


# Flip if your camera setup gives reversed yaw. The PnP solve itself is unambiguous;
# the sign you "want" depends on whether the webcam frame is mirrored before display
# and which way the participant expects head-turn → camera-pan to feel.
SIGN_FLIP_YAW = False


def _camera_matrix(width: int, height: int) -> np.ndarray:
    """Approximate intrinsics when no per-camera calibration is available.
    Focal length ≈ image width is a good rule of thumb for a typical webcam at
    ~50° horizontal FoV; off by 10-20% rarely matters for yaw.
    """
    f = float(width)
    return np.array(
        [[f, 0.0, width / 2.0],
         [0.0, f, height / 2.0],
         [0.0, 0.0, 1.0]],
        dtype=np.float64,
    )


def estimate_head_yaw_deg(
    keypoints: np.ndarray,
    scores: np.ndarray,
    frame_shape: tuple[int, int, int],
    *,
    min_confidence: float = 0.3,
) -> Optional[float]:
    """Compute head yaw in degrees from a Wholebody keypoint set.

    Returns None if any of the 6 PnP landmarks fall below `min_confidence`,
    or if the PnP solve fails (very rare).
    """
    if keypoints is None or len(keypoints) <= max(WHOLEBODY_FACE_INDICES):
        return None

    pts2d = keypoints[list(WHOLEBODY_FACE_INDICES)]
    confs = scores[list(WHOLEBODY_FACE_INDICES)]
    if np.any(confs < min_confidence):
        return None

    h, w = frame_shape[:2]
    cam = _camera_matrix(w, h)
    dist = np.zeros(4, dtype=np.float64)

    ok, rvec, _tvec = cv2.solvePnP(
        _FACE_3D_MODEL,
        pts2d.astype(np.float64),
        cam,
        dist,
        flags=cv2.SOLVEPNP_ITERATIVE,
    )
    if not ok:
        return None

    R, _ = cv2.Rodrigues(rvec)
    yaw_rad = float(np.arctan2(R[1, 0], R[0, 0]))
    yaw_deg = float(np.degrees(yaw_rad))
    return -yaw_deg if SIGN_FLIP_YAW else yaw_deg
