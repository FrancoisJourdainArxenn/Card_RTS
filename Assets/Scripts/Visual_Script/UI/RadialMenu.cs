using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using Unity.VisualScripting;
using System;

public class RadialMenu : MonoBehaviour
{
    [SerializeField]
    GameObject EntryPrefab;

    [SerializeField]
    protected float Radius = 150f;

    [SerializeField]
    protected List<Texture> Icons;

    [SerializeField]
    protected float openingTime = .2f;

    [SerializeField]
    protected float openingDelay = .15f;

    [SerializeField]
    protected float closingTime = .4f;

    List<RadialMenuEntry> Entries;

    bool opened = false;
    bool _openedByHold = false;
    protected virtual bool ZoomedOnly => false;
    protected CameraController cameraController;

    void Start()
    {
        Entries = new List<RadialMenuEntry>();
        cameraController = FindFirstObjectByType<CameraController>();
    }

    protected void AddEntry(int index, string label, Texture icon, float angleDeg, RadialMenuEntry.RadialMenuEntryDelegate callback)
    {
        GameObject entry = Instantiate(EntryPrefab, transform);
        RadialMenuEntry rme = entry.GetComponent<RadialMenuEntry>();
        rme.SetIndex(index);
        rme.SetLabel(label);
        rme.SetIcon(icon);
        rme.SetAngle(angleDeg);
        rme.SetCallback(callback);
        Entries.Add(rme);
    }

    public virtual void AddEntries()
    {
        for(int i=0; i<Icons.Count; i++)
        {
            AddEntry(
                i,
                "Button " + i,
                Icons[i],
                Mathf.PI * 2 * i / Icons.Count,
                entry => {OnOpen(entry); Close(i);}
            );
        }
    }
    
    public virtual void Open()
    {
        
        AddEntries();
        Rearrange();
        opened = true;
    }

    public void OnOpen(RadialMenuEntry entry)
    {
        Debug.Log("entry clicked : " + entry.GetIndex());
    }

    public void Close(int? entryIndex = null)
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            RectTransform rect = Entries[i].GetComponent<RectTransform>();
            GameObject entry = Entries[i].gameObject;
            
            if (i == entryIndex)
            {   
                rect.DOAnchorPos(Vector3.zero, closingTime * 0.75f).SetEase(Ease.OutQuad);
                rect.DOScale(Vector3.zero, closingTime * 1.25f).SetEase(Ease.InSine).onComplete = 
                    delegate()
                    {
                        Destroy(entry);
                    };
            }
            else
            {
                rect.DOScale(Vector3.zero, closingTime);
                rect.DOAnchorPos(Vector3.zero, closingTime).SetEase(Ease.InBack).onComplete = 
                    delegate()
                    {
                        Destroy(entry);
                    };
            }
            
        }
        Entries.Clear();
        opened = false;
    }
    
    public void Toggle()
    {
        if (opened == false)
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
        for (int i = 0; i < Entries.Count; i++)
        {
            float angleRad = Entries[i].GetAngle() * Mathf.Deg2Rad;
            float x = Mathf.Cos(angleRad) * Radius;
            float y = Mathf.Sin(angleRad) * Radius;

            RectTransform rect = Entries[i].GetComponent<RectTransform>();
            Entries[i].SetIconRotation(Entries[i].GetAngle());
            rect.localScale = Vector3.zero;
            rect.DOScale(Vector3.one, openingTime).SetEase(Ease.OutQuad).SetDelay(openingDelay * i / Entries.Count);
            rect.DOAnchorPos(new Vector3(x, y, 0), openingTime).SetEase(Ease.OutQuad).SetDelay(openingDelay * i / Entries.Count);
        }
    }

    void Update()
    {
        if (opened && ZoomedOnly && !cameraController.IsZoomedIn)
        {
            Close();
            return;
        }

        if (Input.GetMouseButtonDown(1) && (!ZoomedOnly || cameraController.IsZoomedIn))
        {
            if (!opened)
            {
                transform.position = Input.mousePosition;
                _openedByHold = true;
            }
            Toggle();
        }

        if (Input.GetMouseButtonUp(1) && _openedByHold)
        {
            _openedByHold = false;
            if (RadialMenuEntry.Hovered != null)
                RadialMenuEntry.Hovered.Invoke();
        }
    }
}
