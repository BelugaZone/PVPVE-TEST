using UnityEngine;
using UnityEngine.UI;

// Displays the current weapon's ammo as a radial ring + "mag / reserve" text,
// plus the weapon name to the right. Reads the REAL CurrentWeapon() so it stays
// in sync with actual firing/reloading.
public class UI_AmmoRing : MonoBehaviour
{
    private Player player;
    private Image ring;            // radial filled
    private Image ringBg;          // dark background ring
    private Text magText;          // center "mag / reserve"
    private Text nameText;         // weapon name

    private WeaponType lastType = (WeaponType)(-1);

    public void Setup(Image ringBg, Image ring, Text magText, Text nameText, Player player)
    {
        this.ringBg = ringBg;
        this.ring = ring;
        this.magText = magText;
        this.nameText = nameText;
        this.player = player;
    }

    private void Update()
    {
        if (player == null || player.weapon == null) return;
        Weapon w = player.weapon.CurrentWeapon();
        
        bool hasWeapon = (w != null);

        if (ringBg != null && ringBg.gameObject.activeSelf != hasWeapon) 
            ringBg.gameObject.SetActive(hasWeapon);
        if (ring != null && ring.gameObject.activeSelf != hasWeapon) 
            ring.gameObject.SetActive(hasWeapon);
        if (magText != null && magText.gameObject.activeSelf != hasWeapon) 
            magText.gameObject.SetActive(hasWeapon);
        if (nameText != null && nameText.gameObject.activeSelf != hasWeapon) 
            nameText.gameObject.SetActive(hasWeapon);

        if (!hasWeapon) return;

        if (ring != null)
        {
            float fill = w.magazineCapacity > 0
                ? Mathf.Clamp01((float)w.bulletsInMagazine / w.magazineCapacity)
                : 1f;
            ring.fillAmount = fill;
            if (w.weaponType != lastType)
            {
                ring.color = ColorFor(w.weaponType);
                lastType = w.weaponType;
            }
        }

        if (magText != null)
        {
            switch (w.weaponType)
            {
                case WeaponType.Melee:
                    magText.text = "8";
                    break;
                case WeaponType.Grenade:
                    magText.text = w.bulletsInMagazine.ToString();
                    break;
                default:
                    magText.text = $"{w.bulletsInMagazine} / {w.totalReserveAmmo}";
                    break;
            }
        }

        if (nameText != null && w.weaponData != null)
            nameText.text = w.weaponData.weaponName;
    }

    // Weapon-specific colors keyed by weaponName, falling back to WeaponType.
    private static Color ColorFor(WeaponType t)
    {
        switch (t)
        {
            case WeaponType.Pistol:     return new Color(0.5f, 0.5f, 0.5f);       // Heaven — 灰
            case WeaponType.Revolver:   return new Color(1.0f, 0.85f, 0.0f);      // Bolt — 黄
            case WeaponType.AutoRifle:  return new Color(0.2f, 0.8f, 0.2f);       // Blaste — 绿
            case WeaponType.Rifle:      return new Color(0.5f, 0.0f, 0.8f);       // Stinger — 紫
            case WeaponType.Shotgun:    return new Color(0.55f, 0.27f, 0.07f);    // Shotgun — 棕
            case WeaponType.Melee:      return new Color(0.05f, 0.05f, 0.05f);    // Melee — 黑
            case WeaponType.Grenade:    return new Color(0.8f, 0.1f, 0.1f);       // Grenade — 红
            default:                    return Color.white;
        }
    }
}
