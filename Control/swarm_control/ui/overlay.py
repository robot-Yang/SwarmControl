from __future__ import annotations

from typing import Iterable

import cv2
import numpy as np


def draw_status_lines(
    frame: np.ndarray,
    lines: Iterable[tuple[str, tuple[int, int, int], float]],
    *,
    origin: tuple[int, int] = (20, 30),
    line_height: int = 30,
) -> None:
    x, y = origin
    for i, (text, color, scale) in enumerate(lines):
        cv2.putText(
            frame, text, (x, y + i * line_height),
            cv2.FONT_HERSHEY_SIMPLEX, scale, color, 2,
        )


def draw_countdown(frame: np.ndarray, remaining: float, total: float) -> None:
    h, w = frame.shape[:2]
    n = int(remaining) + 1
    cv2.putText(frame, str(n), (w // 2 - 50, h // 2),
                cv2.FONT_HERSHEY_SIMPLEX, 5.0, (0, 255, 0), 10)

    bar_x0, bar_x1 = w // 2 - 200, w // 2 + 200
    bar_y0, bar_y1 = h // 2 + 100, h // 2 + 130
    elapsed = total - remaining
    fill = int((elapsed / total) * (bar_x1 - bar_x0))
    cv2.rectangle(frame, (bar_x0, bar_y0), (bar_x0 + fill, bar_y1), (0, 255, 0), -1)
    cv2.rectangle(frame, (bar_x0, bar_y0), (bar_x1, bar_y1), (255, 255, 255), 2)


def draw_capturing(frame: np.ndarray, count: int, total: int) -> None:
    h, w = frame.shape[:2]
    cv2.putText(frame, "CAPTURING...", (w // 2 - 200, h // 2),
                cv2.FONT_HERSHEY_SIMPLEX, 2.0, (0, 0, 255), 5)
    cv2.putText(frame, f"{count}/{total}", (w // 2 - 80, h // 2 + 80),
                cv2.FONT_HERSHEY_SIMPLEX, 1.5, (0, 0, 255), 3)


def draw_show_hands_warning(frame: np.ndarray) -> None:
    h, w = frame.shape[:2]
    cv2.putText(frame, "SHOW BOTH HANDS", (w // 2 - 220, h // 2),
                cv2.FONT_HERSHEY_SIMPLEX, 1.5, (0, 0, 255), 4)
    cv2.putText(frame, "at similar distance from camera", (w // 2 - 250, h // 2 + 60),
                cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 0, 255), 2)
