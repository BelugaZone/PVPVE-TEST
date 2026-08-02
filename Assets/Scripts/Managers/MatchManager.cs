using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Managing.Scened;
using UnityEngine.SceneManagement;

public enum MatchState
{
    Waiting,
    InProgress,
    Ended
}

public class MatchManager : NetworkBehaviour
{
    public static MatchManager Instance { get; private set; }

    public readonly SyncVar<MatchState> currentState = new SyncVar<MatchState>();

    public readonly SyncVar<float> timeRemaining = new SyncVar<float>();

    [Header("Settings")]
    public float matchDuration = 180f;
    public float restartDelay = 5f; // Wait 5 seconds after match ends before restarting (reduced for testing)

    public string coreItemID = "core"; // Configurable ID for the core item

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        StartMatch();
    }

    private void StartMatch()
    {
        currentState.Value = MatchState.InProgress;
        timeRemaining.Value = matchDuration;
    }

    private void Update()
    {
        if (!IsServer) return;

        if (currentState.Value == MatchState.InProgress)
        {
            timeRemaining.Value -= Time.deltaTime;
            if (timeRemaining.Value <= 0)
            {
                timeRemaining.Value = 0;
                EndMatch(null); // Time ran out
            }
        }
    }

    // Called when a player successfully extracts with the core
    [Server]
    public void PlayerExtracted(Player player)
    {
        if (currentState.Value != MatchState.InProgress) return;
        
        Debug.Log($"Player {player.playerID} extracted with the core!");
        EndMatch(player);
    }

    [Server]
    private void EndMatch(Player extractor)
    {
        if (currentState.Value == MatchState.Ended) return; // 防止重复触发
        currentState.Value = MatchState.Ended;

        // 通知 ScoreManager 计算分数，并获取最终分数
        List<PlayerScoreData> scoresList = new List<PlayerScoreData>();
        if (ScoreManager.Instance != null)
            scoresList = ScoreManager.Instance.CalculateScores(extractor);

        string extractorID = extractor != null ? extractor.playerID : "";

        // 使用 Newtonsoft.Json 转换
        string jsonScores = Newtonsoft.Json.JsonConvert.SerializeObject(scoresList);
        Debug.Log($"[MatchManager] Server serialized scores: {jsonScores}");

        // 强制 5 秒倒计时
        float actualDelay = 5f;

        // 通知所有客户端并附带分数数据
        RpcShowMatchEndUI(extractorID, actualDelay, jsonScores);

        // 服务器等待后重启场景
        StartCoroutine(RestartMatchRoutine(actualDelay));
    }


    [ObserversRpc]
    private void RpcShowMatchEndUI(string extractorID, float countdown, string finalScoresJson)
    {
        Debug.Log($"[MatchManager] RpcShowMatchEndUI called! Extractor={extractorID}");

        // 找到本地玩家并禁用操作
        var localPlayers = FindObjectsOfType<Player>();
        Player localPlayer = null;
        foreach (var p in localPlayers)
        {
            if (p.IsOwner) { localPlayer = p; break; }
        }

        if (localPlayer != null)
            localPlayer.controls.Disable();

        // 强制解锁鼠标并显示，否则返回大厅后会卡死
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 解析分数 JSON
        var scores = new List<PlayerScoreData>();
        if (!string.IsNullOrEmpty(finalScoresJson))
        {
            Debug.Log($"[MatchManager] Client received JSON: {finalScoresJson}");
            try
            {
                scores = Newtonsoft.Json.JsonConvert.DeserializeObject<List<PlayerScoreData>>(finalScoresJson);
                if (scores == null) scores = new List<PlayerScoreData>();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[MatchManager] Failed to parse scores JSON via Newtonsoft: " + e.Message);
            }
        }

        bool localExtracted = localPlayer != null && localPlayer.playerID == extractorID;

        if (UI_MatchEndScreen.Instance != null)
            UI_MatchEndScreen.Instance.Show(scores, countdown, localExtracted);
        else
            Debug.LogWarning("[MatchManager] UI_MatchEndScreen.Instance is null!");

        Debug.Log($"[MatchManager] Match ended. Extractor={extractorID}. Showing scoreboard.");
    }

    private IEnumerator RestartMatchRoutine(float delay)
    {
        Debug.Log($"[MatchManager] Restarting match in {delay} seconds...");
        yield return new WaitForSeconds(delay);

        Debug.Log("[MatchManager] Reloading scene now...");

        // 完全抛弃 FishNet 的 SceneManager 场景同步
        // 直接暴力且安全地关闭当前连接，并使用 Unity 原生 API 重新加载整个物理场景
        var nm = FishNet.InstanceFinder.NetworkManager;
        if (nm != null)
        {
            if (nm.IsServerStarted) nm.ServerManager.StopConnection(true);
            if (nm.IsClientStarted) nm.ClientManager.StopConnection();
            
            // 彻底销毁 NetworkManager 实例，保证场景重载时生成全新的实例（避免单例冲突导致卡死）
            Destroy(nm.gameObject);
        }
        
        // 等待一小会儿确保网络连接完全断开释放端口
        yield return new WaitForSeconds(0.5f);
        
        Debug.Log("[MatchManager] Fully resetting scene via Unity SceneManager...");
        UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.buildIndex);
    }
}
