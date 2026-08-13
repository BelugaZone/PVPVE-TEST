using UnityEditor;
using UnityEngine;

public class CheckPhysics
{
    [MenuItem("Tools/Check Physics")]
    public static void Check()
    {
        Debug.Log("Bullet ignores Player: " + Physics.GetIgnoreLayerCollision(9, 8));
        Debug.Log("Bullet ignores Enemy: " + Physics.GetIgnoreLayerCollision(9, 11));
    }
}
