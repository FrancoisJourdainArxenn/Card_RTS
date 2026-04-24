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
    
    RectTransform Rect;
    RadialMenuEntryDelegate Callback;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Rect = Icon.GetComponent<RectTransform>();
    }
    public void SetLabel(string text)
    {
        label.text = text;
    }
    
    public void SetIcon(Texture _icon)
    {
        Icon.texture = _icon;
    }

    public Texture GetIcon()
    {
        return Icon.texture;
    }

    public string GetLabel()
    {
        return label.text;
    }

    public void SetCallback(RadialMenuEntryDelegate callback)
    {
        Callback = callback;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Callback?.Invoke(this);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        Rect.DOComplete();
        Rect.DOScale(Vector3.one * 1.5f, .3f).SetEase(Ease.OutQuad);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        Rect.DOComplete();
        Rect.DOScale(Vector3.one, .3f).SetEase(Ease.OutQuad);
    }
}
