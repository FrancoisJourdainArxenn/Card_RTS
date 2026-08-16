using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;

enum AttackTargetType
{
    Player,
    Base,
    Creature,
    Building,
    Unknown
}

public class CreatureAttackVisual : MonoBehaviour
{
    private OneCreatureManager manager;
    private WhereIsTheCardOrCreature w;
    float moveDuration = VisualManager.Instance != null ? VisualManager.Instance.AttackMoveDuration : 0.4f;
    float postDelay    = VisualManager.Instance != null ? VisualManager.Instance.AttackPostDelay : 0.8f;

    // Nombre d'attaques actuellement "en vol" (windup → impact → retour), tous attaquants confondus.
    // Tant qu'au moins une attaque est en vol, TableVisual ne doit pas repositionner ses rangées :
    // la charge/le projectile vise une position figée au moment de la construction de la séquence,
    // un relayout pendant ce laps de temps la ferait viser un slot vide ou une autre créature.
    private static int _attacksInFlight = 0;
    public static bool AnyAttackInFlight => _attacksInFlight > 0;
    public static event System.Action OnAllAttacksResolved;

    private static void BeginFlight() => _attacksInFlight++;

    private static void EndFlight()
    {
        _attacksInFlight = Mathf.Max(0, _attacksInFlight - 1);
        if (_attacksInFlight == 0)
            OnAllAttacksResolved?.Invoke();
    }

    // À appeler quand une partie se termine anormalement (reload de scène) : sans ça, une attaque
    // interrompue en plein vol laisserait le compteur bloqué > 0, gelant les relayouts de toutes
    // les tables de la partie suivante jusqu'au relancement de l'appli.
    public static void ResetFlightCounter() => _attacksInFlight = 0;

    void Awake()
    {
        manager = GetComponent<OneCreatureManager>();
        w = GetComponent<WhereIsTheCardOrCreature>();
    }

    private AttackTargetType GetTargetType(int targetUniqueID)
    {
        if (
            targetUniqueID == GlobalSettings.Instance.LowPlayer.PlayerID
            || targetUniqueID == GlobalSettings.Instance.TopPlayer.PlayerID
        )
        {
            return AttackTargetType.Player;
        }
        else if (BaseLogic.BasesCreatedThisGame.ContainsKey(targetUniqueID) &&
                 BaseLogic.BasesCreatedThisGame[targetUniqueID] != null)
        {
            return AttackTargetType.Base;
        }
        else if (CreatureLogic.CreaturesCreatedThisGame.ContainsKey(targetUniqueID) &&
                 CreatureLogic.CreaturesCreatedThisGame[targetUniqueID] != null)
        {
            return AttackTargetType.Creature;
        }
        else if (BuildingLogic.BuildingsCreatedThisGame.ContainsKey(targetUniqueID) &&
                 BuildingLogic.BuildingsCreatedThisGame[targetUniqueID] != null)
        {
            return AttackTargetType.Building;
        }
        return AttackTargetType.Unknown;
    }

    // secondaryHits : cibles touchées par les modificateurs d'attaque de l'attaquant (Cone/Piercing/...),
    // dégâts déjà appliqués en logique — jouées dans la même séquence (même windup, même retour) que la cible principale.
    public void AttackTarget(int targetUniqueID, int damageTakenByTarget, int damageTakenByAttacker, int attackerHealthAfter, int targetHealthAfter, float speedMultiplier = 1f, List<AttackHitResult> secondaryHits = null)
    {
        Debug.Log($"[AttackVisual] {gameObject.name} → cible ID:{targetUniqueID} | dégâts cible:{damageTakenByTarget} dégâts attaquant:{damageTakenByAttacker}");
        // L'attaquant peut avoir été détruit entre l'enregistrement et l'exécution de la commande
        if (this == null)
        {
            Debug.LogWarning("[AttackVisual] attaquant détruit avant exécution — CommandExecutionComplete forcé");
            Command.CommandExecutionComplete();
            return;
        }

        GameObject target = IDHolder.GetGameObjectWithID(targetUniqueID);
        if (target == null)
        {
            Debug.LogWarning($"[AttackVisual] Cible ID:{targetUniqueID} introuvable (IDHolder) — CommandExecutionComplete forcé");
            manager.HealthText.text = attackerHealthAfter.ToString();
            Command.CommandExecutionComplete();
            return;
        }

        List<AttackHitResult> hits = secondaryHits ?? new List<AttackHitResult>();

        AttackTargetType targetType = GetTargetType(targetUniqueID);
        float moveDur  = moveDuration / speedMultiplier;
        float postDel  = postDelay    / speedMultiplier;
        float windupDur = VisualManager.Instance != null ? VisualManager.Instance.AttackWindupDuration : 0.15f;
        float windupBack   = VisualManager.Instance != null ? VisualManager.Instance.AttackWindupBack   : 0.3f;
        float windupHeight = VisualManager.Instance != null ? VisualManager.Instance.AttackWindupHeight : 0.35f;
        float projectileSpeed      = VisualManager.Instance != null ? VisualManager.Instance.ProjectileSpeed       : 18f;
        float projectileMinDur     = VisualManager.Instance != null ? VisualManager.Instance.ProjectileMinDuration : 0.12f;
        GameObject projectilePrefab = GlobalSettings.Instance != null ? GlobalSettings.Instance.RangedProjectilePrefab : null;
        GameObject meleeMultiTargetVfxPrefab = GlobalSettings.Instance != null ? GlobalSettings.Instance.MeleeMultiTargetVfxPrefab : null;
        float meleeMultiTargetVfxLifetime = VisualManager.Instance != null ? VisualManager.Instance.MeleeMultiTargetVfxLifetime : 1.5f;

        // Durée de vol basée sur la distance réelle (vitesse constante), pas une durée fixe : sinon un
        // attaquant rapide (AttackSpeedMultiplier élevé) ou une cible éloignée finissait avec un vol trop
        // court pour être visible. Le plancher garantit un minimum de temps de vol dans tous les cas.
        float ProjectileDurationFor(Vector3 origin, Vector3 destination)
        {
            float distance = Vector3.Distance(origin, destination);
            float baseDur = projectileSpeed > 0.01f ? distance / projectileSpeed : projectileMinDur;
            return Mathf.Max(projectileMinDur, baseDur / speedMultiplier);
        }

        IDHolder selfID = GetComponent<IDHolder>();
        bool isRanged = selfID != null
            && CreatureLogic.CreaturesCreatedThisGame.TryGetValue(selfID.UniqueID, out CreatureLogic selfLogic)
            && selfLogic.IsRanged;

        // bring this creature to front sorting-wise.
        w.BringToFront();
        VisualStates tempState = w.VisualState;
        w.VisualState = VisualStates.Transition;
        Vector3 originalPosition = transform.position;

        Vector3 flatDir = target.transform.position - originalPosition;
        flatDir.y = 0f;
        flatDir = flatDir.sqrMagnitude > 0.0001f ? flatDir.normalized : transform.forward;
        Vector3 windupPosition = originalPosition - flatDir * windupBack + Vector3.up * windupHeight;

        void ShakeCamera()
        {
            if (damageTakenByTarget > 0 && selfID != null)
                CameraController.Instance?.ShakeForAttackerID(selfID.UniqueID);
        }

        // Popup de dégâts + rafraîchissement du texte de vie pour une cible touchée.
        void ApplyHitFeedback(int hitTargetID, int damage, int healthAfter)
        {
            GameObject hitTarget = IDHolder.GetGameObjectWithID(hitTargetID);
            if (hitTarget == null) return;
            if (damage > 0)
                VisualFeedbackEffect.CreateDamageEffect(hitTarget.transform.position, damage);

            switch (GetTargetType(hitTargetID))
            {
                case AttackTargetType.Player:
                    hitTarget.GetComponent<MainBaseVisual>().HealthText.text = healthAfter.ToString();
                    GlobalSettings.Instance.UiPlayerVisual.RefreshUI();
                    break;
                case AttackTargetType.Base:
                    hitTarget.GetComponent<OneBaseManager>().HealthText.text = healthAfter.ToString();
                    break;
                case AttackTargetType.Creature:
                    hitTarget.GetComponent<OneCreatureManager>().HealthText.text = healthAfter.ToString();
                    break;
                case AttackTargetType.Building:
                    hitTarget.GetComponent<OneBuildingManager>().HealthText.text = healthAfter.ToString();
                    break;
                case AttackTargetType.Unknown:
                    Debug.Log("Unknown target type: " + hitTargetID);
                    break;
            }
        }

        // Impact de la cible principale : popup cible + popup contre-attaque sur l'attaquant + texte de vie attaquant.
        void ApplyMainImpact()
        {
            if (this == null) return;
            ApplyHitFeedback(targetUniqueID, damageTakenByTarget, targetHealthAfter);
            if (damageTakenByAttacker > 0)
                VisualFeedbackEffect.CreateDamageEffect(transform.position, damageTakenByAttacker);
            manager.HealthText.text = attackerHealthAfter.ToString();
        }

        // Instancie le prefab de projectile (Vfx_Projectile) et l'anime en ligne droite vers la cible ;
        // sans prefab configuré ou cible déjà introuvable, applique l'impact immédiatement.
        // La durée est calculée par l'appelant (avant d'être planifiée dans la séquence DOTween) et
        // simplement rejouée ici, pour que le timing du tween et celui du projectile restent identiques.
        void FireProjectileAt(Vector3 origin, GameObject hitTarget, float duration, System.Action onImpact)
        {
            if (projectilePrefab != null && hitTarget != null)
            {
                GameObject proj = Instantiate(projectilePrefab, origin, Quaternion.identity);
                RangedProjectile projScript = proj.AddComponent<RangedProjectile>();
                projScript.Play(origin, hitTarget.transform.position, duration, onImpact);
            }
            else
            {
                onImpact?.Invoke();
            }
        }

        // Melee avec modificateur d'attaque (Cone/Piercing/...) uniquement : instancie le Melee VFX depuis la
        // position actuelle de l'attaquant (au contact de sa cible principale, juste après la charge) vers
        // chaque cible secondaire, une instance par cible orientée vers elle. Contrairement au projectile
        // ranged, cet effet n'a pas besoin d'être piloté par code — il se joue et se détruit tout seul.
        void FireMeleeVfxTo(GameObject hitTarget, AttackHitResult hit)
        {
            if (meleeMultiTargetVfxPrefab != null && hitTarget != null)
            {
                Vector3 origin = transform.position;
                Vector3 dir = hitTarget.transform.position - origin;
                Quaternion rot = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir) : Quaternion.identity;
                GameObject vfx = Instantiate(meleeMultiTargetVfxPrefab, origin, rot);
                Destroy(vfx, meleeMultiTargetVfxLifetime);
            }
            ApplyHitFeedback(hit.TargetUniqueID, hit.Damage, hit.HealthAfter);
        }

        bool moveDone = false;
        BeginFlight();
        Sequence attackSeq = DOTween.Sequence();
        attackSeq.SetLink(gameObject);
        attackSeq.Append(transform.DOMove(windupPosition, windupDur).SetEase(Ease.OutSine));

        if (isRanged)
        {
            // Un seul windup : l'unité reste en position et tire un projectile sur la cible principale
            // puis, sans revenir entre chaque tir, sur chaque cible secondaire (Cone/Piercing/...).
            // Le retour à la position d'origine n'a lieu qu'à la toute fin, une fois tous les tirs résolus.
            float mainProjectileDur = ProjectileDurationFor(windupPosition, target.transform.position);
            attackSeq.AppendCallback(() =>
                FireProjectileAt(windupPosition, target, mainProjectileDur, () => { ApplyMainImpact(); ShakeCamera(); }));
            attackSeq.AppendInterval(mainProjectileDur);

            foreach (AttackHitResult hit in hits)
            {
                AttackHitResult capturedHit = hit;
                GameObject hitTargetGO = IDHolder.GetGameObjectWithID(capturedHit.TargetUniqueID);
                float hitProjectileDur = hitTargetGO != null ? ProjectileDurationFor(windupPosition, hitTargetGO.transform.position) : projectileMinDur;
                attackSeq.AppendCallback(() =>
                {
                    GameObject liveHitTarget = IDHolder.GetGameObjectWithID(capturedHit.TargetUniqueID);
                    FireProjectileAt(windupPosition, liveHitTarget, hitProjectileDur, () => ApplyHitFeedback(capturedHit.TargetUniqueID, capturedHit.Damage, capturedHit.HealthAfter));
                });
                attackSeq.AppendInterval(hitProjectileDur);
            }

            attackSeq.Append(transform.DOMove(originalPosition, moveDur).SetEase(Ease.OutSine));
        }
        else
        {
            attackSeq.Append(transform.DOMove(target.transform.position, moveDur).SetEase(Ease.InQuad));

            // Shake fires a little before the creature actually reaches the target (impact = windupDur + moveDur),
            // not on the sequence's OnComplete (which only fires after the return move too) — otherwise it lands
            // visibly late. Only the attacker's own hit shakes the camera, never the defender's counter-damage.
            float leadTime = VisualManager.Instance != null ? VisualManager.Instance.CameraShakeAnticipation : 0.05f;
            float shakeTime = Mathf.Max(0f, windupDur + moveDur - leadTime);
            attackSeq.InsertCallback(shakeTime, ShakeCamera);

            // Attaque melee classique (sans modificateur) : comportement strictement inchangé.
            // Avec modificateur : une fois la charge terminée (attaquant au contact de sa cible principale),
            // le Melee VFX part vers chaque cible secondaire avant le retour à la position d'origine.
            if (hits.Count > 0)
            {
                attackSeq.AppendCallback(() =>
                {
                    foreach (AttackHitResult hit in hits)
                    {
                        GameObject hitTargetGO = IDHolder.GetGameObjectWithID(hit.TargetUniqueID);
                        FireMeleeVfxTo(hitTargetGO, hit);
                    }
                });
            }

            attackSeq.Append(transform.DOMove(originalPosition, moveDur).SetEase(Ease.OutSine));
        }

        attackSeq
            .OnComplete((TweenCallback)(() =>
            {
                moveDone = true;
                EndFlight();
                try
                {
                    if (this == null)
                    {
                        Command.CommandExecutionComplete();
                        return;
                    }

                    if (!isRanged)
                    {
                        // Melee : l'impact de la cible principale est révélé une fois l'attaquant revenu à sa
                        // place, comme avant (comportement inchangé). Les cibles secondaires, elles, ont déjà
                        // été révélées plus tôt, au moment où le Melee VFX est parti vers elles.
                        ApplyMainImpact();
                    }

                    w.SetTableSortingOrder();
                    w.VisualState = tempState;

                    bool seqDone = false;
                    Sequence s = DOTween.Sequence();
                    s.AppendInterval(postDel);
                    s.SetLink(gameObject);
                    s.OnComplete(() => { seqDone = true; Command.CommandExecutionComplete(); });
                    s.OnKill(() => { if (!seqDone) Command.CommandExecutionComplete(); });
                }
                catch (System.Exception e)
                {
                    // Si cette exception se produit, la file de commandes (Command.CommandQueue) se bloquerait
                    // silencieusement pour toujours sans ce filet — d'où le log explicite ici.
                    Debug.LogError($"[AttackVisual] EXCEPTION pendant l'animation de {gameObject.name} → cible ID:{targetUniqueID} (type={targetType}) — file débloquée de force: {e}");
                    Command.CommandExecutionComplete();
                }
            }))
            .OnKill(() =>
            {
                if (!moveDone)
                {
                    EndFlight();
                    Command.CommandExecutionComplete();
                }
            });
    }

}
