import cv2
import json
import numpy as np
from pathlib import Path
from hand_detector import HandDetector
from sklearn.metrics import r2_score

def compute_polynomial_fit(samples, degree=3):
    """Compute polynomial regression"""
    if len(samples) < degree + 1:
        return compute_linear_fit(samples)
    
    X = np.array([s[0] for s in samples])
    y = np.array([s[1] for s in samples])
    
    # Polynomial fit
    coefficients = np.polyfit(X, y, degree)
    poly = np.poly1d(coefficients)
    
    # Compute quality metrics
    y_pred = poly(X)
    r_squared = r2_score(y, y_pred)
    errors = np.abs(y - y_pred)
    
    # Reverse coefficients for our format (c0 + c1*x + c2*x^2 + ...)
    coeffs_list = list(reversed(coefficients))
    
    return {
        "type": "polynomial",
        "coefficients": [float(c) for c in coeffs_list],
        "r_squared": float(r_squared),
        "max_error": float(np.max(errors)),
        "mean_error": float(np.mean(errors))
    }

def compute_linear_fit(samples):
    """Compute linear regression"""
    if len(samples) < 2:
        return {"type": "linear", "slope": 1.0, "intercept": 0.0}
    
    X = np.array([s[0] for s in samples])
    y = np.array([s[1] for s in samples])
    
    # Linear regression
    A = np.vstack([X, np.ones(len(X))]).T
    slope, intercept = np.linalg.lstsq(A, y, rcond=None)[0]
    
    y_pred = slope * X + intercept
    r_squared = r2_score(y, y_pred)
    errors = np.abs(y - y_pred)
    
    return {
        "type": "linear",
        "slope": float(slope),
        "intercept": float(intercept),
        "r_squared": float(r_squared),
        "max_error": float(np.max(errors)),
        "mean_error": float(np.mean(errors))
    }

def main():
    print("=== Linearization Data Capture Tool ===\n")
    
    # Load existing calibration
    profile_name = input("Enter calibration profile name to update: ").strip()
    calibration_path = Path("calibrations") / f"{profile_name}.json"
    
    if not calibration_path.exists():
        print(f"Error: Calibration '{profile_name}' not found!")
        print("Please run calibration_tool.py first.")
        return
    
    with open(calibration_path, 'r') as f:
        calibration = json.load(f)
    
    print(f"\nLoaded calibration: {profile_name}")
    print(f"  Horizontal range: {calibration['min_horizontal']:.1f} - {calibration['max_horizontal']:.1f}")
    print(f"  Vertical range: {calibration['min_vertical']:.1f} - {calibration['max_vertical']:.1f}\n")
    
    print("Instructions:")
    print("  H - Switch to Horizontal (spread) mode")
    print("  V - Switch to Vertical (height) mode")
    print("  0-9 - Set target normalized value (0.0 to 0.9)")
    print("  SPACE - Capture samples at current target")
    print("  S - Save and compute linearization")
    print("  Q - Quit without saving\n")
    
    detector = HandDetector()
    cap = cv2.VideoCapture(0)
    
    if not cap.isOpened():
        print("Error: Cannot open camera")
        return
    
    mode = "horizontal"
    target_value = 0.0
    capturing = False
    samples_at_point = []
    
    horizontal_samples = []  # [(raw_value, expected_normalized), ...]
    vertical_samples = []
    
    SAMPLES_PER_TARGET = 30
    
    while True:
        ret, frame = cap.read()
        if not ret:
            break
        
        result = detector.process_frame(frame)
        
        # Display mode and target
        cv2.putText(frame, f"Mode: {mode.upper()}", 
                   (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 0), 2)
        cv2.putText(frame, f"Target: {target_value:.1f}", 
                   (10, 60), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 0), 2)
        
        if result['valid']:
            detector.draw_hands(frame, result['hand_landmarks'], result['centers'])
            
            if mode == "horizontal":
                current_raw = result['distance_raw']
                cv2.putText(frame, f"Raw Spread: {current_raw:.1f}px", 
                           (10, 90), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)
            else:
                current_raw = result['height_raw']
                cv2.putText(frame, f"Raw Height: {current_raw:.1f}px", 
                           (10, 90), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)
            
            if capturing:
                cv2.putText(frame, "CAPTURING...", 
                           (10, 120), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 0, 255), 2)
                samples_at_point.append(current_raw)
                cv2.putText(frame, f"Samples: {len(samples_at_point)}/{SAMPLES_PER_TARGET}", 
                           (10, 150), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 0, 255), 2)
                
                if len(samples_at_point) >= SAMPLES_PER_TARGET:
                    # Save median
                    median_raw = np.median(samples_at_point)
                    if mode == "horizontal":
                        horizontal_samples.append((median_raw, target_value))
                        print(f"✓ Horizontal: raw={median_raw:.1f} -> target={target_value:.1f}")
                    else:
                        vertical_samples.append((median_raw, target_value))
                        print(f"✓ Vertical: raw={median_raw:.1f} -> target={target_value:.1f}")
                    
                    capturing = False
                    samples_at_point = []
        else:
            cv2.putText(frame, "Show BOTH hands", 
                       (10, 90), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 0, 255), 2)
        
        # Display collected samples
        cv2.putText(frame, f"H samples: {len(horizontal_samples)} | V samples: {len(vertical_samples)}", 
                   (10, frame.shape[0] - 40), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1)
        cv2.putText(frame, "H/V: mode | 0-9: target | SPACE: capture | S: save | Q: quit", 
                   (10, frame.shape[0] - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1)
        
        cv2.imshow("Linearization Capture", frame)
        
        key = cv2.waitKey(1) & 0xFF
        
        if key == ord('q'):
            print("Cancelled")
            break
        elif key == ord('h'):
            mode = "horizontal"
            print("\nSwitched to HORIZONTAL mode")
        elif key == ord('v'):
            mode = "vertical"
            print("\nSwitched to VERTICAL mode")
        elif ord('0') <= key <= ord('9'):
            target_value = (key - ord('0')) / 10.0
            print(f"\nTarget set to {target_value:.1f}")
        elif key == ord(' ') and result['valid'] and not capturing:
            capturing = True
            samples_at_point = []
            print(f"Capturing at target {target_value:.1f}...")
        elif key == ord('s'):
            if len(horizontal_samples) == 0 and len(vertical_samples) == 0:
                print("No samples collected!")
                continue
            
            print("\n=== Computing Linearization Functions ===")
            
            # Compute fits
            if len(horizontal_samples) >= 2:
                h_linear = compute_linear_fit(horizontal_samples)
                h_poly = compute_polynomial_fit(horizontal_samples, degree=3)
                
                print(f"\nHorizontal:")
                print(f"  Linear: R²={h_linear['r_squared']:.4f}, Mean Error={h_linear['mean_error']:.4f}")
                print(f"  Polynomial: R²={h_poly['r_squared']:.4f}, Mean Error={h_poly['mean_error']:.4f}")
                
                # Choose best
                if h_poly['r_squared'] - h_linear['r_squared'] > 0.02:
                    calibration['horizontal_linearization'] = h_poly
                    print(f"  → Using polynomial (better fit)")
                else:
                    calibration['horizontal_linearization'] = h_linear
                    print(f"  → Using linear (simpler)")
            
            if len(vertical_samples) >= 2:
                v_linear = compute_linear_fit(vertical_samples)
                v_poly = compute_polynomial_fit(vertical_samples, degree=3)
                
                print(f"\nVertical:")
                print(f"  Linear: R²={v_linear['r_squared']:.4f}, Mean Error={v_linear['mean_error']:.4f}")
                print(f"  Polynomial: R²={v_poly['r_squared']:.4f}, Mean Error={v_poly['mean_error']:.4f}")
                
                # Choose best
                if v_poly['r_squared'] - v_linear['r_squared'] > 0.02:
                    calibration['vertical_linearization'] = v_poly
                    print(f"  → Using polynomial (better fit)")
                else:
                    calibration['vertical_linearization'] = v_linear
                    print(f"  → Using linear (simpler)")
            
            # Save updated calibration
            with open(calibration_path, 'w') as f:
                json.dump(calibration, f, indent=4)
            
            print(f"\n✓ Linearization saved to {calibration_path}")
            break
    
    cap.release()
    cv2.destroyAllWindows()
    detector.release()

if __name__ == "__main__":
    main()