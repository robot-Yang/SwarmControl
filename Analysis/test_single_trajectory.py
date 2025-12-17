
"""
Single Trajectory Viewer
Usage: python view_trajectory.py <trajectory.json>
par exemple tu peux faire : python test_single_trajectory.py Trajectories/Gab_B_1.json
"""

import json
import numpy as np
import matplotlib.pyplot as plt
from pathlib import Path
import sys

# ============================================================
# DATA EXTRACTION
# ============================================================

def get_trial_window(data):
    trials = data.get('trials') or []
    run_trial = next((t for t in trials if t.get('label') == 'Run'), None)
    if not run_trial:
        return 0, float('inf')

    def guess_frame_time_base():
        """Best-effort guess of whether frame['t'] is closer to gameTime or realtime."""
        trajectories = data.get('trajectories') or []
        if not trajectories:
            return 'game'
        frames = (trajectories[0].get('frames') or [])
        if not frames:
            return 'game'

        t_first = frames[0].get('t')
        t_last = frames[-1].get('t')

        end_game = run_trial.get('endGameTime')
        end_real = run_trial.get('endRealtime')
        if t_last is not None and end_game is not None and end_real is not None:
            return 'game' if abs(t_last - end_game) <= abs(t_last - end_real) else 'real'

        start_game = run_trial.get('startGameTime')
        start_real = run_trial.get('startRealtime')
        if t_first is not None and start_game is not None and start_real is not None:
            return 'game' if abs(t_first - start_game) <= abs(t_first - start_real) else 'real'

        return 'game'

    # Prefer realtime bounds for plotting (convert to the same time base as frame['t'] when needed)
    if run_trial.get('endRealtime', 0) > 0 and run_trial.get('startRealtime') is not None:
        start_rt = run_trial['startRealtime']
        end_rt = run_trial['endRealtime']

        if (
            guess_frame_time_base() == 'game'
            and run_trial.get('startGameTime') is not None
            and run_trial.get('startRealtime') is not None
        ):
            offset = run_trial['startRealtime'] - run_trial['startGameTime']
            return start_rt - offset, end_rt - offset

        return start_rt, end_rt

    # Fallback to gameTime bounds
    if run_trial.get('endGameTime', 0) > 0 and run_trial.get('startGameTime') is not None:
        return run_trial['startGameTime'], run_trial['endGameTime']

    return 0, float('inf')


def extract_centroid_trajectory(data):
    start_time, end_time = get_trial_window(data)
    time_points = set()
    for traj in data.get('trajectories', []):
        for frame in traj.get('frames', []):
            if start_time <= frame['t'] <= end_time:
                time_points.add(frame['t'])
    
    centroids = []
    for t in sorted(time_points):
        positions = []
        for traj in data.get('trajectories', []):
            frame = next((f for f in traj['frames']
                         if abs(f['t'] - t) < 0.01 and start_time <= f['t'] <= end_time), None)
            if frame and frame.get('g', 0) == 1:
                positions.append([frame['x'], frame['y'], frame['z']])
        if positions:
            centroids.append(np.mean(positions, axis=0))
    
    return np.array(centroids) if centroids else np.array([])


def extract_embodied_trajectory(data):
    start_time, end_time = get_trial_window(data)
    embodied_id = data.get('embodiedId', -2147483648)
    
    if embodied_id == -2147483648:
        return np.array([])
    
    for traj in data.get('trajectories', []):
        if traj['id'] == embodied_id:
            positions = []
            for frame in traj.get('frames', []):
                if start_time <= frame['t'] <= end_time:
                    positions.append([frame['x'], frame['y'], frame['z']])
            return np.array(positions) if positions else np.array([])
    
    return np.array([])


def extract_crash_positions(data):
    start_time, end_time = get_trial_window(data)
    crash_positions = []
    
    for traj in data.get('trajectories', []):
        frames = traj.get('frames', [])

        # Initialize prev_e using the last frame strictly before the Run window.
        # This filters out drones that were already crashed before Run started.
        prev_e = 1
        last_pre = None
        last_pre_t = None
        for f in frames:
            t = f.get('t')
            if t is None:
                continue
            if t < start_time and (last_pre_t is None or t > last_pre_t):
                last_pre = f
                last_pre_t = t
        if last_pre is not None:
            prev_e = last_pre.get('e', 1)

        for frame in frames:
            t = frame.get('t')
            if t is None:
                continue
            if start_time <= t <= end_time:
                curr_e = frame.get('e', 1)
                if prev_e == 1 and curr_e == 0:
                    crash_positions.append(np.array([frame['x'], frame['y'], frame['z']]))
                prev_e = curr_e
    
    return crash_positions


def extract_disconnection_points(data):
    start_time, end_time = get_trial_window(data)
    disconnection_points = []
    
    for traj in data.get('trajectories', []):
        for frame in traj.get('frames', []):
            if start_time <= frame['t'] <= end_time:
                if frame.get('g', 1) == 0:
                    disconnection_points.append(np.array([frame['x'], frame['y'], frame['z']]))
    
    return disconnection_points


def disconnected_drones_at_run_end(data):
    start_time, end_time = get_trial_window(data)
    disconnected_ids = []
    unknown_ids = []

    for traj in data.get('trajectories', []):
        tid = traj.get('id')
        frames = traj.get('frames', [])

        last_in_window = None
        last_t = None
        for f in frames:
            t = f.get('t')
            if t is None:
                continue
            if start_time <= t <= end_time and (last_t is None or t > last_t):
                last_in_window = f
                last_t = t

        if last_in_window is None:
            unknown_ids.append(tid)
            continue

        if last_in_window.get('g', 1) == 0:
            disconnected_ids.append(tid)

    return disconnected_ids, unknown_ids


def disconnected_positions_at_run_end(data):
    start_time, end_time = get_trial_window(data)
    positions = []
    disconnected_ids = []
    unknown_ids = []

    for traj in data.get('trajectories', []):
        tid = traj.get('id')
        frames = traj.get('frames', [])

        last_in_window = None
        last_t = None
        for f in frames:
            t = f.get('t')
            if t is None:
                continue
            if start_time <= t <= end_time and (last_t is None or t > last_t):
                last_in_window = f
                last_t = t

        if last_in_window is None:
            unknown_ids.append(tid)
            continue

        if last_in_window.get('g', 1) == 0:
            disconnected_ids.append(tid)
            positions.append(np.array([last_in_window['x'], last_in_window['y'], last_in_window['z']]))

    return positions, disconnected_ids, unknown_ids


# ============================================================
# PLOT 1: SWARM vs EMBODIED
# ============================================================

def plot_paths(swarm, embodied, title, run_info=None, save_path=None):
    fig = plt.figure(figsize=(16, 14))
    
    def plot_2d(ax, view, subtitle):
        if len(swarm) > 0:
            if view == 'top':
                ax.plot(swarm[:, 0], swarm[:, 2], '-', lw=2.5, color='#2ecc71', alpha=0.7, label='Swarm')
                ax.scatter(swarm[0, 0], swarm[0, 2], c='#2ecc71', marker='o', s=120, zorder=5, edgecolor='black')
                ax.scatter(swarm[-1, 0], swarm[-1, 2], c='#2ecc71', marker='s', s=120, zorder=5, edgecolor='black')
            elif view == 'side':
                ax.plot(swarm[:, 2], swarm[:, 1], '-', lw=2.5, color='#2ecc71', alpha=0.7)
                ax.scatter(swarm[0, 2], swarm[0, 1], c='#2ecc71', marker='o', s=120, zorder=5, edgecolor='black')
                ax.scatter(swarm[-1, 2], swarm[-1, 1], c='#2ecc71', marker='s', s=120, zorder=5, edgecolor='black')
            elif view == 'front':
                ax.plot(swarm[:, 0], swarm[:, 1], '-', lw=2.5, color='#2ecc71', alpha=0.7)
                ax.scatter(swarm[0, 0], swarm[0, 1], c='#2ecc71', marker='o', s=120, zorder=5, edgecolor='black')
                ax.scatter(swarm[-1, 0], swarm[-1, 1], c='#2ecc71', marker='s', s=120, zorder=5, edgecolor='black')
        
        if len(embodied) > 0:
            if view == 'top':
                ax.plot(embodied[:, 0], embodied[:, 2], '-', lw=3, color='purple', alpha=0.8, label='Embodied')
                ax.scatter(embodied[0, 0], embodied[0, 2], c='purple', marker='o', s=150, zorder=6, edgecolor='white', lw=2)
                ax.scatter(embodied[-1, 0], embodied[-1, 2], c='purple', marker='s', s=150, zorder=6, edgecolor='white', lw=2)
            elif view == 'side':
                ax.plot(embodied[:, 2], embodied[:, 1], '-', lw=3, color='purple', alpha=0.8)
                ax.scatter(embodied[0, 2], embodied[0, 1], c='purple', marker='o', s=150, zorder=6, edgecolor='white', lw=2)
                ax.scatter(embodied[-1, 2], embodied[-1, 1], c='purple', marker='s', s=150, zorder=6, edgecolor='white', lw=2)
            elif view == 'front':
                ax.plot(embodied[:, 0], embodied[:, 1], '-', lw=3, color='purple', alpha=0.8)
                ax.scatter(embodied[0, 0], embodied[0, 1], c='purple', marker='o', s=150, zorder=6, edgecolor='white', lw=2)
                ax.scatter(embodied[-1, 0], embodied[-1, 1], c='purple', marker='s', s=150, zorder=6, edgecolor='white', lw=2)
        
        ax.set_title(subtitle, fontsize=12, fontweight='bold')
        ax.grid(True, alpha=0.3)
        ax.set_aspect('equal', adjustable='datalim')
    
    ax1 = fig.add_subplot(2, 2, 1)
    plot_2d(ax1, 'top', 'TOP VIEW (X-Z)')
    ax1.set_xlabel('X (m)')
    ax1.set_ylabel('Z (m) - Forward')
    ax1.legend(loc='upper right', fontsize=10)
    
    ax2 = fig.add_subplot(2, 2, 2)
    plot_2d(ax2, 'side', 'SIDE VIEW (Z-Y)')
    ax2.set_xlabel('Z (m) - Forward')
    ax2.set_ylabel('Y (m) - Height')
    
    ax3 = fig.add_subplot(2, 2, 3)
    plot_2d(ax3, 'front', 'FRONT VIEW (X-Y)')
    ax3.set_xlabel('X (m)')
    ax3.set_ylabel('Y (m) - Height')
    
    ax4 = fig.add_subplot(2, 2, 4, projection='3d')
    if len(swarm) > 0:
        ax4.plot(swarm[:, 0], swarm[:, 2], swarm[:, 1], '-', lw=2.5, color='#2ecc71', alpha=0.7, label='Swarm')
    if len(embodied) > 0:
        ax4.plot(embodied[:, 0], embodied[:, 2], embodied[:, 1], '-', lw=3, color='purple', alpha=0.8, label='Embodied')
    ax4.set_xlabel('X (m)')
    ax4.set_ylabel('Z (m)')
    ax4.set_zlabel('Y (m)')
    ax4.set_title('3D VIEW', fontsize=12, fontweight='bold')
    ax4.view_init(elev=30, azim=45)
    ax4.legend(fontsize=9)
    
    title_text = f'{title}\nSwarm Centroid vs Embodied Drone  ○ Start  □ End'
    if run_info:
        title_text += f'\n{run_info}'
    fig.suptitle(title_text, fontsize=14, fontweight='bold')
    plt.tight_layout()
    
    if save_path:
        plt.savefig(save_path, dpi=300, bbox_inches='tight', facecolor='white')
        print(f"Saved: {save_path}")
    
    return fig


# ============================================================
# PLOT 2: CRASHES & DISCONNECTIONS
# ============================================================

def plot_events(swarm, crashes, disconnections, title, run_info=None, save_path=None):
    fig = plt.figure(figsize=(16, 14))
    
    def plot_2d(ax, view, subtitle):
        # Trajectory background
        if len(swarm) > 0:
            if view == 'top':
                ax.plot(swarm[:, 0], swarm[:, 2], '-', lw=1.5, color='gray', alpha=0.4, label='Trajectory')
            elif view == 'side':
                ax.plot(swarm[:, 2], swarm[:, 1], '-', lw=1.5, color='gray', alpha=0.4)
            elif view == 'front':
                ax.plot(swarm[:, 0], swarm[:, 1], '-', lw=1.5, color='gray', alpha=0.4)
        
        # Crashes
        for i, c in enumerate(crashes):
            label = 'Crashes' if i == 0 else None
            if view == 'top':
                ax.scatter(c[0], c[2], c='red', marker='X', s=250, edgecolor='black', lw=2, zorder=10, label=label)
            elif view == 'side':
                ax.scatter(c[2], c[1], c='red', marker='X', s=250, edgecolor='black', lw=2, zorder=10)
            elif view == 'front':
                ax.scatter(c[0], c[1], c='red', marker='X', s=250, edgecolor='black', lw=2, zorder=10)
        
        # Disconnected drones at Run end (one marker per drone)
        if disconnections:
            disc = np.array(disconnections)
            if view == 'top':
                ax.scatter(disc[:, 0], disc[:, 2], c='orange', marker='o', s=120, alpha=0.75,
                           label='Disconnected @ Run end')
            elif view == 'side':
                ax.scatter(disc[:, 2], disc[:, 1], c='orange', marker='o', s=120, alpha=0.75)
            elif view == 'front':
                ax.scatter(disc[:, 0], disc[:, 1], c='orange', marker='o', s=120, alpha=0.75)
        
        ax.set_title(subtitle, fontsize=12, fontweight='bold')
        ax.grid(True, alpha=0.3)
        ax.set_aspect('equal', adjustable='datalim')
    
    ax1 = fig.add_subplot(2, 2, 1)
    plot_2d(ax1, 'top', 'TOP VIEW (X-Z)')
    ax1.set_xlabel('X (m)')
    ax1.set_ylabel('Z (m) - Forward')
    ax1.legend(loc='upper right', fontsize=10)
    
    ax2 = fig.add_subplot(2, 2, 2)
    plot_2d(ax2, 'side', 'SIDE VIEW (Z-Y)')
    ax2.set_xlabel('Z (m) - Forward')
    ax2.set_ylabel('Y (m) - Height')
    
    ax3 = fig.add_subplot(2, 2, 3)
    plot_2d(ax3, 'front', 'FRONT VIEW (X-Y)')
    ax3.set_xlabel('X (m)')
    ax3.set_ylabel('Y (m) - Height')
    
    ax4 = fig.add_subplot(2, 2, 4, projection='3d')
    if len(swarm) > 0:
        ax4.plot(swarm[:, 0], swarm[:, 2], swarm[:, 1], '-', lw=1.5, color='gray', alpha=0.4)
    for c in crashes:
        ax4.scatter(c[0], c[2], c[1], c='red', marker='X', s=250, edgecolor='black', lw=2, zorder=10)
    if disconnections:
        disc = np.array(disconnections)
        ax4.scatter(disc[:, 0], disc[:, 2], disc[:, 1], c='orange', marker='o', s=60, alpha=0.6)
    ax4.set_xlabel('X (m)')
    ax4.set_ylabel('Z (m)')
    ax4.set_zlabel('Y (m)')
    ax4.set_title('3D VIEW', fontsize=12, fontweight='bold')
    ax4.view_init(elev=30, azim=45)
    
    title_text = f'{title}\n✕ Crashes ({len(crashes)})  ● Disconnected @ Run end ({len(disconnections)})'
    if run_info:
        title_text += f'\n{run_info}'
    fig.suptitle(title_text, fontsize=14, fontweight='bold')
    plt.tight_layout()
    
    if save_path:
        plt.savefig(save_path, dpi=300, bbox_inches='tight', facecolor='white')
        print(f"Saved: {save_path}")
    
    return fig


# ============================================================
# MAIN
# ============================================================

def main():
    if len(sys.argv) < 2:
        print("Usage: python view_trajectory.py <trajectory.json>")
        print("Example: python view_trajectory.py Gabriel_B_1.json")
        sys.exit(1)
    
    filepath = Path(sys.argv[1])
    if not filepath.exists():
        print(f"Error: {filepath} not found")
        sys.exit(1)
    
    print(f"Loading: {filepath}")
    with open(filepath) as f:
        data = json.load(f)
    
    # Extract data
    swarm = extract_centroid_trajectory(data)
    embodied = extract_embodied_trajectory(data)
    crashes = extract_crash_positions(data)
    disconnected_positions, disconnected_ids, unknown_disc_ids = disconnected_positions_at_run_end(data)
    
    title = filepath.stem
    
    # Get run time info
    trials = data.get('trials') or []
    run_trial = next((t for t in trials if t.get('label') == 'Run'), None)
    run_info = None
    if run_trial and run_trial.get('startRealtime') is not None and run_trial.get('endRealtime') is not None:
        real_duration = run_trial['endRealtime'] - run_trial['startRealtime']
        run_info = f"Run: {run_trial['startRealtime']:.2f}s → {run_trial['endRealtime']:.2f}s (Duration: {real_duration:.2f}s)"
    
    print(f"  Swarm points: {len(swarm)}")
    print(f"  Embodied points: {len(embodied)}")
    print(f"  Crashes: {len(crashes)}")
    print(f"  Disconnected drones at Run end: {len(disconnected_ids)}")
    if disconnected_ids:
        print(f"    IDs: {sorted(disconnected_ids)}")
    if unknown_disc_ids:
        print(f"    (No in-window frames for: {sorted(unknown_disc_ids)})")
    
    # Generate plots
    plot_paths(swarm, embodied, title, run_info, f"{title}_paths.png")
    plot_events(swarm, crashes, disconnected_positions, title, run_info, f"{title}_events.png")
    
    plt.show()


if __name__ == "__main__":
    main()
