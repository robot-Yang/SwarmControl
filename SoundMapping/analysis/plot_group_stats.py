"""Compare multiple SwarmTrajectoryRecorder runs across control groups.

Usage:
    python SoundMapping/analysis/plot_group_stats.py
    python SoundMapping/analysis/plot_group_stats.py --no-gui \
        --joystick run1_traj.json run2_traj.json \
        --body-control run3_traj.json run4_traj.json

The GUI starts from the last saved selection, shows the current files, and lets
you add joystick/body-control runs, remove entries, or continue without adding.
It then computes run-level statistics and writes plots/tables under
SoundMapping/analysis/outputs/group_stats/.
"""

from __future__ import annotations

import argparse
import csv
from dataclasses import dataclass
import json
import os
from pathlib import Path
import re
import tempfile
from typing import Iterable

_cache_root = Path(tempfile.gettempdir()) / "swarmcontrol_plot_group_stats"
_cache_root.mkdir(parents=True, exist_ok=True)
os.environ.setdefault("MPLCONFIGDIR", str(_cache_root / "matplotlib"))
os.environ.setdefault("XDG_CACHE_HOME", str(_cache_root / "xdg"))

import matplotlib.pyplot as plt
import numpy as np

try:
    import tkinter as tk
    from tkinter import filedialog, messagebox, ttk
except Exception:
    tk = None
    filedialog = None
    messagebox = None
    ttk = None

try:
    from scipy.stats import mannwhitneyu
except Exception:
    mannwhitneyu = None

SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parents[1]
DEFAULT_TRAJ_DIR = REPO_ROOT / "SoundMapping" / "SoundMappingUnity" / "Assets" / "Trajectories"
OUT_DIR = SCRIPT_DIR / "outputs" / "group_stats"
SELECTION_CACHE = SCRIPT_DIR / "plot_group_stats_selection.json"

GROUPS = ["joystick", "body-control"]
GROUP_LABELS = {
    "joystick": "Joystick",
    "body-control": "Body-control",
}
GROUP_COLORS = {
    "joystick": "tab:blue",
    "body-control": "tab:orange",
}

try:
    import plot_run
except Exception as exc:
    raise SystemExit(f"Could not import sibling plot_run.py: {exc}") from exc


@dataclass(frozen=True)
class Selection:
    group: str
    path: Path
    participant: str = ""


def normalize_path(path: str | Path) -> Path:
    p = Path(path).expanduser()
    try:
        return p.resolve()
    except Exception:
        return p.absolute()


def load_json(path: Path) -> dict:
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def dedupe_selections(selections: Iterable[Selection]) -> list[Selection]:
    out: list[Selection] = []
    seen: set[str] = set()
    for sel in selections:
        group = normalize_group(sel.group)
        if group not in GROUPS:
            continue
        key = str(normalize_path(sel.path))
        if key in seen:
            continue
        seen.add(key)
        participant = (sel.participant or "").strip()
        out.append(Selection(group=group, path=normalize_path(sel.path), participant=participant))
    return out


def normalize_group(text: str) -> str:
    t = (text or "").strip().lower().replace("_", "-").replace(" ", "-")
    if t in {"joystick", "joy", "traditional", "controller"}:
        return "joystick"
    if t in {"body", "body-control", "bodycontrol", "pose", "imu"}:
        return "body-control"
    return t


def load_selection_cache() -> list[Selection]:
    if not SELECTION_CACHE.exists():
        return []
    try:
        data = json.loads(SELECTION_CACHE.read_text(encoding="utf-8"))
    except Exception:
        return []
    items = []
    for row in data if isinstance(data, list) else []:
        if not isinstance(row, dict):
            continue
        group = normalize_group(str(row.get("group", "")))
        path = row.get("path")
        if group in GROUPS and path:
            items.append(Selection(group=group, path=Path(path), participant=str(row.get("participant", "")).strip()))
    return dedupe_selections(items)


def save_selection_cache(selections: Iterable[Selection]) -> None:
    data = [
        {"group": sel.group, "participant": sel.participant, "path": str(normalize_path(sel.path))}
        for sel in dedupe_selections(selections)
    ]
    SELECTION_CACHE.write_text(json.dumps(data, indent=2), encoding="utf-8")


def infer_participant_from_file(path: Path) -> str:
    try:
        text = Path(path).read_text(encoding="utf-8", errors="ignore")[:16384]
    except Exception:
        return ""
    match = re.search(r'"pid"\s*:\s*"([^"]*)"', text)
    return match.group(1).strip() if match else ""


def is_placeholder_participant(participant: str) -> bool:
    text = (participant or "").strip().lower()
    return text in {"", "default"}


TRIALS_PER_PARTICIPANT = 2


def next_participant_id(existing: Iterable[str], trials_per_participant: int = TRIALS_PER_PARTICIPANT) -> str:
    counts = {}
    for participant in existing:
        text = (participant or "").strip()
        match = re.fullmatch(r"P(\d+)", text, flags=re.IGNORECASE)
        if match:
            idx = int(match.group(1))
            counts[idx] = counts.get(idx, 0) + 1
    idx = 0
    while counts.get(idx, 0) >= trials_per_participant:
        idx += 1
    return f"P{idx}"


def default_participant_id(inferred: str, selections: Iterable[Selection]) -> str:
    if not is_placeholder_participant(inferred):
        return inferred.strip()
    return next_participant_id(sel.participant for sel in selections)


def default_participant_ids_for_paths(paths: Iterable[Path], selections: Iterable[Selection]) -> list[str]:
    out = []
    assigned = list(dedupe_selections(selections))
    for path in paths:
        inferred = infer_participant_from_file(path)
        participant = default_participant_id(inferred, assigned)
        out.append(participant)
        assigned.append(Selection(group="joystick", path=path, participant=participant))
    return out


def fill_default_participants(selections: Iterable[Selection]) -> list[Selection]:
    out = []
    for sel in dedupe_selections(selections):
        participant = sel.participant.strip()
        if is_placeholder_participant(participant):
            inferred = infer_participant_from_file(sel.path)
            participant = default_participant_id(inferred, out)
        out.append(Selection(group=sel.group, path=sel.path, participant=participant))
    return out


def infer_group_from_path(path: Path) -> str | None:
    text = str(path).lower()
    if any(tok in text for tok in ("joystick", "joy", "traditional", "controller")):
        return "joystick"
    if any(tok in text for tok in ("body-control", "body_control", "bodycontrol", "body", "pose", "imu")):
        return "body-control"
    return None


def pick_group_for_files(root, default_group: str) -> str | None:
    if tk is None or ttk is None:
        return default_group
    win = tk.Toplevel(root)
    win.title("Choose group")
    win.geometry("+120+120")
    win.resizable(False, False)
    win.transient(root)
    chosen = {"value": None}

    tk.Label(win, text="Assign selected files to:").grid(row=0, column=0, columnspan=2, padx=12, pady=(12, 6), sticky="w")
    var = tk.StringVar(value=default_group)
    combo = ttk.Combobox(win, textvariable=var, state="readonly", values=GROUPS, width=18)
    combo.grid(row=1, column=0, columnspan=2, padx=12, pady=4, sticky="ew")

    def apply():
        chosen["value"] = normalize_group(var.get())
        try:
            win.attributes("-topmost", False)
        except Exception:
            pass
        win.destroy()

    def cancel():
        chosen["value"] = None
        try:
            win.attributes("-topmost", False)
        except Exception:
            pass
        win.destroy()

    tk.Button(win, text="Cancel", command=cancel).grid(row=2, column=0, padx=12, pady=12, sticky="w")
    tk.Button(win, text="Add", command=apply).grid(row=2, column=1, padx=12, pady=12, sticky="e")
    win.protocol("WM_DELETE_WINDOW", cancel)
    win.update_idletasks()
    try:
        root.attributes("-topmost", False)
    except Exception:
        pass
    win.lift(root)
    try:
        win.attributes("-topmost", True)
    except Exception:
        pass
    try:
        win.focus_force()
    except Exception:
        pass
    win.grab_set()
    win.wait_window()
    return chosen["value"]


def ask_participant_id(root, default_participant: str = "") -> str | None:
    if tk is None:
        return default_participant
    win = tk.Toplevel(root)
    win.title("Participant ID")
    win.geometry("+140+140")
    win.resizable(False, False)
    win.transient(root)
    chosen = {"value": None}

    tk.Label(win, text="Participant ID for selected file(s):").grid(
        row=0, column=0, columnspan=2, padx=12, pady=(12, 4), sticky="w"
    )
    var = tk.StringVar(value=default_participant)
    entry = tk.Entry(win, textvariable=var, width=30)
    entry.grid(row=1, column=0, columnspan=2, padx=12, pady=4, sticky="ew")
    entry.focus_set()
    entry.select_range(0, tk.END)

    def apply() -> None:
        chosen["value"] = var.get().strip()
        try:
            win.attributes("-topmost", False)
        except Exception:
            pass
        win.destroy()

    def cancel() -> None:
        chosen["value"] = None
        try:
            win.attributes("-topmost", False)
        except Exception:
            pass
        win.destroy()

    tk.Button(win, text="Cancel", command=cancel).grid(row=2, column=0, padx=12, pady=12, sticky="w")
    tk.Button(win, text="Apply", command=apply).grid(row=2, column=1, padx=12, pady=12, sticky="e")
    win.bind("<Return>", lambda _event: apply())
    win.protocol("WM_DELETE_WINDOW", cancel)
    win.update_idletasks()
    try:
        root.attributes("-topmost", False)
    except Exception:
        pass
    win.lift(root)
    try:
        win.attributes("-topmost", True)
    except Exception:
        pass
    try:
        win.focus_force()
    except Exception:
        pass
    win.grab_set()
    win.wait_window()
    return chosen["value"]


def show_full_path(root, path: Path) -> None:
    if tk is None:
        return
    win = tk.Toplevel(root)
    win.title("Full file path")
    win.geometry("760x120+160+160")
    win.minsize(520, 110)
    win.transient(root)

    tk.Label(win, text="Full path:").pack(anchor="w", padx=12, pady=(12, 4))
    text = tk.Text(win, height=2, wrap="none")
    text.pack(fill="both", expand=True, padx=12, pady=(0, 8))
    text.insert("1.0", str(path))
    text.configure(state="disabled")

    def close() -> None:
        try:
            win.attributes("-topmost", False)
        except Exception:
            pass
        win.destroy()

    tk.Button(win, text="Close", command=close).pack(anchor="e", padx=12, pady=(0, 10))
    win.protocol("WM_DELETE_WINDOW", close)
    win.update_idletasks()
    try:
        root.attributes("-topmost", False)
    except Exception:
        pass
    win.lift(root)
    try:
        win.attributes("-topmost", True)
    except Exception:
        pass
    try:
        win.focus_force()
    except Exception:
        pass


def select_files_gui(initial: list[Selection]) -> list[Selection] | None:
    if tk is None or filedialog is None:
        return None

    selections = dedupe_selections(initial)
    root = tk.Tk()
    root.title("Trajectory files for group statistics")
    root.geometry("+80+80")
    root.minsize(1120, 520)
    try:
        root.attributes("-topmost", True)
    except Exception:
        pass

    state = {"result": None}
    tk.Label(
        root,
        text="Selected trajectory files. Add files for each group, remove unwanted rows, or continue/skip.",
        anchor="w",
    ).grid(row=0, column=0, columnspan=2, sticky="ew", padx=10, pady=(10, 4))

    trees = {}
    if ttk is None:
        root.destroy()
        return None

    columns = ("participant", "file", "status")
    for col, group in enumerate(GROUPS):
        frame = tk.LabelFrame(root, text=f"{GROUP_LABELS[group]} files", padx=6, pady=6)
        frame.grid(row=1, column=col, sticky="nsew", padx=10, pady=6)
        frame.rowconfigure(0, weight=1)
        frame.columnconfigure(0, weight=1)

        tree = ttk.Treeview(frame, columns=columns, show="headings", selectmode="extended")
        tree.heading("participant", text="Participant ID")
        tree.heading("file", text="File")
        tree.heading("status", text="Status")
        tree.column("participant", width=125, stretch=False)
        tree.column("file", width=370, stretch=True)
        tree.column("status", width=82, stretch=False)
        tree.grid(row=0, column=0, sticky="nsew")
        sb = ttk.Scrollbar(frame, orient="vertical", command=tree.yview)
        sb.grid(row=0, column=1, sticky="ns")
        tree.configure(yscrollcommand=sb.set)
        trees[group] = tree

    def show_row_path(event) -> None:
        tree = event.widget
        iid = tree.identify_row(event.y)
        if not iid:
            return
        idx = int(iid)
        if 0 <= idx < len(selections):
            tree.selection_set(iid)
            show_full_path(root, selections[idx].path)

    for tree in trees.values():
        tree.bind("<Double-1>", show_row_path)

    def refresh() -> None:
        for tree in trees.values():
            tree.delete(*tree.get_children())
        for i, sel in enumerate(selections):
            status = "OK" if sel.path.exists() else "MISSING"
            trees[sel.group].insert("", "end", iid=str(i), values=(sel.participant, str(sel.path), status))

    def selected_indices(group: str | None = None) -> list[int]:
        idxs = []
        active_groups = [group] if group else GROUPS
        for g in active_groups:
            idxs.extend(int(iid) for iid in trees[g].selection())
        return sorted(set(idxs))

    def add_files(group: str | None = None) -> None:
        start = DEFAULT_TRAJ_DIR if DEFAULT_TRAJ_DIR.exists() else Path.cwd()
        paths = filedialog.askopenfilenames(
            parent=root,
            title="Select trajectory JSON files",
            initialdir=str(start),
            filetypes=[("Trajectory JSON", "*.json"), ("All files", "*.*")],
        )
        if not paths:
            return
        target_group = group
        if target_group is None:
            inferred = infer_group_from_path(Path(paths[0])) or "joystick"
            target_group = pick_group_for_files(root, inferred)
        if target_group not in GROUPS:
            return
        existing_paths = {str(normalize_path(sel.path)) for sel in selections}
        path_objs = []
        duplicate_count = 0
        batch_seen = set()
        for p in paths:
            normalized = str(normalize_path(Path(p)))
            if normalized in existing_paths or normalized in batch_seen:
                duplicate_count += 1
                continue
            batch_seen.add(normalized)
            path_objs.append(Path(p))
        if duplicate_count and messagebox is not None:
            messagebox.showinfo(
                "Duplicate file skipped",
                f"Skipped {duplicate_count} duplicate file(s). The same JSON file can only be selected once.",
            )
        if not path_objs:
            return
        default_participants = default_participant_ids_for_paths(path_objs, selections)
        default_participant = default_participants[0] if default_participants else ""
        participant = ask_participant_id(root, default_participant)
        if participant is None:
            return
        if participant == default_participant and len(set(default_participants)) > 1:
            new_rows = [
                Selection(group=target_group, path=path, participant=pid)
                for path, pid in zip(path_objs, default_participants)
            ]
        else:
            new_rows = [
                Selection(group=target_group, path=path, participant=participant)
                for path in path_objs
            ]
        selections.extend(
            new_rows
        )
        selections[:] = dedupe_selections(selections)
        refresh()

    def edit_participant(group: str | None = None) -> None:
        picked = selected_indices(group)
        if not picked:
            if messagebox is not None:
                messagebox.showinfo("No row selected", "Select one or more rows first.")
            return

        current = ""
        first_idx = picked[0]
        if 0 <= first_idx < len(selections):
            current = selections[first_idx].participant

        win = tk.Toplevel(root)
        win.title("Edit participant ID")
        win.geometry("+140+140")
        win.resizable(False, False)
        win.transient(root)
        tk.Label(win, text="Participant ID:").grid(row=0, column=0, padx=12, pady=(12, 4), sticky="w")
        var = tk.StringVar(value=current)
        entry = tk.Entry(win, textvariable=var, width=28)
        entry.grid(row=1, column=0, columnspan=2, padx=12, pady=4, sticky="ew")
        entry.focus_set()

        def apply() -> None:
            participant = var.get().strip()
            for idx in picked:
                if 0 <= idx < len(selections):
                    old = selections[idx]
                    selections[idx] = Selection(group=old.group, path=old.path, participant=participant)
            try:
                win.attributes("-topmost", False)
            except Exception:
                pass
            win.destroy()
            refresh()

        def cancel_edit() -> None:
            try:
                win.attributes("-topmost", False)
            except Exception:
                pass
            win.destroy()

        tk.Button(win, text="Cancel", command=cancel_edit).grid(row=2, column=0, padx=12, pady=12, sticky="w")
        tk.Button(win, text="Apply", command=apply).grid(row=2, column=1, padx=12, pady=12, sticky="e")
        win.bind("<Return>", lambda _event: apply())
        win.protocol("WM_DELETE_WINDOW", cancel_edit)
        win.update_idletasks()
        try:
            root.attributes("-topmost", False)
        except Exception:
            pass
        win.lift(root)
        try:
            win.attributes("-topmost", True)
        except Exception:
            pass
        try:
            win.focus_force()
        except Exception:
            pass
        win.grab_set()
        win.wait_window()

    def remove_selected(group: str | None = None) -> None:
        idxs = sorted(selected_indices(group), reverse=True)
        for idx in idxs:
            if 0 <= idx < len(selections):
                del selections[idx]
        refresh()

    def clear_all() -> None:
        selections.clear()
        refresh()

    def continue_with_selection() -> None:
        valid = [s for s in dedupe_selections(selections) if s.path.exists()]
        if not valid:
            if messagebox is not None:
                messagebox.showwarning("No files", "Add at least one existing trajectory JSON file.")
            return
        state["result"] = valid
        root.destroy()

    def cancel() -> None:
        state["result"] = None
        root.destroy()

    for col, group in enumerate(GROUPS):
        controls = tk.Frame(root)
        controls.grid(row=2, column=col, sticky="ew", padx=10, pady=(0, 8))
        tk.Button(controls, text=f"Add {GROUP_LABELS[group]}", command=lambda g=group: add_files(g)).pack(side="left", padx=(0, 4))
        tk.Button(controls, text="Edit participant ID", command=lambda g=group: edit_participant(g)).pack(side="left", padx=4)
        tk.Button(controls, text="Remove selected", command=lambda g=group: remove_selected(g)).pack(side="right", padx=(4, 0))

    bottom_bar = tk.Frame(root)
    bottom_bar.grid(row=3, column=0, columnspan=2, sticky="ew", padx=10, pady=(0, 10))
    tk.Button(bottom_bar, text="Clear all", command=clear_all).pack(side="left", padx=(0, 4))
    tk.Button(bottom_bar, text="Skip/cancel", command=cancel).pack(side="right", padx=(4, 0))
    tk.Button(bottom_bar, text="Calculate and plot", command=continue_with_selection).pack(side="right", padx=4)

    root.rowconfigure(1, weight=1)
    for col in range(2):
        root.columnconfigure(col, weight=1)
    root.protocol("WM_DELETE_WINDOW", cancel)
    refresh()
    root.mainloop()
    return state["result"]


def center_series(log: dict) -> tuple[np.ndarray, np.ndarray]:
    times, points = plot_run._swarm_center_series(log)
    if len(times) == 0:
        return times, points
    order = np.argsort(times)
    times = times[order]
    points = points[order]
    return times - times[0], points


def path_length(points: np.ndarray) -> float:
    if len(points) < 2:
        return float("nan")
    deltas = np.diff(points, axis=0)
    return float(np.sum(np.linalg.norm(deltas, axis=1)))


def run_duration(log: dict, rel_times: np.ndarray) -> float:
    elapsed = log.get("elapsedTime")
    if isinstance(elapsed, (int, float)) and float(elapsed) > 0:
        return float(elapsed)
    if len(rel_times) > 0:
        return float(rel_times[-1] - rel_times[0])
    return float("nan")


def compute_metrics(sel: Selection) -> dict:
    log = load_json(sel.path)
    participant = sel.participant or str(log.get("pid", "")).strip()
    if is_placeholder_participant(participant):
        participant = "P0"
    times, points = center_series(log)
    duration = run_duration(log, times)
    length = path_length(points)
    speed = length / duration if np.isfinite(length) and np.isfinite(duration) and duration > 0 else float("nan")

    swarm_frames = log.get("swarmFrames") or []
    n_disc = [float(f.get("nDisc", np.nan)) for f in swarm_frames if isinstance(f, dict)]
    n_sub = [float(f.get("nSub", np.nan)) for f in swarm_frames if isinstance(f, dict)]

    gaps = plot_run.derive_gaps_from_obstacles(log.get("obstacles", []))
    if not gaps:
        course_path = plot_run.autodetect_course_json(sel.path)
        gaps = plot_run.load_course_gaps(course_path)
    gap_devs = plot_run.compute_gap_center_deviations(log, gaps)
    mean_gap_dev = float(np.mean([d["deviation"] for d in gap_devs])) if gap_devs else float("nan")

    total_collectibles = log.get("totalCollectibles", np.nan)
    collected = log.get("collectiblesPickedUp", 0)
    collection_pct = (
        100.0 * float(collected) / float(total_collectibles)
        if isinstance(total_collectibles, (int, float)) and float(total_collectibles) > 0
        else float("nan")
    )

    return {
        "group": sel.group,
        "group_label": GROUP_LABELS[sel.group],
        "path": str(sel.path),
        "file": sel.path.name,
        "scene": log.get("scene", ""),
        "participant": participant,
        "json_pid": log.get("pid", ""),
        "duration_s": duration,
        "path_length_m": length,
        "mean_speed_mps": speed,
        "collectibles": float(collected),
        "total_collectibles": float(total_collectibles) if isinstance(total_collectibles, (int, float)) else float("nan"),
        "collection_pct": collection_pct,
        "crash_count": float(log.get("crashCount", len(log.get("crashes", [])))),
        "mean_disconnected": float(np.nanmean(n_disc)) if n_disc else float("nan"),
        "max_subnetworks": float(np.nanmax(n_sub)) if n_sub else float("nan"),
        "mean_gap_deviation_m": mean_gap_dev,
        "_times": times,
        "_points": points,
        "_log": log,
    }


def finite_values(rows: list[dict], group: str, key: str) -> np.ndarray:
    vals = [float(r[key]) for r in rows if r["group"] == group and np.isfinite(float(r.get(key, np.nan)))]
    return np.array(vals, dtype=float)


def summarize(rows: list[dict], metric_keys: list[str]) -> list[dict]:
    summary = []
    for group in GROUPS:
        for key in metric_keys:
            vals = finite_values(rows, group, key)
            summary.append({
                "group": group,
                "metric": key,
                "n": len(vals),
                "mean": float(np.mean(vals)) if len(vals) else float("nan"),
                "std": float(np.std(vals, ddof=1)) if len(vals) > 1 else float("nan"),
                "median": float(np.median(vals)) if len(vals) else float("nan"),
                "min": float(np.min(vals)) if len(vals) else float("nan"),
                "max": float(np.max(vals)) if len(vals) else float("nan"),
            })
    return summary


def compare_groups(rows: list[dict], metric_keys: list[str]) -> list[dict]:
    comparisons = []
    if mannwhitneyu is None:
        return comparisons
    for key in metric_keys:
        a = finite_values(rows, "joystick", key)
        b = finite_values(rows, "body-control", key)
        if len(a) == 0 or len(b) == 0:
            continue
        try:
            stat, p = mannwhitneyu(a, b, alternative="two-sided")
        except Exception:
            continue
        comparisons.append({
            "metric": key,
            "test": "Mann-Whitney U",
            "joystick_n": len(a),
            "body_control_n": len(b),
            "statistic": float(stat),
            "p_value": float(p),
        })
    return comparisons


def write_csv(path: Path, rows: list[dict], fieldnames: list[str]) -> None:
    with open(path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)


def plot_metric_grid(rows: list[dict], metric_keys: list[str], metric_labels: dict[str, str], out_path: Path, show: bool) -> None:
    ncols = 3
    nrows = int(np.ceil(len(metric_keys) / ncols))
    fig, axes = plt.subplots(nrows, ncols, figsize=(14, 4.2 * nrows))
    axes_arr = np.atleast_1d(axes).ravel()
    rng = np.random.default_rng(42)

    for ax, key in zip(axes_arr, metric_keys):
        data = [finite_values(rows, group, key) for group in GROUPS]
        labels = [GROUP_LABELS[g] for g in GROUPS]
        try:
            ax.boxplot(data, tick_labels=labels, showmeans=True, patch_artist=True)
        except TypeError:
            ax.boxplot(data, labels=labels, showmeans=True, patch_artist=True)
        for idx, (group, vals) in enumerate(zip(GROUPS, data), start=1):
            if len(vals) == 0:
                continue
            x = idx + rng.uniform(-0.08, 0.08, size=len(vals))
            ax.scatter(x, vals, color=GROUP_COLORS[group], alpha=0.72, s=34, edgecolor="white", linewidth=0.5)
        ax.set_title(metric_labels[key])
        ax.grid(True, axis="y", alpha=0.25)
        ax.set_xlabel("")

    for ax in axes_arr[len(metric_keys):]:
        ax.axis("off")

    fig.suptitle("Trajectory statistics by control group", fontsize=15)
    fig.tight_layout(rect=(0, 0, 1, 0.97))
    fig.savefig(out_path, dpi=150)
    if show:
        plt.show()
    else:
        plt.close(fig)


def _resample_path(times: np.ndarray, points: np.ndarray, n: int = 160) -> np.ndarray | None:
    if len(times) < 2 or len(points) < 2:
        return None
    t0 = float(times[0])
    t1 = float(times[-1])
    if not np.isfinite(t1 - t0) or t1 <= t0:
        return None
    target = np.linspace(t0, t1, n)
    xs = np.interp(target, times, points[:, 0])
    zs = np.interp(target, times, points[:, 2])
    return np.column_stack([xs, zs])


def plot_trajectory_overlay(rows: list[dict], out_path: Path, show: bool) -> None:
    fig, ax = plt.subplots(figsize=(10, 10))

    first_log = next((r["_log"] for r in rows if r.get("_log")), None)
    if first_log is not None:
        for patch in plot_run._obstacle_patches_xz(first_log.get("obstacles", []), facecolor="#888888", alpha=0.12):
            ax.add_patch(patch)

    for group in GROUPS:
        color = GROUP_COLORS[group]
        resampled = []
        for row in rows:
            if row["group"] != group:
                continue
            times = row["_times"]
            points = row["_points"]
            if len(points) < 2:
                continue
            ax.plot(points[:, 0], points[:, 2], color=color, alpha=0.22, linewidth=1.0)
            rp = _resample_path(times, points)
            if rp is not None:
                resampled.append(rp)
        if resampled:
            mean_path = np.mean(np.stack(resampled, axis=0), axis=0)
            ax.plot(
                mean_path[:, 0],
                mean_path[:, 1],
                color=color,
                linewidth=3.0,
                label=f"{GROUP_LABELS[group]} mean (n={len(resampled)})",
            )

    ax.set_aspect("equal", adjustable="datalim")
    ax.set_xlabel("X (m)")
    ax.set_ylabel("Z (m)")
    ax.set_title("Swarm-center trajectories by control group")
    ax.grid(True, alpha=0.25)
    ax.legend(loc="best", framealpha=0.9)
    plot_run._match_unity_scene_top_view(ax)
    fig.tight_layout()
    fig.savefig(out_path, dpi=150)
    if show:
        plt.show()
    else:
        plt.close(fig)


def printable_summary(summary_rows: list[dict], metric_keys: list[str], metric_labels: dict[str, str]) -> str:
    lines = []
    for key in metric_keys:
        lines.append(metric_labels[key])
        for group in GROUPS:
            row = next(r for r in summary_rows if r["group"] == group and r["metric"] == key)
            mean = row["mean"]
            std = row["std"]
            mean_text = f"{mean:.3g}" if np.isfinite(mean) else "nan"
            std_text = f"{std:.3g}" if np.isfinite(std) else "nan"
            lines.append(f"  {GROUP_LABELS[group]}: n={row['n']}, mean={mean_text}, std={std_text}")
    return "\n".join(lines)


def run_analysis(selections: list[Selection], show: bool) -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    selections = [s for s in fill_default_participants(selections) if s.path.exists()]
    if not selections:
        raise SystemExit("No existing trajectory files selected.")

    rows = []
    for i, sel in enumerate(selections, start=1):
        print(f"[{i}/{len(selections)}] Processing {GROUP_LABELS[sel.group]}: {sel.path}")
        try:
            rows.append(compute_metrics(sel))
        except Exception as exc:
            print(f"  Skipped {sel.path}: {exc}")

    if not rows:
        raise SystemExit("No trajectory files could be processed.")

    metric_keys = [
        "duration_s",
        "path_length_m",
        "mean_speed_mps",
        "collectibles",
        "collection_pct",
        "crash_count",
        "mean_disconnected",
        "max_subnetworks",
        "mean_gap_deviation_m",
    ]
    metric_labels = {
        "duration_s": "Duration (s)",
        "path_length_m": "Swarm-center path length (m)",
        "mean_speed_mps": "Mean swarm-center speed (m/s)",
        "collectibles": "Collectibles picked up",
        "collection_pct": "Collectibles picked up (%)",
        "crash_count": "Crash count",
        "mean_disconnected": "Mean disconnected drones",
        "max_subnetworks": "Max subnetworks",
        "mean_gap_deviation_m": "Mean gap-center deviation (m)",
    }

    public_rows = [{k: v for k, v in row.items() if not k.startswith("_")} for row in rows]
    summary_rows = summarize(rows, metric_keys)
    comparison_rows = compare_groups(rows, metric_keys)

    metrics_csv = OUT_DIR / "group_run_metrics.csv"
    summary_csv = OUT_DIR / "group_summary_stats.csv"
    comparison_csv = OUT_DIR / "group_comparisons.csv"
    metric_plot = OUT_DIR / "group_metric_boxplots.png"
    traj_plot = OUT_DIR / "group_trajectory_overlay.png"

    write_csv(metrics_csv, public_rows, list(public_rows[0].keys()))
    write_csv(summary_csv, summary_rows, ["group", "metric", "n", "mean", "std", "median", "min", "max"])
    if comparison_rows:
        write_csv(comparison_csv, comparison_rows, ["metric", "test", "joystick_n", "body_control_n", "statistic", "p_value"])

    plot_metric_grid(rows, metric_keys, metric_labels, metric_plot, show=show)
    plot_trajectory_overlay(rows, traj_plot, show=show)

    print("\nSummary:")
    print(printable_summary(summary_rows, metric_keys, metric_labels))
    if comparison_rows:
        print(f"\nWrote Mann-Whitney group comparisons to {comparison_csv}")
    elif mannwhitneyu is None:
        print("\nSciPy is not installed; skipped Mann-Whitney group comparisons.")
    print(f"\nWrote run metrics to {metrics_csv}")
    print(f"Wrote summary stats to {summary_csv}")
    print(f"Wrote plots to {metric_plot} and {traj_plot}")


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--joystick", type=Path, nargs="*", default=[], help="Joystick trajectory JSON files.")
    p.add_argument("--body-control", type=Path, nargs="*", default=[], help="Body-control trajectory JSON files.")
    p.add_argument("--joystick-participant", default="", help="Participant ID to assign to all --joystick files.")
    p.add_argument("--body-control-participant", default="", help="Participant ID to assign to all --body-control files.")
    p.add_argument("--no-gui", action="store_true", help="Do not open the Tk file-selection window.")
    p.add_argument("--show", action="store_true", help="Show plots interactively after saving them.")
    p.add_argument("--no-cache", action="store_true", help="Do not load/save the selected-file cache.")
    return p.parse_args()


def main() -> None:
    args = parse_args()
    selections = [
        *(Selection("joystick", p, args.joystick_participant.strip()) for p in args.joystick),
        *(Selection("body-control", p, args.body_control_participant.strip()) for p in args.body_control),
    ]

    if not args.no_cache:
        selections = [*load_selection_cache(), *selections]
    selections = fill_default_participants(selections)

    if not args.no_gui:
        gui_selection = select_files_gui(selections)
        if gui_selection is None:
            print("Selection skipped/canceled; nothing to plot.")
            return
        selections = gui_selection

    if not args.no_cache:
        selections = fill_default_participants(selections)
        save_selection_cache(selections)

    run_analysis(selections, show=args.show)


if __name__ == "__main__":
    main()
