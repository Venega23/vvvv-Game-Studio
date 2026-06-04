using UnityEngine;
using System.Collections;

public class SimpleTeleport : MonoBehaviour
{
    [Header("Settings")]
    public Transform targetLocation;
    public string interactionMessage = "Press E to Enter";
    
    public void Interact(GameObject player)
    {
        if (targetLocation == null)
        {
            Debug.LogWarning("Teleport target not set!");
            return;
        }
        StartCoroutine(DoTeleport(player));
    }

    private IEnumerator DoTeleport(GameObject player)
    {
        // 1. Fade Out
        if (ScreenFader.Instance != null) yield return ScreenFader.Instance.FadeOut();

        // 2. Move Player
        // If player has CharacterController, we must disable it briefly
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        
        player.transform.position = targetLocation.position;
        player.transform.rotation = targetLocation.rotation;
        
        if (cc != null) cc.enabled = true;

        // 3. Wait a tiny bit for physics to settle
        yield return new WaitForSeconds(0.1f);

        // 4. Fade In
        if (ScreenFader.Instance != null) yield return ScreenFader.Instance.FadeIn();
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.5f);
        if (targetLocation != null)
        {
            Gizmos.DrawLine(transform.position, targetLocation.position);
            Gizmos.DrawSphere(targetLocation.position, 0.2f);
        }
    }
}
