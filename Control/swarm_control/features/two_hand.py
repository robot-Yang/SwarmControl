from __future__ import annotations

import math
from dataclasses import dataclass
from typing import Optional

from ..pose.base import TwoHandPose


@dataclass
class TwoHandFeatures:
    """Raw features computed from a two-hand pose, in pixel units."""

    distance: float       # Euclidean pixel distance between left and right
    height: float         # mean Y of the two points (smaller = higher on screen)


def extract_two_hand_features(
    pose: TwoHandPose,
    *,
    enforce_depth_similarity: bool = True,
    size_ratio_min: float = 0.5,
    size_ratio_max: float = 2.0,
) -> Optional[TwoHandFeatures]:
    """Convert a TwoHandPose into raw features. Returns None if the pose is unusable.

    The depth-similarity check (size_ratio) reproduces MediaPipe's behaviour:
    if one hand looks much bigger than the other, they are at very different
    depths, so the 2D distance is no longer a clean proxy for hand spread.

    For RTMPose `size` is forearm length; for MediaPipe it's the bbox area.
    Both scale with depth, so the same ratio test applies.
    """
    if not pose.valid or pose.left_xy is None or pose.right_xy is None:
        return None

    if enforce_depth_similarity and pose.left_size > 0 and pose.right_size > 0:
        ratio = pose.left_size / pose.right_size if pose.left_size > pose.right_size \
            else pose.right_size / pose.left_size
        if not (size_ratio_min <= ratio <= size_ratio_max):
            return None

    x1, y1 = pose.left_xy
    x2, y2 = pose.right_xy
    return TwoHandFeatures(
        distance=math.hypot(x1 - x2, y1 - y2),
        height=(y1 + y2) / 2.0,
    )
