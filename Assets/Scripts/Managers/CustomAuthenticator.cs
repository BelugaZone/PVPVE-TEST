using FishNet.Authenticating;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using UnityEngine;

public class CustomAuthenticator : Authenticator
{
    public override event Action<NetworkConnection, bool> OnAuthenticationResult;

    // A broadcast struct to send the requested login ID
    public struct AuthBroadcast : IBroadcast
    {
        public string loginID;
    }

    [Tooltip("If true, clients logging in with an ID that is already online will be rejected.")]
    public bool preventMultiLogin = true;

    // Keep track of authenticated connections and their IDs
    private Dictionary<string, NetworkConnection> _activeConnections = new Dictionary<string, NetworkConnection>();
    private Dictionary<NetworkConnection, string> _connectionIDs = new Dictionary<NetworkConnection, string>();

    public override void InitializeOnce(NetworkManager networkManager)
    {
        base.InitializeOnce(networkManager);
        
        // Listen for the auth broadcast on the server
        networkManager.ServerManager.RegisterBroadcast<AuthBroadcast>(OnAuthBroadcast, false);
        
        // Listen for disconnects to clean up our active connections dictionary
        networkManager.ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;

        // Register client result broadcast
        networkManager.ClientManager.RegisterBroadcast<AuthResultBroadcast>(OnAuthResultBroadcast);
        networkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
    }

    /// <summary>
    /// Call this on the client right before calling NetworkManager.ClientManager.StartConnection()
    /// </summary>
    public static string PendingLoginID = "";

    private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Started)
        {
            // Client side: Send the AuthBroadcast
            AuthBroadcast msg = new AuthBroadcast()
            {
                loginID = PendingLoginID
            };
            base.NetworkManager.ClientManager.Broadcast(msg);
        }
    }

    /// <summary>
    /// Server side: receives the AuthBroadcast from a client attempting to authenticate.
    /// </summary>
    private void OnAuthBroadcast(NetworkConnection conn, AuthBroadcast msg, Channel channel)
    {
        // If already authenticated, ignore
        if (conn.Authenticated)
            return;

        string id = msg.loginID;
        
        if (string.IsNullOrEmpty(id))
        {
            Debug.LogWarning($"[Authenticator] Connection {conn.ClientId} attempted to login with empty ID. Rejecting.");
            base.NetworkManager.ServerManager.Broadcast(conn, new AuthResultBroadcast() { Success = false, ErrorMessage = "Login ID cannot be empty." });
            return;
        }

        if (preventMultiLogin && _activeConnections.ContainsKey(id))
        {
            Debug.LogWarning($"[Authenticator] Connection {conn.ClientId} attempted to login with ID '{id}', but it is already active! Rejecting.");
            base.NetworkManager.ServerManager.Broadcast(conn, new AuthResultBroadcast() { Success = false, ErrorMessage = "Account is already logged in." });
            // Optionally, you could kick the old connection instead of rejecting the new one.
            return;
        }

        // Authentication successful
        Debug.Log($"[Authenticator] Connection {conn.ClientId} authenticated successfully as '{id}'.");
        
        _activeConnections[id] = conn;
        _connectionIDs[conn] = id;
        
        // Store the ID in the custom data of the connection (optional, but useful)
        // FishNet doesn't have a built-in CustomData on NetworkConnection out of the box in newer versions, 
        // so we rely on our dictionaries. Or we can create an extension.

        // Tell FishNet this connection is now officially authenticated
        OnAuthenticationResult?.Invoke(conn, true);
        base.NetworkManager.ServerManager.Broadcast(conn, new AuthResultBroadcast() { Success = true });
    }

    /// <summary>
    /// Clean up disconnected clients.
    /// </summary>
    private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        if (args.ConnectionState == RemoteConnectionState.Stopped)
        {
            if (_connectionIDs.TryGetValue(conn, out string id))
            {
                _activeConnections.Remove(id);
                _connectionIDs.Remove(conn);
                Debug.Log($"[Authenticator] Connection {conn.ClientId} ({id}) disconnected. Cleared from active sessions.");
            }
        }
    }

    public string GetIDForConnection(NetworkConnection conn)
    {
        if (_connectionIDs.TryGetValue(conn, out string id))
            return id;
        return string.Empty;
    }

    // Client needs to know if auth failed
    public struct AuthResultBroadcast : IBroadcast
    {
        public bool Success;
        public string ErrorMessage;
    }
    
    // Client side: receive result
    private void OnAuthResultBroadcast(AuthResultBroadcast msg, Channel channel)
    {
        if (!msg.Success)
        {
            Debug.LogError($"[Authenticator] Login failed: {msg.ErrorMessage}");
            // Disconnect if auth fails
            base.NetworkManager.ClientManager.StopConnection();
            OnClientAuthFailed?.Invoke(msg.ErrorMessage);
        }
    }

    public static event Action<string> OnClientAuthFailed;
}
