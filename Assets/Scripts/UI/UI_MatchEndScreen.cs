using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 对局结算界面：运行时纯代码构建。
/// 由 MatchManager.RpcShowMatchEndUI 调用 Show()。
/// </summary>
public class UI_MatchEndScreen : MonoBehaviour
{
    public static UI_MatchEndScreen Instance { get; private set; }

    private Canvas _canvas;
    private GameObject _root;   // 根容器，控制整体显示/隐藏（含 Dim + Panel）
    private GameObject _panel;
    private Text _titleText;
    private Transform _scoreListRoot;
    private Text _countdownText;
    private Coroutine _countdownCoroutine;

    private static readonly Color BgDark    = new Color(0.05f, 0.05f, 0.1f, 0.92f);
    private static readonly Color Gold      = new Color(1f, 0.85f, 0.2f, 1f);
    private static readonly Color White     = Color.white;
    private static readonly Color GreenHigh = new Color(0.2f, 1f, 0.5f, 1f);

    private void Awake()
    {
        Instance = this;
        BuildUI();
        _root.SetActive(false);   // 整个根节点隐藏，Dim + Panel 都不可见
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_canvas != null) Destroy(_canvas.gameObject);
    }

    // ─────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────

    public void Show(List<PlayerScoreData> scores, float restartCountdown, bool localExtracted)
    {
        PopulateScores(scores, localExtracted);
        _root.SetActive(true);

        if (_countdownCoroutine != null) StopCoroutine(_countdownCoroutine);
        _countdownCoroutine = StartCoroutine(CountdownRoutine(restartCountdown));
    }

    // ─────────────────────────────────────────────────
    //  Internal
    // ─────────────────────────────────────────────────

    private void PopulateScores(List<PlayerScoreData> scores, bool localExtracted)
    {
        _titleText.text = localExtracted ? "✔ 撤离成功！对局结束" : "⏱ 时间结束！对局结束";
        _titleText.color = localExtracted ? GreenHigh : Gold;

        // 清空旧条目
        foreach (Transform child in _scoreListRoot)
            Destroy(child.gameObject);

        // 按分数降序排列
        scores.Sort((a, b) => b.score.CompareTo(a.score));

        for (int i = 0; i < scores.Count; i++)
        {
            var data = scores[i];
            CreateScoreRow(i, data);
        }
    }

    private void CreateScoreRow(int rank, PlayerScoreData data)
    {
        var row = new GameObject($"Row_{rank}");
        row.transform.SetParent(_scoreListRoot, false);

        var img = row.AddComponent<Image>();
        img.color = rank == 1
            ? new Color(1f, 0.85f, 0.1f, 0.15f)
            : new Color(1f, 1f, 1f, 0.05f);

        var rect = row.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(0f, 52f);
        
        var layoutElem = row.AddComponent<LayoutElement>();
        layoutElem.preferredHeight = 52f;
        layoutElem.minHeight = 52f;

        // 排名
        AddText(row, $"#{rank}", 26, Gold, TextAnchor.MiddleLeft,
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(14f, 0f), new Vector2(60f, 0f));

        // 玩家 ID
        AddText(row, data.playerID, 22, White, TextAnchor.MiddleLeft,
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(74f, 0f), new Vector2(200f, 0f));

        // 标签（提取/存活）
        string tags = "";
        if (data.extracted) tags += " [撤离✔]";
        if (data.survived)  tags += " [存活]";
        AddText(row, tags, 18, GreenHigh, TextAnchor.MiddleLeft,
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(280f, 0f), new Vector2(180f, 0f));

        // 分数（右对齐）
        AddText(row, data.score + " pts", 26, Gold, TextAnchor.MiddleRight,
            new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-14f, 0f), new Vector2(120f, 0f));
    }

    private IEnumerator CountdownRoutine(float total)
    {
        float t = total;
        while (t > 0f)
        {
            t -= Time.deltaTime;
            _countdownText.text = $"下一局将在 {Mathf.CeilToInt(t)} 秒后开始...";
            yield return null;
        }
        _countdownText.text = "正在重新加载场景...";
    }

    // ─────────────────────────────────────────────────
    //  UI 构建
    // ─────────────────────────────────────────────────

    private void BuildUI()
    {
        // Canvas
        var canvasObj = new GameObject("MatchEndCanvas");
        _canvas = canvasObj.AddComponent<Canvas>();
        _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 500;
        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution  = new Vector2(1920, 1080);
        canvasObj.AddComponent<GraphicRaycaster>();

        // Root container（统一控制 Dim + Panel 的显示）
        _root = new GameObject("MatchEndRoot");
        _root.transform.SetParent(canvasObj.transform, false);
        Stretch(_root.AddComponent<RectTransform>());

        // Dim overlay（在 Root 下，随 Root 一起隐藏）
        var dimObj = new GameObject("Dim");
        dimObj.transform.SetParent(_root.transform, false);
        var dimImg = dimObj.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.55f);
        Stretch(dimObj.GetComponent<RectTransform>());

        // Panel（在 Root 下）
        _panel = new GameObject("Panel");
        _panel.transform.SetParent(_root.transform, false);
        var panelImg = _panel.AddComponent<Image>();
        panelImg.color = BgDark;
        var panelRect = _panel.GetComponent<RectTransform>();
        panelRect.anchorMin        = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax        = new Vector2(0.5f, 0.5f);
        panelRect.pivot            = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta        = new Vector2(640f, 520f);

        // Outline
        var ol = _panel.AddComponent<Outline>();
        ol.effectColor    = new Color(1f, 0.85f, 0.2f, 0.7f);
        ol.effectDistance = new Vector2(2f, 2f);

        // Title
        var titleObj = new GameObject("Title");
        titleObj.transform.SetParent(_panel.transform, false);
        _titleText = titleObj.AddComponent<Text>();
        _titleText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _titleText.fontSize  = 32;
        _titleText.fontStyle = FontStyle.Bold;
        _titleText.alignment = TextAnchor.MiddleCenter;
        _titleText.color     = Gold;
        var titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchorMin        = new Vector2(0f, 1f);
        titleRect.anchorMax        = new Vector2(1f, 1f);
        titleRect.pivot            = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -10f);
        titleRect.sizeDelta        = new Vector2(0f, 55f);

        // Divider
        var divObj = new GameObject("Divider");
        divObj.transform.SetParent(_panel.transform, false);
        var divImg = divObj.AddComponent<Image>();
        divImg.color = new Color(1f, 0.85f, 0.2f, 0.4f);
        var divRect = divObj.GetComponent<RectTransform>();
        divRect.anchorMin        = new Vector2(0.04f, 1f);
        divRect.anchorMax        = new Vector2(0.96f, 1f);
        divRect.pivot            = new Vector2(0.5f, 1f);
        divRect.anchoredPosition = new Vector2(0f, -68f);
        divRect.sizeDelta        = new Vector2(0f, 2f);

        // Score list root (vertical layout)
        var listObj = new GameObject("ScoreList");
        listObj.transform.SetParent(_panel.transform, false);
        var layout = listObj.AddComponent<VerticalLayoutGroup>();
        layout.spacing           = 8f;
        layout.padding           = new RectOffset(16, 16, 0, 0);
        layout.childForceExpandWidth  = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childAlignment    = TextAnchor.UpperLeft;
        listObj.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var listRect = listObj.GetComponent<RectTransform>();
        listRect.anchorMin        = new Vector2(0f, 1f);
        listRect.anchorMax        = new Vector2(1f, 1f);
        listRect.pivot            = new Vector2(0.5f, 1f);
        listRect.anchoredPosition = new Vector2(0f, -80f);
        listRect.sizeDelta        = new Vector2(0f, 0f);
        _scoreListRoot = listObj.transform;

        // Countdown text (bottom)
        var cdObj = new GameObject("Countdown");
        cdObj.transform.SetParent(_panel.transform, false);
        _countdownText = cdObj.AddComponent<Text>();
        _countdownText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _countdownText.fontSize  = 19;
        _countdownText.color     = new Color(0.7f, 0.7f, 0.7f, 1f);
        _countdownText.alignment = TextAnchor.MiddleCenter;
        var cdRect = cdObj.GetComponent<RectTransform>();
        cdRect.anchorMin        = new Vector2(0f, 0f);
        cdRect.anchorMax        = new Vector2(1f, 0f);
        cdRect.pivot            = new Vector2(0.5f, 0f);
        cdRect.anchoredPosition = new Vector2(0f, 12f);
        cdRect.sizeDelta        = new Vector2(0f, 36f);
    }

    private static void AddText(GameObject parent, string txt, int size, Color col,
        TextAnchor anchor, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        var go = new GameObject("T_" + txt.Substring(0, Mathf.Min(txt.Length, 6)));
        go.transform.SetParent(parent.transform, false);
        var t = go.AddComponent<Text>();
        t.text      = txt;
        t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize  = size;
        t.color     = col;
        t.alignment = anchor;
        var r = go.GetComponent<RectTransform>();
        r.anchorMin        = anchorMin;
        r.anchorMax        = anchorMax;
        r.pivot            = new Vector2(anchorMin.x, 0.5f);
        r.anchoredPosition = anchoredPos;
        r.sizeDelta        = sizeDelta;
    }

    private static void Stretch(RectTransform r)
    {
        r.anchorMin        = Vector2.zero;
        r.anchorMax        = Vector2.one;
        r.anchoredPosition = Vector2.zero;
        r.sizeDelta        = Vector2.zero;
    }
}
