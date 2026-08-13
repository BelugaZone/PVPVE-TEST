using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FishNet;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using System.Collections;

public class UI_StartScreen : MonoBehaviour
{
    public static UI_StartScreen Instance;

    [SerializeField] private string defaultClientIP = "127.0.0.1";

    private TMP_InputField _idInput;
    private TMP_InputField _ipInput;
    private Button _startServerButton;
    private Button _hostButton;
    private Button _clientButton;
    private TextMeshProUGUI _errorText;

    private void Awake()
    {
        if (Instance == null) { Instance = this; }
        else { Destroy(gameObject); return; }
        DontDestroyOnLoad(gameObject);
        BuildUI();
    }

    private Canvas _canvas;

    private void Update()
    {
        // Force-hide outside the Lobby by toggling the Canvas (not SetActive) so Update keeps running.
        string activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        bool shouldBeVisible = activeScene == "Lobby";
        if (_canvas != null && _canvas.enabled != shouldBeVisible)
            _canvas.enabled = shouldBeVisible;
    }

    private void Start()
    {
        _startServerButton.onClick.AddListener(StartServer);
        _hostButton.onClick.AddListener(LoginAsHost);
        _clientButton.onClick.AddListener(LoginAsClient);

        if (_errorText != null) _errorText.text = "";
        CustomAuthenticator.OnClientAuthFailed += HandleAuthFailed;

        var nm = InstanceFinder.NetworkManager;
        if (nm != null)
        {
            nm.ClientManager.OnClientConnectionState += OnClientConnectionState;
            nm.ServerManager.OnServerConnectionState += OnServerConnectionState;
        }
    }

    private void OnDestroy()
    {
        CustomAuthenticator.OnClientAuthFailed -= HandleAuthFailed;
        var nm = InstanceFinder.NetworkManager;
        if (nm != null)
        {
            nm.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            nm.ServerManager.OnServerConnectionState -= OnServerConnectionState;
        }
    }

    // --- Connection actions ---

    private void StartServer()
    {
        var nm = InstanceFinder.NetworkManager;
        if (nm == null) { SetError("No NetworkManager."); return; }
        SetError("Starting server...");
        nm.ServerManager.StartConnection();
        _startServerButton.interactable = false;
        // Server-only: keep the screen visible so the host can also log in later
        // via the Host button (server already running).
    }

    private void LoginAsHost()
    {
        if (!TryPrepareLogin(out var nm)) return;
        SetError("Starting host (server + client)...");
        // Host = server + client in one process.
        if (!nm.ServerManager.Started)
            nm.ServerManager.StartConnection();
        nm.ClientManager.StartConnection();
    }

    private void LoginAsClient()
    {
        if (!TryPrepareLogin(out var nm)) return;
        // Set the transport address for the client connection.
        var tug = nm.TransportManager.GetTransport<Tugboat>();
        if (tug != null && _ipInput != null && !string.IsNullOrWhiteSpace(_ipInput.text))
            tug.SetClientAddress(_ipInput.text.Trim());
        SetError("Connecting as client...");
        nm.ClientManager.StartConnection();
    }

    private bool TryPrepareLogin(out FishNet.Managing.NetworkManager nm)
    {
        nm = InstanceFinder.NetworkManager;
        if (nm == null) { SetError("No NetworkManager."); return false; }
        if (_idInput == null || string.IsNullOrWhiteSpace(_idInput.text))
        {
            SetError("Please enter a valid ID.");
            return false;
        }
        CustomAuthenticator.PendingLoginID = _idInput.text.Trim();
        _hostButton.interactable = false;
        _clientButton.interactable = false;
        return true;
    }

    // --- Connection state / auth feedback ---

    private void OnServerConnectionState(ServerConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Started)
        {
            SetError("Server started successfully!");
            _startServerButton.interactable = false;
        }
    }

    private void OnClientConnectionState(ClientConnectionStateArgs args)
    {
        // During scene transitions FishNet may fire transient Stopped events. Do NOT re-show
        // the start screen on Stopped — the Update() poll controls visibility by scene name.
        // Only react to Started (hide after auth).
        if (args.ConnectionState != LocalConnectionState.Started)
            return;

        SetError("Connection successful! Entering game...");
        StartCoroutine(HideAfterDelay());
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(1.0f);
        if (InstanceFinder.ClientManager.Connection.IsActive)
            Hide();
    }

    private void HandleAuthFailed(string error)
    {
        SetError(error);
        _hostButton.interactable = true;
        _clientButton.interactable = true;
    }

    public void Show() { _canvas.enabled = true; }
    public void Hide() { _canvas.enabled = false; }

    private void SetError(string msg)
    {
        if (_errorText != null) _errorText.text = msg;
    }

    // --- UI construction (runtime, matches existing code-built canvases) ---

    private void BuildUI()
    {
        _canvas = gameObject.GetComponent<Canvas>();
        if (_canvas == null)
            _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;
        gameObject.AddComponent<GraphicRaycaster>();

        // Panel background
        var panel = CreateImage("Panel", transform);
        panel.color = new Color(0.08f, 0.08f, 0.1f, 0.95f);
        var panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero; panelRt.anchorMax = Vector2.one;
        panelRt.offsetMin = Vector2.zero; panelRt.offsetMax = Vector2.zero;

        // Title
        var title = CreateText("Title", "PVPVE — Start", transform);
        title.fontSize = 48; title.alignment = TextAlignmentOptions.Center;
        SetAnchors(title.rectTransform, new Vector2(0.5f, 0.9f), new Vector2(0.5f, 0.9f));
        title.rectTransform.sizeDelta = new Vector2(600, 80);

        // ID input
        _idInput = CreateInputField("IDInput", "Enter player ID...", transform,
            new Vector2(0.5f, 0.74f), new Vector2(360, 50));

        // IP input (client only)
        _ipInput = CreateInputField("IPInput", defaultClientIP, transform,
            new Vector2(0.5f, 0.66f), new Vector2(360, 50));
        _ipInput.text = defaultClientIP;

        // Buttons
        _startServerButton = CreateButton("StartServerBtn", "Start Server Only", transform,
            new Vector2(0.5f, 0.52f), new Vector2(360, 56));
        _hostButton = CreateButton("HostBtn", "Login as Host (Server + Client)", transform,
            new Vector2(0.5f, 0.42f), new Vector2(360, 56));
        _clientButton = CreateButton("ClientBtn", "Login as Client", transform,
            new Vector2(0.5f, 0.32f), new Vector2(360, 56));

        // Error text
        _errorText = CreateText("ErrorText", "", transform);
        _errorText.fontSize = 22; _errorText.color = Color.yellow;
        _errorText.alignment = TextAlignmentOptions.Center;
        SetAnchors(_errorText.rectTransform, new Vector2(0.5f, 0.20f), new Vector2(0.5f, 0.20f));
        _errorText.rectTransform.sizeDelta = new Vector2(700, 40);
    }

    // --- uGUI helpers (procedural; no external assets) ---

    private Image CreateImage(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<Image>();
    }

    private TextMeshProUGUI CreateText(string name, string content, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = content;
        t.font = TMPro.TMP_Settings.defaultFontAsset;
        t.color = Color.white;
        return t;
    }

    private TMP_InputField CreateInputField(string name, string placeholder, Transform parent, Vector2 anchor, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        SetAnchors(rt, anchor, anchor);
        rt.sizeDelta = size;

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.2f, 0.2f, 0.25f, 1f);

        var input = go.AddComponent<TMP_InputField>();
        var textObj = new GameObject("Text");
        textObj.transform.SetParent(go.transform, false);
        var textRt = textObj.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero; textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(10, 6); textRt.offsetMax = new Vector2(-10, -6);
        var text = textObj.AddComponent<TextMeshProUGUI>();
        text.font = TMPro.TMP_Settings.defaultFontAsset;
        text.color = Color.white; text.fontSize = 28;
        input.textComponent = text;
        input.textViewport = textRt;

        var phObj = new GameObject("Placeholder");
        phObj.transform.SetParent(go.transform, false);
        var phRt = phObj.AddComponent<RectTransform>();
        phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one;
        phRt.offsetMin = new Vector2(10, 6); phRt.offsetMax = new Vector2(-10, -6);
        var ph = phObj.AddComponent<TextMeshProUGUI>();
        ph.font = TMPro.TMP_Settings.defaultFontAsset;
        ph.color = new Color(0.6f, 0.6f, 0.6f, 1f); ph.fontSize = 28; ph.text = placeholder;
        input.placeholder = ph;

        // TMP creates the caret renderer inside OnEnable(), but only when m_TextComponent is
        // already assigned. AddComponent fires OnEnable before we set textComponent above, so
        // the caret never gets built and the blinking cursor is invisible even though typing
        // works. Re-enabling now (textComponent is set) builds the caret renderer.
        input.enabled = false;
        input.enabled = true;

        return input;
    }

    private Button CreateButton(string name, string label, Transform parent, Vector2 anchor, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        SetAnchors(rt, anchor, anchor);
        rt.sizeDelta = size;
        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.15f, 0.45f, 0.8f, 1f);
        var btn = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.25f, 0.55f, 0.9f, 1f);
        colors.pressedColor = new Color(0.1f, 0.3f, 0.6f, 1f);
        btn.colors = colors;

        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(go.transform, false);
        var labelRt = labelObj.AddComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero; labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero; labelRt.offsetMax = Vector2.zero;
        var t = labelObj.AddComponent<TextMeshProUGUI>();
        t.font = TMPro.TMP_Settings.defaultFontAsset;
        t.text = label; t.alignment = TextAlignmentOptions.Center; t.fontSize = 26; t.color = Color.white;
        return btn;
    }

    private void SetAnchors(RectTransform rt, Vector2 min, Vector2 max)
    {
        rt.anchorMin = min; rt.anchorMax = max;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }
}
