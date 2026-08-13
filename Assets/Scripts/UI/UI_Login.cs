using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FishNet;
using System.Collections;

public class UI_Login : MonoBehaviour
{
    [SerializeField] private TMP_InputField idInput;
    [SerializeField] private Button loginButton;
    [SerializeField] private TextMeshProUGUI errorText;

    private void Start()
    {
        if (idInput != null)
        {
            idInput.customCaretColor = false;
            if (idInput.caretWidth == 0)
            {
                idInput.caretWidth = 2;
            }
            if (idInput.caretBlinkRate == 0)
            {
                idInput.caretBlinkRate = 0.85f;
            }
        }

        if (loginButton != null)
        {
            loginButton.onClick.AddListener(OnLoginClicked);
        }

        if (errorText != null)
        {
            errorText.text = "";
        }

        CustomAuthenticator.OnClientAuthFailed += HandleAuthFailed;
        
        // Optionally listen for successful connection to hide the UI
        InstanceFinder.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
    }

    private void OnDestroy()
    {
        CustomAuthenticator.OnClientAuthFailed -= HandleAuthFailed;
        if (InstanceFinder.NetworkManager != null)
        {
            InstanceFinder.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
        }
    }

    private void ClientManager_OnClientConnectionState(FishNet.Transporting.ClientConnectionStateArgs args)
    {
        // If we disconnect, show the login screen again
        if (args.ConnectionState == FishNet.Transporting.LocalConnectionState.Stopped)
        {
            gameObject.SetActive(true);
            if (loginButton != null) loginButton.interactable = true;
        }
        // If we connect and authenticate, hide this UI
        else if (args.ConnectionState == FishNet.Transporting.LocalConnectionState.Started)
        {
            // We hide it here or wait for the Authenticator's AuthResultBroadcast
            // Actually, we'll let a successful authentication hide it, or just hide it on Started.
            // Let's hide it when started. If auth fails, it will stop connection and show again.
            StartCoroutine(HideAfterDelay());
        }
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(0.5f);
        // Make sure we are still connected before hiding
        if (!InstanceFinder.ClientManager.Connection.IsActive) yield break;
        
        gameObject.SetActive(false);
    }

    private void HandleAuthFailed(string error)
    {
        if (errorText != null)
        {
            errorText.text = error;
        }
        if (loginButton != null)
        {
            loginButton.interactable = true;
        }
    }

    private void OnLoginClicked()
    {
        if (idInput == null || string.IsNullOrWhiteSpace(idInput.text))
        {
            if (errorText != null) errorText.text = "Please enter a valid ID.";
            return;
        }

        if (loginButton != null)
        {
            loginButton.interactable = false;
        }
        if (errorText != null)
        {
            errorText.text = "Connecting...";
        }

        // Set the ID for the authenticator to use
        CustomAuthenticator.PendingLoginID = idInput.text.Trim();

        // Start the client connection
        // Note: For a real game you might read the IP from another input field. 
        // Here we just use the default configured in the NetworkManager/Transport.
        InstanceFinder.ClientManager.StartConnection();
    }
}
