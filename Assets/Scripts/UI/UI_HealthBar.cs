using UnityEngine;
using UnityEngine.UI;

public class UI_HealthBar : MonoBehaviour
{
    private HealthController healthController;
    private Image fillImage;
    private Canvas canvas;
    private RectTransform canvasRect;

    [SerializeField] private float worldHeightOffset = 2.2f;

    public void Initialize(HealthController health, bool isLocalPlayer, bool isPlayerEntity)
    {
        this.healthController = health;

        // Determine if we need screen space UI (local player) or world space UI (enemies/other players)
        if (isLocalPlayer && isPlayerEntity)
        {
            CreateScreenSpaceUI();
        }
        else
        {
            CreateWorldSpaceUI(isPlayerEntity);
        }

        healthController.currentHealth.OnChange += OnHealthChanged;
        UpdateHealthUI(healthController.currentHealth.Value);
    }

    private void OnDestroy()
    {
        if (healthController != null)
        {
            healthController.currentHealth.OnChange -= OnHealthChanged;
        }
        
        if (canvas != null)
        {
            Destroy(canvas.gameObject);
        }
    }

    private void OnHealthChanged(int oldHealth, int newHealth, bool asServer)
    {
        UpdateHealthUI(newHealth);

        if (newHealth <= 0 && canvas != null)
        {
            canvas.gameObject.SetActive(false);
        }
    }

    private void UpdateHealthUI(int currentHealth)
    {
        if (fillImage != null && healthController != null)
        {
            fillImage.fillAmount = (float)currentHealth / healthController.maxHealth;
        }
    }

    private Sprite CreateWhiteSprite()
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
    }

    private void CreateScreenSpaceUI()
    {
        GameObject canvasObj = new GameObject("PlayerHealthCanvas");
        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // Ensure it's on top
        
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        
        GameObject bgObj = new GameObject("HealthBackground");
        bgObj.transform.SetParent(canvasObj.transform, false);
        Image bgImage = bgObj.AddComponent<Image>();
        bgImage.color = new Color(0, 0, 0, 0.5f);
        RectTransform bgRect = bgObj.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0.5f, 0);
        bgRect.anchorMax = new Vector2(0.5f, 0);
        bgRect.pivot = new Vector2(0.5f, 0);
        bgRect.anchoredPosition = new Vector2(0, 30); // 30 pixels from bottom
        bgRect.sizeDelta = new Vector2(400, 20); // Width 400, Height 20

        GameObject fillObj = new GameObject("HealthFill");
        fillObj.transform.SetParent(bgObj.transform, false);
        fillImage = fillObj.AddComponent<Image>();
        fillImage.sprite = CreateWhiteSprite();
        fillImage.color = Color.green;
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        
        RectTransform fillRect = fillObj.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.sizeDelta = Vector2.zero;
        fillRect.anchoredPosition = Vector2.zero;
    }

    public void ShowUI()
    {
        if (canvas != null && !canvas.gameObject.activeSelf)
        {
            canvas.gameObject.SetActive(true);
        }
    }

    private void CreateWorldSpaceUI(bool isPlayerEntity)
    {
        GameObject canvasObj = new GameObject("FloatingHealthCanvas");
        canvasObj.transform.SetParent(this.transform);
        canvasObj.transform.localPosition = new Vector3(0, worldHeightOffset, 0);
        
        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        
        canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(0.8f, 0.1f); // 0.8 meters wide, 0.1 meters tall
        
        GameObject bgObj = new GameObject("HealthBackground");
        bgObj.transform.SetParent(canvasObj.transform, false);
        Image bgImage = bgObj.AddComponent<Image>();
        bgImage.color = new Color(0, 0, 0, 0.7f);
        RectTransform bgRect = bgObj.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;
        
        GameObject fillObj = new GameObject("HealthFill");
        fillObj.transform.SetParent(bgObj.transform, false);
        fillImage = fillObj.AddComponent<Image>();
        fillImage.sprite = CreateWhiteSprite();
        
        // Red for enemies, Blue/Cyan for other players
        fillImage.color = isPlayerEntity ? Color.cyan : Color.red; 
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        
        RectTransform fillRect = fillObj.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.sizeDelta = Vector2.zero;

        canvasObj.AddComponent<UI_Billboard>();

        // Hide enemy health bars by default
        if (!isPlayerEntity)
        {
            canvasObj.SetActive(false);
        }
    }
}
