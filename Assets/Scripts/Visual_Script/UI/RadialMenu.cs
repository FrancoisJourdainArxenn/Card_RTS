using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class RadialMenu : MonoBehaviour
{
    [SerializeField]
    GameObject EntryPrefab;

    [SerializeField]
    float Radius = 200f;

    [SerializeField]
    List<Texture> Icons;

    List<RadialMenuEntry> Entries;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Entries = new List<RadialMenuEntry>();
    }

    void AddEntry(string label, Texture icon, RadialMenuEntry.RadialMenuEntryDelegate callback)
    {
        GameObject entry = Instantiate(EntryPrefab, transform);
        RadialMenuEntry rme = entry.GetComponent<RadialMenuEntry>();
        rme.SetLabel(label);
        rme.SetIcon(icon);
        rme.SetCallback(callback);
        Entries.Add(rme);
    }

    public void Open()
    {
        for (int i = 0; i < 5; i++)
        {
            AddEntry("Button " + i.ToString(), Icons[i], DebugMenuEntry);
        }
        Rearrange();
    }

    public void Close()
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            RectTransform rect = Entries[i].GetComponent<RectTransform>();
            GameObject entry = Entries[i].gameObject;
            
            rect.DOScale(Vector3.zero, .6f);
            rect.DOAnchorPos(Vector3.zero, .6f).SetEase(Ease.InBack).onComplete = 
                delegate()
                {
                    Destroy(entry);
                };
        }
        Entries.Clear();
    }
    
    public void Toggle()
    {
        if (Entries.Count == 0)
        {
            Open();
        }
        else
        {
            Close();
        }
    }

    void Rearrange()
    {
        float radiansOfSeparation = Mathf.PI * 2 / Entries.Count;
        for (int i = 0; i < Entries.Count; i++)
        {
            float x = Mathf.Cos(Mathf.PI / 2 + radiansOfSeparation * -i) * Radius;
            float y = Mathf.Sin(Mathf.PI / 2 + radiansOfSeparation * -i) * Radius;

            RectTransform rect = Entries[i].GetComponent<RectTransform>();
            rect.localScale = Vector3.zero;
            rect.DOScale(Vector3.one, .3f).SetEase(Ease.OutQuad).SetDelay(.05f * i);
            rect.DOAnchorPos(new Vector3(x, y, 0), .3f).SetEase(Ease.OutQuad).SetDelay(.05f * i);
        }
    }

    void DebugMenuEntry(RadialMenuEntry entry)
    {
        Debug.Log("Clicked " + entry.GetLabel());
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
