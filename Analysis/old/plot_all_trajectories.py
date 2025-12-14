"""
Plot All Trajectories
Loads all JSON trajectory files and plots them in a clean 3D visualization.
Each trajectory gets its own color, shows start/end points.
"""

import json
import matplotlib.pyplot as plt
import numpy as np
from pathlib import Path
from mpl_toolkits.mplot3d import Axes3D


def load_trajectory_data(json_path):
    """Load trajectory JSON file."""
    with open(json_path, 'r') as f:
        return json.load(f)


def compute_swarm_centroid(data, filter_trial=True):
    """Compute centroid trajectory of connected drones."""
    # Get trial window if available
    start_time = 0
    end_time = float('inf')
    
    if filter_trial and data.get('trials'):
        run_trial = next((t for t in data['trials'] if t['label'] == 'Run'), None)
        if run_trial:
            start_time = run_trial['startGameTime']
            end_time = run_trial['endGameTime']
    
    # Collect all time points
    time_points = set()
    for traj in data['trajectories']:
        for frame in traj['frames']:
            if start_time <= frame['t'] <= end_time:
                time_points.add(frame['t'])
    
    time_points = sorted(time_points)
    
    # Compute centroid at each time
    cx, cy, cz = [], [], []
    
    for t in time_points:
        positions = []
        for traj in data['trajectories']:
            frame = next((f for f in traj['frames'] if abs(f['t'] - t) < 0.01), None)
            if frame and frame['g'] == 1:  # Only connected drones
                positions.append([frame['x'], frame['y'], frame['z']])
        
        if positions:
            centroid = np.mean(positions, axis=0)
            cx.append(centroid[0])
            cy.append(centroid[1])
            cz.append(centroid[2])
    
    return np.array(cx), np.array(cy), np.array(cz)


def plot_all_trajectories_3d():
    """Plot all trajectories in one 3D figure with different colors."""
    # Find all trajectory files
    trajectory_dir = Path(__file__).parent.parent/ "Trajectories"
    
    if not trajectory_dir.exists():
        print(f"Trajectory directory not found: {trajectory_dir}")
        return
    
    json_files = sorted(trajectory_dir.glob("*.json"))
    
    if not json_files:
        print(f"No trajectory files found in {trajectory_dir}")
        return
    
    print(f"Found {len(json_files)} trajectory files")
    
    # Create figure
    fig = plt.figure(figsize=(16, 12))
    ax = fig.add_subplot(111, projection='3d')
    
    # Color palette
    colors = plt.cm.tab10(np.linspace(0, 1, len(json_files)))
    
    all_x, all_y, all_z = [], [], []
    
    # Plot each trajectory
    for i, json_path in enumerate(json_files):
        print(f"Loading: {json_path.name}")
        data = load_trajectory_data(json_path)
        
        # Compute centroid
        cx, cy, cz = compute_swarm_centroid(data, filter_trial=True)
        
        if len(cx) == 0:
            print(f"  Skipping {json_path.name} - no connected drones")
            continue
        
        # Store for axis limits
        all_x.extend(cx)
        all_y.extend(cy)
        all_z.extend(cz)
        
        # Extract label from filename
        label = json_path.stem  # filename without extension
        
        # Plot trajectory
        ax.plot(cx, cz, cy, '-', linewidth=2.5, alpha=0.8, color=colors[i], label=label)
        
        # Start point (circle)
        ax.scatter(cx[0], cz[0], cy[0], c=[colors[i]], marker='o', s=200, 
                   edgecolors='black', linewidths=2, zorder=10)
        
        # End point (square)
        ax.scatter(cx[-1], cz[-1], cy[-1], c=[colors[i]], marker='s', s=200,
                   edgecolors='black', linewidths=2, zorder=10)
    
    # Set labels
    ax.set_xlabel('X Position (m) - Left/Right', fontsize=13, fontweight='bold')
    ax.set_ylabel('Z Position (m) - Forward/Back', fontsize=13, fontweight='bold')
    ax.set_zlabel('Y Position (m) - Height', fontsize=13, fontweight='bold')
    ax.set_title('All Trajectories - Swarm Centroid Paths', fontsize=16, fontweight='bold', pad=20)
    
    # Equal aspect ratio
    if all_x:
        all_x = np.array(all_x)
        all_y = np.array(all_y)
        all_z = np.array(all_z)
        
        max_range = np.array([all_x.max()-all_x.min(), 
                              all_z.max()-all_z.min(), 
                              all_y.max()-all_y.min()]).max() / 2.0
        
        mid_x = (all_x.max() + all_x.min()) * 0.5
        mid_z = (all_z.max() + all_z.min()) * 0.5
        mid_y = (all_y.max() + all_y.min()) * 0.5
        
        ax.set_xlim(mid_x - max_range, mid_x + max_range)
        ax.set_ylim(mid_z - max_range, mid_z + max_range)
        ax.set_zlim(mid_y - max_range, mid_y + max_range)
    
    # Legend
    ax.legend(loc='upper left', fontsize=10, framealpha=0.9)
    
    # Add grid
    ax.grid(True, alpha=0.3)
    
    plt.tight_layout()
    
    # Save figure
    output_path = Path(__file__).parent / "plots" / "all_trajectories_3d.png"
    output_path.parent.mkdir(exist_ok=True)
    plt.savefig(output_path, dpi=300, bbox_inches='tight')
    print(f"\nSaved plot to: {output_path}")
    
    plt.show()


if __name__ == "__main__":
    plot_all_trajectories_3d()
