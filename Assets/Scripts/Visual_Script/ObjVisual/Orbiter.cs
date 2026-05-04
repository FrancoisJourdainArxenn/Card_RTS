using UnityEngine;

public class Orbiter : MonoBehaviour
{
    [SerializeField] private Transform center;
    [SerializeField] private float speed = 10f;   // degrés par seconde
    [SerializeField] private Vector3 axis = Vector3.up;
    [SerializeField] private bool lookAtCenter = false;

    private void Update()
    {
        transform.RotateAround(center.position, axis, speed * Time.deltaTime);

        if (lookAtCenter)
            transform.LookAt(center);
    }
}
