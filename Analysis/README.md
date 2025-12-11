# Trajectory Analysis Tools

Comprehensive analysis suite for SwarmControl trajectory data. Analyzes drone swarm performance, path efficiency, network connectivity, and embodied behavior across experimental conditions.

## Overview

This toolset processes trajectory JSON files recorded during Unity experiments and generates statistical comparisons and visualizations across:
- **Body vs Controller conditions** (embodied control methods)
- **Learning effects** (trial-by-trial improvement)
- **Individual differences** (participant comparisons)
- **Within-subject analysis** (condition effects per participant)

## File Structure

```
Analysis/
├── README.md                    # This file
├── analyze_metrics.py           # Core metric calculation engine
├── compare_conditions.py        # Statistical comparisons
├── visualize_results.py         # Visualization generator
├── plot_trajectories.py         # 3D trajectory plotting
├── requirements.txt             # Python dependencies
└── plots/                       # Generated visualizations (auto-created)
```

## Naming Convention

Trajectory files must follow this naming pattern for automatic analysis:

```
ParticipantName_B/C_TrialNumber.json
```

**Examples:**
- `Darius_C_1.json` → Participant: Darius, Condition: Controller, Trial: 1
- `Darius_B_2.json` → Participant: Darius, Condition: Body, Trial: 2
- `Gabriel_C_3.json` → Participant: Gabriel, Condition: Controller, Trial: 3

**Where:**
- **ParticipantName**: Any alphabetic name (e.g., Darius, Gabriel)
- **B/C**: Condition code
  - `B` = Body (embodied control)
  - `C` = Controller (traditional control)
- **TrialNumber**: Integer trial number (1, 2, 3, ...)

Files saved in Unity go to: `../SoundMapping/SoundMappingUnity/Assets/Trajectories/`

## Installation

```bash
cd Analysis
pip install -r requirements.txt
```

**Dependencies:**
- Python 3.7+
- matplotlib (visualization)
- numpy (numerical computation)
- pandas (data analysis)

## Usage

### 1. Comprehensive Analysis & Visualization

Analyze all trajectories and generate publication-ready plots:

```bash
python visualize_results.py
```

**Generates:**
- `plots/performance_comparison.png` - Elapsed time, collection efficiency, learning curves
- `plots/connectivity_analysis.png` - Network connectivity, disconnection events
- `plots/embodied_analysis.png` - Embodied drone behavior, leading percentage

### 2. Statistical Comparisons

Run statistical comparisons only (terminal output + CSV):

```bash
python compare_conditions.py
```

**Outputs:**
- Terminal: Summary statistics with mean ± std
- `trajectory_summary.csv` - Full dataset export

**Comparisons performed:**
- Body vs Controller (main effect)
- Trial progression (learning effect)
- Participant differences
- Within-subject analysis (condition × participant)

### 3. Individual Trajectory Analysis

Analyze and print metrics for all trajectories:

```bash
python analyze_metrics.py
```

**Displays:**
- Performance: Elapsed time, collectibles picked, efficiency
- Path: Total distance, path efficiency, speed, smoothness
- Compactness: Swarm variance, centroid distance
- Connectivity: Connection percentage, disconnection events
- Embodied: Distance from swarm, leading behavior

### 4. 3D Trajectory Visualization

Plot 3D flight paths with optional reference comparison:

```bash
python plot_trajectories.py
```

**Features:**
- Automatic detection of latest trajectory
- Color-coded drone paths
- Centroid trajectory overlay
- Optional reference trajectory comparison

**To set reference trajectory:** Edit line 23 in `plot_trajectories.py`:
```python
REFERENCE_TRAJECTORY = "Darius_B_1.json"  # Or None to disable
```

## Metrics Explained

### Performance Metrics
- **Elapsed Time**: Total time between start and end colliders (seconds)
- **Collectibles Picked Up**: Total count of collected items
- **Collectibles per Second**: Collection efficiency metric

### Path Metrics
- **Total Distance**: Sum of centroid movement (meters)
- **Direct Distance**: Straight-line start-to-end distance
- **Path Efficiency**: Direct distance / Total distance (higher = more direct path)
- **Average Speed**: Mean centroid velocity (m/s)
- **Path Smoothness**: Inverse of acceleration variance (higher = smoother)

### Swarm Compactness
- **Average Swarm Variance**: Mean spatial spread of drones
- **Max Swarm Variance**: Maximum spread during trial
- **Avg Distance from Centroid**: Mean drone distance from swarm center
- **Compactness Std**: Variability in swarm cohesion

### Network Connectivity
- **Connectivity Percentage**: % of time swarm was fully connected
- **Disconnection Events**: Number of times connectivity dropped below 100%
- **Avg Disconnection Duration**: Mean length of disconnection events
- **Max Disconnection Duration**: Longest disconnection period

### Embodied Behavior
- **Avg Distance from Swarm**: Mean distance of embodied drone from others
- **Max Distance from Swarm**: Furthest separation from swarm
- **Embodied Speed**: Average velocity of embodied drone
- **Swarm Speed**: Average velocity of swarm centroid
- **Speed Difference**: Embodied - Swarm speed (positive = leading)
- **Leading Percentage**: % of time embodied drone was ahead of swarm

## Data Format

Trajectory JSON files contain:

```json
{
  "trajectories": [
    {
      "droneId": "Drone_0",
      "frames": [
        {
          "t": 12.34,
          "x": 1.5, "y": 2.0, "z": 3.2,
          "connected": true
        }
      ]
    }
  ],
  "trials": [
    {
      "label": "Run",
      "startGameTime": 5.0,
      "endGameTime": 125.5
    }
  ],
  "collectiblesPickedUp": 8,
  "elapsedTime": 120.5
}
```

**Key fields:**
- `trajectories`: Per-drone position/connectivity data
- `trials`: Start/end times for filtering
- `collectiblesPickedUp`: Total collectibles gathered
- `elapsedTime`: Calculated trial duration

## Workflow Example

1. **Record in Unity**: Save as `Darius_C_1.json`
2. **Record more trials**: `Darius_C_2.json`, `Darius_B_1.json`, etc.
3. **Analyze**: `python visualize_results.py`
4. **Review plots**: Check `plots/` folder
5. **Export data**: `trajectory_summary.csv` for external analysis

## Troubleshooting

**"No trajectory files found"**
- Check files are in `../SoundMapping/SoundMappingUnity/Assets/Trajectories/`
- Verify `.json` extension

**"Not enough data to compare conditions"**
- Need at least 2 conditions (Body AND Controller)
- Check filename format matches `Name_B/C_Trial.json`

**"Import pandas/matplotlib could not be resolved"**
- Run: `pip install -r requirements.txt`

**Custom filename not recognized**
- Files like `test.json` will show as "Unknown" in comparisons
- Use structured naming for proper analysis

## Advanced Usage

### Modify Metrics

Edit `analyze_metrics.py` to add custom calculations:

```python
def calculate_custom_metric(data: Dict) -> Dict:
    # Your analysis here
    return {'metric_name': value}
```

Add to `analyze_trajectory()` function.

### Change Visualization Style

Edit `visualize_results.py`:
- Modify color schemes in plot functions
- Adjust figure sizes: `figsize=(16, 12)`
- Change DPI: `savefig(..., dpi=300)`

### Filter Data

In `compare_conditions.py`, filter before analysis:

```python
df = df[df['participant'] != 'Unknown']  # Remove unstructured filenames
df = df[df['trial'] <= 5]  # Only first 5 trials
```

## Research Applications

- **Condition Effects**: Does embodied (Body) control outperform traditional (Controller)?
- **Learning Curves**: How does performance improve across trials?
- **Individual Differences**: Which participants adapted fastest?
- **Interaction Effects**: Does condition effect vary by participant?
- **Network Analysis**: How does control method affect swarm connectivity?
- **Path Analysis**: Are Body-controlled paths more/less efficient?

## Citation

If you use this analysis suite, please cite the SwarmControl project.

## Support

For issues or questions, contact the SwarmControl development team.
