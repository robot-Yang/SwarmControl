from __future__ import annotations

from typing import Optional

from .profile import CalibrationProfile


def _clamp(x: float, lo: float, hi: float) -> float:
    return max(lo, min(hi, x))


class Normalizer:
    """Stateful normalizer: raw (distance, height) → smoothed (0..1, -1..+1).

    Holds an EMA per channel so smoothing survives across frames.
    """

    def __init__(self, profile: CalibrationProfile):
        self.profile = profile
        self.alpha = float(profile.smooth_alpha)
        self._dist_ema: Optional[float] = None
        self._height_ema: Optional[float] = None

    def reset(self) -> None:
        self._dist_ema = None
        self._height_ema = None

    def normalize_horizontal(self, raw_distance: float) -> float:
        p = self.profile
        span = p.max_horizontal - p.min_horizontal
        norm = (raw_distance - p.min_horizontal) / span if span > 0 else 0.0
        norm = _clamp(norm, 0.0, 1.0)
        norm = _clamp(p.horizontal_linearization.apply(norm), 0.0, 1.0)
        self._dist_ema = norm if self._dist_ema is None \
            else self.alpha * norm + (1 - self.alpha) * self._dist_ema
        return self._dist_ema

    def normalize_vertical(self, raw_height: float) -> float:
        p = self.profile
        if raw_height < p.neutral_vertical:
            span = p.neutral_vertical - p.min_vertical
            norm = (raw_height - p.neutral_vertical) / span if span > 0 else 0.0
        else:
            span = p.max_vertical - p.neutral_vertical
            norm = (raw_height - p.neutral_vertical) / span if span > 0 else 0.0

        # Screen Y grows downward → invert so "hands up" = positive
        norm = -norm
        norm = _clamp(norm, -1.0, 1.0)
        norm = _clamp(p.vertical_linearization.apply(norm), -1.0, 1.0)
        self._height_ema = norm if self._height_ema is None \
            else self.alpha * norm + (1 - self.alpha) * self._height_ema
        return self._height_ema

    def normalize(self, raw_distance: float, raw_height: float) -> tuple[float, float]:
        return self.normalize_horizontal(raw_distance), self.normalize_vertical(raw_height)
