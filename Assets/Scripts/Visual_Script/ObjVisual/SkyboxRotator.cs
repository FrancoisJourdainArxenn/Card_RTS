using UnityEngine;

public class SkyboxRotator : MonoBehaviour
{
    [SerializeField] private float rotationSpeed = 1f; // degrees per second

    private float currentRotation = 0f;

    private void Update()
    {
        currentRotation += rotationSpeed * Time.deltaTime;
        currentRotation %= 360f;
        RenderSettings.skybox.SetFloat("_Rotation", currentRotation);
    }
}
