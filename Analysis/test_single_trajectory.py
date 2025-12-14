
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
    if data.get('trials'):
        run_trial = next((t for t in data['trials'] if t['label'] == 'Run'), None)
        if run_trial and run_trial.get('endGameTime', 0) > 0:
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
        prev_e = 1
        for frame in traj.get('frames', []):
            if start_time <= frame['t'] <= end_time:
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


# ============================================================
# PLOT 1: SWARM vs EMBODIED
# ============================================================

def plot_paths(swarm, embodied, title, save_path=None):
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
    
    fig.suptitle(f'{title}\nSwarm Centroid vs Embodied Drone  ○ Start  □ End', fontsize=14, fontweight='bold')
    plt.tight_layout()
    
    if save_path:
        plt.savefig(save_path, dpi=300, bbox_inches='tight', facecolor='white')
        print(f"Saved: {save_path}")
    
    return fig


# ============================================================
# PLOT 2: CRASHES & DISCONNECTIONS
# ============================================================

def plot_events(swarm, crashes, disconnections, title, save_path=None):
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
        
        # Disconnections
        if disconnections:
            disc = np.array(disconnections)
            step = max(1, len(disc) // 100)
            disc = disc[::step]
            if view == 'top':
                ax.scatter(disc[:, 0], disc[:, 2], c='orange', marker='o', s=60, alpha=0.6, label='Disconnections')
            elif view == 'side':
                ax.scatter(disc[:, 2], disc[:, 1], c='orange', marker='o', s=60, alpha=0.6)
            elif view == 'front':
                ax.scatter(disc[:, 0], disc[:, 1], c='orange', marker='o', s=60, alpha=0.6)
        
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
        step = max(1, len(disc) // 100)
        disc = disc[::step]
        ax4.scatter(disc[:, 0], disc[:, 2], disc[:, 1], c='orange', marker='o', s=40, alpha=0.5)
    ax4.set_xlabel('X (m)')
    ax4.set_ylabel('Z (m)')
    ax4.set_zlabel('Y (m)')
    ax4.set_title('3D VIEW', fontsize=12, fontweight='bold')
    ax4.view_init(elev=30, azim=45)
    
    fig.suptitle(f'{title}\n✕ Crashes ({len(crashes)})  ● Disconnections ({len(disconnections)} points)',
                 fontsize=14, fontweight='bold')
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
    disconnections = extract_disconnection_points(data)
    
    title = filepath.stem
    
    print(f"  Swarm points: {len(swarm)}")
    print(f"  Embodied points: {len(embodied)}")
    print(f"  Crashes: {len(crashes)}")
    print(f"  Disconnection points: {len(disconnections)}")
    
    # Generate plots
    plot_paths(swarm, embodied, title, f"{title}_paths.png")
    plot_events(swarm, crashes, disconnections, title, f"{title}_events.png")
    
    plt.show()


if __name__ == "__main__":
    main()
