"""
Visualization Tool for Trajectory Analysis
Generate comprehensive plots comparing experimental conditions.
"""

import matplotlib.pyplot as plt
import numpy as np
from pathlib import Path
import sys

# Import comparison tools
try:
    from compare_conditions import results_to_dataframe
    from analyze_metrics import analyze_all_trajectories
except ImportError:
    print("Error: Make sure analyze_metrics.py and compare_conditions.py are in the same directory")
    sys.exit(1)


def plot_performance_comparison(df, save_path=None):
    """Plot performance metrics comparison."""
    fig, axes = plt.subplots(2, 2, figsize=(16, 12))
    fig.suptitle('Performance Metrics Comparison', fontsize=16, fontweight='bold')
    
    # Elapsed time by condition (Body vs Controller)
    if 'condition' in df.columns and df['condition'].nunique() > 1:
        ax = axes[0, 0]
        condition_data = df.groupby('condition')['elapsed_time'].agg(['mean', 'std', 'count'])
        x_pos = np.arange(len(condition_data))
        ax.bar(x_pos, condition_data['mean'], yerr=condition_data['std'], 
               capsize=5, alpha=0.7, color=['#1f77b4', '#ff7f0e'])
        ax.set_xticks(x_pos)
        ax.set_xticklabels(condition_data.index)
        ax.set_ylabel('Elapsed Time (s)')
        ax.set_title('Elapsed Time: Body vs Controller')
        ax.grid(True, alpha=0.3)
        
        # Add count labels
        for i, (idx, row) in enumerate(condition_data.iterrows()):
            ax.text(i, row['mean'] + row['std'], f"n={int(row['count'])}", 
                   ha='center', va='bottom', fontsize=9)
    
    # Collectibles per second by condition
    if 'condition' in df.columns and df['condition'].nunique() > 1:
        ax = axes[0, 1]
        coll_data = df.groupby('condition')['collectibles_per_sec'].agg(['mean', 'std', 'count'])
        x_pos = np.arange(len(coll_data))
        ax.bar(x_pos, coll_data['mean'], yerr=coll_data['std'], 
               capsize=5, alpha=0.7, color=['#2ca02c', '#d62728'])
        ax.set_xticks(x_pos)
        ax.set_xticklabels(coll_data.index)
        ax.set_ylabel('Collectibles per Second')
        ax.set_title('Collection Efficiency: Body vs Controller')
        ax.grid(True, alpha=0.3)
        
        for i, (idx, row) in enumerate(coll_data.iterrows()):
            ax.text(i, row['mean'] + row['std'], f"n={int(row['count'])}", 
                   ha='center', va='bottom', fontsize=9)
    
    # Learning effect across trials
    if 'trial' in df.columns and df['trial'].nunique() > 1:
        ax = axes[1, 0]
        trial_data = df.groupby('trial')['elapsed_time'].agg(['mean', 'std'])
        ax.plot(trial_data.index, trial_data['mean'], marker='o', linewidth=2, markersize=8)
        ax.fill_between(trial_data.index, 
                        trial_data['mean'] - trial_data['std'],
                        trial_data['mean'] + trial_data['std'],
                        alpha=0.3)
        ax.set_xlabel('Trial Number')
        ax.set_ylabel('Elapsed Time (s)')
        ax.set_title('Learning Effect Across Trials')
        ax.grid(True, alpha=0.3)
    
    # Participant comparison
    if 'participant' in df.columns and df['participant'].nunique() > 1:
        ax = axes[1, 1]
        part_data = df.groupby('participant')['elapsed_time'].mean().sort_values()
        ax.barh(range(len(part_data)), part_data.values, alpha=0.7, color='steelblue')
        ax.set_yticks(range(len(part_data)))
        ax.set_yticklabels(part_data.index)
        ax.set_xlabel('Avg Elapsed Time (s)')
        ax.set_title('Average Completion Time by Participant')
        ax.grid(True, alpha=0.3, axis='x')
    
    plt.tight_layout()
    
    if save_path:
        plt.savefig(save_path, dpi=300, bbox_inches='tight')
        print(f"Saved: {save_path}")
    
    return fig


def plot_connectivity_analysis(df, save_path=None):
    """Plot connectivity metrics."""
    fig, axes = plt.subplots(2, 2, figsize=(16, 12))
    fig.suptitle('Network Connectivity Analysis', fontsize=16, fontweight='bold')
    
    # Connectivity percentage by condition
    if 'condition' in df.columns and df['condition'].nunique() > 1:
        ax = axes[0, 0]
        cond_data = df.groupby('condition')['connectivity_percentage'].agg(['mean', 'std'])
        x_pos = np.arange(len(cond_data))
        ax.bar(x_pos, cond_data['mean'], yerr=cond_data['std'], 
               capsize=5, alpha=0.7, color=['#1f77b4', '#ff7f0e'])
        ax.set_xticks(x_pos)
        ax.set_xticklabels(cond_data.index)
        ax.set_ylabel('Connectivity %')
        ax.set_title('Network Connectivity: Body vs Controller')
        ax.set_ylim([0, 100])
        ax.axhline(y=80, color='red', linestyle='--', alpha=0.5, label='80% threshold')
        ax.legend()
        ax.grid(True, alpha=0.3)
    
    # Disconnection events by condition
    if 'condition' in df.columns and df['condition'].nunique() > 1:
        ax = axes[0, 1]
        disc_data = df.groupby('condition')['disconnection_events'].agg(['mean', 'std'])
        x_pos = np.arange(len(disc_data))
        ax.bar(x_pos, disc_data['mean'], yerr=disc_data['std'], 
               capsize=5, alpha=0.7, color=['#2ca02c', '#d62728'])
        ax.set_xticks(x_pos)
        ax.set_xticklabels(disc_data.index)
        ax.set_ylabel('Number of Events')
        ax.set_title('Disconnection Events: Body vs Controller')
        ax.grid(True, alpha=0.3)
    
    # Connectivity vs elapsed time scatter
    ax = axes[1, 0]
    if 'condition' in df.columns:
        for condition in df['condition'].unique():
            subset = df[df['condition'] == condition]
            ax.scatter(subset['elapsed_time'], subset['connectivity_percentage'], 
                      label=condition, alpha=0.6, s=100)
    else:
        ax.scatter(df['elapsed_time'], df['connectivity_percentage'], alpha=0.6, s=100)
    
    ax.set_xlabel('Elapsed Time (s)')
    ax.set_ylabel('Connectivity %')
    ax.set_title('Connectivity vs Completion Time')
    ax.legend()
    ax.grid(True, alpha=0.3)
    
    # Swarm compactness by condition
    if 'condition' in df.columns and df['condition'].nunique() > 1:
        ax = axes[1, 1]
        comp_data = df.groupby('condition')['avg_distance_from_centroid'].agg(['mean', 'std'])
        x_pos = np.arange(len(comp_data))
        ax.bar(x_pos, comp_data['mean'], yerr=comp_data['std'], 
               capsize=5, alpha=0.7, color=['#9467bd', '#8c564b'])
        ax.set_xticks(x_pos)
        ax.set_xticklabels(comp_data.index)
        ax.set_ylabel('Avg Distance from Centroid (m)')
        ax.set_title('Swarm Compactness: Body vs Controller')
        ax.grid(True, alpha=0.3)
    
    plt.tight_layout()
    
    if save_path:
        plt.savefig(save_path, dpi=300, bbox_inches='tight')
        print(f"Saved: {save_path}")
    
    return fig


def plot_embodied_analysis(df, save_path=None):
    """Plot embodied drone behavior."""
    fig, axes = plt.subplots(2, 2, figsize=(16, 12))
    fig.suptitle('Embodied Drone Behavior Analysis', fontsize=16, fontweight='bold')
    
    # Distance from swarm by condition
    if 'condition' in df.columns and df['condition'].nunique() > 1:
        ax = axes[0, 0]
        cond_data = df.groupby('condition')['avg_distance_from_swarm'].agg(['mean', 'std'])
        x_pos = np.arange(len(cond_data))
        ax.bar(x_pos, cond_data['mean'], yerr=cond_data['std'], 
               capsize=5, alpha=0.7, color=['#1f77b4', '#ff7f0e'])
        ax.set_xticks(x_pos)
        ax.set_xticklabels(cond_data.index)
        ax.set_ylabel('Avg Distance (m)')
        ax.set_title('Embodied Distance from Swarm: Body vs Controller')
        ax.grid(True, alpha=0.3)
    
    # Speed comparison
    ax = axes[0, 1]
    speeds_embodied = df['embodied_speed'].dropna()
    speeds_swarm = df['swarm_speed'].dropna()
    
    if len(speeds_embodied) > 0 and len(speeds_swarm) > 0:
        x_pos = np.arange(2)
        means = [speeds_embodied.mean(), speeds_swarm.mean()]
        stds = [speeds_embodied.std(), speeds_swarm.std()]
        ax.bar(x_pos, means, yerr=stds, capsize=5, alpha=0.7, 
               color=['#e377c2', '#7f7f7f'])
        ax.set_xticks(x_pos)
        ax.set_xticklabels(['Embodied Drone', 'Swarm Centroid'])
        ax.set_ylabel('Average Speed (m/s)')
        ax.set_title('Speed Comparison')
        ax.grid(True, alpha=0.3)
    
    # Leading percentage by condition
    if 'condition' in df.columns and df['condition'].nunique() > 1:
        ax = axes[1, 0]
        lead_data = df.groupby('condition')['leading_percentage'].agg(['mean', 'std'])
        x_pos = np.arange(len(lead_data))
        ax.bar(x_pos, lead_data['mean'], yerr=lead_data['std'], 
               capsize=5, alpha=0.7, color=['#2ca02c', '#d62728'])
        ax.set_xticks(x_pos)
        ax.set_xticklabels(lead_data.index)
        ax.set_ylabel('Leading %')
        ax.set_title('Embodied Leading Percentage: Body vs Controller')
        ax.set_ylim([0, 100])
        ax.axhline(y=50, color='gray', linestyle='--', alpha=0.5, label='50% (neutral)')
        ax.legend()
        ax.grid(True, alpha=0.3)
    
    # Speed difference distribution
    ax = axes[1, 1]
    speed_diffs = df['speed_difference'].dropna()
    if len(speed_diffs) > 0:
        ax.hist(speed_diffs, bins=15, alpha=0.7, color='steelblue', edgecolor='black')
        ax.axvline(x=0, color='red', linestyle='--', linewidth=2, label='Equal speed')
        ax.set_xlabel('Speed Difference (Embodied - Swarm) [m/s]')
        ax.set_ylabel('Frequency')
        ax.set_title('Distribution of Speed Differences')
        ax.legend()
        ax.grid(True, alpha=0.3)
    
    plt.tight_layout()
    
    if save_path:
        plt.savefig(save_path, dpi=300, bbox_inches='tight')
        print(f"Saved: {save_path}")
    
    return fig


def plot_all_analyses(df, output_dir=None):
    """Generate all analysis plots."""
    if output_dir is None:
        output_dir = Path(__file__).parent / "plots"
    
    output_dir = Path(output_dir)
    output_dir.mkdir(exist_ok=True)
    
    print("\nGenerating visualizations...")
    
    fig1 = plot_performance_comparison(df, output_dir / "performance_comparison.png")
    fig2 = plot_connectivity_analysis(df, output_dir / "connectivity_analysis.png")
    fig3 = plot_embodied_analysis(df, output_dir / "embodied_analysis.png")
    
    print(f"\nAll plots saved to: {output_dir}")
    
    return [fig1, fig2, fig3]


def main():
    """Run visualization tool."""
    print("Loading trajectory data...")
    results = analyze_all_trajectories()
    
    if not results:
        print("No trajectory files found")
        return
    
    df = results_to_dataframe(results)
    
    print(f"Loaded {len(df)} trajectories")
    
    # Generate all plots
    figs = plot_all_analyses(df)
    
    print("\nShowing plots... Close windows to exit.")
    plt.show()


if __name__ == "__main__":
    main()
