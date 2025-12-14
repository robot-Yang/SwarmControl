"""
Condition Comparison Tool
Compare metrics across different experimental conditions (Haptics, Order, PID, Scene).
"""

import pandas as pd
import numpy as np
from pathlib import Path
from analyze_metrics import analyze_all_trajectories
from typing import List, Dict


def results_to_dataframe(results: List[Dict]) -> pd.DataFrame:
    """Convert analysis results to a flat pandas DataFrame."""
    rows = []
    
    for result in results:
        row = {
            'filename': result['filename'],
            'participant': result['participant'],
            'condition': result['condition'],
            'trial': result['trial'],
            # Performance
            'elapsed_time': result['performance']['elapsed_time'],
            'collectibles': result['performance']['collectibles_picked_up'],
            'collectibles_per_sec': result['performance']['collectibles_per_second'],
            # Path
            'total_distance': result['path']['total_distance'],
            'direct_distance': result['path']['direct_distance'],
            'path_efficiency': result['path']['path_efficiency'],
            'avg_speed': result['path']['avg_speed'],
            'path_smoothness': result['path']['path_smoothness'],
            # Compactness
            'avg_swarm_variance': result['compactness']['avg_swarm_variance'],
            'max_swarm_variance': result['compactness']['max_swarm_variance'],
            'avg_distance_from_centroid': result['compactness']['avg_distance_from_centroid'],
            'swarm_compactness_std': result['compactness']['swarm_compactness_std'],
            # Connectivity
            'connectivity_percentage': result['connectivity']['connectivity_percentage'],
            'disconnection_events': result['connectivity']['disconnection_events'],
            'avg_disconnection_duration': result['connectivity']['avg_disconnection_duration'],
            'max_disconnection_duration': result['connectivity']['max_disconnection_duration'],
            # Embodied
            'avg_distance_from_swarm': result['embodied']['avg_distance_from_swarm'],
            'max_distance_from_swarm': result['embodied']['max_distance_from_swarm'],
            'embodied_speed': result['embodied']['embodied_speed'],
            'swarm_speed': result['embodied']['swarm_speed'],
            'speed_difference': result['embodied']['speed_difference'],
            'leading_percentage': result['embodied']['leading_percentage']
        }
        rows.append(row)
    
    return pd.DataFrame(rows)


def compare_conditions(df: pd.DataFrame) -> pd.DataFrame:
    """Compare metrics between Body vs Controller conditions."""
    if 'condition' not in df.columns or df['condition'].nunique() < 2:
        print("Not enough data to compare conditions")
        return pd.DataFrame()
    
    comparison = df.groupby('condition').agg({
        'elapsed_time': ['mean', 'std', 'count'],
        'collectibles_per_sec': ['mean', 'std'],
        'path_efficiency': ['mean', 'std'],
        'connectivity_percentage': ['mean', 'std'],
        'avg_distance_from_swarm': ['mean', 'std'],
        'total_distance': ['mean', 'std']
    }).round(3)
    
    return comparison


def compare_trials(df: pd.DataFrame) -> pd.DataFrame:
    """Compare metrics across trial numbers (learning effects)."""
    if 'trial' not in df.columns or df['trial'].nunique() < 2:
        print("Not enough data to compare trials")
        return pd.DataFrame()
    
    comparison = df.groupby('trial').agg({
        'elapsed_time': ['mean', 'std', 'count'],
        'collectibles_per_sec': ['mean', 'std'],
        'path_efficiency': ['mean', 'std'],
        'connectivity_percentage': ['mean', 'std'],
        'path_smoothness': ['mean', 'std'],
        'total_distance': ['mean', 'std']
    }).round(3)
    
    return comparison


def compare_participants(df: pd.DataFrame) -> pd.DataFrame:
    """Compare metrics across different participants."""
    if 'participant' not in df.columns or df['participant'].nunique() < 2:
        print("Not enough data to compare participants")
        return pd.DataFrame()
    
    comparison = df.groupby('participant').agg({
        'elapsed_time': ['mean', 'std', 'count'],
        'collectibles_per_sec': ['mean', 'std'],
        'path_efficiency': ['mean', 'std'],
        'connectivity_percentage': ['mean', 'std'],
        'avg_distance_from_swarm': ['mean', 'std']
    }).round(3)
    
    return comparison


def compare_condition_by_participant(df: pd.DataFrame) -> pd.DataFrame:
    """Compare Body vs Controller for each participant (within-subject)."""
    if 'participant' not in df.columns or 'condition' not in df.columns:
        print("Not enough data to compare conditions by participant")
        return pd.DataFrame()
    
    comparison = df.groupby(['participant', 'condition']).agg({
        'elapsed_time': ['mean', 'std', 'count'],
        'collectibles_per_sec': ['mean', 'std'],
        'path_efficiency': ['mean', 'std'],
        'connectivity_percentage': ['mean', 'std'],
        'total_distance': ['mean', 'std']
    }).round(3)
    
    return comparison


def export_summary(df: pd.DataFrame, output_file: Path = None):
    """Export summary statistics to CSV."""
    if output_file is None:
        output_file = Path(__file__).parent / "trajectory_summary.csv"
    
    df.to_csv(output_file, index=False)
    print(f"\nSummary exported to: {output_file}")


def main():
    """Run all comparisons and display results."""
    print("Loading and analyzing trajectories...")
    results = analyze_all_trajectories()
    
    if not results:
        print("No trajectory files found to analyze")
        return
    
    df = results_to_dataframe(results)
    
    print("\n" + "="*70)
    print("TRAJECTORY DATA SUMMARY")
    print("="*70)
    print(f"Total trajectories analyzed: {len(df)}")
    print(f"Unique participants: {df['participant'].nunique()}")
    print(f"Participants: {sorted(df['participant'].unique())}")
    print(f"Conditions: {sorted(df['condition'].unique())}")
    print(f"Trial numbers: {sorted(df['trial'].unique())}")
    
    print("\n" + "="*70)
    print("CONDITION COMPARISON (Body vs Controller)")
    print("="*70)
    condition_comp = compare_conditions(df)
    if not condition_comp.empty:
        print(condition_comp)
    
    print("\n" + "="*70)
    print("TRIAL COMPARISON (Learning Effects)")
    print("="*70)
    trial_comp = compare_trials(df)
    if not trial_comp.empty:
        print(trial_comp)
    
    print("\n" + "="*70)
    print("PARTICIPANT COMPARISON")
    print("="*70)
    participant_comp = compare_participants(df)
    if not participant_comp.empty:
        print(participant_comp)
    
    print("\n" + "="*70)
    print("CONDITION BY PARTICIPANT (Within-Subject)")
    print("="*70)
    within_comp = compare_condition_by_participant(df)
    if not within_comp.empty:
        print(within_comp)
    
    # Export full summary
    export_summary(df)
    
    return df


if __name__ == "__main__":
    df = main()
