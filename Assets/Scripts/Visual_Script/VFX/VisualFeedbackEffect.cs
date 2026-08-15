using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using TMPro;

public class VisualFeedbackEffect : MonoBehaviour
{
    private static VisualFeedbackEffect _manager;

    [Header("Manager")]
    [SerializeField] private GameObject damagePrefab;
    [SerializeField] private GameObject textPrefab;

    [Header("Effect")]
    [SerializeField] private Sprite[] splashes;
    [SerializeField] private Image damageImage;
    [SerializeField] private CanvasGroup cg;
    [SerializeField] private TMP_Text amountText;

    void Awake()
    {
        if (damagePrefab != null)
            _manager = this;
        else if (damageImage != null && splashes != null && splashes.Length > 0)
            damageImage.sprite = splashes[Random.Range(0, splashes.Length)];
    }

    public static void CreateDamageEffect(Vector3 position, int amount)
    {
        if (amount == 0 || _manager == null) return;
        var go = Instantiate(_manager.damagePrefab, position, Quaternion.Euler(90, 0, 0));
        var de = go.GetComponent<VisualFeedbackEffect>();
        de.amountText.text = "-" + amount;
        ValuePopAnimation.Pop(de.transform);
        de.StartCoroutine(de.ShowFeedbackEffect());
    }

    public static void CreateTextEffect(Vector3 position, int amount, Color color, string prefix = "+")
    {
        if (amount == 0 || _manager == null) return;
        var go = Instantiate(_manager.textPrefab, position, Quaternion.Euler(90, 0, 0));
        var de = go.GetComponent<VisualFeedbackEffect>();
        de.amountText.text = prefix + amount;
        de.amountText.color = color;
        de.StartCoroutine(de.ShowFeedbackEffect());
    }

    private IEnumerator ShowFeedbackEffect()
    {
        cg.alpha = 1f;
        yield return new WaitForSeconds(.5f);
        while (cg.alpha > 0)
        {
            cg.alpha -= 0.1f;
            yield return new WaitForSeconds(0.05f);
        }
        Destroy(gameObject);
    }
}
