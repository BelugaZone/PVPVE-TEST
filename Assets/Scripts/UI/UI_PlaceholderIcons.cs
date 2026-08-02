using UnityEngine;
using UnityEngine.UI;

// Procedural placeholder sprites (no external art). Colored by type with a letter badge
// baked into the texture. Cached statically for the session.
public static class UI_PlaceholderIcons
{
    private static Sprite _white;
    private static readonly System.Collections.Generic.Dictionary<WeaponType, Sprite> _weapon = new System.Collections.Generic.Dictionary<WeaponType, Sprite>();
    private static readonly System.Collections.Generic.Dictionary<ItemType, Sprite> _item = new System.Collections.Generic.Dictionary<ItemType, Sprite>();

    public static Sprite White()
    {
        if (_white == null) _white = Make(Color.white, null);
        return _white;
    }

    public static Sprite Get(WeaponType t)
    {
        if (!_weapon.TryGetValue(t, out Sprite s))
        {
            Color c;
            char badge;
            switch (t)
            {
                case WeaponType.Pistol:     c = new Color(0.9f, 0.8f, 0.2f); badge = 'P'; break;
                case WeaponType.Revolver:   c = new Color(0.9f, 0.7f, 0.3f); badge = 'R'; break;
                case WeaponType.AutoRifle:  c = new Color(0.9f, 0.5f, 0.2f); badge = 'A'; break;
                case WeaponType.Rifle:      c = new Color(0.8f, 0.4f, 0.2f); badge = 'R'; break;
                case WeaponType.Shotgun:    c = new Color(0.9f, 0.3f, 0.3f); badge = 'S'; break;
                case WeaponType.Melee:      c = new Color(0.5f, 0.35f, 0.2f); badge = 'M'; break;
                case WeaponType.Grenade:    c = new Color(0.3f, 0.7f, 0.3f); badge = 'G'; break;
                default:                    c = Color.gray;                   badge = '?'; break;
            }
            s = Make(c, badge);
            _weapon[t] = s;
        }
        return s;
    }

    public static Sprite Get(ItemType t)
    {
        if (!_item.TryGetValue(t, out Sprite s))
        {
            Color c;
            char badge;
            switch (t)
            {
                case ItemType.Weapon:     c = Color.gray;                    badge = 'W'; break;
                case ItemType.Ammo:       c = new Color(0.9f, 0.85f, 0.3f);  badge = 'A'; break;
                case ItemType.Consumable: c = new Color(0.3f, 0.8f, 0.6f);  badge = '+'; break;
                default:                  c = Color.gray;                    badge = '?'; break;
            }
            s = Make(c, badge);
            _item[t] = s;
        }
        return s;
    }

    // For an InventoryItem: prefer its ItemData.icon if set, else procedural by type.
    public static Sprite ForItem(InventoryItem item)
    {
        if (item == null) return White();
        if (item.data != null && item.data.icon != null) return item.data.icon;
        if (item.IsWeapon) return Get(item.WeaponData.weaponType);
        return Get(item.ItemType);
    }

    private static Sprite Make(Color color, char? badge)
    {
        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color[] px = new Color[size * size];
        for (int i = 0; i < px.Length; i++) px[i] = color;
        // Dark border
        Color border = color * 0.5f; border.a = 1f;
        for (int x = 0; x < size; x++)
        {
            px[x] = border;
            px[(size - 1) * size + x] = border;
            px[x * size] = border;
            px[x * size + size - 1] = border;
        }
        tex.SetPixels(px);

        if (badge.HasValue)
        {
            DrawLetter(tex, badge.Value, size);
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    // Minimal 5x7 bitmap font for A-Z, 0-9, +, ?.
    private static void DrawLetter(Texture2D tex, char c, int size)
    {
        bool[,] g = Glyph(c);
        if (g == null) return;
        int gw = g.GetLength(0), gh = g.GetLength(1);
        int cell = Mathf.Min(size / 3, 24);
        int ox = (size - gw * cell) / 2;
        int oy = (size - gh * cell) / 2;
        Color ink = Color.white;
        for (int y = 0; y < gh; y++)
            for (int x = 0; x < gw; x++)
                if (g[x, y])
                    for (int dy = 0; dy < cell; dy++)
                        for (int dx = 0; dx < cell; dx++)
                        {
                            int px = ox + x * cell + dx;
                            int py = oy + y * cell + dy;
                            if (px >= 0 && px < size && py >= 0 && py < size)
                                tex.SetPixel(px, py, ink);
                        }
    }

    private static bool[,] Glyph(char c)
    {
        // Returns a 5-wide (x) by 7-tall (y, bottom=0) bitmap. y row 6 is top.
        // Only a handful of glyphs are needed; unknown -> null.
        string[] rows; // top row first
        switch (char.ToUpper(c))
        {
            case 'R': rows = new[] { "01110", "10001", "10001", "10001", "01110", "00100", "00100" }; break; // simplified R-ish; reuse
            case 'P': rows = new[] { "01110", "10001", "10001", "11110", "10000", "10000", "10000" }; break;
            case 'A': rows = new[] { "00100", "01010", "10001", "10001", "11111", "10001", "10001" }; break;
            case 'S': rows = new[] { "01111", "10000", "10000", "01110", "00001", "00001", "11110" }; break;
            case 'M': rows = new[] { "10001", "11011", "10101", "10101", "10001", "10001", "10001" }; break;
            case 'G': rows = new[] { "01110", "10001", "10000", "10111", "10001", "10001", "01110" }; break;
            case 'W': rows = new[] { "10001", "10001", "10001", "10101", "10101", "11011", "10001" }; break;
            case '+': rows = new[] { "00100", "00100", "00100", "11111", "00100", "00100", "00100" }; break;
            case '?': rows = new[] { "01110", "10001", "00010", "00100", "00100", "00000", "00100" }; break;
            default: return null;
        }
        bool[,] g = new bool[5, 7];
        for (int y = 0; y < 7; y++)
            for (int x = 0; x < 5; x++)
                g[x, y] = rows[6 - y][x] == '1'; // flip so y=0 is bottom
        return g;
    }
}
