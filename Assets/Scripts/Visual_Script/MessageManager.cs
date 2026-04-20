using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using TMPro;

public class MessageManager : MonoBehaviour 
{
    public TMP_Text MessageText;
    public GameObject MessageHolder;

    public static MessageManager Instance;

    void Awake()
    {
        MessageHolder.SetActive(false);
        Instance = this;
    }

    public void ShowMessage(string Message, float Duration)
    {
        StartCoroutine(ShowMessageCoroutine(Message, Duration));
    }

    IEnumerator ShowMessageCoroutine(string Message, float Duration)
    {
        //Debug.Log("Showing some message. Duration: " + Duration);
        MessageHolder.SetActive(true);
        MessageText.text = Message;

        yield return new WaitForSeconds(Duration);
        MessageHolder.SetActive(false);

        Command.CommandExecutionComplete();
    }

    // TEST PURPOSES ONLY
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Y))
            ShowMessage("Your Turn", 3f);
        
        if (Input.GetKeyDown(KeyCode.E))
            ShowMessage("Enemy Turn", 3f);
    }
}
