using UnityEngine;
using FishNet;
using FishNet.Connection;
using FishNet.Transporting;

public class ServerLogger : MonoBehaviour
{
    public static ServerLogger Instance;

    public bool logConnections = true;
    public bool logSpawns = true;
    public bool logCombat = true;
    public bool logSystem = true;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        if (InstanceFinder.ServerManager != null)
        {
            InstanceFinder.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            InstanceFinder.ServerManager.OnServerConnectionState += OnServerConnectionState;
        }
    }

    private void OnDestroy()
    {
        if (InstanceFinder.ServerManager != null)
        {
            InstanceFinder.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            InstanceFinder.ServerManager.OnServerConnectionState -= OnServerConnectionState;
        }
    }

    private void OnServerConnectionState(ServerConnectionStateArgs args)
    {
        if (!logSystem) return;
        
        if (args.ConnectionState == LocalConnectionState.Started)
            Debug.Log("<color=yellow>[Server-System]</color> Server Started.");
        else if (args.ConnectionState == LocalConnectionState.Stopped)
            Debug.Log("<color=yellow>[Server-System]</color> Server Stopped.");
    }

    private void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        if (!logConnections) return;
        
        if (args.ConnectionState == RemoteConnectionState.Started)
            Debug.Log($"<color=cyan>[Server-Connection]</color> Client {conn.ClientId} connected.");
        else if (args.ConnectionState == RemoteConnectionState.Stopped)
            Debug.Log($"<color=cyan>[Server-Connection]</color> Client {conn.ClientId} disconnected.");
    }

    public static void LogSpawn(string message)
    {
        if (Instance != null && Instance.logSpawns && InstanceFinder.IsServer)
            Debug.Log($"<color=green>[Server-Spawn]</color> {message}");
    }

    public static void LogCombat(string message)
    {
        if (Instance != null && Instance.logCombat && InstanceFinder.IsServer)
            Debug.Log($"<color=orange>[Server-Combat]</color> {message}");
    }

    public static void LogSystem(string message)
    {
        if (Instance != null && Instance.logSystem && InstanceFinder.IsServer)
            Debug.Log($"<color=yellow>[Server-System]</color> {message}");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoInitialize()
    {
        GameObject go = new GameObject("ServerLogger");
        go.AddComponent<ServerLogger>();
    }
}
