using UnityEngine;
using System;
using System.Collections;

// Ajouté au runtime (AddComponent) sur une instance du prefab de projectile.
// Trajectoire en ligne droite vers la cible ; l'impact déclenche le callback puis détruit l'objet.
public class RangedProjectile : MonoBehaviour
{
    public void Play(Vector3 start, Vector3 end, float duration, Action onImpact)
    {
        transform.position = start;
        Vector3 dir = end - start;
        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(dir);

        StartCoroutine(Animate(start, end, duration, onImpact));
    }

    private IEnumerator Animate(Vector3 start, Vector3 end, float duration, Action onImpact)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(start, end, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        transform.position = end;
        onImpact?.Invoke();
        Destroy(gameObject);
    }
}
