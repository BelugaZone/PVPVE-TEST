using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Player_AnimationEvents : MonoBehaviour
{
    private Player_WeaponVisuals visualController;
    private Player_WeaponController weaponController;

    private void Start()
    {
        visualController = GetComponentInParent<Player_WeaponVisuals>();
        weaponController = GetComponentInParent<Player_WeaponController>();
    }

    public void ReloadIsOver()
    {
        visualController.MaximizeRigWeight();
        weaponController.CurrentWeapon().RefillBullets();

        weaponController.SetWeaponReady(true);
    }


    public void ReturnRig()
    {
        visualController.MaximizeRigWeight();
        visualController.MaximizeLeftHandWeight();
    }

    public void WeaponEquipingIsOver()
    {
        weaponController.SetWeaponReady(true);
    }

    public void SwitchOnWeaponModel() => visualController.SwitchOnCurrentWeaponModel();

    // --- Melee Attack Hit Detection Events ---
    public void BeginMeleeAttackCheck()
    {
        weaponController.EnableMeleeAttackCheck(true);
    }

    public void FinishMeleeAttackCheck()
    {
        weaponController.EnableMeleeAttackCheck(false);
    }

    public void StartManualMovement()
    {
        // Currently ignored for Player to preserve normal movement logic
    }

    public void StopManualMovement()
    {
        // Currently ignored for Player to preserve normal movement logic
    }

    public void AnimationTrigger()
    {
        // Suppress warning for enemy animations reused on the player
    }
}
