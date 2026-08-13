using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;

// Single-room state machine. Scene-placed in the Lobby. Phase 2: create/join/leave/transfer.
// Phase 3 will implement the actual match-start scene transition (CmdStartMatch is a stub here).
public class RoomManager : NetworkBehaviour
{
    public enum RoomState { Empty, Gathering, InMatch }

    public static RoomManager Instance;

    public readonly SyncVar<RoomState> State = new SyncVar<RoomState>();
    public readonly SyncVar<int> OwnerClientId = new SyncVar<int>(-1);
    public readonly SyncList<int> MemberClientIds = new SyncList<int>();
    public readonly SyncVar<int> MaxMembers = new SyncVar<int>(4);

    // Static state save/restore across scene transitions (RoomManager is destroyed when
    // Lobby unloads and re-created when it reloads; these survive the transition).
    private static RoomState _savedState;
    private static int _savedOwnerClientId;
    private static int[] _savedMembers;
    private static bool _hasSavedState = false;

    /// <summary>Called by ServerDataManager.EndMatchAndReturnToLobby: reset the saved room
    /// state to Empty so players can create a fresh room. The previous room is dissolved
    /// on match end — players re-create/join in the lobby.</summary>
    public static void SetSavedStateForLobbyReturn()
    {
        _savedState = RoomState.Empty;
        _savedOwnerClientId = -1;
        _savedMembers = new int[0];
        _hasSavedState = true;
    }

    // SyncVar change callbacks so clients refresh the UI when state/members change.
    public event System.Action OnRoomChanged;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        if (_hasSavedState)
        {
            State.Value = _savedState;
            OwnerClientId.Value = _savedOwnerClientId;
            MemberClientIds.Clear();
            foreach (var m in _savedMembers) MemberClientIds.Add(m);
            _hasSavedState = false; // consumed
            Debug.Log("[RoomManager] Restored room state from static.");
        }
        else
        {
            State.Value = RoomState.Empty;
        }
        base.NetworkManager.ServerManager.OnRemoteConnectionState += HandleRemoteConnectionState;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        if (base.NetworkManager != null)
            base.NetworkManager.ServerManager.OnRemoteConnectionState -= HandleRemoteConnectionState;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        // SyncVar<T>.OnChange is `void OnChanged(T prev, T next, bool asServer)` (3 params).
        // SyncList<T>.OnChange is `void SyncListChanged(SyncListOperation op, int index, T oldItem, T newItem, bool asServer)` (5 params).
        // A single-discard `_ =>` lambda only matches a 1-parameter delegate, so use the full
        // parameter list with discards here.
        State.OnChange += (prev, next, asServer) => OnRoomChanged?.Invoke();
        OwnerClientId.OnChange += (prev, next, asServer) => OnRoomChanged?.Invoke();
        MemberClientIds.OnChange += (op, index, oldItem, newItem, asServer) => OnRoomChanged?.Invoke();
        OnRoomChanged?.Invoke();
    }

    // --- Client requests ---

    // NOTE: RoomManager is a scene object (server-owned), so these use RequireOwnership=false
    // — any client can call them; the server validates via the caller's connection.

    public void CreateRoom() { var c = FishNet.InstanceFinder.ClientManager.Connection; if (c != null) CmdCreateRoom(c); }
    public void JoinRoom() { var c = FishNet.InstanceFinder.ClientManager.Connection; if (c != null) CmdJoinRoom(c); }
    public void LeaveRoom() { var c = FishNet.InstanceFinder.ClientManager.Connection; if (c != null) CmdLeaveRoom(c); }
    public void StartMatch() { var c = FishNet.InstanceFinder.ClientManager.Connection; if (c != null) CmdStartMatch(c); }

    // --- Server RPCs ---

    [ServerRpc(RequireOwnership = false)]
    private void CmdCreateRoom(NetworkConnection senderConn)
    {
        if (State.Value != RoomState.Empty)
        {
            Debug.Log("[RoomManager] Cannot create room: room already exists.");
            return;
        }
        int clientId = (int)senderConn.ClientId;
        State.Value = RoomState.Gathering;
        OwnerClientId.Value = clientId;
        MemberClientIds.Clear();
        MemberClientIds.Add(clientId);
        Debug.Log($"[RoomManager] Room created by client {clientId}.");
    }

    [ServerRpc(RequireOwnership = false)]
    private void CmdJoinRoom(NetworkConnection senderConn)
    {
        if (State.Value != RoomState.Gathering)
        {
            Debug.Log("[RoomManager] Cannot join: room not open.");
            return;
        }
        if (MemberClientIds.Count >= MaxMembers.Value)
        {
            Debug.Log("[RoomManager] Cannot join: room full.");
            return;
        }
        int clientId = (int)senderConn.ClientId;
        if (MemberClientIds.Contains(clientId))
        {
            Debug.Log($"[RoomManager] Client {clientId} already in room.");
            return;
        }
        MemberClientIds.Add(clientId);
        Debug.Log($"[RoomManager] Client {clientId} joined. Members: {MemberClientIds.Count}.");
    }

    [ServerRpc(RequireOwnership = false)]
    private void CmdLeaveRoom(NetworkConnection senderConn)
    {
        int clientId = (int)senderConn.ClientId;
        RemoveMember(clientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void CmdStartMatch(NetworkConnection senderConn)
    {
        if (State.Value != RoomState.Gathering)
        {
            Debug.Log("[RoomManager] Cannot start: room not gathering.");
            return;
        }
        int clientId = (int)senderConn.ClientId;
        if (clientId != OwnerClientId.Value)
        {
            Debug.Log($"[RoomManager] Client {clientId} cannot start: not owner ({OwnerClientId.Value}).");
            return;
        }
        if (MemberClientIds.Count == 0)
        {
            Debug.Log("[RoomManager] Cannot start: no members.");
            return;
        }

        // Save room state to static (RoomManager will be destroyed when Lobby unloads).
        _savedState = RoomState.InMatch;
        _savedOwnerClientId = OwnerClientId.Value;
        _savedMembers = new int[MemberClientIds.Count];
        for (int i = 0; i < MemberClientIds.Count; i++) _savedMembers[i] = MemberClientIds[i];
        _hasSavedState = true;

        Debug.Log($"[RoomManager] StartMatch: owner={clientId}, members={MemberClientIds.Count}. Loading Game scene.");

        // Delegate to ServerDataManager: move loadout→in-raid, load Game scene, spawn players.
        if (ServerDataManager.Instance != null)
        {
            var memberList = new System.Collections.Generic.List<int>();
            foreach (var m in MemberClientIds) memberList.Add(m);
            ServerDataManager.Instance.StartMatchForMembers(memberList);
        }
    }

    // --- Disconnect handling: remove member, transfer/empty room ---

    private void HandleRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        if (args.ConnectionState != RemoteConnectionState.Stopped) return;
        int clientId = (int)conn.ClientId;
        if (MemberClientIds.Contains(clientId))
            RemoveMember(clientId);
    }

    private void RemoveMember(int clientId)
    {
        if (!MemberClientIds.Contains(clientId)) return;
        MemberClientIds.Remove(clientId);

        // Owner left: transfer to the earliest remaining member, or empty the room.
        if (clientId == OwnerClientId.Value)
        {
            if (MemberClientIds.Count > 0)
            {
                OwnerClientId.Value = MemberClientIds[0];
                Debug.Log($"[RoomManager] Owner left; transferred ownership to client {MemberClientIds[0]}.");
            }
            else
            {
                OwnerClientId.Value = -1;
                State.Value = RoomState.Empty;
                Debug.Log("[RoomManager] Owner left; room empty.");
            }
        }
    }

    // --- Client-side helpers for the UI ---
    // NOTE: avoid base.IsClientStarted here — it can NRE during OnStartClient before the
    // NetworkBehaviour is fully initialized. Use InstanceFinder instead.

    public bool IsLocalOwner()
    {
        var cm = FishNet.InstanceFinder.ClientManager;
        if (cm == null || cm.Connection == null) return false;
        return (int)cm.Connection.ClientId == OwnerClientId.Value;
    }

    public bool IsLocalMember()
    {
        var cm = FishNet.InstanceFinder.ClientManager;
        if (cm == null || cm.Connection == null) return false;
        return MemberClientIds.Contains((int)cm.Connection.ClientId);
    }
}
