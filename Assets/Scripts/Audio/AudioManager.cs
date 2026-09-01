using UnityEngine;
using FMODUnity;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("Cards")]
    [SerializeField] private EventReference drawCardSound;

    void Awake()
    {
        Instance = this;
    }

    public void PlayDrawCard()
    {
        RuntimeManager.PlayOneShot(drawCardSound);
    }
}
