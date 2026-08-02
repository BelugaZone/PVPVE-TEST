using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;

[System.Serializable]
public struct PlayerScoreData
{
    public string playerID;
    public int score;
    public bool extracted;
    public bool survived;
    public int itemsValue;
}

public class ScoreManager : NetworkBehaviour
{
    public static ScoreManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    [Server]
    public List<PlayerScoreData> CalculateScores(Player extractor)
    {
        var resultMap = new Dictionary<string, PlayerScoreData>();

        Player[] allPlayers = FindObjectsOfType<Player>();

        foreach (Player p in allPlayers)
        {
            if (string.IsNullOrEmpty(p.playerID)) continue;

            PlayerScoreData data = new PlayerScoreData();
            data.playerID = p.playerID;

            // Check extraction
            if (extractor != null && p == extractor)
            {
                data.extracted = true;
                data.score += 1000;
            }

            // Check survival
            if (p.health != null && p.health.currentHealth.Value > 0 && !p.health.isDead.Value)
            {
                data.survived = true;
                data.score += 100;
            }

            // Check inventory value
            if (p.inventory != null)
            {
                int itemScore = 0;
                foreach (var item in p.inventory.netBackpack)
                    if (!string.IsNullOrEmpty(item.itemId)) itemScore += 10 * item.count;
                foreach (var item in p.inventory.netEquipment)
                    if (!string.IsNullOrEmpty(item.itemId)) itemScore += 10 * item.count;

                data.itemsValue = itemScore;
                data.score += itemScore;
            }

            // Deduplicate logic
            if (resultMap.TryGetValue(p.playerID, out PlayerScoreData existingData))
            {
                // If the new one is alive and old one is dead, or if it simply has a higher score
                if (!existingData.survived && data.survived)
                {
                    resultMap[p.playerID] = data;
                }
                else if (existingData.survived == data.survived && data.score > existingData.score)
                {
                    resultMap[p.playerID] = data;
                }
            }
            else
            {
                resultMap.Add(p.playerID, data);
            }
        }

        var result = new List<PlayerScoreData>(resultMap.Values);

        foreach (var data in result)
        {
            Debug.Log($"[ScoreManager] {data.playerID}: {data.score}pts (Extracted={data.extracted}, Survived={data.survived}, Items={data.itemsValue})");
        }

        return result;
    }
}
