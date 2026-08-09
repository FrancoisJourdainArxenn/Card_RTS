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
    float moveDuration = GlobalSettings.Instance != null ? GlobalSettings.Instance.AttackMoveDuration : 0.4f;
    float postDelay    = GlobalSettings.Instance != null ? GlobalSettings.Instance.AttackPostDelay : 0.8f;

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
        float windupDur = GlobalSettings.Instance != null ? GlobalSettings.Instance.AttackWindupDuration : 0.15f;
        float windupBack   = GlobalSettings.Instance != null ? GlobalSettings.Instance.AttackWindupBack   : 0.3f;
        float windupHeight = GlobalSettings.Instance != null ? GlobalSettings.Instance.AttackWindupHeight : 0.35f;
        float projectileSpeed      = GlobalSettings.Instance != null ? GlobalSettings.Instance.ProjectileSpeed       : 18f;
        float projectileMinDur     = GlobalSettings.Instance != null ? GlobalSettings.Instance.ProjectileMinDuration : 0.12f;
        GameObject projectilePrefab = GlobalSettings.Instance != null ? GlobalSettings.Instance.RangedProjectilePrefab : null;

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

        bool moveDone = false;
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
            float leadTime = GlobalSettings.Instance != null ? GlobalSettings.Instance.CameraShakeAnticipation : 0.05f;
            float shakeTime = Mathf.Max(0f, windupDur + moveDur - leadTime);
            attackSeq.InsertCallback(shakeTime, ShakeCamera);

            attackSeq.Append(transform.DOMove(originalPosition, moveDur).SetEase(Ease.OutSine));
        }

        attackSeq
            .OnComplete((TweenCallback)(() =>
            {
                moveDone = true;
                try
                {
                    if (this == null)
                    {
                        Command.CommandExecutionComplete();
                        return;
                    }

                    if (!isRanged)
                    {
                        // Melee : les impacts (cible principale + secondaires) sont révélés une fois
                        // l'attaquant revenu à sa place, comme c'était déjà le cas pour la cible principale.
                        ApplyMainImpact();
                        foreach (AttackHitResult hit in hits)
                            ApplyHitFeedback(hit.TargetUniqueID, hit.Damage, hit.HealthAfter);
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
            .OnKill(() => { if (!moveDone) Command.CommandExecutionComplete(); });
    }

}
