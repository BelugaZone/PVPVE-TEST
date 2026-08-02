using UnityEngine;
using UnityEngine.UI;

public class UI_MatchTimer : MonoBehaviour
{
    private Text _timerText;
    
    // Auto-inject into the game
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        var go = new GameObject("UI_MatchTimer");
        DontDestroyOnLoad(go);
        go.AddComponent<UI_MatchTimer>();
    }

    private void Awake()
    {
        BuildUI();
    }

    private void BuildUI()
    {
        // 1. Create Canvas
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 99; // Ensure it's on top

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;

        // 2. Create Text
        var textObj = new GameObject("TimerText");
        textObj.transform.SetParent(transform, false);
        
        _timerText = textObj.AddComponent<Text>();
        _timerText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _timerText.fontSize = 42;
        _timerText.fontStyle = FontStyle.Bold;
        _timerText.alignment = TextAnchor.UpperRight;
        _timerText.raycastTarget = false;
        
        // Add outline for visibility
        var outline = textObj.AddComponent<Outline>();
        outline.effectColor = new Color(0, 0, 0, 0.8f);
        outline.effectDistance = new Vector2(2, -2);

        // Position Top-Right
        var rect = _timerText.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(1, 1);
        rect.anchoredPosition = new Vector2(-40, -40); // 40px padding from top right edge
        rect.sizeDelta = new Vector2(400, 60);
    }

    private void Update()
    {
        if (MatchManager.Instance == null)
        {
            _timerText.text = "";
            return;
        }

        if (MatchManager.Instance.currentState.Value == MatchState.InProgress)
        {
            float timeRemaining = MatchManager.Instance.timeRemaining.Value;
            float duration = MatchManager.Instance.matchDuration;

            // Format time
            int seconds = Mathf.CeilToInt(timeRemaining);
            _timerText.text = $"剩余时间: {seconds} 秒";

            // Color gradient (Green -> Red)
            float t = Mathf.Clamp01(timeRemaining / duration);
            // Green when t=1, Red when t=0
            Color color = Color.Lerp(Color.red, Color.green, t);
            _timerText.color = color;
        }
        else if (MatchManager.Instance.currentState.Value == MatchState.Ended)
        {
            _timerText.text = "对局结束";
            _timerText.color = Color.gray;
        }
    }
}
