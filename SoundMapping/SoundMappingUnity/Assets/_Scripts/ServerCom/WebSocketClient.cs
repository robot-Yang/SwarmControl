using UnityEngine;
using UnityEngine.UI;
using WebSocketSharp;

public class WebSocketClient : MonoBehaviour
{
    private WebSocket ws;

    // Public properties for other scripts to access
    public float HandDistance { get; private set; } = 0f;
    public float HandHeight { get; private set; } = 0f;
    public bool IsConnected => ws != null && ws.IsAlive;

    void Start()
    {
        // Use 127.0.0.1 instead of localhost for better Windows compatibility
        ws = new WebSocket("ws://127.0.0.1:9052");

        ws.OnOpen += (sender, e) =>
        {
            UpdateStatus("Connected to MediaPipe Python server.");
        };

        ws.OnMessage += (sender, e) =>
        {
            // Parse JSON data from Python
            ParseHandData(e.Data);
        };

        ws.OnError += (sender, e) =>
        {
            UpdateStatus("Error: " + e.Message);
        };

        ws.OnClose += (sender, e) =>
        {
            UpdateStatus("Connection closed oh noooo.");
        };

        // Connect asynchronously to avoid blocking Unity
        ws.ConnectAsync();
    }

    void ParseHandData(string jsonData)
    {
        try
        {
            MediaPipeHandData data = JsonUtility.FromJson<MediaPipeHandData>(jsonData);
            HandDistance = data.distance;
            HandHeight = data.height;
            
            // Debug logging enabled
            // Debug.Log($"Received - Distance: {HandDistance:F2}, Height: {HandHeight:F2}");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Failed to parse MediaPipe data: {ex.Message}");
            Debug.LogWarning($"Raw JSON: {jsonData}");
        }
    }

    void UpdateStatus(string message)
    {
        Debug.Log(message);
    }

    public void SendMessageToServer(string message)
    {
        if (ws != null && ws.IsAlive)
        {
            ws.Send(message);
            Debug.Log("Sent: " + message);
        }
        else
        {
            UpdateStatus("Cannot send message. Not connected to server.");
        }
    }

    void OnApplicationQuit()
    {
        if (ws != null)
        {
            ws.Close();
        }
    }

    void OnDestroy()
    {
        if (ws != null)
        {
            ws.Close();
        }
    }
}
