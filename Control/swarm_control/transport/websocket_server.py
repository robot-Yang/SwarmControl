from __future__ import annotations

import asyncio
import json
import threading
import time
from typing import Optional

import websockets


class WebSocketServer:
    """WebSocket broadcaster. Producer pushes via send_frame(); consumers attach as clients.

    Message schema (JSON):
        {
            "t":         float, send time (seconds, time.time())
            "t_capture": float | null, frame capture time (seconds, time.time())
                                       — diff (t - t_capture) is Python-side latency
            "seq":       int, monotonically increasing per server
            "valid":     bool
            "backend":   str, name of the pose backend that produced this frame
            "left":      [x, y] | null         # raw keypoint, pixel coords
            "right":     [x, y] | null
            "distance":  float | null          # normalized 0..1
            "height":    float | null          # normalized -1..+1
            "yaw":       float | null          # head yaw in degrees, neutral-subtracted
                                                # (only present for backends with face landmarks)
        }
    """

    def __init__(self, host: str = "0.0.0.0", port: int = 9052):
        self.host = host
        self.port = port
        self._clients: set = set()
        self._loop: Optional[asyncio.AbstractEventLoop] = None
        self._thread: Optional[threading.Thread] = None
        self._seq = 0

    async def _handle_client(self, websocket):
        self._clients.add(websocket)
        print(f"[ws] client connected, total={len(self._clients)}")
        try:
            await websocket.wait_closed()
        except websockets.exceptions.ConnectionClosed:
            pass
        finally:
            self._clients.discard(websocket)
            print(f"[ws] client disconnected, total={len(self._clients)}")

    async def _run(self):
        server = await websockets.serve(self._handle_client, self.host, self.port)
        print(f"[ws] listening on ws://{self.host}:{self.port}")
        await server.wait_closed()

    def _thread_main(self):
        self._loop = asyncio.new_event_loop()
        asyncio.set_event_loop(self._loop)
        self._loop.run_until_complete(self._run())

    def start(self) -> None:
        self._thread = threading.Thread(target=self._thread_main, daemon=True)
        self._thread.start()

    def stop(self) -> None:
        if self._loop is not None:
            self._loop.call_soon_threadsafe(self._loop.stop)

    def get_client_count(self) -> int:
        return len(self._clients)

    async def _broadcast(self, payload: str) -> None:
        if not self._clients:
            return
        await asyncio.gather(
            *(c.send(payload) for c in self._clients),
            return_exceptions=True,
        )

    def send_frame(
        self,
        *,
        valid: bool,
        backend: str,
        left_xy: tuple[float, float] | None,
        right_xy: tuple[float, float] | None,
        distance: float | None,
        height: float | None,
        yaw: float | None = None,
        t_capture: float | None = None,
    ) -> None:
        if not self._clients or self._loop is None:
            return

        self._seq += 1
        payload = json.dumps({
            "t": time.time(),
            "t_capture": round(t_capture, 6) if t_capture is not None else None,
            "seq": self._seq,
            "valid": bool(valid),
            "backend": backend,
            "left": [round(left_xy[0], 2), round(left_xy[1], 2)] if left_xy else None,
            "right": [round(right_xy[0], 2), round(right_xy[1], 2)] if right_xy else None,
            "distance": round(distance, 4) if distance is not None else None,
            "height": round(height, 4) if height is not None else None,
            "yaw": round(yaw, 3) if yaw is not None else None,
        })
        asyncio.run_coroutine_threadsafe(self._broadcast(payload), self._loop)
