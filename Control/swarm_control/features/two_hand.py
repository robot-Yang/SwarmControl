from __future__ import annotations

import math
from dataclasses import dataclass
from typing import Optional

from ..pose.base import TwoHandPose


@dataclass
class TwoHandFeatures:
    """Raw features computed from a two-hand pose.

    Units depend on whether the originating pose carried a shoulder anchor:
      • body-relative mode (anchor present) — both `distance` and `height` are
        **dimensionless multiples of shoulder width**. `distance` = wrist
        spread / shoulder width. `height` = (mean wrist Y − shoulder mid Y) /
        shoulder width, signed (negative = wrists above shoulders). Camera-
        distance-invariant.
      • image-relative mode (no anchor) — `distance` is pixel distance,
        `height` is mean wrist Y in pixel space (larger = lower on screen).
    """

    distance: float
    height: float


def extract_two_hand_features(
    pose: TwoHandPose,
    *,
    enforce_depth_similarity: bool = True,
    size_ratio_min: float = 0.5,
    size_ratio_max: float = 2.0,
    min_confidence: float = 0.0,
) -> Optional[TwoHandFeatures]:
    """Convert a TwoHandPose into raw features. Returns None if the pose is unusable.

    The depth-similarity check (size_ratio) reproduces MediaPipe's behaviour:
    if one hand looks much bigger than the other, they are at very different
    depths, so the 2D distance is no longer a clean proxy for hand spread.

    For RTMPose `size` is forearm length; for MediaPipe it's the bbox area.
    Both scale with depth, so the same ratio test applies.

    When the pose carries a shoulder anchor (`shoulder_mid_xy` and
    `shoulder_width_px`), features are body-relative: this makes the
    measurements robust to participant tilt (a forward lean shifts hands and
    shoulders together, so wrist-relative-to-shoulder stays stable). When the
    anchor is missing, falls back to image-relative measurements unchanged.
    """
    if not pose.valid or pose.left_xy is None or pose.right_xy is None:
        return None

    # Drop frames where the backend's reported confidence is too low — these
    # tend to be the source of single-frame jitter spikes in the broadcast.
    if min_confidence > 0.0 and pose.confidence < min_confidence:
        return None

    if enforce_depth_similarity and pose.left_size > 0 and pose.right_size > 0:
        ratio = pose.left_size / pose.right_size if pose.left_size > pose.right_size \
            else pose.right_size / pose.left_size
        if not (size_ratio_min <= ratio <= size_ratio_max):
            return None

    x1, y1 = pose.left_xy
    x2, y2 = pose.right_xy
    raw_distance = math.hypot(x1 - x2, y1 - y2)
    raw_mid_y = (y1 + y2) / 2.0

    # Body-relative path: use shoulder anchor when both midpoint and a non-degenerate
    # shoulder width are reported. Tiny shoulder widths (< 1 px) would explode the
    # ratio, so guard against it and fall back to image-relative in that case.
    if pose.shoulder_mid_xy is not None and pose.shoulder_width_px > 1.0:
        _, shoulder_y = pose.shoulder_mid_xy
        sw = pose.shoulder_width_px
        return TwoHandFeatures(
            distance=raw_distance / sw,             # dimensionless: shoulder-widths
            height=(raw_mid_y - shoulder_y) / sw,   # dimensionless: shoulder-widths above/below shoulder line
        )

    return TwoHandFeatures(
        distance=raw_distance,
        height=raw_mid_y,
    )
