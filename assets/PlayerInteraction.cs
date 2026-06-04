using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteraction : MonoBehaviour
{
    public float interactionDistance = 3f;
    public Key interactKey = Key.E;
    
    private GUIStyle labelStyle;
    private SimpleTeleport currentTeleport;

    private void Awake()
    {
        labelStyle = new GUIStyle();
        labelStyle.fontSize = 22;
        labelStyle.alignment = TextAnchor.MiddleCenter;
        labelStyle.normal.textColor = Color.white;
    }

    private void Update()
    {
        currentTeleport = null;
        
        // Raycast from camera center
        Ray ray = Camera.main.ScreenPointToRay(new Vector2(Screen.width / 2, Screen.height / 2));
        if (Physics.Raycast(ray, out RaycastHit hit, interactionDistance))
        {
            var tp = hit.collider.GetComponent<SimpleTeleport>();
            if (tp != null)
            {
                currentTeleport = tp;
                
                if (Keyboard.current != null && Keyboard.current[interactKey].wasPressedThisFrame)
                {
                    tp.Interact(gameObject);
                }
            }
        }
    }

    private void OnGUI()
    {
        if (currentTeleport != null)
        {
            float x = Screen.width / 2;
            float y = Screen.height / 2 + 50;
            GUI.Label(new Rect(x - 150, y, 300, 40), currentTeleport.interactionMessage, labelStyle);
        }
    }
}
