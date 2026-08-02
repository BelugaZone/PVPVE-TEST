using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 撤离区 HUD：运行时纯代码构建，挂在本地玩家身上。
/// 由 ExtractionZone 通过 TargetRpc 调用 Show / Cancel / Success。
/// </summary>
public class UI_ExtractionHUD : MonoBehaviour
{
    // ── 单例（本地玩家唯一） ─────────────────────────────
    public static UI_ExtractionHUD Local { get; private set; }

    // ── 私有 UI 引用 ───────────────────────────────────
    private Canvas      _canvas;
    private GameObject  _panel;
    private Text        _messageText;
    private Text        _countdownText;
    private Image       _progressBg;
    private Image       _progressFill;

    // ── 状态 ───────────────────────────────────────────
    private Coroutine _countdownCoroutine;
    private float     _totalTime;

    // ── 颜色常量 ────────────────────────────────────────
    private static readonly Color ColWarning  = new Color(1f, 0.85f, 0f, 1f);   // 金黄：无核心
    private static readonly Color ColReady    = new Color(0f, 1f, 0.4f, 1f);    // 绿色：有核心
    private static readonly Color ColSuccess  = new Color(0f, 0.8f, 1f, 1f);    // 青色：成功

    // ────────────────────────────────────────────────────
    private void Awake()
    {
        Local = this;
        BuildUI();
        HidePanel();
    }

    private void OnDestroy()
    {
        if (Local == this) Local = null;
        if (_canvas != null) Destroy(_canvas.gameObject);
    }

    // ──────────────────────────────────────────────────────
    //  外部接口（由 ExtractionZone RPC 调用）
    // ──────────────────────────────────────────────────────

    /// <summary>无核心进入撤离区</summary>
    public void ShowNoCore()
    {
        StopCountdown();
        _messageText.text  = "需要核心才能撤离";
        _messageText.color = ColWarning;
        _countdownText.gameObject.SetActive(false);
        _progressBg.gameObject.SetActive(false);
        ShowPanel();
    }

    /// <summary>携带核心进入撤离区，开始 totalSeconds 倒计时</summary>
    public void ShowCountdown(float totalSeconds)
    {
        _totalTime = totalSeconds;
        _messageText.text  = "已检测到核心，请等待撤离...";
        _messageText.color = ColReady;
        _countdownText.gameObject.SetActive(true);
        _progressBg.gameObject.SetActive(true);
        ShowPanel();
        StopCountdown();
        _countdownCoroutine = StartCoroutine(CountdownRoutine(totalSeconds));
    }

    /// <summary>撤离被取消（离开区域）</summary>
    public void Cancel()
    {
        StopCountdown();
        HidePanel();
    }

    /// <summary>撤离成功</summary>
    public void Success()
    {
        StopCountdown();
        _progressFill.fillAmount = 1f;
        _messageText.text  = "撤离成功！";
        _messageText.color = ColSuccess;
        _countdownText.text = "0.0 s";
        ShowPanel();
        StartCoroutine(HideAfterDelay(3f));
    }

    // ──────────────────────────────────────────────────────
    //  内部逻辑
    // ──────────────────────────────────────────────────────

    private IEnumerator CountdownRoutine(float total)
    {
        float remaining = total;
        while (remaining > 0f)
        {
            remaining -= Time.deltaTime;
            if (remaining < 0f) remaining = 0f;

            float pct = 1f - remaining / total;
            _progressFill.fillAmount = pct;

            // 颜色渐变：绿 → 黄 → 橙
            _progressFill.color = Color.Lerp(ColReady, ColWarning, pct);
            _countdownText.text = remaining.ToString("F1") + " s";

            yield return null;
        }
    }

    private void StopCountdown()
    {
        if (_countdownCoroutine != null)
        {
            StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = null;
        }
    }

    private IEnumerator HideAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        HidePanel();
    }

    private void ShowPanel() => _panel.SetActive(true);
    private void HidePanel() => _panel.SetActive(false);

    // ──────────────────────────────────────────────────────
    //  UI 构建（纯代码，Screen Space Overlay）
    // ──────────────────────────────────────────────────────

    private void BuildUI()
    {
        // -- Canvas
        var canvasObj = new GameObject("ExtractionHUDCanvas");
        _canvas = canvasObj.AddComponent<Canvas>();
        _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 200;
        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasObj.AddComponent<GraphicRaycaster>();

        // -- Panel 容器：屏幕上方 1/3 处居中
        _panel = new GameObject("ExtractionPanel");
        _panel.transform.SetParent(canvasObj.transform, false);

        var panelImg  = _panel.AddComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.65f);

        var panelRect = _panel.GetComponent<RectTransform>();
        panelRect.anchorMin       = new Vector2(0.5f, 0.72f);
        panelRect.anchorMax       = new Vector2(0.5f, 0.72f);
        panelRect.pivot           = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta       = new Vector2(520f, 110f);

        // 圆角感：用 outline 替代（纯 Image 无圆角，保持简洁）
        var outline = _panel.AddComponent<Outline>();
        outline.effectColor    = new Color(0f, 1f, 0.4f, 0.8f);
        outline.effectDistance = new Vector2(2f, 2f);

        // -- 图标文字（左侧 ⬡）
        var iconObj  = new GameObject("Icon");
        iconObj.transform.SetParent(_panel.transform, false);
        var iconText = iconObj.AddComponent<Text>();
        iconText.text      = "⬡";
        iconText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        iconText.fontSize  = 36;
        iconText.color     = ColReady;
        iconText.alignment = TextAnchor.MiddleLeft;
        var iconRect = iconObj.GetComponent<RectTransform>();
        iconRect.anchorMin       = new Vector2(0f, 0.55f);
        iconRect.anchorMax       = new Vector2(0f, 0.95f);
        iconRect.pivot           = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(16f, 0f);
        iconRect.sizeDelta       = new Vector2(40f, 36f);

        // -- 主消息文字
        var msgObj = new GameObject("MessageText");
        msgObj.transform.SetParent(_panel.transform, false);
        _messageText = msgObj.AddComponent<Text>();
        _messageText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _messageText.fontSize  = 22;
        _messageText.fontStyle = FontStyle.Bold;
        _messageText.color     = ColReady;
        _messageText.alignment = TextAnchor.MiddleLeft;
        var msgRect = msgObj.GetComponent<RectTransform>();
        msgRect.anchorMin       = new Vector2(0f, 0.52f);
        msgRect.anchorMax       = new Vector2(1f, 0.95f);
        msgRect.pivot           = new Vector2(0.5f, 0.5f);
        msgRect.anchoredPosition = new Vector2(30f, 0f);
        msgRect.sizeDelta       = new Vector2(-90f, 0f);

        // -- 进度条背景
        var bgObj = new GameObject("ProgressBg");
        bgObj.transform.SetParent(_panel.transform, false);
        _progressBg = bgObj.AddComponent<Image>();
        _progressBg.color = new Color(0.15f, 0.15f, 0.15f, 1f);
        var bgRect = bgObj.GetComponent<RectTransform>();
        bgRect.anchorMin       = new Vector2(0.04f, 0.08f);
        bgRect.anchorMax       = new Vector2(0.96f, 0.46f);
        bgRect.anchoredPosition = Vector2.zero;
        bgRect.sizeDelta       = Vector2.zero;

        // -- 进度条填充
        var fillObj = new GameObject("ProgressFill");
        fillObj.transform.SetParent(bgObj.transform, false);
        _progressFill = fillObj.AddComponent<Image>();
        _progressFill.color      = ColReady;
        _progressFill.type       = Image.Type.Filled;
        _progressFill.fillMethod = Image.FillMethod.Horizontal;
        _progressFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        _progressFill.fillAmount = 0f;
        _progressFill.sprite     = CreateWhiteSprite();
        var fillRect = fillObj.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.sizeDelta = Vector2.zero;

        // -- 倒计时数字（右侧）
        var cdObj = new GameObject("CountdownText");
        cdObj.transform.SetParent(_panel.transform, false);
        _countdownText = cdObj.AddComponent<Text>();
        _countdownText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _countdownText.fontSize  = 24;
        _countdownText.fontStyle = FontStyle.Bold;
        _countdownText.color     = Color.white;
        _countdownText.alignment = TextAnchor.MiddleRight;
        var cdRect = cdObj.GetComponent<RectTransform>();
        cdRect.anchorMin       = new Vector2(1f, 0.5f);
        cdRect.anchorMax       = new Vector2(1f, 0.95f);
        cdRect.pivot           = new Vector2(1f, 0.5f);
        cdRect.anchoredPosition = new Vector2(-12f, 0f);
        cdRect.sizeDelta       = new Vector2(100f, 36f);
    }

    private static Sprite CreateWhiteSprite()
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
    }
}
