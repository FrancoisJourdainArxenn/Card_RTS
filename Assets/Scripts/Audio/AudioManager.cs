using System.Collections;
using UnityEngine;
using FMODUnity;

public enum FireIntensity
{
    Light,
    Medium,
    Heavy
}

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("Cards")]
    [SerializeField] private EventReference drawCardSound;
    // Les pioches "fast" (main de départ, début de tour) s'enchaînent toutes les ~0.05s : sans
    // écart minimum, les sons se superposent et on n'entend qu'un seul bloc.
    [SerializeField, Range(0f, 0.5f)] private float drawCardSoundInterval = 0.12f;
    private float nextDrawCardSoundTime;

    [Header("Global")]
    [SerializeField] private EventReference upgradeTechSound;
    [SerializeField] private EventReference gameMusic;

    [Header("UI")]
    [SerializeField] private EventReference EndTurnButtonClickSound;

    [Header("Combat")]
    [SerializeField] private EventReference fireLightSound;
    [SerializeField] private EventReference fireMediumSound;
    [SerializeField] private EventReference fireHeavySound;
    [SerializeField] private EventReference punchMediumSound;
    [SerializeField] private EventReference punchHeavySound;
    [SerializeField] private EventReference unitHitSound;
    [SerializeField] private EventReference unitDeathSound;

    // Volumes par groupe, appliqués aux bus du mixer FMOD (les events doivent y être routés dans
    // FMOD Studio). Modifiables en Play mode depuis l'Inspector pour régler la balance à l'oreille.
    [Header("Mix")]
    [SerializeField] private string musicBusPath = "bus:/Music";
    [SerializeField] private string sfxBusPath = "bus:/SFX";
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.6f;
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;

    private FMOD.Studio.Bus musicBus;
    private FMOD.Studio.Bus sfxBus;

    // Contrairement aux one-shots, la musique a besoin d'une instance gardée en mémoire pour
    // pouvoir être arrêtée en fin de partie.
    private FMOD.Studio.EventInstance musicInstance;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        musicBus = GetBus(musicBusPath);
        sfxBus = GetBus(sfxBusPath);
        ApplyVolumes();
    }

    // Réapplique les volumes quand on bouge les curseurs dans l'Inspector pendant le Play mode.
    void OnValidate()
    {
        if (Application.isPlaying) ApplyVolumes();
    }

    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        ApplyVolumes();
    }

    public void SetSfxVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
        ApplyVolumes();
    }

    void ApplyVolumes()
    {
        if (musicBus.isValid()) musicBus.setVolume(musicVolume);
        if (sfxBus.isValid()) sfxBus.setVolume(sfxVolume);
    }

    // RuntimeManager.GetBus lève une exception si le bus n'existe pas — on passe par le
    // StudioSystem pour simplement prévenir sans casser le reste de l'audio.
    static FMOD.Studio.Bus GetBus(string path)
    {
        if (RuntimeManager.StudioSystem.getBus(path, out FMOD.Studio.Bus bus) != FMOD.RESULT.OK)
            Debug.LogWarning($"[AudioManager] Bus FMOD introuvable : {path}. Crée-le dans le Mixer de FMOD Studio puis rebuild les banks.");
        return bus;
    }

    void OnDestroy()
    {
        // Changement de scène (retour menu via pause, reload...) : sans ça la musique continuerait
        // de jouer dans le menu, l'instance FMOD n'étant pas liée au GameObject.
        StopMusic(immediate: true);
        if (Instance == this) Instance = null;
    }

    public void StartMusic()
    {
        if (gameMusic.IsNull) return;
        if (musicInstance.isValid()) return; // déjà lancée (évite un doublon si OnGameStart est rappelé)

        musicInstance = RuntimeManager.CreateInstance(gameMusic);
        musicInstance.start();
    }

    // immediate = false laisse FMOD appliquer le fade-out défini sur l'event (AHDSR / release).
    public void StopMusic(bool immediate = false)
    {
        if (!musicInstance.isValid()) return;

        musicInstance.stop(immediate ? FMOD.Studio.STOP_MODE.IMMEDIATE : FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        musicInstance.release();
        musicInstance.clearHandle();
    }

    // Si un son de pioche vient d'être joué, celui-ci est décalé au prochain créneau libre
    // plutôt que joué en même temps.
    public void PlayDrawCard()
    {
        float delay = Mathf.Max(0f, nextDrawCardSoundTime - Time.time);
        nextDrawCardSoundTime = Time.time + delay + drawCardSoundInterval;

        if (delay <= 0f)
        {
            Debug.Log($"[AudioManager] PlayDrawCard immédiat");
            RuntimeManager.PlayOneShot(drawCardSound);
        }
        else
            StartCoroutine(PlayDrawCardDelayed(delay));
    }

    IEnumerator PlayDrawCardDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        Debug.Log($"[AudioManager] PlayDrawCardDelayed");
        RuntimeManager.PlayOneShot(drawCardSound);
    }

    public void PlayUpgradeTech()
    {
        RuntimeManager.PlayOneShot(upgradeTechSound);
    }

    // 1-2 → Light, 3-5 → Medium, 6+ → Heavy. Rien en dessous de 1.
    public void PlayFireForAttack(int attack)
    {
        if (attack <= 0) return;
        if (attack <= 2) PlayFire(FireIntensity.Light);
        else if (attack <= 5) PlayFire(FireIntensity.Medium);
        else PlayFire(FireIntensity.Heavy);
    }

    public void PlayFire(FireIntensity intensity)
    {
        switch (intensity)
        {
            case FireIntensity.Light:
                RuntimeManager.PlayOneShot(fireLightSound);
                break;
            case FireIntensity.Medium:
                RuntimeManager.PlayOneShot(fireMediumSound);
                break;
            case FireIntensity.Heavy:
                RuntimeManager.PlayOneShot(fireHeavySound);
                break;
        }
    }
    public void PlayPunchForAttack(int attack)
    {
        if (attack <= 0) return;
        if (attack <= 3) PlayPunch(FireIntensity.Medium);
        else PlayPunch(FireIntensity.Heavy);
    }

    public void PlayPunch(FireIntensity intensity)
    {
        switch (intensity)
        {
            case FireIntensity.Medium:
                RuntimeManager.PlayOneShot(punchMediumSound);
                break;
            case FireIntensity.Heavy:
                RuntimeManager.PlayOneShot(punchHeavySound);
                break;
        }
    }

    public void PlayUnitHit()
    {
        RuntimeManager.PlayOneShot(unitHitSound);
    }

    public void PlayUnitDeath()
    {
        RuntimeManager.PlayOneShot(unitDeathSound);
    }

    public void PlayEndTurnButtonClick()
    {
        RuntimeManager.PlayOneShot(EndTurnButtonClickSound);
    }
}
