using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;

public class RadialMenuEntry : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public delegate void RadialMenuEntryDelegate(RadialMenuEntry entry);
    
    [SerializeField]
    TextMeshProUGUI label;

    [SerializeField]
    RawImage Icon;
    
    int index;
    float _angleDeg;
    RectTransform Rect;
    RadialMenuEntryDelegate Callback;
    // Start is called once before the first execution of Update after the MonoBehaviour is create
    
    void Awake()
    {
        Rect = Icon.GetComponent<RectTransform>();
    }
    
    public void SetIndex(int entryIndex) => index = entryIndex;
    public int GetIndex() => index;
    
    public void SetLabel(string text) => label.text = text;
    public string GetLabel() => label.text;
    
    public void SetIcon(Texture _icon) => Icon.texture = _icon;
    public Texture GetIcon() => Icon.texture;

    public void SetAngle(float angleDeg) => _angleDeg = angleDeg;
    public float GetAngle() => _angleDeg;

    public void SetCallback(RadialMenuEntryDelegate callback) => Callback = callback;

    public void SetIconRotation(float angleDeg)
    {
        Rect.localEulerAngles = new Vector3(0, 0, angleDeg);
    }

    public static RadialMenuEntry Hovered { get; private set; }

    public void Invoke() => Callback?.Invoke(this);

    public void OnPointerClick(PointerEventData eventData)
    {
        Callback?.Invoke(this);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        Hovered = this;
        Rect.DOComplete();
        Rect.DOScale(Vector3.one * 1.5f, .3f).SetEase(Ease.OutQuad);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (Hovered == this) Hovered = null;
        Rect.DOComplete();
        Rect.DOScale(Vector3.one, .3f).SetEase(Ease.OutQuad);
    }
}
