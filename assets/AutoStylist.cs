using UnityEngine;

public class AutoStylist : MonoBehaviour
{
    private void Start()
    {
        // Магия ИИ: автоматически находим Plane и красим его как траву.
        GameObject plane = GameObject.Find("Plane");
        if (plane != null)
        {
            Renderer planeRenderer = plane.GetComponent<Renderer>();
            if (planeRenderer != null)
            {
                planeRenderer.material.color = Color.green;
            }
        }

        // Магия ИИ: делаем фон камеры ярко-голубым.
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = new Color(0.3f, 0.8f, 1f);
        }
    }
}
