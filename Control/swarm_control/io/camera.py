"""Threaded webcam reader.

OpenCV's `cv2.VideoCapture.read()` blocks until the next frame arrives. On a
30 FPS webcam that's up to 33 ms of synchronous wait per main-loop iteration,
even when there's processing budget to spare. This module wraps VideoCapture
in a background thread that always holds the latest frame; the main loop's
`read()` returns the most recent one immediately.

Net effect: shaves up to half a frame of latency off the end-to-end pipeline.
Drop-in replacement — same `read()`, `get()`, `isOpened()`, `release()` API
as cv2.VideoCapture for the calls our apps make.
"""

from __future__ import annotations

import threading
from typing import Optional

import cv2
import numpy as np


class ThreadedCamera:
    """Background-thread frame reader. The thread keeps a single most-recent
    frame in memory; `read()` returns a copy of it without blocking on I/O."""

    def __init__(
        self,
        source: int,
        width: int = 640,
        height: int = 360,
        fps: int = 60,
    ) -> None:
        self._cap = cv2.VideoCapture(source)
        if self._cap.isOpened():
            # Set requested camera mode (driver may snap to nearest supported).
            self._cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
            self._cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
            self._cap.set(cv2.CAP_PROP_FPS, fps)

        self._lock = threading.Lock()
        self._latest: Optional[np.ndarray] = None
        self._stopped = False
        self._thread: Optional[threading.Thread] = None

        if self._cap.isOpened():
            self._thread = threading.Thread(target=self._loop, daemon=True)
            self._thread.start()

    def _loop(self) -> None:
        while not self._stopped:
            ok, frame = self._cap.read()
            if not ok:
                continue
            with self._lock:
                self._latest = frame

    def isOpened(self) -> bool:  # noqa: N802 — match cv2 API
        return self._cap.isOpened()

    def read(self) -> tuple[bool, Optional[np.ndarray]]:
        """Returns (True, latest_frame_copy) or (False, None) before the first
        frame has arrived. We copy so the caller can mutate freely without
        racing the writer thread."""
        with self._lock:
            if self._latest is None:
                return False, None
            return True, self._latest.copy()

    def get(self, prop: int) -> float:
        return self._cap.get(prop)

    def release(self) -> None:
        self._stopped = True
        if self._thread is not None:
            self._thread.join(timeout=1.0)
        self._cap.release()
