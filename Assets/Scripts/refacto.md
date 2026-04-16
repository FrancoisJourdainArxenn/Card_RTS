A faire de 16/04/26
Refacto sur la UI. Nécessite de changer le fonctionnement et les appels de BaseVisual. GlobalSettings doit définir les éléments de la UI à mettre sur le canvas, dans un novueau script reprenant une partie de la logique de BaseVisual.
Adapter les appels pour que tout ce mettes à jour. Puis faire en sorte que ça fonctionne en multijoueur. 

Soucis à réfler dans CreatureAttackVisual :                         // target.GetComponent<BaseVisual>().UiHealthText.text = targetHealthAfter.ToString();















Plan de refacto multijoueur
Diagnostic
L'architecture est en réalité bien adaptée au multijoueur grâce à la séparation logique/visuel et le pattern Command. Le problème est simple : toutes les actions se font localement sans aucun RPC. Chaque client joue sa propre partie indépendante.

Le principe cible : autorité serveur

Le client envoie son intention (ServerRpc)
Le serveur valide et exécute la logique
Le serveur diffuse le résultat à tous les clients (ClientRpc) → les clients exécutent le visuel
Phase 1 — Fondation réseau dans la BattleScene
Objectif : Créer le NetworkBehaviour central qui orchestre la partie.

Créer GameNetworkManager.cs (NetworkBehaviour, singleton)

S'active dans la BattleScene
Assigne les clientId aux joueurs (host = LowPlayer, client = TopPlayer)
Disable PlayerArea.AllowedToControlThisPlayer pour le joueur adverse
Démarre la partie une fois que les deux clients ont signalé être prêts (via ServerRpc)
Fichiers touchés :

Nouveau : NetworkSessionData.cs (déjà fait)
Nouveau : Assets/Scripts/GameNetworkManager.cs
Modifié : GlobalSettings.cs — retirer la logique réseau de Start(), la déléguer à GameNetworkManager
Phase 2 — Synchronisation du TurnManager
Objectif : Les phases de tour sont identiques chez les deux joueurs.

Modifier TurnManager.cs :

EnterPhase() ne s'appelle que sur le serveur
Ajouter un ClientRpc SyncPhaseClientRpc(TurnPhases phase) qui déclenche le visuel de changement de phase chez tous les clients
RegisterEndPhase(Player p) devient un ServerRpc : le client signale "je suis prêt", le serveur décide de passer à la phase suivante

Client: fin de phase bouton
  → ServerRpc RegisterEndPhaseServerRpc(clientId)
  → Serveur: logique RegisterEndPhase()
  → Si tous prêts: EnterPhase()
  → ClientRpc SyncPhaseClientRpc(nextPhase)
  → Tous: exécutent visuels de transition
Phase 3 — Actions de jeu comme RPCs
C'est le cœur du refacto. Le pattern est le même pour toutes les actions :


Input local → validation légère → ServerRpc → logique serveur → ClientRpc → Command visuel chez tous
A) Jouer une créature (DragCreatureOnTable.cs)

OnEndDrag() → au lieu d'appeler Player.PlayACreatureFromHand(), envoie ServerRpc PlayCreatureServerRpc(cardUniqueId, tablePos, baseId)
Serveur : valide ressources, crée CreatureLogic, envoie ClientRpc PlayCreatureClientRpc(cardAssetName, creatureUniqueId, tablePos, baseId, ownerClientId)
Tous les clients : exécutent PlayACreatureCommand avec les données reçues
B) Déplacer/attaquer une créature (DragCreatureActions.cs)

Même pattern : MoveCreatureServerRpc(creatureId, targetBaseId) / AttackServerRpc(attackerId, targetId)
C) Jouer un sort (DragSpellOnTarget.cs, DragSpellNoTarget.cs)

PlaySpellServerRpc(cardId, targetId)
D) Piocher une carte

La main est privée : utiliser ClientRpc ciblé (SendTo.SpecificClients) pour n'envoyer les infos de carte qu'au bon joueur
Phase 4 — Synchronisation de l'état
Resources (Player.cs propriétés MainRessourceAvailable etc.)

Remplacer les UpdateRessourcesCommand auto-déclenchés par des NetworkVariable<int> sur un PlayerNetworkState (NetworkBehaviour)
Les clients lisent les NetworkVariables pour afficher les ressources
Santé des bases

NetworkVariable<int> pour le health de chaque base
OnValueChanged met à jour le visuel BaseVisual
IDHolder

Les IDs des créatures/cartes doivent être générés côté serveur et transmis aux clients via les RPCs (Phase 3). IDFactory reste côté serveur uniquement.
Phase 5 — Instanciation réseau des prefabs
Actuellement TableVisual.AddCreatureAtIndex() fait un Instantiate() local. Il faut :

Préfab créature → avoir un composant NetworkObject
L'instanciation se fait via NetworkObject.InstantiateAndSpawn() côté serveur
Netcode synchronise automatiquement la présence de l'objet chez tous les clients
Idem pour les cartes en main (mais les cartes sont privées → instanciées localement, pas en NetworkObject, juste guidées par les RPCs privés de la Phase 3D).

Ordre recommandé
Phase	Effort	Résultat visible
1 — GameNetworkManager	Moyen	Les deux joueurs voient le bon joueur actif
2 — TurnManager sync	Moyen	Les phases s'avancent en sync
3 — Actions RPCs	Élevé	Actions d'un joueur visibles chez l'autre
4 — State sync	Moyen	Ressources/HP cohérents
5 — NetworkObject prefabs	Faible	Optionnel si Phase 3 gère bien l'instanciation
La Phase 5 peut être évitée entièrement si les ClientRpc de Phase 3 transmettent suffisamment d'info pour que chaque client instancie lui-même les prefabs (approche "message-driven" plutôt que "object-sync"). C'est probablement plus simple étant donné l'architecture Command existante.

Dis-moi par quelle phase tu veux commencer et je passe à l'implémentation.

Etat actuel
=> Lorsqu'on joue une carte, ça joue une carte aléatoire de la main (les mains ne sont pas synchronisées)
- Du coup des fois on a un Baneling au lieu d'un Zergling

=> Les boutons pass se désync, lorsque J1 passe, ça unlock ceux de J2

Correctifs recommandés (ordre)
Court terme (stabilité) : guards null dans HighlightPlayableCards pour OneCardManager et OneCreatureManager (évite de casser les RPC callbacks).
Moyen terme (vrai fix) : stratégie d’ID unique global en multi :
soit tout ID gameplay est server-authoritative,
soit partition d’espace d’IDs (ex: plages réservées par type/source),
soit synchroniser le compteur local avec les IDs reçus réseau (Count = max(Count, receivedId)).
Si tu veux, je te propose une stratégie d’ID simple “safe multiplayer” compatible avec ton architecture actuelle (sans tout réécrire).