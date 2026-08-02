using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Enemy_Health : HealthController
{
    public override void OnStartClient()
    {
        base.OnStartClient();
        Debug.Log($"[Enemy_Health] OnStartClient on {gameObject.name}");
    }

    private void Start()
    {
        if (GetComponent<UI_HealthBar>() == null)
        {
            UI_HealthBar ui = gameObject.AddComponent<UI_HealthBar>();
            // Enemy is never local player, so isLocalPlayer = false, isPlayerEntity = false
            ui.Initialize(this, false, false);
        }
    }
}
