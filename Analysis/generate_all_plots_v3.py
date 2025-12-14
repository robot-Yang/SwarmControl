import json
import numpy as np
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d import Axes3D
from pathlib import Path
from typing import Dict, List, Optional
import re


# ============================================================
# CONFIGURATION
# ============================================================

MAX_COLLECTIBLES = 40
MAX_TIME = 180.0
TOTAL_DRONES = 9
MAX_TRAJECTORY_DEVIATION = 1000
TRAJECTORIES_DIR = Path(__file__).parent/"Trajectories" 
REFERENCE_TRAJECTORY_FILE = TRAJECTORIES_DIR / "reference_trajectory.json" 

COLORS = {
    'Body': '#2ecc71',
    'Controller': '#3498db',
    'Reference': '#e74c3c',
}

# Métriques avec leurs unités (données brutes)
METRIC_CONFIG = {
    'time': {'label': 'Time', 'unit': 's', 'better': 'lower'},
    'collectibles': {'label': 'Collectibles', 'unit': '', 'better': 'higher'},
    'crashes': {'label': 'Crashes', 'unit': '', 'better': 'lower'},
    'disconnections': {'label': 'Disconnections', 'unit': '', 'better': 'lower'},
    'path_efficiency': {'label': 'Path Efficiency', 'unit': '%', 'better': 'higher'},
}

# ============================================================
# DATA LOADING & PROCESSING
# ============================================================

def parse_filename(filename: str) -> Dict:
    pattern = r'([A-Za-z]+)_(B|C)_(\d+)'
    match = re.match(pattern, filename)
    if match:
        return {
            'participant': match.group(1),
            'condition': {'B': 'Body', 'C': 'Controller'}[match.group(2)],
            'trial': int(match.group(3))
        }
    return {'participant': 'Unknown', 'condition': 'Unknown', 'trial': 0}


def load_trajectory(json_path: Path) -> Dict:
    with open(json_path, 'r') as f:
        return json.load(f)


def get_trial_window(data: Dict) -> tuple:
    if data.get('trials'):
        run_trial = next((t for t in data['trials'] if t['label'] == 'Run'), None)
        if run_trial and run_trial.get('endGameTime', 0) > 0:
            return run_trial['startGameTime'], run_trial['endGameTime']
    return 0, float('inf')


def extract_centroid_trajectory(data: Dict) -> np.ndarray:
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


def calculate_trajectory_similarity(trajectory: np.ndarray, reference: np.ndarray) -> float:
    if len(trajectory) == 0 or len(reference) == 0:
        return MAX_TRAJECTORY_DEVIATION
    distances = []
    for point in trajectory:
        dists_to_ref = np.sqrt(np.sum((reference - point)**2, axis=1))
        distances.append(np.min(dists_to_ref))
    return float(np.mean(distances))


def calculate_raw_metrics(data: Dict) -> Dict:
    """Calcule les métriques BRUTES (non normalisées)."""
    start_time, end_time = get_trial_window(data)
    
    # Time
    elapsed_time = data.get('elapsedTime', 0)
    
    # Collectibles
    collectibles = data.get('collectiblesPickedUp', 0)
    
    # Crashes (drones avec e=0)
    crashed_drones = set()
    for traj in data.get('trajectories', []):
        for frame in traj.get('frames', []):
            if start_time <= frame['t'] <= end_time:
                if frame.get('e', 1) == 0:
                    crashed_drones.add(traj['id'])
                    break
    
    # Disconnection events
    time_points = sorted(set(
        frame['t'] for traj in data.get('trajectories', [])
        for frame in traj.get('frames', [])
        if start_time <= frame['t'] <= end_time
    ))
    
    disconnection_events = 0
    was_all_connected = True
    for t in time_points:
        connected = sum(1 for traj in data.get('trajectories', [])
                       for f in [next((f for f in traj['frames'] if abs(f['t'] - t) < 0.01), None)]
                       if f and f.get('g', 0) == 1)
        total = len(data.get('trajectories', []))
        all_connected = (connected == total and total > 0)
        if was_all_connected and not all_connected:
            disconnection_events += 1
        was_all_connected = all_connected
    
    # Path efficiency (en pourcentage)
    centroids = extract_centroid_trajectory(data)
    if len(centroids) >= 2:
        total_dist = np.sum(np.sqrt(np.sum(np.diff(centroids, axis=0)**2, axis=1)))
        direct_dist = np.sqrt(np.sum((centroids[-1] - centroids[0])**2))
        path_efficiency = (direct_dist / total_dist * 100) if total_dist > 0 else 0
    else:
        path_efficiency = 0
    
    return {
        'time': elapsed_time,
        'collectibles': collectibles,
        'crashes': len(crashed_drones),
        'disconnections': disconnection_events,
        'path_efficiency': path_efficiency
    }


def load_all_trajectories(directory: Path) -> Dict[str, List]:
    trajectories = {'Body': [], 'Controller': []}
    for json_file in directory.glob("*.json"):
        if REFERENCE_TRAJECTORY_FILE and json_file.name == REFERENCE_TRAJECTORY_FILE:
            continue
        try:
            info = parse_filename(json_file.stem)
            data = load_trajectory(json_file)
            data['_filename'] = json_file.name
            data['_info'] = info
            if info['condition'] in trajectories:
                trajectories[info['condition']].append(data)
        except Exception as e:
            print(f"Error loading {json_file.name}: {e}")
    return trajectories


def prepare_raw_stats(trajectories_by_condition: Dict) -> Dict:
    """Calcule mean et std des métriques brutes pour chaque condition."""
    stats = {}
    for condition, traj_list in trajectories_by_condition.items():
        if not traj_list:
            continue
        all_metrics = [calculate_raw_metrics(data) for data in traj_list]
        
        stats[condition] = {
            'mean': {k: np.mean([m[k] for m in all_metrics]) for k in all_metrics[0]},
            'std': {k: np.std([m[k] for m in all_metrics]) for k in all_metrics[0]},
            'n': len(traj_list),
            'all': all_metrics
        }
    return stats


# ============================================================
# PLOT 1: RADAR CHART (données brutes normalisées pour affichage)
# ============================================================

def plot_radar_raw(stats: Dict, save_path: Path = None) -> plt.Figure:
    """Radar chart avec valeurs brutes affichées."""
    categories = [METRIC_CONFIG[k]['label'] for k in METRIC_CONFIG.keys()]
    num_vars = len(categories)
    angles = np.linspace(0, 2 * np.pi, num_vars, endpoint=False).tolist()
    angles += angles[:1]
    
    fig, ax = plt.subplots(figsize=(10, 10), subplot_kw=dict(polar=True))
    
    # Normaliser pour l'affichage radar (0-1) mais afficher valeurs brutes
    max_vals = {
        'time': MAX_TIME,
        'collectibles': MAX_COLLECTIBLES,
        'crashes': TOTAL_DRONES,
        'disconnections': 20,
        'path_efficiency': 100
    }
    
    for condition, data in stats.items():
        # Normaliser pour le radar
        norm_values = []
        for k in METRIC_CONFIG.keys():
            val = data['mean'][k] / max_vals[k]
            # Inverser pour "lower is better"
            if METRIC_CONFIG[k]['better'] == 'lower':
                val = 1 - val
            norm_values.append(min(1, max(0, val)))
        norm_values += norm_values[:1]
        
        color = COLORS.get(condition, '#95a5a6')
        ax.plot(angles, norm_values, 'o-', linewidth=2.5, label=f"{condition} (n={data['n']})", 
                color=color, markersize=8)
        ax.fill(angles, norm_values, alpha=0.25, color=color)
        
        # Ajouter les valeurs brutes sur le graphique
        for i, k in enumerate(METRIC_CONFIG.keys()):
            val = data['mean'][k]
            unit = METRIC_CONFIG[k]['unit']
            ax.annotate(f'{val:.1f}{unit}', 
                       xy=(angles[i], norm_values[i]),
                       xytext=(5, 5), textcoords='offset points',
                       fontsize=9, color=color, fontweight='bold')
    
    ax.set_xticks(angles[:-1])
    ax.set_xticklabels(categories, size=12, fontweight='bold')
    ax.set_ylim(0, 1)
    ax.set_yticklabels([])  # Cacher les ticks car on affiche les vraies valeurs
    ax.grid(True, alpha=0.3)
    ax.set_title('PLOT 1: Radar Chart\nRaw values shown on points', 
                 size=14, fontweight='bold', y=1.08)
    ax.legend(loc='upper right', bbox_to_anchor=(1.3, 1.1))
    
    plt.tight_layout()
    if save_path:
        plt.savefig(save_path, dpi=300, bbox_inches='tight', facecolor='white')
    return fig


# ============================================================
# PLOT 2: GROUPED BAR CHART (données brutes)
# ============================================================

def plot_grouped_bars_raw(stats: Dict, save_path: Path = None) -> plt.Figure:
    """Bar chart avec valeurs brutes - une subplot par métrique."""
    metrics = list(METRIC_CONFIG.keys())
    conditions = list(stats.keys())
    
    fig, axes = plt.subplots(1, 5, figsize=(18, 5))
    fig.suptitle('PLOT 2: Grouped Bar Charts - Raw Values', fontsize=14, fontweight='bold')
    
    for idx, (metric_key, config) in enumerate(METRIC_CONFIG.items()):
        ax = axes[idx]
        
        x = np.arange(len(conditions))
        means = [stats[c]['mean'][metric_key] for c in conditions]
        stds = [stats[c]['std'][metric_key] for c in conditions]
        colors_list = [COLORS.get(c, '#95a5a6') for c in conditions]
        
        bars = ax.bar(x, means, yerr=stds, capsize=8, color=colors_list, alpha=0.8,
                     edgecolor='black', linewidth=1.5)
        
        # Valeurs sur les barres
        for i, (bar, mean) in enumerate(zip(bars, means)):
            ax.text(bar.get_x() + bar.get_width()/2, bar.get_height() + stds[i] + 0.5,
                   f'{mean:.1f}', ha='center', va='bottom', fontsize=11, fontweight='bold')
        
        ax.set_xticks(x)
        ax.set_xticklabels(conditions, fontsize=11)
        ax.set_title(f"{config['label']}", fontsize=12, fontweight='bold')
        ax.set_ylabel(f"{config['label']} ({config['unit']})" if config['unit'] else config['label'])
        ax.grid(True, alpha=0.3, axis='y')
        
        # Indicateur si "lower is better"
        if config['better'] == 'lower':
            ax.set_xlabel('↓ Lower is better', fontsize=9, style='italic')
        else:
            ax.set_xlabel('↑ Higher is better', fontsize=9, style='italic')
    
    plt.tight_layout()
    if save_path:
        plt.savefig(save_path, dpi=300, bbox_inches='tight', facecolor='white')
    return fig


# ============================================================
# PLOT 3: TABLE / HEATMAP (données brutes)
# ============================================================

def plot_table_raw(stats: Dict, save_path: Path = None) -> plt.Figure:
    """Tableau avec valeurs brutes et code couleur."""
    conditions = list(stats.keys())
    metrics = list(METRIC_CONFIG.keys())
    
    fig, ax = plt.subplots(figsize=(12, 4))
    ax.axis('off')
    
    # Préparer les données du tableau
    cell_text = []
    cell_colors = []
    
    for cond in conditions:
        row_text = []
        row_colors = []
        for metric in metrics:
            mean = stats[cond]['mean'][metric]
            std = stats[cond]['std'][metric]
            unit = METRIC_CONFIG[metric]['unit']
            row_text.append(f'{mean:.1f} ± {std:.1f}{unit}')
            
            # Couleur basée sur la performance relative
            row_colors.append('#ffffff')
        cell_text.append(row_text)
        cell_colors.append(row_colors)
    
    col_labels = [f"{METRIC_CONFIG[m]['label']}\n({'↓' if METRIC_CONFIG[m]['better']=='lower' else '↑'})" 
                  for m in metrics]
    row_labels = [f"{c} (n={stats[c]['n']})" for c in conditions]
    
    table = ax.table(cellText=cell_text,
                     rowLabels=row_labels,
                     colLabels=col_labels,
                     cellLoc='center',
                     loc='center',
                     cellColours=cell_colors)
    
    table.auto_set_font_size(False)
    table.set_fontsize(11)
    table.scale(1.2, 2)
    
    # Style des headers
    for (row, col), cell in table.get_celld().items():
        if row == 0:
            cell.set_text_props(fontweight='bold')
            cell.set_facecolor('#e6e6e6')
        if col == -1:
            cell.set_text_props(fontweight='bold')
            cell.set_facecolor(COLORS.get(conditions[row-1], '#e6e6e6') if row > 0 else '#e6e6e6')
    
    ax.set_title('PLOT 3: Summary Table - Raw Values (mean ± std)\n↓ = lower is better, ↑ = higher is better', 
                 fontsize=14, fontweight='bold', pad=20)
    
    plt.tight_layout()
    if save_path:
        plt.savefig(save_path, dpi=300, bbox_inches='tight', facecolor='white')
    return fig


# ============================================================
# PLOT 4: BOX PLOTS (données brutes)
# ============================================================

def plot_boxplots_raw(stats: Dict, save_path: Path = None) -> plt.Figure:
    """Box plots avec valeurs brutes."""
    metrics = list(METRIC_CONFIG.keys())
    conditions = list(stats.keys())
    
    fig, axes = plt.subplots(1, 5, figsize=(18, 5))
    fig.suptitle('PLOT 4: Box Plots - Raw Value Distributions', fontsize=14, fontweight='bold')
    
    for idx, (metric_key, config) in enumerate(METRIC_CONFIG.items()):
        ax = axes[idx]
        
        data_to_plot = []
        labels = []
        colors_list = []
        
        for condition in conditions:
            values = [m[metric_key] for m in stats[condition]['all']]
            data_to_plot.append(values)
            labels.append(condition)
            colors_list.append(COLORS.get(condition, '#95a5a6'))
        
        bp = ax.boxplot(data_to_plot, tick_labels=labels, patch_artist=True)
        
        for patch, color in zip(bp['boxes'], colors_list):
            patch.set_facecolor(color)
            patch.set_alpha(0.6)
        
        ax.set_title(config['label'], fontsize=11, fontweight='bold')
        ylabel = f"{config['unit']}" if config['unit'] else "count"
        ax.set_ylabel(ylabel)
        ax.grid(True, alpha=0.3, axis='y')
        
        if config['better'] == 'lower':
            ax.set_xlabel('↓ Lower is better', fontsize=9, style='italic')
        else:
            ax.set_xlabel('↑ Higher is better', fontsize=9, style='italic')
    
    plt.tight_layout()
    if save_path:
        plt.savefig(save_path, dpi=300, bbox_inches='tight', facecolor='white')
    return fig


# ============================================================
# PLOT 5: LOLLIPOP CHART (données brutes)
# ============================================================

def plot_lollipop_raw(stats: Dict, save_path: Path = None) -> plt.Figure:
    """Lollipop chart - élégant pour comparaison."""
    metrics = list(METRIC_CONFIG.keys())
    conditions = list(stats.keys())
    
    fig, axes = plt.subplots(1, 5, figsize=(18, 6))
    fig.suptitle('PLOT 5: Lollipop Chart - Raw Values Comparison', fontsize=14, fontweight='bold')
    
    for idx, (metric_key, config) in enumerate(METRIC_CONFIG.items()):
        ax = axes[idx]
        
        y_pos = np.arange(len(conditions))
        means = [stats[c]['mean'][metric_key] for c in conditions]
        colors_list = [COLORS.get(c, '#95a5a6') for c in conditions]
        
        # Lignes
        ax.hlines(y=y_pos, xmin=0, xmax=means, color=colors_list, linewidth=3, alpha=0.7)
        # Points
        ax.scatter(means, y_pos, color=colors_list, s=200, zorder=3, edgecolor='black', linewidth=2)
        
        # Valeurs
        for i, (mean, y) in enumerate(zip(means, y_pos)):
            ax.text(mean + max(means)*0.05, y, f'{mean:.1f}', va='center', fontsize=11, fontweight='bold')
        
        ax.set_yticks(y_pos)
        ax.set_yticklabels(conditions, fontsize=11)
        ax.set_title(config['label'], fontsize=12, fontweight='bold')
        ax.set_xlim(0, max(means) * 1.3)
        xlabel = f"{config['unit']}" if config['unit'] else "count"
        ax.set_xlabel(xlabel)
        ax.grid(True, alpha=0.3, axis='x')
        
        # Indicateur
        better = '← Better' if config['better'] == 'lower' else 'Better →'
        ax.annotate(better, xy=(0.5, -0.15), xycoords='axes fraction', 
                   ha='center', fontsize=9, style='italic')
    
    plt.tight_layout()
    if save_path:
        plt.savefig(save_path, dpi=300, bbox_inches='tight', facecolor='white')
    return fig


# ============================================================
# PLOT 6: PARALLEL COORDINATES (données brutes normalisées)
# ============================================================

def plot_parallel_coordinates(stats: Dict, save_path: Path = None) -> plt.Figure:
    """Parallel coordinates plot."""
    metrics = list(METRIC_CONFIG.keys())
    conditions = list(stats.keys())
    
    fig, ax = plt.subplots(figsize=(12, 7))
    
    # Normaliser pour pouvoir comparer sur un même axe
    max_vals = {
        'time': max(stats[c]['mean']['time'] for c in conditions) * 1.2,
        'collectibles': MAX_COLLECTIBLES,
        'crashes': max(max(stats[c]['mean']['crashes'] for c in conditions), 1) * 1.2,
        'disconnections': max(max(stats[c]['mean']['disconnections'] for c in conditions), 1) * 1.2,
        'path_efficiency': 100
    }
    
    x = np.arange(len(metrics))
    
    for condition in conditions:
        values = []
        for metric in metrics:
            val = stats[condition]['mean'][metric] / max_vals[metric]
            # Inverser pour "lower is better" pour que "haut = bon"
            if METRIC_CONFIG[metric]['better'] == 'lower':
                val = 1 - val
            values.append(val)
        
        color = COLORS.get(condition, '#95a5a6')
        ax.plot(x, values, 'o-', linewidth=3, markersize=12, label=condition, color=color)
        
        # Afficher valeurs brutes
        for i, metric in enumerate(metrics):
            raw_val = stats[condition]['mean'][metric]
            unit = METRIC_CONFIG[metric]['unit']
            ax.annotate(f'{raw_val:.1f}{unit}', xy=(i, values[i]), 
                       xytext=(0, 10), textcoords='offset points',
                       ha='center', fontsize=9, color=color, fontweight='bold')
    
    ax.set_xticks(x)
    ax.set_xticklabels([METRIC_CONFIG[m]['label'] for m in metrics], fontsize=11, fontweight='bold')
    ax.set_ylim(-0.1, 1.2)
    ax.set_ylabel('Performance (higher = better)', fontsize=11)
    ax.axhline(y=1, color='green', linestyle='--', alpha=0.5, label='Optimal')
    ax.axhline(y=0, color='red', linestyle='--', alpha=0.5, label='Worst')
    ax.legend(loc='upper right', fontsize=10)
    ax.grid(True, alpha=0.3)
    ax.set_title('PLOT 6: Parallel Coordinates\nNormalized for comparison (raw values shown)', 
                 fontsize=14, fontweight='bold')
    
    plt.tight_layout()
    if save_path:
        plt.savefig(save_path, dpi=300, bbox_inches='tight', facecolor='white')
    return fig


# ============================================================
# PLOT 7: POLAR BAR CHART (données brutes)
# ============================================================

def plot_polar_bars(stats: Dict, save_path: Path = None) -> plt.Figure:
    """Polar bar chart - alternative au radar."""
    metrics = list(METRIC_CONFIG.keys())
    conditions = list(stats.keys())
    
    fig, axes = plt.subplots(1, len(conditions), figsize=(6*len(conditions), 6), 
                             subplot_kw=dict(polar=True))
    if len(conditions) == 1:
        axes = [axes]
    
    fig.suptitle('PLOT 7: Polar Bar Charts - Per Condition', fontsize=14, fontweight='bold')
    
    angles = np.linspace(0, 2*np.pi, len(metrics), endpoint=False)
    width = 2*np.pi / len(metrics) * 0.8
    
    max_vals = {
        'time': MAX_TIME,
        'collectibles': MAX_COLLECTIBLES,
        'crashes': TOTAL_DRONES,
        'disconnections': 20,
        'path_efficiency': 100
    }
    
    for ax, condition in zip(axes, conditions):
        values = []
        for metric in metrics:
            val = stats[condition]['mean'][metric] / max_vals[metric]
            if METRIC_CONFIG[metric]['better'] == 'lower':
                val = 1 - val
            values.append(min(1, max(0, val)))
        
        color = COLORS.get(condition, '#95a5a6')
        bars = ax.bar(angles, values, width=width, color=color, alpha=0.7, edgecolor='black')
        
        # Valeurs brutes
        for angle, val, metric in zip(angles, values, metrics):
            raw_val = stats[condition]['mean'][metric]
            unit = METRIC_CONFIG[metric]['unit']
            ax.text(angle, val + 0.1, f'{raw_val:.0f}{unit}', ha='center', fontsize=9, fontweight='bold')
        
        ax.set_xticks(angles)
        ax.set_xticklabels([METRIC_CONFIG[m]['label'] for m in metrics], fontsize=10)
        ax.set_ylim(0, 1.3)
        ax.set_yticklabels([])
        ax.set_title(f'{condition} (n={stats[condition]["n"]})', fontsize=12, fontweight='bold', y=1.1)
    
    plt.tight_layout()
    if save_path:
        plt.savefig(save_path, dpi=300, bbox_inches='tight', facecolor='white')
    return fig


# ============================================================
# PLOT 8: DOT PLOT / CLEVELAND PLOT (données brutes)
# ============================================================

def plot_cleveland_dot(stats: Dict, save_path: Path = None) -> plt.Figure:
    """Cleveland dot plot - très lisible pour comparaisons."""
    metrics = list(METRIC_CONFIG.keys())
    conditions = list(stats.keys())
    
    fig, ax = plt.subplots(figsize=(10, 8))
    
    y_positions = []
    y_labels = []
    current_y = 0
    
    for metric in metrics:
        for i, condition in enumerate(conditions):
            y_positions.append(current_y + i * 0.4)
            y_labels.append(f"{condition}" if i == 0 else "")
        current_y += len(conditions) * 0.4 + 0.8  # Gap between metrics
    
    # Tracer les points
    idx = 0
    for metric in metrics:
        config = METRIC_CONFIG[metric]
        for condition in conditions:
            mean = stats[condition]['mean'][metric]
            std = stats[condition]['std'][metric]
            color = COLORS.get(condition, '#95a5a6')
            
            # Point avec barre d'erreur horizontale
            ax.errorbar(mean, y_positions[idx], xerr=std, fmt='o', 
                       color=color, markersize=12, capsize=5, capthick=2, linewidth=2)
            
            # Valeur
            ax.text(mean, y_positions[idx] + 0.15, f'{mean:.1f}', 
                   ha='center', fontsize=9, fontweight='bold', color=color)
            
            idx += 1
    
    # Labels des métriques (à gauche)
    current_y = 0
    for metric in metrics:
        config = METRIC_CONFIG[metric]
        mid_y = current_y + (len(conditions) - 1) * 0.2
        ax.text(-max([stats[c]['mean'][metric] for c in conditions]) * 0.1, mid_y, 
               f"{config['label']}\n({config['unit'] if config['unit'] else 'count'})", 
               ha='right', va='center', fontsize=11, fontweight='bold')
        current_y += len(conditions) * 0.4 + 0.8
    
    ax.set_yticks([])
    ax.set_xlabel('Value', fontsize=11)
    ax.grid(True, alpha=0.3, axis='x')
    ax.set_title('PLOT 8: Cleveland Dot Plot\nMean ± Std for each metric', 
                 fontsize=14, fontweight='bold')
    
    # Légende
    for condition in conditions:
        ax.scatter([], [], c=COLORS.get(condition), s=100, label=condition)
    ax.legend(loc='lower right', fontsize=10)
    
    plt.tight_layout()
    if save_path:
        plt.savefig(save_path, dpi=300, bbox_inches='tight', facecolor='white')
    return fig


# ============================================================
# PLOT 9: TRAJECTOIRES - MULTI-VUES (2D + 3D)
# ============================================================

def extract_crash_positions(data: Dict) -> List[np.ndarray]:
    """Extrait les positions où les drones crashent (e passe à 0)."""
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


def extract_disconnection_segments(data: Dict) -> List[np.ndarray]:
    """Extrait les segments où des drones sont déconnectés (g=0)."""
    start_time, end_time = get_trial_window(data)
    disconnection_points = []
    
    for traj in data.get('trajectories', []):
        for frame in traj.get('frames', []):
            if start_time <= frame['t'] <= end_time:
                if frame.get('g', 1) == 0:
                    pos = np.array([frame['x'], frame['y'], frame['z']])
                    disconnection_points.append(pos)
    
    return disconnection_points


def extract_embodied_trajectory(data: Dict) -> np.ndarray:
    """Extrait la trajectoire du drone embodied."""
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


def plot_trajectory_multiview(trajectories_by_condition: Dict, 
                               reference_trajectory: np.ndarray = None,
                               save_path: Path = None) -> plt.Figure:
    """Plot 9a: Swarm vs Embodied trajectories (4 vues) - SANS crashes/disconnections."""
    fig = plt.figure(figsize=(18, 16))
    
    # Collecter données
    all_data = []
    for condition, traj_list in trajectories_by_condition.items():
        for data in traj_list:
            all_data.append({
                'condition': condition,
                'swarm': extract_centroid_trajectory(data),
                'embodied': extract_embodied_trajectory(data),
            })
    
    def plot_2d_view(ax, view_type, title):
        for item in all_data:
            color = COLORS.get(item['condition'], '#95a5a6')
            
            # Swarm
            swarm = item['swarm']
            if len(swarm) > 0:
                if view_type == 'top':
                    ax.plot(swarm[:, 0], swarm[:, 2], '-', linewidth=2.5, color=color, alpha=0.7, label='Swarm Centroid')
                    ax.scatter(swarm[0, 0], swarm[0, 2], c=color, marker='o', s=120, zorder=5, edgecolor='black')
                    ax.scatter(swarm[-1, 0], swarm[-1, 2], c=color, marker='s', s=120, zorder=5, edgecolor='black')
                elif view_type == 'side':
                    ax.plot(swarm[:, 2], swarm[:, 1], '-', linewidth=2.5, color=color, alpha=0.7)
                    ax.scatter(swarm[0, 2], swarm[0, 1], c=color, marker='o', s=120, zorder=5, edgecolor='black')
                    ax.scatter(swarm[-1, 2], swarm[-1, 1], c=color, marker='s', s=120, zorder=5, edgecolor='black')
                elif view_type == 'front':
                    ax.plot(swarm[:, 0], swarm[:, 1], '-', linewidth=2.5, color=color, alpha=0.7)
                    ax.scatter(swarm[0, 0], swarm[0, 1], c=color, marker='o', s=120, zorder=5, edgecolor='black')
                    ax.scatter(swarm[-1, 0], swarm[-1, 1], c=color, marker='s', s=120, zorder=5, edgecolor='black')
            
            # Embodied
            emb = item['embodied']
            if len(emb) > 0:
                if view_type == 'top':
                    ax.plot(emb[:, 0], emb[:, 2], '-', linewidth=3, color='purple', alpha=0.8, label='Embodied Drone')
                    ax.scatter(emb[0, 0], emb[0, 2], c='purple', marker='o', s=150, zorder=6, edgecolor='white', linewidth=2)
                    ax.scatter(emb[-1, 0], emb[-1, 2], c='purple', marker='s', s=150, zorder=6, edgecolor='white', linewidth=2)
                elif view_type == 'side':
                    ax.plot(emb[:, 2], emb[:, 1], '-', linewidth=3, color='purple', alpha=0.8)
                    ax.scatter(emb[0, 2], emb[0, 1], c='purple', marker='o', s=150, zorder=6, edgecolor='white', linewidth=2)
                    ax.scatter(emb[-1, 2], emb[-1, 1], c='purple', marker='s', s=150, zorder=6, edgecolor='white', linewidth=2)
                elif view_type == 'front':
                    ax.plot(emb[:, 0], emb[:, 1], '-', linewidth=3, color='purple', alpha=0.8)
                    ax.scatter(emb[0, 0], emb[0, 1], c='purple', marker='o', s=150, zorder=6, edgecolor='white', linewidth=2)
                    ax.scatter(emb[-1, 0], emb[-1, 1], c='purple', marker='s', s=150, zorder=6, edgecolor='white', linewidth=2)
        
        # Référence
        if reference_trajectory is not None and len(reference_trajectory) > 0:
            ref = reference_trajectory
            if view_type == 'top':
                ax.plot(ref[:, 0], ref[:, 2], '--', linewidth=2, color=COLORS['Reference'], alpha=0.7, label='Reference')
            elif view_type == 'side':
                ax.plot(ref[:, 2], ref[:, 1], '--', linewidth=2, color=COLORS['Reference'], alpha=0.7)
            elif view_type == 'front':
                ax.plot(ref[:, 0], ref[:, 1], '--', linewidth=2, color=COLORS['Reference'], alpha=0.7)
        
        ax.set_title(title, fontsize=12, fontweight='bold')
        ax.grid(True, alpha=0.3)
        ax.set_aspect('equal', adjustable='datalim')
    
    # 4 vues
    ax1 = fig.add_subplot(2, 2, 1)
    plot_2d_view(ax1, 'top', 'TOP VIEW (X-Z)')
    ax1.set_xlabel('X Position (m)')
    ax1.set_ylabel('Z Position (m) - Forward')
    ax1.legend(loc='upper right', fontsize=10)
    
    ax2 = fig.add_subplot(2, 2, 2)
    plot_2d_view(ax2, 'side', 'SIDE VIEW (Z-Y)')
    ax2.set_xlabel('Z Position (m) - Forward')
    ax2.set_ylabel('Y Position (m) - Height')
    
    ax3 = fig.add_subplot(2, 2, 3)
    plot_2d_view(ax3, 'front', 'FRONT VIEW (X-Y)')
    ax3.set_xlabel('X Position (m)')
    ax3.set_ylabel('Y Position (m) - Height')
    
    ax4 = fig.add_subplot(2, 2, 4, projection='3d')
    for item in all_data:
        color = COLORS.get(item['condition'], '#95a5a6')
        swarm = item['swarm']
        if len(swarm) > 0:
            ax4.plot(swarm[:, 0], swarm[:, 2], swarm[:, 1], '-', linewidth=2.5, color=color, alpha=0.7, label='Swarm')
        emb = item['embodied']
        if len(emb) > 0:
            ax4.plot(emb[:, 0], emb[:, 2], emb[:, 1], '-', linewidth=3, color='purple', alpha=0.8, label='Embodied')
    ax4.set_xlabel('X (m)')
    ax4.set_ylabel('Z (m)')
    ax4.set_zlabel('Y (m)')
    ax4.set_title('3D VIEW', fontsize=12, fontweight='bold')
    ax4.view_init(elev=30, azim=45)
    
    fig.suptitle('PLOT 9a: Trajectory Paths\nSwarm Centroid vs Embodied Drone  ○ Start  □ End', 
                 fontsize=14, fontweight='bold')
    
    plt.tight_layout()
    if save_path:
        plt.savefig(save_path, dpi=300, bbox_inches='tight', facecolor='white')
    return fig


def plot_3d_trajectories(trajectories_by_condition: Dict, 
                         reference_trajectory: np.ndarray = None,
                         save_path: Path = None) -> plt.Figure:
    """Plot 9b: Crashes et Disconnections (4 vues) - trajectoire en fond gris."""
    fig = plt.figure(figsize=(18, 16))
    
    # Collecter données
    all_data = []
    for condition, traj_list in trajectories_by_condition.items():
        for data in traj_list:
            all_data.append({
                'condition': condition,
                'swarm': extract_centroid_trajectory(data),
                'crashes': extract_crash_positions(data),
                'disconnections': extract_disconnection_segments(data),
            })
    
    def plot_2d_view(ax, view_type, title):
        for item in all_data:
            # Swarm (ligne de fond grise)
            swarm = item['swarm']
            if len(swarm) > 0:
                if view_type == 'top':
                    ax.plot(swarm[:, 0], swarm[:, 2], '-', linewidth=1.5, color='gray', alpha=0.4, label='Trajectory')
                elif view_type == 'side':
                    ax.plot(swarm[:, 2], swarm[:, 1], '-', linewidth=1.5, color='gray', alpha=0.4)
                elif view_type == 'front':
                    ax.plot(swarm[:, 0], swarm[:, 1], '-', linewidth=1.5, color='gray', alpha=0.4)
            
            # Crashes
            for i, crash in enumerate(item['crashes']):
                label = 'Crashes' if i == 0 else None
                if view_type == 'top':
                    ax.scatter(crash[0], crash[2], c='red', marker='X', s=250, edgecolor='black', linewidth=2, zorder=10, label=label)
                elif view_type == 'side':
                    ax.scatter(crash[2], crash[1], c='red', marker='X', s=250, edgecolor='black', linewidth=2, zorder=10)
                elif view_type == 'front':
                    ax.scatter(crash[0], crash[1], c='red', marker='X', s=250, edgecolor='black', linewidth=2, zorder=10)
            
            # Disconnections
            disc = item['disconnections']
            if disc:
                disc_arr = np.array(disc)
                step = max(1, len(disc_arr) // 100)
                disc_sample = disc_arr[::step]
                if view_type == 'top':
                    ax.scatter(disc_sample[:, 0], disc_sample[:, 2], c='orange', marker='o', s=60, alpha=0.6, label='Disconnections')
                elif view_type == 'side':
                    ax.scatter(disc_sample[:, 2], disc_sample[:, 1], c='orange', marker='o', s=60, alpha=0.6)
                elif view_type == 'front':
                    ax.scatter(disc_sample[:, 0], disc_sample[:, 1], c='orange', marker='o', s=60, alpha=0.6)
        
        ax.set_title(title, fontsize=12, fontweight='bold')
        ax.grid(True, alpha=0.3)
        ax.set_aspect('equal', adjustable='datalim')
    
    # 4 vues
    ax1 = fig.add_subplot(2, 2, 1)
    plot_2d_view(ax1, 'top', 'TOP VIEW (X-Z)')
    ax1.set_xlabel('X Position (m)')
    ax1.set_ylabel('Z Position (m) - Forward')
    ax1.legend(loc='upper right', fontsize=10)
    
    ax2 = fig.add_subplot(2, 2, 2)
    plot_2d_view(ax2, 'side', 'SIDE VIEW (Z-Y)')
    ax2.set_xlabel('Z Position (m) - Forward')
    ax2.set_ylabel('Y Position (m) - Height')
    
    ax3 = fig.add_subplot(2, 2, 3)
    plot_2d_view(ax3, 'front', 'FRONT VIEW (X-Y)')
    ax3.set_xlabel('X Position (m)')
    ax3.set_ylabel('Y Position (m) - Height')
    
    ax4 = fig.add_subplot(2, 2, 4, projection='3d')
    for item in all_data:
        swarm = item['swarm']
        if len(swarm) > 0:
            ax4.plot(swarm[:, 0], swarm[:, 2], swarm[:, 1], '-', linewidth=1.5, color='gray', alpha=0.4)
        for crash in item['crashes']:
            ax4.scatter(crash[0], crash[2], crash[1], c='red', marker='X', s=250, edgecolor='black', linewidth=2, zorder=10)
        disc = item['disconnections']
        if disc:
            disc_arr = np.array(disc)
            step = max(1, len(disc_arr) // 100)
            disc_sample = disc_arr[::step]
            ax4.scatter(disc_sample[:, 0], disc_sample[:, 2], disc_sample[:, 1], c='orange', marker='o', s=40, alpha=0.5)
    ax4.set_xlabel('X (m)')
    ax4.set_ylabel('Z (m)')
    ax4.set_zlabel('Y (m)')
    ax4.set_title('3D VIEW', fontsize=12, fontweight='bold')
    ax4.view_init(elev=30, azim=45)
    
    # Compte des événements
    total_crashes = sum(len(item['crashes']) for item in all_data)
    total_disc = sum(len(item['disconnections']) for item in all_data)
    
    fig.suptitle(f'PLOT 9b: Events Analysis\n✕ Crashes ({total_crashes})  ● Disconnections ({total_disc} points)', 
                 fontsize=14, fontweight='bold')
    
    plt.tight_layout()
    if save_path:
        plt.savefig(save_path, dpi=300, bbox_inches='tight', facecolor='white')
    return fig


# ============================================================
# PLOT 10: DASHBOARD SUMMARY
# ============================================================

def plot_dashboard_raw(stats: Dict, trajectories_by_condition: Dict,
                       reference_trajectory: np.ndarray = None,
                       save_path: Path = None) -> plt.Figure:
    """Dashboard complet avec toutes les infos."""
    fig = plt.figure(figsize=(20, 12))
    
    # Layout: 2 rows, 3 columns
    # Row 1: Radar | Bars | Table
    # Row 2: 3D Trajectory (spanning 2 cols) | Lollipop
    
    conditions = list(stats.keys())
    metrics = list(METRIC_CONFIG.keys())
    
    # --- RADAR (top left) ---
    ax1 = fig.add_subplot(2, 3, 1, projection='polar')
    angles = np.linspace(0, 2*np.pi, len(metrics), endpoint=False).tolist()
    angles += angles[:1]
    
    max_vals = {'time': MAX_TIME, 'collectibles': MAX_COLLECTIBLES, 
                'crashes': TOTAL_DRONES, 'disconnections': 20, 'path_efficiency': 100}
    
    for condition, data in stats.items():
        norm_values = []
        for k in metrics:
            val = data['mean'][k] / max_vals[k]
            if METRIC_CONFIG[k]['better'] == 'lower':
                val = 1 - val
            norm_values.append(min(1, max(0, val)))
        norm_values += norm_values[:1]
        
        color = COLORS.get(condition, '#95a5a6')
        ax1.plot(angles, norm_values, 'o-', linewidth=2, label=condition, color=color)
        ax1.fill(angles, norm_values, alpha=0.25, color=color)
    
    ax1.set_xticks(angles[:-1])
    ax1.set_xticklabels([METRIC_CONFIG[m]['label'] for m in metrics], size=9)
    ax1.set_ylim(0, 1)
    ax1.set_yticklabels([])
    ax1.set_title('Performance Radar', fontsize=11, fontweight='bold', y=1.08)
    ax1.legend(loc='upper right', bbox_to_anchor=(1.2, 1.0), fontsize=8)
    
    # --- BARS (top middle) ---
    ax2 = fig.add_subplot(2, 3, 2)
    x = np.arange(len(metrics))
    width = 0.35
    
    for i, condition in enumerate(conditions):
        means = [stats[condition]['mean'][k] / max_vals[k] for k in metrics]
        # Inverser pour "lower is better"
        means = [1 - m if METRIC_CONFIG[k]['better'] == 'lower' else m 
                 for m, k in zip(means, metrics)]
        offset = (i - len(conditions)/2 + 0.5) * width
        ax2.bar(x + offset, means, width, label=condition,
               color=COLORS.get(condition, '#95a5a6'), alpha=0.8)
    
    ax2.set_xticks(x)
    ax2.set_xticklabels([METRIC_CONFIG[m]['label'] for m in metrics], fontsize=9, rotation=45, ha='right')
    ax2.set_ylim(0, 1.1)
    ax2.set_ylabel('Score (normalized)')
    ax2.set_title('Normalized Scores', fontsize=11, fontweight='bold')
    ax2.legend(fontsize=8)
    ax2.grid(True, alpha=0.3, axis='y')
    
    # --- TABLE (top right) ---
    ax3 = fig.add_subplot(2, 3, 3)
    ax3.axis('off')
    
    cell_text = []
    for cond in conditions:
        row = [f"{stats[cond]['mean'][m]:.1f}" for m in metrics]
        cell_text.append(row)
    
    col_labels = [METRIC_CONFIG[m]['label'] for m in metrics]
    row_labels = conditions
    
    table = ax3.table(cellText=cell_text, rowLabels=row_labels, colLabels=col_labels,
                      cellLoc='center', loc='center')
    table.auto_set_font_size(False)
    table.set_fontsize(10)
    table.scale(1.2, 1.8)
    ax3.set_title('Raw Values', fontsize=11, fontweight='bold', y=0.85)
    
    # --- 3D TRAJECTORY (bottom, spanning 2 columns) ---
    ax4 = fig.add_subplot(2, 3, (4, 5), projection='3d')
    
    for condition, traj_list in trajectories_by_condition.items():
        color = COLORS.get(condition, '#95a5a6')
        for i, data in enumerate(traj_list):
            centroids = extract_centroid_trajectory(data)
            if len(centroids) == 0:
                continue
            cx, cy, cz = centroids[:, 0], centroids[:, 2], centroids[:, 1]
            label = condition if i == 0 else None
            ax4.plot(cx, cy, cz, '-', linewidth=2, color=color, alpha=0.7, label=label)
    
    if reference_trajectory is not None and len(reference_trajectory) > 0:
        rx, ry, rz = reference_trajectory[:, 0], reference_trajectory[:, 2], reference_trajectory[:, 1]
        ax4.plot(rx, ry, rz, '--', linewidth=2.5, color=COLORS['Reference'], alpha=0.8, label='Reference')
    
    ax4.set_xlabel('X (m)', fontsize=9)
    ax4.set_ylabel('Z (m)', fontsize=9)
    ax4.set_zlabel('Y (m)', fontsize=9)
    ax4.set_title('3D Trajectories', fontsize=11, fontweight='bold')
    ax4.legend(fontsize=8)
    ax4.view_init(elev=20, azim=45)
    
    # --- METRICS SUMMARY (bottom right) ---
    ax5 = fig.add_subplot(2, 3, 6)
    y_pos = np.arange(len(metrics))
    
    for i, condition in enumerate(conditions):
        means = [stats[condition]['mean'][m] / max_vals[m] for m in metrics]
        means = [1 - m if METRIC_CONFIG[k]['better'] == 'lower' else m 
                 for m, k in zip(means, metrics)]
        offset = (i - len(conditions)/2 + 0.5) * 0.3
        ax5.barh(y_pos + offset, means, height=0.3, label=condition,
                color=COLORS.get(condition, '#95a5a6'), alpha=0.8)
    
    ax5.set_yticks(y_pos)
    ax5.set_yticklabels([METRIC_CONFIG[m]['label'] for m in metrics], fontsize=10)
    ax5.set_xlim(0, 1.1)
    ax5.set_xlabel('Score')
    ax5.set_title('Score Summary', fontsize=11, fontweight='bold')
    ax5.legend(fontsize=8)
    ax5.grid(True, alpha=0.3, axis='x')
    
    fig.suptitle('PLOT 10: DASHBOARD - Complete Study Summary', 
                 fontsize=16, fontweight='bold', y=1.02)
    
    plt.tight_layout()
    if save_path:
        plt.savefig(save_path, dpi=300, bbox_inches='tight', facecolor='white')
    return fig


# ============================================================
# MAIN
# ============================================================

def main():
    traj_dir = Path(TRAJECTORIES_DIR) if TRAJECTORIES_DIR else Path(__file__).parent
    output_dir = traj_dir / "plots"
    output_dir.mkdir(exist_ok=True)
    
    print(f"Loading trajectories from: {traj_dir}")
    
    all_trajectories = load_all_trajectories(traj_dir)
    total = sum(len(v) for v in all_trajectories.values())
    
    print(f"Found {total} trajectory files")
    for cond, lst in all_trajectories.items():
        print(f"  - {cond}: {len(lst)}")
    
    if total == 0:
        print("No trajectory files found!")
        return
    
    # Charger la référence
    reference_trajectory = None
    if REFERENCE_TRAJECTORY_FILE:
        ref_path = traj_dir / REFERENCE_TRAJECTORY_FILE
        if ref_path.exists():
            print(f"\nLoading reference: {REFERENCE_TRAJECTORY_FILE}")
            ref_data = load_trajectory(ref_path)
            reference_trajectory = extract_centroid_trajectory(ref_data)
    
    # Calculer les stats
    stats = prepare_raw_stats(all_trajectories)
    
    # Afficher résumé
    print("\n" + "="*70)
    print("RAW METRICS SUMMARY")
    print("="*70)
    for condition, data in stats.items():
        print(f"\n{condition} (n={data['n']}):")
        for metric, config in METRIC_CONFIG.items():
            unit = config['unit']
            print(f"  {config['label']}: {data['mean'][metric]:.1f}{unit} (±{data['std'][metric]:.1f})")
    
    # Générer tous les plots
    print("\n" + "="*70)
    print("GENERATING PLOTS")
    print("="*70)
    
    plots = [
        ("plot01_radar.png", lambda: plot_radar_raw(stats)),
        ("plot02_grouped_bars.png", lambda: plot_grouped_bars_raw(stats)),
        ("plot03_table.png", lambda: plot_table_raw(stats)),
        ("plot04_boxplots.png", lambda: plot_boxplots_raw(stats)),
        ("plot05_lollipop.png", lambda: plot_lollipop_raw(stats)),
        ("plot06_parallel.png", lambda: plot_parallel_coordinates(stats)),
        ("plot07_polar_bars.png", lambda: plot_polar_bars(stats)),
        ("plot08_cleveland_dot.png", lambda: plot_cleveland_dot(stats)),
        ("plot09a_trajectory_multiview.png", lambda: plot_trajectory_multiview(all_trajectories, reference_trajectory)),
        ("plot09b_trajectory_3d.png", lambda: plot_3d_trajectories(all_trajectories, reference_trajectory)),
        ("plot10_dashboard.png", lambda: plot_dashboard_raw(stats, all_trajectories, reference_trajectory)),
    ]
    
    for filename, plot_func in plots:
        print(f"  Generating {filename}...")
        fig = plot_func()
        fig.savefig(output_dir / filename, dpi=300, bbox_inches='tight', facecolor='white')
        plt.close(fig)
    
    print(f"\n✓ All plots saved to: {output_dir}")
    print("\n" + "="*70)
    print("PLOT DESCRIPTIONS")
    print("="*70)
    print("""
  1. Radar Chart        - Spider plot avec valeurs brutes annotées
  2. Grouped Bars       - Barres groupées par métrique (format académique)
  3. Summary Table      - Tableau mean ± std (pour rapport)
  4. Box Plots          - Distributions par métrique
  5. Lollipop Chart     - Comparaison élégante
  6. Parallel Coords    - Vue multi-dimensionnelle
  7. Polar Bars         - Barres polaires par condition
  8. Cleveland Dot      - Points avec barres d'erreur
  9. 3D Trajectory      - Parcours dans l'espace
 10. Dashboard          - Vue résumé complète
""")


if __name__ == "__main__":
    main()
