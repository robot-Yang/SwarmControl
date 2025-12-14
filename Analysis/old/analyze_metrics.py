"""
Trajectory Metrics Analyzer
Computes comprehensive metrics from trajectory JSON files.
"""

import json
import numpy as np
from pathlib import Path
from typing import Dict, List, Tuple
import re


def parse_filename(filename: str) -> Dict:
    """
    Parse experimental conditions from filename.
    
    Expected naming convention:
        [ParticipantName]_[B/C]_[TrialNumber].json
        
    Examples:
        Darius_C_1.json  → participant='Darius', condition='Controller', trial=1
        Darius_B_2.json  → participant='Darius', condition='Body', trial=2
        Gabriel_C_3.json → participant='Gabriel', condition='Controller', trial=3
        CustomName.json  → defaults to unknown values
    """
    # Try to parse structured filename: Name_B/C_Number
    pattern = r'([A-Za-z]+)_(B|C)_(\d+)'
    match = re.match(pattern, filename)
    
    if match:
        condition_map = {'B': 'Body', 'C': 'Controller'}
        return {
            'participant': match.group(1),
            'condition': condition_map[match.group(2)],
            'trial': int(match.group(3))
        }
    else:
        # Fallback for custom names
        return {
            'participant': 'Unknown',
            'condition': 'Unknown',
            'trial': 0
        }


def load_trajectory(json_path: Path) -> Dict:
    """Load trajectory data from JSON file."""
    with open(json_path, 'r') as f:
        return json.load(f)


def get_trial_window(data: Dict) -> Tuple[float, float]:
    """Get start and end time of the Run trial."""
    if data.get('trials'):
        run_trial = next((t for t in data['trials'] if t['label'] == 'Run'), None)
        if run_trial and run_trial.get('endGameTime', 0) > 0:
            return run_trial['startGameTime'], run_trial['endGameTime']
    return 0, float('inf')


def filter_frames_by_trial(frames: List, start_time: float, end_time: float) -> List:
    """Filter frames to only include those within trial window."""
    return [f for f in frames if start_time <= f['t'] <= end_time]


# ==================== PERFORMANCE METRICS ====================

def calculate_performance_metrics(data: Dict) -> Dict:
    """Calculate elapsed time and collectibles efficiency."""
    metrics = {
        'elapsed_time': data.get('elapsedTime', 0),
        'collectibles_picked_up': data.get('collectiblesPickedUp', 0),
        'collectibles_per_second': 0,
        'haptics': data.get('haptics', 'NH'),
        'order': data.get('order', 'NO'),
        'pid': data.get('pid', 'Unknown'),
        'scene': data.get('scene', 'Unknown')
    }
    
    if metrics['elapsed_time'] > 0:
        metrics['collectibles_per_second'] = metrics['collectibles_picked_up'] / metrics['elapsed_time']
    
    return metrics


# ==================== TRAJECTORY ANALYSIS ====================

def calculate_path_metrics(data: Dict) -> Dict:
    """Calculate path efficiency metrics for the swarm centroid."""
    start_time, end_time = get_trial_window(data)
    
    # Collect centroid positions over time
    time_points = set()
    for traj in data['trajectories']:
        frames = filter_frames_by_trial(traj['frames'], start_time, end_time)
        for frame in frames:
            time_points.add(frame['t'])
    
    time_points = sorted(time_points)
    
    if len(time_points) < 2:
        return {
            'total_distance': 0,
            'direct_distance': 0,
            'path_efficiency': 0,
            'avg_speed': 0,
            'path_smoothness': 0
        }
    
    # Calculate centroid at each time point
    centroids = []
    for t in time_points:
        positions = []
        for traj in data['trajectories']:
            frames = filter_frames_by_trial(traj['frames'], start_time, end_time)
            frame = next((f for f in frames if abs(f['t'] - t) < 0.01), None)
            if frame and frame['g'] == 1:  # Only connected drones
                positions.append([frame['x'], frame['y'], frame['z']])
        
        if positions:
            centroids.append(np.mean(positions, axis=0))
    
    if len(centroids) < 2:
        return {
            'total_distance': 0,
            'direct_distance': 0,
            'path_efficiency': 0,
            'avg_speed': 0,
            'path_smoothness': 0
        }
    
    centroids = np.array(centroids)
    
    # Total distance traveled
    distances = np.sqrt(np.sum(np.diff(centroids, axis=0)**2, axis=1))
    total_distance = np.sum(distances)
    
    # Direct distance (start to end)
    direct_distance = np.sqrt(np.sum((centroids[-1] - centroids[0])**2))
    
    # Path efficiency (direct / total)
    path_efficiency = direct_distance / total_distance if total_distance > 0 else 0
    
    # Average speed
    elapsed_time = data.get('elapsedTime', 0)
    avg_speed = total_distance / elapsed_time if elapsed_time > 0 else 0
    
    # Path smoothness (variance of direction changes)
    if len(distances) > 1:
        # Calculate angle changes between consecutive segments
        vectors = np.diff(centroids, axis=0)
        vectors_norm = vectors / (np.linalg.norm(vectors, axis=1, keepdims=True) + 1e-10)
        
        angles = []
        for i in range(len(vectors_norm) - 1):
            cos_angle = np.dot(vectors_norm[i], vectors_norm[i+1])
            cos_angle = np.clip(cos_angle, -1, 1)
            angle = np.arccos(cos_angle)
            angles.append(angle)
        
        path_smoothness = np.std(angles) if angles else 0
    else:
        path_smoothness = 0
    
    return {
        'total_distance': float(total_distance),
        'direct_distance': float(direct_distance),
        'path_efficiency': float(path_efficiency),
        'avg_speed': float(avg_speed),
        'path_smoothness': float(path_smoothness)
    }


def calculate_swarm_compactness(data: Dict) -> Dict:
    """Calculate swarm compactness metrics."""
    start_time, end_time = get_trial_window(data)
    
    # Collect positions at each time point
    time_points = set()
    for traj in data['trajectories']:
        frames = filter_frames_by_trial(traj['frames'], start_time, end_time)
        for frame in frames:
            time_points.add(frame['t'])
    
    time_points = sorted(time_points)
    
    variances = []
    avg_distances_from_centroid = []
    
    for t in time_points:
        positions = []
        for traj in data['trajectories']:
            frames = filter_frames_by_trial(traj['frames'], start_time, end_time)
            frame = next((f for f in frames if abs(f['t'] - t) < 0.01), None)
            if frame and frame['g'] == 1:  # Only connected drones
                positions.append([frame['x'], frame['y'], frame['z']])
        
        if len(positions) > 1:
            positions = np.array(positions)
            centroid = np.mean(positions, axis=0)
            
            # Variance in positions
            variance = np.mean(np.sum((positions - centroid)**2, axis=1))
            variances.append(variance)
            
            # Average distance from centroid
            distances = np.sqrt(np.sum((positions - centroid)**2, axis=1))
            avg_distances_from_centroid.append(np.mean(distances))
    
    return {
        'avg_swarm_variance': float(np.mean(variances)) if variances else 0,
        'max_swarm_variance': float(np.max(variances)) if variances else 0,
        'avg_distance_from_centroid': float(np.mean(avg_distances_from_centroid)) if avg_distances_from_centroid else 0,
        'swarm_compactness_std': float(np.std(avg_distances_from_centroid)) if avg_distances_from_centroid else 0
    }


# ==================== NETWORK CONNECTIVITY ====================

def calculate_connectivity_metrics(data: Dict) -> Dict:
    """Calculate network connectivity metrics."""
    start_time, end_time = get_trial_window(data)
    
    total_samples = 0
    connected_samples = 0
    disconnection_events = 0
    was_connected = True
    disconnection_durations = []
    disconnect_start = None
    
    drone_disconnect_count = {}
    
    # Collect time points
    time_points = set()
    for traj in data['trajectories']:
        frames = filter_frames_by_trial(traj['frames'], start_time, end_time)
        for frame in frames:
            time_points.add(frame['t'])
    
    time_points = sorted(time_points)
    
    for t in time_points:
        total_samples += 1
        
        # Count connected drones at this time
        connected_count = 0
        total_drones = 0
        
        for traj in data['trajectories']:
            frames = filter_frames_by_trial(traj['frames'], start_time, end_time)
            frame = next((f for f in frames if abs(f['t'] - t) < 0.01), None)
            if frame:
                total_drones += 1
                if frame['g'] == 1:
                    connected_count += 1
                else:
                    # Track which drones disconnect
                    drone_id = traj['id']
                    drone_disconnect_count[drone_id] = drone_disconnect_count.get(drone_id, 0) + 1
        
        # Check if all drones connected
        all_connected = (connected_count == total_drones and total_drones > 0)
        
        if all_connected:
            connected_samples += 1
            if not was_connected and disconnect_start is not None:
                # End of disconnection event
                disconnection_durations.append(t - disconnect_start)
                disconnect_start = None
            was_connected = True
        else:
            if was_connected:
                # Start of disconnection event
                disconnection_events += 1
                disconnect_start = t
            was_connected = False
    
    # Calculate percentage
    connectivity_percentage = (connected_samples / total_samples * 100) if total_samples > 0 else 0
    
    # Find drone that disconnects most
    most_disconnected_drone = max(drone_disconnect_count.items(), key=lambda x: x[1]) if drone_disconnect_count else (None, 0)
    
    return {
        'connectivity_percentage': float(connectivity_percentage),
        'disconnection_events': disconnection_events,
        'avg_disconnection_duration': float(np.mean(disconnection_durations)) if disconnection_durations else 0,
        'max_disconnection_duration': float(np.max(disconnection_durations)) if disconnection_durations else 0,
        'most_disconnected_drone_id': most_disconnected_drone[0],
        'most_disconnected_drone_count': most_disconnected_drone[1]
    }


# ==================== EMBODIED DRONE BEHAVIOR ====================

def calculate_embodied_metrics(data: Dict) -> Dict:
    """Calculate embodied drone behavior metrics."""
    start_time, end_time = get_trial_window(data)
    
    embodied_id = data.get('embodiedId', -2147483648)
    
    if embodied_id == -2147483648:
        return {
            'avg_distance_from_swarm': 0,
            'max_distance_from_swarm': 0,
            'embodied_speed': 0,
            'swarm_speed': 0,
            'speed_difference': 0,
            'leading_percentage': 0
        }
    
    # Find embodied drone trajectory
    embodied_traj = next((t for t in data['trajectories'] if t['id'] == embodied_id), None)
    if not embodied_traj:
        return {
            'avg_distance_from_swarm': 0,
            'max_distance_from_swarm': 0,
            'embodied_speed': 0,
            'swarm_speed': 0,
            'speed_difference': 0,
            'leading_percentage': 0
        }
    
    embodied_frames = filter_frames_by_trial(embodied_traj['frames'], start_time, end_time)
    
    distances_from_swarm = []
    embodied_positions = []
    swarm_centroids = []
    times = []
    
    for frame in embodied_frames:
        t = frame['t']
        embodied_pos = np.array([frame['x'], frame['y'], frame['z']])
        
        # Calculate swarm centroid (excluding embodied drone)
        swarm_positions = []
        for traj in data['trajectories']:
            if traj['id'] == embodied_id:
                continue
            frames = filter_frames_by_trial(traj['frames'], start_time, end_time)
            other_frame = next((f for f in frames if abs(f['t'] - t) < 0.01), None)
            if other_frame and other_frame['g'] == 1:
                swarm_positions.append([other_frame['x'], other_frame['y'], other_frame['z']])
        
        if swarm_positions:
            swarm_centroid = np.mean(swarm_positions, axis=0)
            distance = np.sqrt(np.sum((embodied_pos - swarm_centroid)**2))
            distances_from_swarm.append(distance)
            embodied_positions.append(embodied_pos)
            swarm_centroids.append(swarm_centroid)
            times.append(t)
    
    # Calculate speeds
    embodied_speed = 0
    swarm_speed = 0
    if len(embodied_positions) > 1:
        embodied_positions = np.array(embodied_positions)
        swarm_centroids = np.array(swarm_centroids)
        
        embodied_distances = np.sqrt(np.sum(np.diff(embodied_positions, axis=0)**2, axis=1))
        swarm_distances = np.sqrt(np.sum(np.diff(swarm_centroids, axis=0)**2, axis=1))
        
        time_diffs = np.diff(times)
        
        embodied_speeds = embodied_distances / (time_diffs + 1e-10)
        swarm_speeds = swarm_distances / (time_diffs + 1e-10)
        
        embodied_speed = float(np.mean(embodied_speeds))
        swarm_speed = float(np.mean(swarm_speeds))
    
    # Calculate leading percentage (embodied ahead of swarm in direction of movement)
    leading_count = 0
    total_count = 0
    if len(swarm_centroids) > 1:
        for i in range(len(swarm_centroids) - 1):
            movement_direction = swarm_centroids[i+1] - swarm_centroids[i]
            to_embodied = embodied_positions[i] - swarm_centroids[i]
            
            # Check if embodied is ahead (positive dot product with movement direction)
            if np.dot(movement_direction, to_embodied) > 0:
                leading_count += 1
            total_count += 1
    
    leading_percentage = (leading_count / total_count * 100) if total_count > 0 else 0
    
    return {
        'avg_distance_from_swarm': float(np.mean(distances_from_swarm)) if distances_from_swarm else 0,
        'max_distance_from_swarm': float(np.max(distances_from_swarm)) if distances_from_swarm else 0,
        'embodied_speed': embodied_speed,
        'swarm_speed': swarm_speed,
        'speed_difference': embodied_speed - swarm_speed,
        'leading_percentage': float(leading_percentage)
    }


# ==================== MAIN ANALYSIS FUNCTION ====================

def analyze_trajectory(json_path: Path) -> Dict:
    """Perform complete analysis on a trajectory file."""
    print(f"\nAnalyzing: {json_path.name}")
    
    data = load_trajectory(json_path)
    
    # Parse conditions from filename
    file_info = parse_filename(json_path.stem)
    
    results = {
        'filename': json_path.name,
        'participant': file_info['participant'],
        'condition': file_info['condition'],
        'trial': file_info['trial'],
        'performance': calculate_performance_metrics(data),
        'path': calculate_path_metrics(data),
        'compactness': calculate_swarm_compactness(data),
        'connectivity': calculate_connectivity_metrics(data),
        'embodied': calculate_embodied_metrics(data)
    }
    
    return results


def analyze_all_trajectories(trajectory_dir: Path = None) -> List[Dict]:
    """Analyze all trajectory files in the directory."""
    if trajectory_dir is None:
        trajectory_dir = Path(__file__).parent.parent / "SoundMapping" / "SoundMappingUnity" / "Assets" / "Trajectories"
    
    if not trajectory_dir.exists():
        print(f"Trajectory directory not found: {trajectory_dir}")
        return []
    
    json_files = list(trajectory_dir.glob("*.json"))
    
    if not json_files:
        print(f"No trajectory files found in {trajectory_dir}")
        return []
    
    print(f"Found {len(json_files)} trajectory files")
    
    results = []
    for json_file in json_files:
        try:
            result = analyze_trajectory(json_file)
            results.append(result)
        except Exception as e:
            print(f"Error analyzing {json_file.name}: {e}")
    
    return results


if __name__ == "__main__":
    # Example usage
    results = analyze_all_trajectories()
    
    if results:
        print("\n" + "="*60)
        print("ANALYSIS SUMMARY")
        print("="*60)
        
        for result in results:
            print(f"\nFile: {result['filename']}")
            print(f"  Performance:")
            print(f"    Elapsed Time: {result['performance']['elapsed_time']:.2f}s")
            print(f"    Collectibles: {result['performance']['collectibles_picked_up']}")
            print(f"    Collectibles/sec: {result['performance']['collectibles_per_second']:.2f}")
            print(f"  Path:")
            print(f"    Total Distance: {result['path']['total_distance']:.2f}m")
            print(f"    Path Efficiency: {result['path']['path_efficiency']:.2%}")
            print(f"    Avg Speed: {result['path']['avg_speed']:.2f}m/s")
            print(f"  Connectivity:")
            print(f"    Connected: {result['connectivity']['connectivity_percentage']:.1f}%")
            print(f"    Disconnection Events: {result['connectivity']['disconnection_events']}")
