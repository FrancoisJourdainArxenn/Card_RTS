using System.Collections.Generic;

/// <summary>
/// Enregistre l'intégralité du drain côté serveur comme une séquence ordonnée d'événements :
/// morts (DeathEvent) et animations de dégâts (AnimEvent). Le client reçoit cette séquence
/// via BroadcastDeathDrainClientRpc et la rejoue dans le même ordre, sans randomness.
/// </summary>
public static class DeathDrainRecorder
{
    public enum EventType { Death, Anim }

    public struct DrainEvent
    {
        public EventType Type;
        public int CreatureID;   // Death : créature morte
        public int SourceID;     // Anim  : entité source réelle (toujours renseigné, même si custom VFX)
        public int EffectIndex;  // Anim  : index de l'effet dans la liste du source (-1 si inconnu)
        public int TargetID;     // Anim  : entité cible
        public int Damage;       // Anim  : dégâts infligés
        public int HealthAfter;  // Anim  : HP cible après absorption
    }

    private static readonly List<DrainEvent> _events = new();
    public static bool IsRecording { get; private set; }

    public static void Begin()
    {
        IsRecording = true;
        _events.Clear();
    }

    public static List<DrainEvent> End()
    {
        IsRecording = false;
        return new List<DrainEvent>(_events);
    }

    public static void RecordDeath(int creatureID)
    {
        if (IsRecording)
            _events.Add(new DrainEvent { Type = EventType.Death, CreatureID = creatureID });
    }

    public static void RecordAnim(int sourceID, int targetID, int damage, int healthAfter, int effectIndex = -1)
    {
        if (IsRecording)
            _events.Add(new DrainEvent
            {
                Type        = EventType.Anim,
                SourceID    = sourceID,
                EffectIndex = effectIndex,
                TargetID    = targetID,
                Damage      = damage,
                HealthAfter = healthAfter
            });
    }
}
