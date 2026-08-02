using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public enum EquipType { SideEquipAnimation, BackEquipAnimation };
public enum HoldType { None = 0, CommonHold = 1, LowHold = 2, HighHold = 3, MeleeHold = 4, GrenadeHold = 5 };


public class WeaponModel : MonoBehaviour
{

    public WeaponType weaponType;
    public EquipType equipAnimationType;
    public HoldType holdType;

    public Transform gunPoint;
    public Transform holdPoint;

    [Header("Melee Damage Attributes")]
    public Transform[] damagePoints;
    public float attackRadius = 0.4f;

    private void OnDrawGizmos()
    {
        if (damagePoints != null && damagePoints.Length > 0)
        {
            Gizmos.color = Color.red;
            foreach(Transform point in damagePoints)
            {
                if (point != null)
                {
                    Gizmos.DrawWireSphere(point.position, attackRadius);
                }
            }
        }
    }

}
