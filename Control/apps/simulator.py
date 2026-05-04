"""Pygame two-ball test client. Connects to the tracker WebSocket and
visualizes the (distance, height) pair so you can sanity-check tracking
without launching Unity.
"""

from __future__ import annotations

import argparse
import asyncio
import json
import sys
import threading
from pathlib import Path

import pygame
import websockets

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))


BLACK = (0, 0, 0)
WHITE = (255, 255, 255)
CYAN = (0, 255, 255)
RED = (255, 100, 100)
GREEN = (100, 255, 100)
BLUE = (100, 150, 255)


class DroneSimulation:
    def __init__(self, width: int = 800, height: int = 600):
        pygame.init()
        self.width = width
        self.height = height
        self.screen = pygame.display.set_mode((width, height))
        pygame.display.set_caption("Tracker simulator")
        self.clock = pygame.time.Clock()
        self.font = pygame.font.Font(None, 36)
        self.small_font = pygame.font.Font(None, 24)

        self.distance = 0.5
        self.height_value = 0.0
        self.target_distance = 0.5
        self.target_height_value = 0.0
        self.connected = False
        self.backend = "?"
        self.last_seq = 0
        self.last_t = 0.0

        self.ball_radius = 30
        self.center_x = width // 2
        self.center_y = height // 2

    def update_tracking_data(self, distance: float, height: float, *, backend: str, seq: int, t: float) -> None:
        if distance is not None:
            self.target_distance = distance
        if height is not None:
            self.target_height_value = height
        self.connected = True
        self.backend = backend
        self.last_seq = seq
        self.last_t = t

    def smooth(self) -> None:
        f = 0.15
        self.distance += (self.target_distance - self.distance) * f
        self.height_value += (self.target_height_value - self.height_value) * f

    def draw(self) -> None:
        self.screen.fill(BLACK)
        max_sep = self.width * 0.4
        sep = self.distance * max_sep
        max_h = (self.height - 100) * 0.35
        y_off = -self.height_value * max_h
        ball_y = int(self.center_y + y_off)
        margin = self.ball_radius + 20
        ball_y = max(margin, min(self.height - margin, ball_y))

        b1x = int(self.center_x - sep / 2)
        b2x = int(self.center_x + sep / 2)
        pygame.draw.line(self.screen, WHITE, (b1x, ball_y), (b2x, ball_y), 2)
        pygame.draw.circle(self.screen, RED, (b1x, ball_y), self.ball_radius)
        pygame.draw.circle(self.screen, BLUE, (b2x, ball_y), self.ball_radius)

        title = self.font.render("Tracker simulator", True, CYAN)
        self.screen.blit(title, (20, 20))

        status_color = GREEN if self.connected else RED
        status = self.small_font.render(
            "CONNECTED" if self.connected else "WAITING…",
            True, status_color,
        )
        self.screen.blit(status, (20, 60))

        info = [
            f"backend: {self.backend}",
            f"seq: {self.last_seq}",
            f"distance: {self.distance:.3f}",
            f"height: {self.height_value:+.3f}",
        ]
        for i, line in enumerate(info):
            self.screen.blit(self.small_font.render(line, True, WHITE),
                             (20, 100 + i * 26))

    def run(self) -> None:
        running = True
        while running:
            for event in pygame.event.get():
                if event.type == pygame.QUIT:
                    running = False
                elif event.type == pygame.KEYDOWN and event.key == pygame.K_ESCAPE:
                    running = False
            self.smooth()
            self.draw()
            pygame.display.flip()
            self.clock.tick(60)
        pygame.quit()


class WebSocketClient:
    def __init__(self, simulation: DroneSimulation, host: str, port: int):
        self.simulation = simulation
        self.uri = f"ws://{host}:{port}"
        self.running = False

    async def loop(self) -> None:
        while self.running:
            try:
                print(f"connecting to {self.uri}…")
                async with websockets.connect(self.uri) as ws:
                    print("connected")
                    while self.running:
                        try:
                            msg = await asyncio.wait_for(ws.recv(), timeout=1.0)
                            data = json.loads(msg)
                            self.simulation.update_tracking_data(
                                data.get("distance"),
                                data.get("height"),
                                backend=data.get("backend", "?"),
                                seq=int(data.get("seq", 0)),
                                t=float(data.get("t", 0.0)),
                            )
                        except asyncio.TimeoutError:
                            continue
                        except json.JSONDecodeError:
                            continue
            except (ConnectionRefusedError, OSError):
                print("waiting for tracker…")
                await asyncio.sleep(2)
            except Exception as e:
                print(f"client error: {e}")
                await asyncio.sleep(2)

    def start(self) -> None:
        self.running = True

        def runner():
            loop = asyncio.new_event_loop()
            asyncio.set_event_loop(loop)
            loop.run_until_complete(self.loop())

        threading.Thread(target=runner, daemon=True).start()

    def stop(self) -> None:
        self.running = False


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Pygame visual sanity-check client")
    p.add_argument("--host", default="localhost")
    p.add_argument("--port", type=int, default=9052)
    return p.parse_args()


def main() -> int:
    args = parse_args()
    sim = DroneSimulation()
    client = WebSocketClient(sim, args.host, args.port)
    client.start()
    try:
        sim.run()
    finally:
        client.stop()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
