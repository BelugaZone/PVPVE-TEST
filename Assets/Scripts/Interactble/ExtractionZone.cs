using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;
using FishNet.Connection;

public class ExtractionZone : NetworkBehaviour
{
    public float requiredWaitTime = 10f;

    // 只响应 Player Layer（8），屏蔽骨骼节点、道具、建筑等
    private readonly LayerMask _playerLayer = 1 << 8;

    private Dictionary<Player, float> extractingPlayers = new Dictionary<Player, float>();
    // 记录已撤离成功的玩家，避免 OnTriggerExit 重复取消
    private HashSet<Player> extractedPlayers = new HashSet<Player>();

    private void OnTriggerEnter(Collider other)
    {
        // 只处理 Player Layer，屏蔽骨骼节点/道具/建筑
        if ((_playerLayer.value & (1 << other.gameObject.layer)) == 0) return;

        Debug.Log($"[ExtractionZone] OnTriggerEnter: {other.name} | IsServer={IsServer} | MatchManager={(MatchManager.Instance != null ? "OK" : "NULL")}");
        if (!IsServer) return;

        Player player = other.GetComponentInParent<Player>();
        if (player != null)
        {
            // 忽略骨骼子节点的碰撞，只响应玩家根节点的碰撞体
            if (other.gameObject != player.gameObject) return;

            if (HasCoreItem(player))
            {
                if (!extractingPlayers.ContainsKey(player))
                {
                    extractingPlayers[player] = requiredWaitTime;
                    RpcStartExtractionUI(player.Owner, requiredWaitTime);
                    Debug.Log($"[ExtractionZone] Player {player.playerID} started extraction. Timer={requiredWaitTime}");
                }
            }
            else
            {
                RpcNoCoreUI(player.Owner);
                Debug.Log($"[ExtractionZone] Player {player.playerID} has no core.");
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        // 只处理 Player Layer
        if ((_playerLayer.value & (1 << other.gameObject.layer)) == 0) return;
        if (!IsServer) return;

        Player player = other.GetComponentInParent<Player>();

        // 忽略骨骼子节点的碰撞
        if (player != null && other.gameObject != player.gameObject) return;

        // 已成功撤离的玩家不触发取消
        if (player != null && !extractedPlayers.Contains(player))
        {
            extractingPlayers.Remove(player);
            RpcCancelExtractionUI(player.Owner);
            Debug.Log($"[ExtractionZone] Player {player.playerID} left extraction zone. Timer cancelled.");
        }
    }

    private void Update()
    {
        if (!IsServer) return;

        List<Player> playersInZone = new List<Player>(extractingPlayers.Keys);

        foreach (Player player in playersInZone)
        {
            if (player == null || player.health.currentHealth.Value <= 0 || !HasCoreItem(player))
            {
                extractingPlayers.Remove(player);
                if (player != null) RpcCancelExtractionUI(player.Owner);
                continue;
            }

            extractingPlayers[player] -= Time.deltaTime;

            if (extractingPlayers[player] <= 0)
            {
                extractingPlayers.Remove(player);
                extractedPlayers.Add(player);  // 标记为已撤离，防止 OnTriggerExit 取消
                MatchManager.Instance.PlayerExtracted(player);
                RpcExtractionSuccess(player.Owner);
            }
        }
    }

    private bool HasCoreItem(Player player)
    {
        string coreId = MatchManager.Instance != null ? MatchManager.Instance.coreItemID : "core";

        foreach (var item in player.GetComponent<PlayerInventory>().netBackpack)
            if (item.itemId == coreId) return true;

        foreach (var item in player.GetComponent<PlayerInventory>().netEquipment)
            if (item.itemId == coreId) return true;

        return false;
    }

    [TargetRpc]
    private void RpcNoCoreUI(NetworkConnection conn)
    {
        if (UI_ExtractionHUD.Local != null)
            UI_ExtractionHUD.Local.ShowNoCore();
    }

    [TargetRpc]
    private void RpcStartExtractionUI(NetworkConnection conn, float time)
    {
        if (UI_ExtractionHUD.Local != null)
            UI_ExtractionHUD.Local.ShowCountdown(time);
    }

    [TargetRpc]
    private void RpcCancelExtractionUI(NetworkConnection conn)
    {
        if (UI_ExtractionHUD.Local != null)
            UI_ExtractionHUD.Local.Cancel();
    }

    [TargetRpc]
    private void RpcExtractionSuccess(NetworkConnection conn)
    {
        if (UI_ExtractionHUD.Local != null)
            UI_ExtractionHUD.Local.Success();
    }
}
