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

    private static void BeginFlight()
    {
        _attacksInFlight++;
        // Debug.Log($"[DBG][Timing] BeginFlight — _attacksInFlight={_attacksInFlight} @ {Time.realtimeSinceStartup:F3}");
    }

    private static void EndFlight()
    {
        _attacksInFlight = Mathf.Max(0, _attacksInFlight - 1);
        // Debug.Log($"[DBG][Timing] EndFlight — _attacksInFlight={_attacksInFlight} @ {Time.realtimeSinceStartup:F3}");
        if (_attacksInFlight == 0)
            OnAllAttacksResolved?.Invoke();
    }

    // À appeler quand une partie se termine anormalement (reload de scène) : sans ça, une attaque
    // interrompue en plein vol laisserait le compteur bloqué > 0, gelant les relayouts de toutes
    // les tables de la partie suivante jusqu'au relancement de l'appli. _pausedMidAttack contiendrait
    // de même des références à des GameObjects de la partie précédente (détruits au reload), les
    // excluant à tort des repositionnements de la partie suivante.
    public static void ResetFlightCounter()
    {
        _attacksInFlight = 0;
        _pausedMidAttack.Clear();
    }

    void Awake()
    {
        manager = GetComponent<OneCreatureManager>();
        w = GetComponent<WhereIsTheCardOrCreature>();
    }

    // Pont entre PlayWindup et PlayResolve quand les deux sont joués comme deux Commands séparées
    // (voir CreatureAttackCommand.EnqueueAttack) : un seul attaquant ne peut avoir qu'une attaque
    // "en vol" à la fois (file de Commands globale strictement séquentielle), donc pas de risque
    // qu'une deuxième attaque écrase ces valeurs avant que PlayResolve ne les ait lues.
    private Vector3 _pendingOriginalPosition;
    private VisualStates _pendingTempState;

    // Créatures actuellement figées entre wind-up et résolution (voir PlayWindup/PlayResolve) : la
    // fenêtre AnyAttackInFlight ne les couvre plus pendant cet intervalle (sinon interblocage avec un
    // effet OnAttack qui a besoin d'un repositionnement de table pour se terminer — voir PlayWindup),
    // donc TableVisual.PlaceRowOnSlots peut vouloir les repositionner comme n'importe quelle autre
    // créature de la rangée. On les exclut explicitement du TWEEN de repositionnement (pas du calcul
    // de mise en page — elles comptent toujours dans l'effectif de la rangée) pour qu'elles restent
    // visuellement figées à leur position de recul jusqu'à ce que leur propre PlayResolve reprenne.
    private static readonly HashSet<GameObject> _pausedMidAttack = new HashSet<GameObject>();
    public static bool IsPausedMidAttack(GameObject creature) => _pausedMidAttack.Contains(creature);

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
    // Enchaînement direct wind-up → résolution, sans aucune pause entre les deux — utilisé par tout ce qui
    // n'est PAS le chemin auto-battle (CreatureLogic.GoFace/AttackCreature/AttackBase, inchangés). Le chemin
    // auto-battle (CreatureAttackCommand.EnqueueAttack) appelle PlayWindup/PlayResolve séparément, comme
    // deux Commands distinctes, pour pouvoir intercaler les commandes différées d'un effet OnAttack entre les deux.
    public void AttackTarget(int targetUniqueID, int damageTakenByTarget, int damageTakenByAttacker, int attackerHealthAfter, int targetHealthAfter, float speedMultiplier = 1f, List<AttackHitResult> secondaryHits = null)
    {
        PlayWindup(targetUniqueID, () =>
            PlayResolve(targetUniqueID, damageTakenByTarget, damageTakenByAttacker, attackerHealthAfter, targetHealthAfter, speedMultiplier, secondaryHits));
    }

    // Phase 1 : recul (wind-up) seul. onWindupComplete est appelé au lieu de Command.CommandExecutionComplete
    // directement, pour rester utilisable aussi bien comme maillon interne d'AttackTarget (enchaînement
    // immédiat) que comme corps d'une Command séparée (CreatureAttackWindupCommand, qui elle appelle
    // Command.CommandExecutionComplete depuis onWindupComplete).
    public void PlayWindup(int targetUniqueID, System.Action onWindupComplete)
    {
        // Debug.Log($"[AttackVisual] {gameObject.name} → wind-up vers cible ID:{targetUniqueID}");
        // L'attaquant peut avoir été détruit entre l'enregistrement et l'exécution de la commande
        if (this == null)
        {
            // Debug.LogWarning("[AttackVisual] attaquant détruit avant exécution — CommandExecutionComplete forcé");
            Command.CommandExecutionComplete();
            return;
        }

        GameObject target = IDHolder.GetGameObjectWithID(targetUniqueID);
        if (target == null)
        {
            // Debug.LogWarning($"[AttackVisual] Cible ID:{targetUniqueID} introuvable (IDHolder) — CommandExecutionComplete forcé");
            Command.CommandExecutionComplete();
            return;
        }

        float windupDur    = VisualManager.Instance != null ? VisualManager.Instance.AttackWindupDuration : 0.15f;
        float windupBack   = VisualManager.Instance != null ? VisualManager.Instance.AttackWindupBack   : 0.3f;
        float windupHeight = VisualManager.Instance != null ? VisualManager.Instance.AttackWindupHeight : 0.35f;

        // bring this creature to front sorting-wise.
        w.BringToFront();
        _pendingTempState = w.VisualState;
        w.VisualState = VisualStates.Transition;
        _pendingOriginalPosition = transform.position;

        Vector3 flatDir = target.transform.position - _pendingOriginalPosition;
        flatDir.y = 0f;
        flatDir = flatDir.sqrMagnitude > 0.0001f ? flatDir.normalized : transform.forward;
        Vector3 windupPosition = _pendingOriginalPosition - flatDir * windupBack + Vector3.up * windupHeight;

        // BeginFlight/EndFlight n'encadrent QUE le tween de recul lui-même, pas l'écart qui peut suivre
        // avant PlayResolve (commandes différées d'un effet OnAttack, voir CreatureAttackCommand.EnqueueAttack) :
        // si AnyAttackInFlight restait vrai pendant cet écart, une commande qui a besoin d'un
        // repositionnement de table pour se terminer (ex: TableVisual.AddCreatureAtIndex, appelée par
        // le spawn d'un token OnAttack) resterait bloquée indéfiniment — TableVisual.PlaceCreaturesOnNewSlots
        // ne fait alors que différer le repositionnement jusqu'à la fin du DERNIER vol en cours, qui est
        // PlayResolve lui-même, jamais encore joué puisqu'il attend que cette commande se termine
        // (interblocage observé : créature suspendue en l'air après avoir spawn son token OnAttack).
        // PlayResolve rouvre sa propre fenêtre de vol pour protéger la charge/le tir.
        bool windupDone = false;
        BeginFlight();
        Sequence windupSeq = DOTween.Sequence();
        windupSeq.SetLink(gameObject);
        windupSeq.Append(transform.DOMove(windupPosition, windupDur).SetEase(Ease.OutSine));
        windupSeq
            .OnComplete(() => { windupDone = true; EndFlight(); _pausedMidAttack.Add(gameObject); onWindupComplete(); })
            .OnKill(() =>
            {
                if (!windupDone)
                {
                    EndFlight();
                    Command.CommandExecutionComplete();
                }
            });
    }

    // Phase 2 : charge (mêlée) ou tir (ranged), impact, retour. Part de transform.position tel quel
    // (déjà au point de wind-up, PlayWindup vient de l'y amener) ; relit _pendingOriginalPosition/
    // _pendingTempState posés par PlayWindup pour le retour et la restauration du VisualState.
    public void PlayResolve(int targetUniqueID, int damageTakenByTarget, int damageTakenByAttacker, int attackerHealthAfter, int targetHealthAfter, float speedMultiplier = 1f, List<AttackHitResult> secondaryHits = null)
    {
        // // Debug.Log($"[AttackVisual] {gameObject.name} → résolution vers cible ID:{targetUniqueID} | dégâts cible:{damageTakenByTarget} dégâts attaquant:{damageTakenByAttacker}");
        // L'attaquant peut avoir été détruit entre le wind-up et la résolution (ex: par les commandes
        // différées d'un effet OnAttack intercalées entre les deux, voir CreatureAttackCommand.EnqueueAttack).
        // Pas d'EndFlight ici : le vol de PlayWindup s'est déjà refermé de son côté (voir PlayWindup),
        // et celui de PlayResolve n'a pas encore été ouvert à ce stade (BeginFlight plus bas).
        if (this == null)
        {
            // Debug.LogWarning("[AttackVisual] attaquant détruit avant exécution — CommandExecutionComplete forcé");
            Command.CommandExecutionComplete();
            return;
        }

        // Redevient repositionnable normalement dès que la résolution reprend (voir PlayWindup/_pausedMidAttack).
        _pausedMidAttack.Remove(gameObject);

        GameObject target = IDHolder.GetGameObjectWithID(targetUniqueID);
        if (target == null)
        {
            //  Debug.LogWarning($"[AttackVisual] Cible ID:{targetUniqueID} introuvable (IDHolder) — CommandExecutionComplete forcé");
            manager.HealthText.text = attackerHealthAfter.ToString();
            Command.CommandExecutionComplete();
            return;
        }

        List<AttackHitResult> hits = secondaryHits ?? new List<AttackHitResult>();

        AttackTargetType targetType = GetTargetType(targetUniqueID);
        float moveDur  = moveDuration / speedMultiplier;
        float postDel  = postDelay    / speedMultiplier;
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

        Vector3 originalPosition = _pendingOriginalPosition;
        Vector3 windupPosition = transform.position; // déjà atteint par PlayWindup
        VisualStates tempState = _pendingTempState;

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

        // Rouvre la fenêtre de vol pour la charge/le tir (voir PlayWindup, qui a refermé la sienne dès
        // la fin du recul) : protège cette fois la cible/le chemin de vol pendant le mouvement réel.
        bool moveDone = false;
        BeginFlight();
        Sequence attackSeq = DOTween.Sequence();
        attackSeq.SetLink(gameObject);

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

            // Shake fires a little before the creature actually reaches the target (impact = moveDur, le
            // wind-up n'est plus dans cette séquence — voir PlayWindup), not on the sequence's OnComplete
            // (which only fires after the return move too) — otherwise it lands visibly late. Only the
            // attacker's own hit shakes the camera, never the defender's counter-damage.
            float leadTime = VisualManager.Instance != null ? VisualManager.Instance.CameraShakeAnticipation : 0.05f;
            float shakeTime = Mathf.Max(0f, moveDur - leadTime);
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
                    // Debug.LogError($"[AttackVisual] EXCEPTION pendant l'animation de {gameObject.name} → cible ID:{targetUniqueID} (type={targetType}) — file débloquée de force: {e}");
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
