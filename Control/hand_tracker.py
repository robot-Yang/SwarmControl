import cv2
import mediapipe as mp
import numpy as np
import asyncio
import websockets
import json
import threading

# --- WebSocket Server Setup ---
connected_clients = set()

async def handle_client(websocket, path):
    """Handle WebSocket connection from Unity"""
    connected_clients.add(websocket)
    print(f"Unity client connected. Total clients: {len(connected_clients)}")
    try:
        await websocket.wait_closed()
    finally:
        connected_clients.remove(websocket)
        print(f"Unity client disconnected. Total clients: {len(connected_clients)}")

async def start_websocket_server():
    """Start WebSocket server on port 9052"""
    server = await websockets.serve(handle_client, "localhost", 9052)
    print("WebSocket server started on ws://localhost:9052")
    await server.wait_closed()

def run_websocket_server():
    """Run WebSocket server in asyncio event loop"""
    asyncio.run(start_websocket_server())

# Start WebSocket server in background thread
websocket_thread = threading.Thread(target=run_websocket_server, daemon=True)
websocket_thread.start()

# --- MediaPipe Hands setup ---
mp_hands = mp.solutions.hands
mp_drawing = mp.solutions.drawing_utils

hands = mp_hands.Hands(
    static_image_mode=False,
    max_num_hands=2,
    min_detection_confidence=0.6,
    min_tracking_confidence=0.6
)

# --- Choix de la caméra ---
cap = cv2.VideoCapture(0)

if not cap.isOpened():
    print("Impossible d'ouvrir la caméra. Vérifie que ton iPhone/caméra est bien reconnue.")
    exit(1)

# --- Variables de calibration MANUELLES ---
d0 = 80.0         # distance minimale en pixels (mains collées) - AJUSTE CETTE VALEUR
d_max = 1450.0      # distance maximale en pixels (mains écartées) - AJUSTE CETTE VALEUR
calib_alpha = 0.0  # désactivé (mis à 0 pour calibration manuelle)

# --- Variables de calibration HEIGHT ---
neutral_height = 240.0  # Position Y neutre en pixels (milieu de l'écran 480/2) - AJUSTE CETTE VALEUR
height_range = 200.0    # Plage de mouvement vertical en pixels - AJUSTE CETTE VALEUR

# --- Smoothing sur la distance normalisée ---
norm_smooth = None
height_smooth = None
smooth_alpha = 0.2  # 0.2 = lissage modéré, augmente pour plus de douceur

def get_hand_center_and_size(image, hand_landmarks):
    """
    Renvoie:
      - centre de la main (cx, cy) en pixels
      - taille approximative (aire de la bounding box) en pixels^2
    """
    h, w, _ = image.shape
    xs = [lm.x * w for lm in hand_landmarks.landmark]
    ys = [lm.y * h for lm in hand_landmarks.landmark]

    cx = float(np.mean(xs))
    cy = float(np.mean(ys))

    min_x, max_x = min(xs), max(xs)
    min_y, max_y = min(ys), max(ys)
    width = max_x - min_x
    height = max_y - min_y
    area = width * height  # aire de la main dans l'image

    return cx, cy, area

def send_to_unity(distance_norm, height_norm):
    """Send normalized distance and height to Unity via WebSocket"""
    if len(connected_clients) == 0:
        return  # No clients connected, skip
    
    # Create JSON message
    message = json.dumps({
        "distance": round(distance_norm, 4),
        "height": round(height_norm, 4)
    })
    
    # Send to all connected clients
    disconnected = set()
    for client in connected_clients:
        try:
            asyncio.run(client.send(message))
        except:
            disconnected.add(client)
    
    # Remove disconnected clients
    connected_clients.difference_update(disconnected)

print("Calibration automatique avec filtrage :")
print("  - Place tes deux mains dans le champ.")
print("  - Colle-les quelques instants -> le système apprend le 0.")
print("  - Ecarte-les -> il apprend aussi le max.")
print("  - La valeur normalisée lissée (0..1) est indiquée en bas.")
print("Touche 'q' pour quitter.\n")

while True:
    ret, frame = cap.read()
    if not ret:
        print("Impossible de lire l'image de la caméra.")
        break

    frame_rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
    results = hands.process(frame_rgb)

    distance_raw = None
    distance_calibrated = None
    distance_normalized = None
    height_raw = None
    height_normalized = None
    valid_pair = False

    if results.multi_hand_landmarks and len(results.multi_hand_landmarks) >= 2:
        # On ne prend que les deux premières mains détectées
        hand1 = results.multi_hand_landmarks[0]
        hand2 = results.multi_hand_landmarks[1]

        # Dessiner les mains
        mp_drawing.draw_landmarks(frame, hand1, mp_hands.HAND_CONNECTIONS)
        mp_drawing.draw_landmarks(frame, hand2, mp_hands.HAND_CONNECTIONS)

        # Centres + tailles
        x1, y1, s1 = get_hand_center_and_size(frame, hand1)
        x2, y2, s2 = get_hand_center_and_size(frame, hand2)

        # Vérifier que les tailles sont comparables (mains à ~ même distance de la caméra)
        if s1 > 0 and s2 > 0:
            size_ratio = s1 / s2 if s1 > s2 else s2 / s1
        else:
            size_ratio = 999.0

        # Seulement si les mains ont une taille comparable (évite mains à des profondeurs très différentes)
        if 0.5 <= size_ratio <= 2.0:
            valid_pair = True

            # Distance euclidienne en pixels
            distance_raw = np.sqrt((x1 - x2) ** 2 + (y1 - y2) ** 2)
            
            # Calculate average height (Y position) of both hands
            height_raw = (y1 + y2) / 2.0

            # Afficher les centres
            cv2.circle(frame, (int(x1), int(y1)), 6, (0, 255, 0), -1)
            cv2.circle(frame, (int(x2), int(y2)), 6, (0, 255, 0), -1)

            # --- Calibration MANUELLE (aucun ajustement automatique) ---
            # Les valeurs d0 et d_max sont fixes en haut du fichier

            # Calcul des distances calibrée et normalisée
            if d0 is not None and d_max is not None and d_max > d0 + 1e-6:
                distance_calibrated = max(0.0, distance_raw - d0)
                distance_normalized = (distance_raw - d0) / (d_max - d0)
                distance_normalized = max(0.0, min(1.0, distance_normalized))

                # Lissage de la distance normalisée
                if norm_smooth is None:
                    norm_smooth = distance_normalized
                else:
                    norm_smooth = smooth_alpha * distance_normalized + (1 - smooth_alpha) * norm_smooth
                
                # Normalize height relative to neutral position
                # Negative = below neutral (down), Positive = above neutral (up)
                height_normalized = (height_raw - neutral_height) / height_range
                height_normalized = max(-1.0, min(1.0, height_normalized))  # Clamp to -1..+1
                
                # Smooth height value
                if height_smooth is None:
                    height_smooth = height_normalized
                else:
                    height_smooth = smooth_alpha * height_normalized + (1 - smooth_alpha) * height_smooth
                
                # Send data to Unity
                send_to_unity(norm_smooth, height_smooth)

    # --- Affichage du texte ---
    h, w, _ = frame.shape

    if not valid_pair:
        cv2.putText(frame, "Montre bien les DEUX mains a la camera, a peu pres a la meme distance.",
                    (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 0, 255), 2)
    else:
        if distance_raw is not None:
            cv2.putText(frame, f"Raw: {distance_raw:.1f}px",
                        (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)

        if d0 is not None:
            cv2.putText(frame, f"d0 (min): {d0:.1f}px",
                        (10, 60), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 255), 2)

        if d_max is not None:
            cv2.putText(frame, f"d_max: {d_max:.1f}px",
                        (10, 85), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 255), 2)

        if distance_calibrated is not None:
            cv2.putText(frame, f"Calib: {distance_calibrated:.1f}",
                        (10, 115), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)

        if norm_smooth is not None:
            cv2.putText(frame, f"Norm lisse: {norm_smooth:.2f} (0..1)",
                        (10, 145), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 0, 0), 2)
        
        if height_raw is not None:
            cv2.putText(frame, f"Height Raw: {height_raw:.1f}px",
                        (10, 175), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 0), 2)
        
        if height_smooth is not None:
            cv2.putText(frame, f"Height Norm: {height_smooth:.2f} (-1..+1)",
                        (10, 205), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 255), 2)

    cv2.putText(frame, "Appuie sur 'q' pour quitter.",
                (10, h - 20), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (200, 200, 200), 2)

    cv2.imshow("Distance entre les mains - auto calib + filtre", frame)

    key = cv2.waitKey(1) & 0xFF
    if key == ord('q'):
        break

cap.release()
cv2.destroyAllWindows()