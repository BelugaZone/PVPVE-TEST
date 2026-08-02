using UnityEngine;
using UnityEngine.UI;

// Always-on ammo HUD: radial ring (mag/capacity) + "mag / reserve" text + weapon name,
// laid out as a strip immediately LEFT of the existing UI_HealthBar.
//
// The health bar is built at runtime by UI_HealthBar on its own ScreenSpaceOverlay
// canvas ("PlayerHealthCanvas"), anchored bottom-center. To guarantee the ammo strip
// stays flush against the health bar regardless of resolution/CanvasScaler match, we
// do NOT use a separate canvas with hardcoded pixel offsets. Instead, at Build() time we
// locate the health bar's RectTransform and parent our strip to the SAME canvas, then
// position the strip relative to the health bar's rect (right edge of strip = left edge
// of health bar, minus a small gap). Both elements then share one coordinate space and
// scale identically.
public class UI_HudRoot : MonoBehaviour
{
    [SerializeField] private float ringRadius = 34f;          // px (in the health bar canvas's scaled space)
    [SerializeField] private float ringThickness = 8f;
    [SerializeField] private float gapFromHealthBar = 8f;     // px between strip right edge and health bar left edge
    [SerializeField] private float nameWidth = 150f;
    [SerializeField] private float slotGap = 6f;
    [SerializeField] private float searchTimeout = 2f;        // seconds to wait for the health bar to appear

    private UI_AmmoRing ammoRing;

    public void Init(Player player)
    {
        // The health bar is created by HealthController.OnStartClient which may run
        // slightly after PlayerInventory.InitializeLocal. Poll for it briefly.
        StartCoroutine(BuildWhenReady(player));
    }

    private System.Collections.IEnumerator BuildWhenReady(Player player)
    {
        float t = 0f;
        RectTransform healthBar = null;
        while (healthBar == null && t < searchTimeout)
        {
            healthBar = FindHealthBar();
            if (healthBar == null)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        Build(player, healthBar);
    }

    // UI_HealthBar names the bar background "HealthBackground" under a canvas
    // "PlayerHealthCanvas". Search the whole scene for it.
    private static RectTransform FindHealthBar()
    {
        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach (var c in canvases)
        {
            if (c.name != "PlayerHealthCanvas") continue;
            var t = c.transform.Find("HealthBackground");
            if (t != null) return t as RectTransform;
        }
        return null;
    }

    private void Build(Player player, RectTransform healthBar)
    {
        // Parent to the health bar's canvas so we share its coordinate space & scale.
        Transform parent = healthBar != null ? healthBar.parent : transform;
        bool sameCanvasAsHealthBar = healthBar != null;

        // Root strip: anchored to the health bar's left edge, grows leftward.
        // We position elements relative to a strip whose RIGHT edge sits `gapFromHealthBar`
        // left of the health bar's left edge.
        float stripRightX;   // x (in parent space) of the strip's right edge
        float centerY;       // vertical center of the strip (matches health bar center)

        if (healthBar != null)
        {
            // healthBar pivot is (0.5, 0); its center x in parent space = its anchoredPosition.x.
            // Its half-width = sizeDelta.x / 2.
            float hbCenterX = healthBar.anchoredPosition.x;
            float hbHalfWidth = healthBar.rect.width * 0.5f;
            float hbLeftEdge = hbCenterX - hbHalfWidth;
            stripRightX = hbLeftEdge - gapFromHealthBar;

            // Health bar vertical center: pivot (0.5,0) anchored at bottom, sizeDelta.y tall,
            // positioned at anchoredPosition.y. Center y = anchoredPosition.y + rect.height/2.
            centerY = healthBar.anchoredPosition.y + healthBar.rect.height * 0.5f;
        }
        else
        {
            // Fallback: bottom-center, hardcoded (shouldn't happen in normal play).
            stripRightX = -200f - gapFromHealthBar;
            centerY = 30f + 10f;
        }

        // Lay out right-to-left from stripRightX.
        float div2X = stripRightX - 1f;                                  // divider | health bar
        float nameRight = div2X - 1f - slotGap;
        float nameX = nameRight - nameWidth * 0.5f;                      // name center
        float div1X = nameX - nameWidth * 0.5f - slotGap - 1f;           // ring | name divider
        float ringX = div1X - 1f - slotGap - ringRadius;                 // ring center

        // If we're on the health bar's canvas, anchor everything to that canvas's bottom-center
        // (matching the health bar's own anchor convention) so coordinates are in the same space.
        Vector2 anchor = sameCanvasAsHealthBar ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 0f);

        Image ringBg = AddRadial(parent, "RingBg", ringX, centerY, anchor, ringRadius * 2f, new Color(0, 0, 0, 0.6f));
        Image ring = AddRadial(parent, "Ring", ringX, centerY, anchor, (ringRadius - ringThickness) * 2f, new Color(0.9f, 0.5f, 0.2f));
        Text magText = AddText(parent, "AmmoMagText", ringX, centerY, anchor, ringRadius * 2f, 16, TextAnchor.MiddleCenter, "0 / 0");
        AddDivider(parent, div1X, centerY, anchor, ringRadius * 2f);
        Text nameText = AddText(parent, "AmmoWeaponName", nameX, centerY, anchor, nameWidth, 18, TextAnchor.MiddleCenter, "Weapon");
        AddDivider(parent, div2X, centerY, anchor, ringRadius * 2f);

        // Per-frame updater.
        GameObject updaterObj = new GameObject("AmmoRingUpdater");
        updaterObj.transform.SetParent(parent, false);
        ammoRing = updaterObj.AddComponent<UI_AmmoRing>();
        ammoRing.Setup(ringBg, ring, magText, nameText, player);
    }

    private Image AddRadial(Transform parent, string name, float x, float y, Vector2 anchor, float size, Color color)
    {
        RectTransform rt = MakeRect(parent, name, x, y, anchor, new Vector2(size, size));
        Image img = rt.gameObject.AddComponent<Image>();
        img.sprite = UI_PlaceholderIcons.White();
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Radial360;
        img.fillOrigin = (int)Image.Origin360.Top;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private Text AddText(Transform parent, string name, float x, float y, Vector2 anchor, float width, int fontSize, TextAnchor align, string text)
    {
        RectTransform rt = MakeRect(parent, name, x, y, anchor, new Vector2(width, 24));
        Text t = rt.gameObject.AddComponent<Text>();
        t.font = GetDefaultFont();
        t.fontSize = fontSize;
        t.alignment = align;
        t.color = Color.white;
        t.raycastTarget = false;
        t.text = text;
        return t;
    }

    private void AddDivider(Transform parent, float x, float y, Vector2 anchor, float height)
    {
        RectTransform rt = MakeRect(parent, "AmmoDivider", x, y, anchor, new Vector2(2, height));
        Image img = rt.gameObject.AddComponent<Image>();
        img.sprite = UI_PlaceholderIcons.White();
        img.color = new Color(1, 1, 1, 0.6f);
        img.raycastTarget = false;
    }

    private RectTransform MakeRect(Transform parent, string name, float x, float y, Vector2 anchor, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = size;
        return rt;
    }

    private static Font GetDefaultFont()
    {
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
               ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
    }
}
