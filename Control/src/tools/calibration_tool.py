import cv2
import json
import numpy as np
from pathlib import Path
from hand_detector import HandDetector

def main():
    print("=== Hand Tracking Calibration Tool ===\n")
    
    # Get profile name
    profile_name = input("Enter calibration profile name (e.g., 'gabriel'): ").strip()
    if not profile_name:
        profile_name = "default"
    
    description = input("Description (optional): ").strip()
    
    print("\nStarting calibration...\n")
    print("Instructions:")
    print("  1. Show both hands to camera at MINIMUM spread")
    print("  2. Press SPACE to capture minimum")
    print("  3. Move hands to MAXIMUM spread")
    print("  4. Press SPACE to capture maximum")
    print("  5. Move hands to MINIMUM height")
    print("  6. Press SPACE to capture minimum height")
    print("  7. Move hands to MAXIMUM height")
    print("  8. Press SPACE to capture maximum height")
    print("  9. Move hands to NEUTRAL height (center)")
    print("  10. Press SPACE to capture neutral height")
    print("  Press 'q' to quit\n")
    
    detector = HandDetector()
    cap = cv2.VideoCapture(0)
    
    if not cap.isOpened():
        print("Error: Cannot open camera")
        return
    
    # Calibration state
    state = "min_horizontal"
    samples = {
        'min_horizontal': [],
        'max_horizontal': [],
        'min_vertical': [],
        'max_vertical': [],
        'neutral_vertical': []
    }
    
    state_messages = {
        'min_horizontal': "Place hands at MINIMUM SPREAD - Press SPACE",
        'max_horizontal': "Place hands at MAXIMUM SPREAD - Press SPACE",
        'min_vertical': "Place hands at MINIMUM HEIGHT - Press SPACE",
        'max_vertical': "Place hands at MAXIMUM HEIGHT - Press SPACE",
        'neutral_vertical': "Place hands at NEUTRAL HEIGHT (center) - Press SPACE",
        'done': "Calibration complete! Saving..."
    }
    
    capturing = False
    capture_count = 0
    SAMPLES_PER_POINT = 30
    
    while True:
        ret, frame = cap.read()
        if not ret:
            break
        
        result = detector.process_frame(frame)
        
        # Draw instructions
        cv2.putText(frame, state_messages.get(state, ""), 
                   (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 255), 2)
        
        if result['valid']:
            detector.draw_hands(frame, result['hand_landmarks'], result['centers'])
            
            cv2.putText(frame, f"Raw Spread: {result['distance_raw']:.1f}px", 
                       (10, 60), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 2)
            cv2.putText(frame, f"Raw Height: {result['height_raw']:.1f}px", 
                       (10, 85), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 2)
            
            # Capture samples
            if capturing and state != 'done':
                if 'horizontal' in state:
                    samples[state].append(result['distance_raw'])
                else:
                    samples[state].append(result['height_raw'])
                
                capture_count += 1
                cv2.putText(frame, f"Capturing: {capture_count}/{SAMPLES_PER_POINT}", 
                           (10, 110), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 0, 255), 2)
                
                if capture_count >= SAMPLES_PER_POINT:
                    capturing = False
                    capture_count = 0
                    print(f"✓ Captured {state}: {np.median(samples[state]):.1f}px")
                    
                    # Move to next state
                    if state == 'min_horizontal':
                        state = 'max_horizontal'
                    elif state == 'max_horizontal':
                        state = 'min_vertical'
                    elif state == 'min_vertical':
                        state = 'max_vertical'
                    elif state == 'max_vertical':
                        state = 'neutral_vertical'
                    elif state == 'neutral_vertical':
                        state = 'done'
        else:
            cv2.putText(frame, "Show BOTH hands at similar distance from camera", 
                       (10, 60), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 0, 255), 2)
        
        cv2.putText(frame, "Press 'q' to quit", 
                   (10, frame.shape[0] - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1)
        
        cv2.imshow("Calibration Tool", frame)
        
        key = cv2.waitKey(1) & 0xFF
        
        if key == ord('q'):
            print("Calibration cancelled")
            break
        elif key == ord(' ') and result['valid'] and not capturing and state != 'done':
            capturing = True
            capture_count = 0
            print(f"Capturing {state}...")
        
        if state == 'done':
            # Compute final values
            calibration = {
                "profile_name": profile_name,
                "description": description,
                "min_horizontal": float(np.median(samples['min_horizontal'])),
                "max_horizontal": float(np.median(samples['max_horizontal'])),
                "min_vertical": float(np.median(samples['min_vertical'])),
                "max_vertical": float(np.median(samples['max_vertical'])),
                "neutral_vertical": float(np.median(samples['neutral_vertical'])),
                "smooth_alpha": 0.2,
                "horizontal_linearization": {
                    "type": "linear",
                    "slope": 1.0,
                    "intercept": 0.0
                },
                "vertical_linearization": {
                    "type": "linear",
                    "slope": 1.0,
                    "intercept": 0.0
                }
            }
            
            # Save to JSON
            Path("calibrations").mkdir(exist_ok=True)
            filepath = Path("calibrations") / f"{profile_name}.json"
            
            with open(filepath, 'w') as f:
                json.dump(calibration, f, indent=4)
            
            print(f"\n✓ Calibration saved to {filepath}")
            print(f"  min_horizontal: {calibration['min_horizontal']:.1f}")
            print(f"  max_horizontal: {calibration['max_horizontal']:.1f}")
            print(f"  min_vertical: {calibration['min_vertical']:.1f}")
            print(f"  max_vertical: {calibration['max_vertical']:.1f}")
            print(f"  neutral_vertical: {calibration['neutral_vertical']:.1f}")
            
            break
    
    cap.release()
    cv2.destroyAllWindows()
    detector.release()

if __name__ == "__main__":
    main()