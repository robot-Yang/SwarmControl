"""
Swarm Trajectory Plotter
Loads JSON trajectory data from SwarmTrajectoryRecorder and creates visualizations.


Usage:
    python plot_trajectories.py path/to/trajectory.json
   
Or run interactively to select the latest file automatically.
"""


import json
import matplotlib.pyplot as plt
import numpy as np
from pathlib import Path
import sys
from mpl_toolkits.mplot3d import Axes3D




def load_trajectory_data(json_path):
    """Load and parse trajectory JSON file."""
    with open(json_path, 'r') as f:
        data = json.load(f)
    print(f"Loaded: {json_path.name}")
    print(f"Scene: {data['scene']}")
    print(f"PID: {data['pid']}")
    print(f"Haptics: {data['haptics']}, Order: {data['order']}")
    print(f"Sample rate: {data['sampleHz']} Hz")
    print(f"Number of drones: {len(data['trajectories'])}")
    if data.get('embodiedId', -2147483648) != -2147483648:
        print(f"Embodied drone: {data['embodiedName']} (ID: {data['embodiedId']})")
    if data.get('trials'):
        print(f"Trials recorded: {len(data['trials'])}")
        for trial in data['trials']:
            duration = trial['endGameTime'] - trial['startGameTime']
            print(f"  - {trial['label']}: {duration:.2f}s")
    print()
    return data




def compute_swarm_centroid_trajectory(data, height_deadzone=1.0):
    """Compute centroid of connected drones at each time point.
   
    Args:
        data: Trajectory data dictionary
        height_deadzone: If > 0, clamps height values within +/- deadzone of the initial height
    """
    # Collect all unique time points
    time_points = set()
    for traj in data['trajectories']:
        for frame in traj['frames']:
            time_points.add(frame['t'])
   
    time_points = sorted(time_points)
   
    # Compute centroid at each time point
    centroid_x = []
    centroid_y = []
    centroid_z = []
    valid_times = []
    initial_height = None
   
    for t in time_points:
        positions = []
        for traj in data['trajectories']:
            # Find frame at this time
            frame = next((f for f in traj['frames'] if abs(f['t'] - t) < 0.01), None)
            if frame and frame['g'] == 1:  # Only connected drones
                positions.append([frame['x'], frame['y'], frame['z']])
       
        if len(positions) > 0:
            centroid = np.mean(positions, axis=0)
           
            # Apply height deadzone if specified
            if height_deadzone > 0:
                if initial_height is None:
                    initial_height = centroid[1]
                # Clamp height to initial +/- deadzone
                height_deviation = abs(centroid[1] - initial_height)
                if height_deviation < height_deadzone:
                    centroid[1] = initial_height
           
            centroid_x.append(centroid[0])
            centroid_y.append(centroid[1])
            centroid_z.append(centroid[2])
            valid_times.append(t)
   
    return np.array(valid_times), np.array(centroid_x), np.array(centroid_y), np.array(centroid_z)




def plot_2d_trajectories(data, show_connectivity=True, height_deadzone=0.2):
    """Plot 2D (XZ plane) swarm centroid trajectory.
   
    Args:
        height_deadzone: Clamp height variations smaller than this value (meters). Default 0.2m.
    """
    fig, ax = plt.subplots(figsize=(12, 10))
   
    # Compute swarm centroid
    times, cx, cy, cz = compute_swarm_centroid_trajectory(data, height_deadzone=height_deadzone)
   
    if len(cx) == 0:
        ax.text(0.5, 0.5, 'No connected drones found',
                horizontalalignment='center', verticalalignment='center',
                transform=ax.transAxes, fontsize=14)
        return fig
   
    # Plot centroid trajectory
    ax.plot(cx, cz, 'b-', linewidth=2, alpha=0.8, label='Swarm Centroid')
   
    # Mark start and end
    ax.plot(cx[0], cz[0], 'go', markersize=12, label='Start', zorder=5)
    ax.plot(cx[-1], cz[-1], 'rs', markersize=12, label='End', zorder=5)
   
    # Add time markers every N seconds
    time_interval = max(1, int(len(times) / 10))  # ~10 markers
    for i in range(0, len(times), time_interval):
        ax.plot(cx[i], cz[i], 'ko', markersize=4, alpha=0.5)
        ax.text(cx[i], cz[i], f' {times[i]:.1f}s', fontsize=8, alpha=0.7)
   
    ax.set_xlabel('X Position (m)', fontsize=12)
    ax.set_ylabel('Z Position (m) - Forward/Back', fontsize=12)
    ax.set_title(f"Swarm Centroid Trajectory (2D Top View)\n{data['scene']} - {data['pid']} - {data['haptics']}/{data['order']}", fontsize=14)
    ax.grid(True, alpha=0.3)
    ax.axis('equal')
    ax.legend()
   
    plt.tight_layout()
    return fig




def plot_3d_trajectories(data, height_deadzone=0.2):
    """Plot 3D swarm centroid trajectory.
   
    Args:
        height_deadzone: Clamp height variations smaller than this value (meters). Default 0.2m.
    """
    fig = plt.figure(figsize=(14, 10))
    ax = fig.add_subplot(111, projection='3d')
   
    # Compute swarm centroid
    times, cx, cy, cz = compute_swarm_centroid_trajectory(data, height_deadzone=height_deadzone)
   
    if len(cx) == 0:
        ax.text2D(0.5, 0.5, 'No connected drones found',
                  horizontalalignment='center', verticalalignment='center',
                  transform=ax.transAxes, fontsize=14)
        return fig
   
    # Plot centroid trajectory
    # Unity: x=left/right, y=height, z=forward/back
    # Matplotlib 3D: plot(X, Y, Z) where Z-axis is vertical
    # So we map: Unity.x → Plot.X, Unity.z → Plot.Y, Unity.y → Plot.Z (vertical)
    ax.plot(cx, cz, cy, 'b-', linewidth=2, alpha=0.8, label='Swarm Centroid')
   
    # Mark start and end
    ax.scatter(cx[0], cz[0], cy[0], c='green', marker='o', s=150, label='Start', zorder=5)
    ax.scatter(cx[-1], cz[-1], cy[-1], c='red', marker='s', s=150, label='End', zorder=5)
   
    # Add time markers
    time_interval = max(1, int(len(times) / 10))
    for i in range(0, len(times), time_interval):
        ax.scatter(cx[i], cz[i], cy[i], c='black', marker='o', s=30, alpha=0.5)
   
    # Print height range for debugging
    print(f"Height (Unity Y) range: {cy.min():.3f} to {cy.max():.3f} (variation: {cy.max()-cy.min():.3f}m)")
   
    ax.set_xlabel('X Position (m) - Left/Right', fontsize=12)
    ax.set_ylabel('Z Position (m) - Forward/Back', fontsize=12)
    ax.set_zlabel('Y Position (m) - Height', fontsize=12)
    ax.set_title(f"Swarm Centroid Trajectory (3D)\n{data['scene']} - {data['pid']}", fontsize=14)
    ax.legend()
   
    plt.tight_layout()
    return fig




def plot_connectivity_over_time(data):
    """Plot network connectivity statistics over time."""
    fig, (ax1, ax2) = plt.subplots(2, 1, figsize=(14, 10), sharex=True)
   
    # Collect time series data
    time_points = set()
    for traj in data['trajectories']:
        for frame in traj['frames']:
            time_points.add(frame['t'])
   
    time_points = sorted(time_points)
    connected_count = []
    disconnected_count = []
   
    for t in time_points:
        conn = 0
        disc = 0
        for traj in data['trajectories']:
            # Find frame at this time
            frame = next((f for f in traj['frames'] if abs(f['t'] - t) < 0.01), None)
            if frame:
                if frame['g'] == 1:
                    conn += 1
                else:
                    disc += 1
        connected_count.append(conn)
        disconnected_count.append(disc)
   
    # Plot counts
    ax1.plot(time_points, connected_count, 'g-', linewidth=2, label='Connected')
    ax1.plot(time_points, disconnected_count, 'r-', linewidth=2, label='Disconnected')
    ax1.set_ylabel('Number of Drones', fontsize=12)
    ax1.set_title('Network Connectivity Over Time', fontsize=14)
    ax1.legend()
    ax1.grid(True, alpha=0.3)
   
    # Plot connectivity ratio
    connectivity_ratio = [c / (c + d) if (c + d) > 0 else 0 for c, d in zip(connected_count, disconnected_count)]
    ax2.plot(time_points, connectivity_ratio, 'b-', linewidth=2)
    ax2.axhline(y=0.8, color='orange', linestyle='--', label='80% threshold')
    ax2.set_xlabel('Time (s)', fontsize=12)
    ax2.set_ylabel('Connectivity Ratio', fontsize=12)
    ax2.set_ylim([0, 1])
    ax2.legend()
    ax2.grid(True, alpha=0.3)
   
    # Mark trial windows if available
    if data.get('trials'):
        for trial in data['trials']:
            for ax in [ax1, ax2]:
                ax.axvspan(trial['startGameTime'], trial['endGameTime'], alpha=0.2, color='yellow', label=f"Trial: {trial['label']}")
   
    plt.tight_layout()
    return fig




def plot_height_profiles(data):
    """Plot height (Unity Y-axis) over time for all drones."""
    fig, ax = plt.subplots(figsize=(14, 8))
   
    embodied_id = data.get('embodiedId', -2147483648)
   
    for traj in data['trajectories']:
        drone_id = traj['id']
        frames = traj['frames']
       
        if len(frames) == 0:
            continue
       
        times = [f['t'] for f in frames]
        heights = [f['y'] for f in frames]  # Unity Y is height
       
        is_embodied = (drone_id == embodied_id)
        color = 'blue' if is_embodied else 'gray'
        linewidth = 2 if is_embodied else 0.5
        alpha = 1.0 if is_embodied else 0.3
       
        ax.plot(times, heights, color=color, alpha=alpha, linewidth=linewidth, label=traj['name'] if is_embodied else None)
   
    ax.set_xlabel('Time (s)', fontsize=12)
    ax.set_ylabel('Height (m)', fontsize=12)
    ax.set_title(f"Drone Height Profiles Over Time\n{data['scene']}", fontsize=14)
    ax.grid(True, alpha=0.3)
   
    if embodied_id != -2147483648:
        ax.legend()
   
    plt.tight_layout()
    return fig




def find_latest_trajectory():
    """Find the most recent trajectory JSON file."""
    trajectory_dir = Path(__file__).parent.parent / "SoundMapping" / "SoundMappingUnity" / "Assets" / "Trajectories"
   
    if not trajectory_dir.exists():
        print(f"Trajectory directory not found: {trajectory_dir}")
        return None
   
    json_files = list(trajectory_dir.glob("*_traj.json"))
   
    if not json_files:
        print(f"No trajectory files found in {trajectory_dir}")
        return None
   
    # Sort by modification time, newest first
    latest = max(json_files, key=lambda p: p.stat().st_mtime)
    return latest




def main():
    if len(sys.argv) > 1:
        json_path = Path(sys.argv[1])
    else:
        print("No file specified, searching for latest trajectory...")
        json_path = find_latest_trajectory()
        if json_path is None:
            print("\nUsage: python plot_trajectories.py path/to/trajectory.json")
            return
   
    if not json_path.exists():
        print(f"Error: File not found: {json_path}")
        return
   
    # Load data
    data = load_trajectory_data(json_path)
   
    # Create plots
    print("Generating plots...")
   
    #print("  - 2D trajectories with connectivity...")
    #fig1 = plot_2d_trajectories(data, show_connectivity=True)
   
    print("  - 3D swarm centroid trajectory...")
    fig2 = plot_3d_trajectories(data)
   
    #print("  - Connectivity over time...")
    #fig3 = plot_connectivity_over_time(data)
   
    #print("  - Height profiles...")
    #fig4 = plot_height_profiles(data)
   
    print("\nDone! Close the plot windows to exit.")
    plt.show()




if __name__ == "__main__":
    main()



