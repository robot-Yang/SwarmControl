from __future__ import annotations

import json
from dataclasses import dataclass, field, asdict
from pathlib import Path
from typing import Any


CALIBRATIONS_DIR = Path(__file__).resolve().parents[2] / "calibrations"


@dataclass
class Linearization:
    """Linearization function applied after raw → 0..1 (or -1..+1) normalization."""

    type: str = "linear"        # "linear" | "polynomial"
    slope: float = 1.0
    intercept: float = 0.0
    coefficients: list[float] = field(default_factory=list)  # used if type == "polynomial"
    r_squared: float | None = None
    max_error: float | None = None
    mean_error: float | None = None

    def apply(self, x: float) -> float:
        if self.type == "polynomial" and self.coefficients:
            result = 0.0
            for i, c in enumerate(self.coefficients):
                result += c * (x ** i)
            return result
        return self.slope * x + self.intercept

    def to_dict(self) -> dict[str, Any]:
        d: dict[str, Any] = {"type": self.type}
        if self.type == "polynomial":
            d["coefficients"] = list(self.coefficients)
        else:
            d["slope"] = self.slope
            d["intercept"] = self.intercept
        for k in ("r_squared", "max_error", "mean_error"):
            v = getattr(self, k)
            if v is not None:
                d[k] = v
        return d

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> "Linearization":
        return cls(
            type=d.get("type", "linear"),
            slope=float(d.get("slope", 1.0)),
            intercept=float(d.get("intercept", 0.0)),
            coefficients=[float(c) for c in d.get("coefficients", [])],
            r_squared=d.get("r_squared"),
            max_error=d.get("max_error"),
            mean_error=d.get("mean_error"),
        )


@dataclass
class CalibrationProfile:
    profile_name: str
    description: str = ""
    min_horizontal: float = 0.0
    max_horizontal: float = 1.0
    min_vertical: float = 0.0
    max_vertical: float = 1.0
    neutral_vertical: float = 0.5
    smooth_alpha: float = 0.2
    horizontal_linearization: Linearization = field(default_factory=Linearization)
    vertical_linearization: Linearization = field(default_factory=Linearization)
    backend: str = "mediapipe"  # which pose backend produced this calibration

    def to_dict(self) -> dict[str, Any]:
        d = asdict(self)
        d["horizontal_linearization"] = self.horizontal_linearization.to_dict()
        d["vertical_linearization"] = self.vertical_linearization.to_dict()
        return d

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> "CalibrationProfile":
        return cls(
            profile_name=d.get("profile_name", "default"),
            description=d.get("description", ""),
            min_horizontal=float(d["min_horizontal"]),
            max_horizontal=float(d["max_horizontal"]),
            min_vertical=float(d["min_vertical"]),
            max_vertical=float(d["max_vertical"]),
            neutral_vertical=float(d["neutral_vertical"]),
            smooth_alpha=float(d.get("smooth_alpha", 0.2)),
            horizontal_linearization=Linearization.from_dict(
                d.get("horizontal_linearization", {})
            ),
            vertical_linearization=Linearization.from_dict(
                d.get("vertical_linearization", {})
            ),
            backend=d.get("backend", "mediapipe"),
        )

    @classmethod
    def load(cls, name: str, calibrations_dir: Path | None = None) -> "CalibrationProfile":
        directory = calibrations_dir or CALIBRATIONS_DIR
        path = directory / f"{name}.json"
        if not path.exists():
            raise FileNotFoundError(f"Calibration profile not found: {path}")
        with path.open("r") as f:
            return cls.from_dict(json.load(f))

    def save(self, calibrations_dir: Path | None = None) -> Path:
        directory = calibrations_dir or CALIBRATIONS_DIR
        directory.mkdir(parents=True, exist_ok=True)
        path = directory / f"{self.profile_name}.json"
        with path.open("w") as f:
            json.dump(self.to_dict(), f, indent=4)
        return path
