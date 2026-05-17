"""Render the standard set of plots from a SwarmTrajectoryRecorder JSON file.

Usage (from anywhere):
    python SoundMapping/analysis/plot_run.py                              # auto-pick the most recent *_traj.json in Assets/Trajectories
    python SoundMapping/analysis/plot_run.py path/to/run_traj.json        # specific file
    python SoundMapping/analysis/plot_run.py path/to/run.json --stride 20 # decimate the trajectory plot (faster on long runs)
    python SoundMapping/analysis/plot_run.py path/to/run.json --show      # also show interactively
    python SoundMapping/analysis/plot_run.py path/to/run.json -o outdir/  # custom output directory

What gets produced (PNGs in <outdir>):
    <stem>_trajectory.png    - top-down (XZ) drone paths + obstacles + crash markers
    <stem>_trajectory_3d.png - 3D drone paths + obstacles (wireframes/faces) + crashes
    <stem>_swarm_health.png  - subnetworks/main/disconnected/cumulative crashes over time
    <stem>_crashes.png       - crash timeline (when, which drone, embodied flag)
    <stem>_inputs.png        - fused movement/spread/rotation + per-source rotation breakdown
    <stem>_gaps.png          - subnetwork centroids during gaps (color = time, size scales with drones)
    <stem>_summary.png       - text card with headline stats

Run with the SwarmControl uv env:
    uv run python SoundMapping/analysis/plot_run.py
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import matplotlib.patches as mpatches
import matplotlib.pyplot as plt
import numpy as np
from mpl_toolkits.mplot3d.art3d import Poly3DCollection  # noqa: F401 -- registers 3d projection


# --------------------------------------------------------------------------- #
# Loading
# --------------------------------------------------------------------------- #

def load(path: str | Path) -> dict:
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def _t0(log: dict) -> float:
    """Reference time = first sample we recorded. Used to make every plot's
    time axis run-relative rather than relative to Time.time at scene load."""
    sf = log.get("swarmFrames") or []
    if sf:
        return sf[0]["t"]
    for d in log.get("trajectories", []):
        if d.get("frames"):
            return d["frames"][0]["t"]
    return 0.0


def autodetect_json(start: Path) -> Path | None:
    """Find the most recent *_traj.json under Assets/Trajectories, walking up
    from `start` until we hit a SwarmControl-like root."""
    cur = start.resolve()
    for parent in (cur, *cur.parents):
        candidate_root = parent / "SoundMapping" / "SoundMappingUnity" / "Assets" / "Trajectories"
        if candidate_root.exists():
            files = sorted(candidate_root.rglob("*_traj.json"),
                           key=lambda p: p.stat().st_mtime, reverse=True)
            if files:
                return files[0]
        # Also accept being inside the Trajectories dir directly.
        if parent.name == "Trajectories":
            files = sorted(parent.rglob("*_traj.json"),
                           key=lambda p: p.stat().st_mtime, reverse=True)
            if files:
                return files[0]
    return None


# --------------------------------------------------------------------------- #
# Obstacle shapes for the top-down (XZ) projection
# --------------------------------------------------------------------------- #

def _yaw_from_quat(qw: float, qx: float, qy: float, qz: float) -> float:
    """Yaw (rotation about Unity's Y axis) from a quaternion. Top-down XZ
    only cares about yaw — pitch/roll get dropped."""
    return float(np.arctan2(2.0 * (qw * qy + qx * qz),
                            1.0 - 2.0 * (qy * qy + qx * qx)))


def _obstacle_patches_xz(obstacles: list, **patch_kwargs) -> list:
    """Build matplotlib patches for a top-down XZ obstacle overlay.
    Spheres + cylinders -> circles; boxes -> rotated rectangles."""
    defaults = dict(facecolor="#888888", alpha=0.35, edgecolor="black", linewidth=0.8)
    defaults.update(patch_kwargs)
    out = []
    for o in obstacles or []:
        t = o.get("type", "")
        cx, cz = float(o["cx"]), float(o["cz"])
        if t in ("Sphere", "Cylinder"):
            r = float(o.get("radius", 0.5))
            out.append(mpatches.Circle((cx, cz), r, **defaults))
        elif t == "Box":
            sx = float(o.get("sx", 1.0))
            sz = float(o.get("sz", 1.0))
            yaw = _yaw_from_quat(o.get("qw", 1.0), o.get("qx", 0.0),
                                 o.get("qy", 0.0), o.get("qz", 0.0))
            half = np.array([[-sx / 2, -sz / 2],
                             [ sx / 2, -sz / 2],
                             [ sx / 2,  sz / 2],
                             [-sx / 2,  sz / 2]])
            c, s = np.cos(yaw), np.sin(yaw)
            R = np.array([[c, -s], [s, c]])
            corners = (R @ half.T).T + np.array([cx, cz])
            out.append(mpatches.Polygon(corners, **defaults))
        else:
            r = float(o.get("radius", 0.5))
            out.append(mpatches.Circle((cx, cz), r, linestyle=":", **defaults))
    return out


# --------------------------------------------------------------------------- #
# Plots
# --------------------------------------------------------------------------- #

def plot_trajectory_2d(log: dict, ax=None, stride: int = 1):
    """Top-down (XZ) drone paths + obstacles + crash markers.

    `stride` keeps every Nth frame in the path polylines. For long runs (e.g.
    8000+ frames per drone) stride=10–20 gives a near-identical plot at a
    fraction of the matplotlib draw cost. Stride is *only* applied to path
    rendering — start/end markers always use the true first/last frame so
    they don't shift when stride changes.
    """
    if ax is None:
        _, ax = plt.subplots(figsize=(10, 10))

    stride = max(1, int(stride))

    for p in _obstacle_patches_xz(log.get("obstacles", [])):
        ax.add_patch(p)

    trajs = log.get("trajectories", [])
    embodied_id = log.get("embodiedId", None)
    cmap = plt.get_cmap("tab20")

    for i, drone in enumerate(trajs):
        frames = drone.get("frames", [])
        if not frames:
            continue
        sampled = frames[::stride]
        # Make sure the last point is included even if stride doesn't hit it,
        # otherwise the visible path ends slightly short on long runs.
        if sampled and sampled[-1] is not frames[-1]:
            sampled = list(sampled) + [frames[-1]]
        xs = np.fromiter((f["x"] for f in sampled), dtype=float, count=len(sampled))
        zs = np.fromiter((f["z"] for f in sampled), dtype=float, count=len(sampled))

        if drone.get("id") == embodied_id:
            ax.plot(xs, zs, color="red", lw=2.0, alpha=0.95, zorder=5,
                    label=f"embodied ({drone.get('name', '?')})")
            # Always anchor markers to the true endpoints, not the strided sample.
            f0, fL = frames[0], frames[-1]
            ax.scatter([f0["x"]], [f0["z"]], marker="o", s=80, facecolor="white",
                       edgecolor="red", zorder=7, label="embodied start")
            ax.scatter([fL["x"]], [fL["z"]], marker="s", s=80, facecolor="white",
                       edgecolor="red", zorder=7, label="embodied end")
        else:
            ax.plot(xs, zs, color=cmap(i % 20), lw=0.7, alpha=0.7)

    crashes = log.get("crashes", [])
    if crashes:
        ax.scatter([c["x"] for c in crashes], [c["z"] for c in crashes],
                   marker="x", s=90, color="black", linewidths=2.2, zorder=6,
                   label=f"crashes ({len(crashes)})")

    ax.set_aspect("equal", adjustable="datalim")
    ax.set_xlabel("X (m)"); ax.set_ylabel("Z (m)")
    title_extra = f"  (stride={stride})" if stride > 1 else ""
    ax.set_title(f"Trajectory + obstacles + crashes — "
                 f"PID {log.get('pid','?')} ({log.get('haptics','?')}/{log.get('order','?')})"
                 f"{title_extra}")
    ax.legend(loc="best", fontsize=8, framealpha=0.85)
    ax.grid(True, alpha=0.2)
    return ax


# --------------------------------------------------------------------------- #
# 3D plot helpers
# --------------------------------------------------------------------------- #

def _quat_to_matrix(qw: float, qx: float, qy: float, qz: float) -> np.ndarray:
    """Quaternion -> 3x3 rotation matrix. Unity uses left-handed coords but
    matplotlib's 3D axes are right-handed; we just plot positions as-is, so
    rotations work the same since the recorded quaternion already encodes
    Unity's world orientation."""
    n = max(1e-12, qw * qw + qx * qx + qy * qy + qz * qz)
    qw, qx, qy, qz = qw / np.sqrt(n), qx / np.sqrt(n), qy / np.sqrt(n), qz / np.sqrt(n)
    return np.array([
        [1 - 2 * (qy * qy + qz * qz),     2 * (qx * qy - qz * qw),     2 * (qx * qz + qy * qw)],
        [    2 * (qx * qy + qz * qw), 1 - 2 * (qx * qx + qz * qz),     2 * (qy * qz - qx * qw)],
        [    2 * (qx * qz - qy * qw),     2 * (qy * qz + qx * qw), 1 - 2 * (qx * qx + qy * qy)],
    ])


def _add_obstacle_3d(ax, o: dict, color: str = "#888888", alpha: float = 0.18) -> None:
    """Render one obstacle in 3D. Spheres + cylinders as wireframes (cheap to
    draw, doesn't occlude trajectories); boxes as semi-transparent faces."""
    t = o.get("type", "")
    cx, cy, cz = float(o["cx"]), float(o["cy"]), float(o["cz"])
    if t == "Sphere":
        r = float(o.get("radius", 0.5))
        u = np.linspace(0, 2 * np.pi, 18)
        v = np.linspace(0, np.pi, 10)
        x = cx + r * np.outer(np.cos(u), np.sin(v))
        z = cz + r * np.outer(np.sin(u), np.sin(v))
        y = cy + r * np.outer(np.ones_like(u), np.cos(v))
        ax.plot_wireframe(x, y, z, color=color, alpha=alpha, linewidth=0.5)
    elif t == "Cylinder":
        r = float(o.get("radius", 0.5))
        h = float(o.get("height", 1.0))
        R = _quat_to_matrix(o.get("qw", 1.0), o.get("qx", 0.0), o.get("qy", 0.0), o.get("qz", 0.0))
        theta = np.linspace(0, 2 * np.pi, 24)
        ys = np.array([-h / 2, h / 2])
        # Local-space ring at each end.
        circle = np.stack([r * np.cos(theta), np.zeros_like(theta), r * np.sin(theta)], axis=1)
        for y_local in ys:
            ring = circle.copy()
            ring[:, 1] = y_local
            world = (R @ ring.T).T + np.array([cx, cy, cz])
            ax.plot(world[:, 0], world[:, 1], world[:, 2],
                    color=color, alpha=alpha, linewidth=0.7)
        # Vertical struts at a few angles so the wireframe reads as a tube.
        for k in range(0, len(theta), 4):
            seg = np.stack([
                [r * np.cos(theta[k]), -h / 2, r * np.sin(theta[k])],
                [r * np.cos(theta[k]),  h / 2, r * np.sin(theta[k])],
            ])
            world = (R @ seg.T).T + np.array([cx, cy, cz])
            ax.plot(world[:, 0], world[:, 1], world[:, 2],
                    color=color, alpha=alpha, linewidth=0.5)
    elif t == "Box":
        sx, sy, sz = float(o.get("sx", 1)), float(o.get("sy", 1)), float(o.get("sz", 1))
        R = _quat_to_matrix(o.get("qw", 1.0), o.get("qx", 0.0), o.get("qy", 0.0), o.get("qz", 0.0))
        h = np.array([sx / 2, sy / 2, sz / 2])
        # 8 corners in local space.
        corners_local = np.array([
            [-h[0], -h[1], -h[2]], [ h[0], -h[1], -h[2]],
            [ h[0],  h[1], -h[2]], [-h[0],  h[1], -h[2]],
            [-h[0], -h[1],  h[2]], [ h[0], -h[1],  h[2]],
            [ h[0],  h[1],  h[2]], [-h[0],  h[1],  h[2]],
        ])
        corners = (R @ corners_local.T).T + np.array([cx, cy, cz])
        faces = [
            [corners[i] for i in (0, 1, 2, 3)],  # bottom
            [corners[i] for i in (4, 5, 6, 7)],  # top
            [corners[i] for i in (0, 1, 5, 4)],
            [corners[i] for i in (1, 2, 6, 5)],
            [corners[i] for i in (2, 3, 7, 6)],
            [corners[i] for i in (3, 0, 4, 7)],
        ]
        coll = Poly3DCollection(faces, facecolor=color, alpha=alpha,
                                edgecolor="black", linewidth=0.4)
        ax.add_collection3d(coll)


def plot_trajectory_3d(log: dict, ax=None, stride: int = 1):
    """3D trajectory plot with obstacles as wireframes (spheres/cylinders) or
    translucent faces (boxes). Stride decimates the per-drone polyline, same
    as the 2D plot."""
    if ax is None:
        fig = plt.figure(figsize=(11, 9))
        ax = fig.add_subplot(111, projection="3d")

    stride = max(1, int(stride))

    for obs in log.get("obstacles", []):
        _add_obstacle_3d(ax, obs)

    trajs = log.get("trajectories", [])
    embodied_id = log.get("embodiedId", None)
    cmap = plt.get_cmap("tab20")

    all_pts = []  # accumulate for axis-limit computation
    for i, drone in enumerate(trajs):
        frames = drone.get("frames", [])
        if not frames:
            continue
        sampled = frames[::stride]
        if sampled and sampled[-1] is not frames[-1]:
            sampled = list(sampled) + [frames[-1]]
        xs = np.fromiter((f["x"] for f in sampled), dtype=float, count=len(sampled))
        ys = np.fromiter((f["y"] for f in sampled), dtype=float, count=len(sampled))
        zs = np.fromiter((f["z"] for f in sampled), dtype=float, count=len(sampled))
        all_pts.append((xs, ys, zs))

        if drone.get("id") == embodied_id:
            ax.plot(xs, ys, zs, color="red", lw=2.0, alpha=0.95,
                    label=f"embodied ({drone.get('name','?')})")
            f0, fL = frames[0], frames[-1]
            ax.scatter([f0["x"]], [f0["y"]], [f0["z"]], marker="o", s=70,
                       facecolor="white", edgecolor="red", label="embodied start")
            ax.scatter([fL["x"]], [fL["y"]], [fL["z"]], marker="s", s=70,
                       facecolor="white", edgecolor="red", label="embodied end")
        else:
            ax.plot(xs, ys, zs, color=cmap(i % 20), lw=0.7, alpha=0.7)

    crashes = log.get("crashes", [])
    if crashes:
        ax.scatter([c["x"] for c in crashes],
                   [c["y"] for c in crashes],
                   [c["z"] for c in crashes],
                   marker="x", s=70, color="black", linewidths=2.0,
                   label=f"crashes ({len(crashes)})")

    # Equal aspect across X/Y/Z (matplotlib doesn't give true equal automatically
    # for 3D; we pad each axis to the largest range so cubes look like cubes).
    if all_pts:
        xs = np.concatenate([p[0] for p in all_pts])
        ys = np.concatenate([p[1] for p in all_pts])
        zs = np.concatenate([p[2] for p in all_pts])
        # Include obstacle centers in the bounding box so they're never cropped.
        for o in log.get("obstacles", []):
            xs = np.append(xs, float(o["cx"]))
            ys = np.append(ys, float(o["cy"]))
            zs = np.append(zs, float(o["cz"]))
        mid = np.array([(xs.max() + xs.min()) / 2,
                        (ys.max() + ys.min()) / 2,
                        (zs.max() + zs.min()) / 2])
        half = max(xs.max() - xs.min(),
                   ys.max() - ys.min(),
                   zs.max() - zs.min()) / 2 * 1.05
        half = max(half, 1.0)
        ax.set_xlim(mid[0] - half, mid[0] + half)
        ax.set_ylim(mid[1] - half, mid[1] + half)
        ax.set_zlim(mid[2] - half, mid[2] + half)
        try:
            ax.set_box_aspect([1, 1, 1])
        except AttributeError:
            pass  # older matplotlib

    ax.set_xlabel("X (m)"); ax.set_ylabel("Y (m)"); ax.set_zlabel("Z (m)")
    title_extra = f"  (stride={stride})" if stride > 1 else ""
    ax.set_title(f"3D trajectory + obstacles + crashes — "
                 f"PID {log.get('pid','?')} ({log.get('haptics','?')}/{log.get('order','?')})"
                 f"{title_extra}")
    ax.legend(loc="best", fontsize=8)
    return ax


def plot_swarm_health(log: dict, ax=None):
    """Counts over time: main group size, disconnected drones, subnetwork count,
    cumulative crashes. Trial windows are shaded."""
    if ax is None:
        _, ax = plt.subplots(figsize=(12, 5))

    sf = log.get("swarmFrames", [])
    if not sf:
        ax.text(0.5, 0.5, "no swarmFrames recorded",
                ha="center", va="center", transform=ax.transAxes)
        return ax

    t0 = _t0(log)
    t = np.array([f["t"] - t0 for f in sf])
    ax.plot(t, [f["nMain"]  for f in sf], label="main group size",            color="tab:green",  lw=1.6)
    ax.plot(t, [f["nDisc"]  for f in sf], label="disconnected drones",        color="tab:orange", lw=1.4)
    ax.plot(t, [f["nSub"]   for f in sf], label="subnetworks (gaps = nSub-1)", color="tab:blue",   lw=1.2)
    ax.plot(t, [f["nCrash"] for f in sf], label="cumulative crashes",          color="tab:red",    lw=1.2, linestyle="--")

    for trial in log.get("trials", []):
        ts = trial["startGameTime"] - t0
        ax.axvline(ts, color="black", linestyle=":", alpha=0.4)
        if trial.get("endGameTime", 0) > 0:
            te = trial["endGameTime"] - t0
            ax.axvline(te, color="black", linestyle=":", alpha=0.4)
            ax.axvspan(ts, te, alpha=0.04, color="black")

    ax.set_xlabel("time since start (s)"); ax.set_ylabel("count")
    ax.set_title("Swarm health over time")
    ax.legend(loc="best", framealpha=0.9); ax.grid(True, alpha=0.3)
    return ax


def plot_crash_timeline(log: dict, ax=None):
    """x = time, y = drone id, red = embodied at crash."""
    if ax is None:
        _, ax = plt.subplots(figsize=(10, 3.5))

    crashes = log.get("crashes", [])
    if not crashes:
        ax.text(0.5, 0.5, "no crashes recorded",
                ha="center", va="center", transform=ax.transAxes)
        return ax

    t0 = _t0(log)
    ts = [c["t"] - t0 for c in crashes]
    ids = [c["droneId"] for c in crashes]
    colors = ["red" if c.get("embodied", 0) else "black" for c in crashes]
    ax.scatter(ts, ids, c=colors, marker="x", s=90, linewidths=2.2)
    ax.set_xlabel("time since start (s)"); ax.set_ylabel("drone id")
    ax.set_title(f"Crash timeline ({len(crashes)} total; red = embodied at crash)")
    ax.grid(True, alpha=0.3)
    return ax


def plot_inputs(log: dict, axes=None):
    """4-panel: fused movement / spread / rotation / per-source rotation breakdown."""
    inputs = log.get("inputs", [])
    if not inputs:
        if axes is None:
            _, axes = plt.subplots(figsize=(12, 3))
        ax = axes if not hasattr(axes, "__len__") else axes[0]
        ax.text(0.5, 0.5, "no inputs recorded (logInputs disabled?)",
                ha="center", va="center", transform=ax.transAxes)
        return axes

    if axes is None:
        _, axes = plt.subplots(4, 1, figsize=(12, 10), sharex=True)

    t0 = _t0(log)
    t = np.array([f["t"] - t0 for f in inputs])

    axes[0].plot(t, [f["fmx"] for f in inputs], label="x",          color="tab:blue")
    axes[0].plot(t, [f["fmy"] for f in inputs], label="y (height)", color="tab:green")
    axes[0].plot(t, [f["fmz"] for f in inputs], label="z",          color="tab:red")
    axes[0].set_ylabel("SwarmMovement"); axes[0].legend(loc="best"); axes[0].grid(True, alpha=0.3)

    axes[1].plot(t, [f["fs"] for f in inputs], color="tab:purple")
    axes[1].set_ylabel("SwarmSpread"); axes[1].grid(True, alpha=0.3)

    axes[2].plot(t, [f["fr"] for f in inputs], color="tab:orange")
    axes[2].set_ylabel("CameraRotation"); axes[2].grid(True, alpha=0.3)

    imu_y  = np.array([f.get("imu_y",  np.nan) for f in inputs], dtype=float)
    trad_r = np.array([f.get("trad_r", np.nan) for f in inputs], dtype=float)
    mq_y   = np.array([f.get("mq_y",   np.nan) for f in inputs], dtype=float)
    pose_y = np.array([f.get("pose_y", np.nan) for f in inputs], dtype=float)
    plotted = False
    if np.any(~np.isnan(imu_y)):  axes[3].plot(t, imu_y,  label="IMU yaw rate",     color="tab:cyan");                 plotted = True
    if np.any(~np.isnan(trad_r)): axes[3].plot(t, trad_r, label="traditional R",    color="tab:gray",   lw=0.8);       plotted = True
    if np.any(~np.isnan(mq_y)):   axes[3].plot(t, mq_y,   label="MQ headset yaw",   color="tab:olive",  lw=0.8);       plotted = True
    if np.any(~np.isnan(pose_y)): axes[3].plot(t, pose_y, label="webcam pose yaw",  color="tab:pink",   lw=0.8);       plotted = True
    if plotted:
        axes[3].legend(loc="best", fontsize=8)
    axes[3].set_ylabel("rotation sources")
    axes[3].set_xlabel("time since start (s)")
    axes[3].grid(True, alpha=0.3)
    return axes


def plot_gaps(log: dict, ax=None):
    """Subnetwork centroids overlaid on obstacles, only for split frames.
    Marker size scales with drone count, color encodes time."""
    if ax is None:
        _, ax = plt.subplots(figsize=(10, 10))

    for p in _obstacle_patches_xz(log.get("obstacles", []),
                                  facecolor="#888888", alpha=0.12):
        ax.add_patch(p)

    snaps = log.get("subnetworks", [])
    if not snaps:
        ax.text(0.5, 0.5, "no subnetwork data",
                ha="center", va="center", transform=ax.transAxes)
        ax.set_aspect("equal", adjustable="datalim")
        return ax

    by_t: dict[float, list] = {}
    for s in snaps:
        by_t.setdefault(s["t"], []).append(s)
    split_t = sorted(t for t, lst in by_t.items() if len(lst) > 1)
    if not split_t:
        ax.text(0.5, 0.5, "swarm never split during this run",
                ha="center", va="center", transform=ax.transAxes)
        ax.set_aspect("equal", adjustable="datalim")
        return ax

    t0 = _t0(log)
    t_lo = split_t[0] - t0
    t_hi = split_t[-1] - t0
    span = max(1e-6, t_hi - t_lo)
    cmap = plt.get_cmap("plasma")
    for t in split_t:
        col = cmap((t - t0 - t_lo) / span)
        for s in by_t[t]:
            ax.scatter(s["cx"], s["cz"],
                       s=max(15.0, float(s["size"]) * 25.0),
                       facecolor=col, alpha=0.55,
                       edgecolor="black", linewidth=0.4)
    sm = plt.cm.ScalarMappable(cmap=cmap, norm=plt.Normalize(vmin=t_lo, vmax=t_hi))
    sm.set_array([])
    plt.colorbar(sm, ax=ax, label="time since start (s)")
    ax.set_aspect("equal", adjustable="datalim")
    ax.set_xlabel("X (m)"); ax.set_ylabel("Z (m)")
    ax.set_title(f"Subnetwork centroids during gaps "
                 f"(n={len(split_t)} split samples; size scales with drones)")
    return ax


def plot_summary(log: dict, ax=None):
    """Text card with the headline run metrics."""
    if ax is None:
        _, ax = plt.subplots(figsize=(7, 5))

    elapsed   = log.get("elapsedTime", 0.0)
    picked    = log.get("collectiblesPickedUp", 0)
    total     = log.get("totalCollectibles", 0)
    n_crashes = log.get("crashCount", 0)
    sf        = log.get("swarmFrames", [])
    mean_disc = float(np.mean([f["nDisc"] for f in sf])) if sf else 0.0
    max_sub   = int(np.max([f["nSub"] for f in sf])) if sf else 0

    ratio = f"  ({100 * picked / total:.0f}%)" if total > 0 else ""
    lines = [
        f"PID:        {log.get('pid','?')}    Haptics: {log.get('haptics','?')}    Order: {log.get('order','?')}",
        f"Scene:      {log.get('scene','?')}",
        f"Embodied:   {log.get('embodiedName','?')} (id={log.get('embodiedId','?')})",
        "",
        f"Elapsed time:        {elapsed:.2f} s",
        f"Collectibles:        {picked} / {total}{ratio}",
        f"Crashes:             {n_crashes}",
        f"Mean disconnected:   {mean_disc:.2f} drones/frame",
        f"Max subnetworks:     {max_sub}  ({max(0, max_sub - 1)} gaps at peak)",
    ]
    ax.axis("off")
    ax.text(0.02, 0.98, "\n".join(lines), family="monospace", fontsize=11,
            va="top", ha="left", transform=ax.transAxes)
    ax.set_title("Run summary")
    return ax


# --------------------------------------------------------------------------- #
# Driver
# --------------------------------------------------------------------------- #

def make_all(json_path: Path, outdir: Path, stride: int = 1, show: bool = False) -> None:
    log = load(json_path)
    outdir.mkdir(parents=True, exist_ok=True)
    stem = json_path.stem

    figs: list = []

    fig, ax = plt.subplots(figsize=(10, 10))
    plot_trajectory_2d(log, ax=ax, stride=stride)
    fig.tight_layout(); fig.savefig(outdir / f"{stem}_trajectory.png", dpi=130)
    figs.append(fig)

    fig = plt.figure(figsize=(11, 9))
    ax = fig.add_subplot(111, projection="3d")
    plot_trajectory_3d(log, ax=ax, stride=stride)
    fig.tight_layout(); fig.savefig(outdir / f"{stem}_trajectory_3d.png", dpi=130)
    figs.append(fig)

    fig, ax = plt.subplots(figsize=(12, 5))
    plot_swarm_health(log, ax=ax)
    fig.tight_layout(); fig.savefig(outdir / f"{stem}_swarm_health.png", dpi=130)
    figs.append(fig)

    fig, ax = plt.subplots(figsize=(10, 3.5))
    plot_crash_timeline(log, ax=ax)
    fig.tight_layout(); fig.savefig(outdir / f"{stem}_crashes.png", dpi=130)
    figs.append(fig)

    fig, axes = plt.subplots(4, 1, figsize=(12, 10), sharex=True)
    plot_inputs(log, axes=axes)
    fig.tight_layout(); fig.savefig(outdir / f"{stem}_inputs.png", dpi=130)
    figs.append(fig)

    fig, ax = plt.subplots(figsize=(10, 10))
    plot_gaps(log, ax=ax)
    fig.tight_layout(); fig.savefig(outdir / f"{stem}_gaps.png", dpi=130)
    figs.append(fig)

    fig, ax = plt.subplots(figsize=(7, 5))
    plot_summary(log, ax=ax)
    fig.tight_layout(); fig.savefig(outdir / f"{stem}_summary.png", dpi=130)
    figs.append(fig)

    print(f"Wrote 7 plots to {outdir}/")
    if show:
        plt.show()
    else:
        for f in figs:
            plt.close(f)


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("json", type=Path, nargs="?", default=None,
                   help="Path to *_traj.json. If omitted, auto-picks the most recent under "
                        "<repo>/SoundMapping/SoundMappingUnity/Assets/Trajectories/.")
    p.add_argument("-o", "--outdir", type=Path, default=None,
                   help="Output directory (default: <json-parent>/<json-stem>_plots/).")
    p.add_argument("--stride", type=int, default=10,
                   help="Keep every Nth trajectory frame in plot 1 (default: 10). "
                        "Use 1 for full detail, higher values for faster rendering on long runs.")
    p.add_argument("--show", action="store_true",
                   help="Open the plots in an interactive window after saving.")
    args = p.parse_args()

    json_path = args.json
    if json_path is None:
        json_path = autodetect_json(Path.cwd())
        if json_path is None:
            raise SystemExit(
                "No *_traj.json found. Pass a path explicitly, or make sure you've "
                "run a session in Unity that saved a trajectory under Assets/Trajectories/."
            )
        print(f"Auto-picked: {json_path}")
    elif not json_path.exists():
        raise SystemExit(f"File not found: {json_path}")

    outdir = args.outdir or (json_path.parent / f"{json_path.stem}_plots")
    make_all(json_path, outdir, stride=args.stride, show=args.show)


if __name__ == "__main__":
    main()
