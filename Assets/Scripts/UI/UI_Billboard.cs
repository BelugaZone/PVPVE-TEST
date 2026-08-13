using UnityEngine;

public class UI_Billboard : MonoBehaviour
{
    private Transform camTransform;

    private void Start()
    {
        if (Camera.main != null)
        {
            camTransform = Camera.main.transform;
        }
    }

    private void LateUpdate()
    {
        if (camTransform == null)
        {
            if (Camera.main != null)
                camTransform = Camera.main.transform;
            else
                return;
        }

        // True billboard mode: match the camera's rotation exactly so the UI is perfectly flat to the screen.
        transform.rotation = camTransform.rotation;
    }
}
