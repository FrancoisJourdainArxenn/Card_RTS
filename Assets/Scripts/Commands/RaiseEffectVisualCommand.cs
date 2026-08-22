// Partie 1 (popup pour effet auto isolé) désactivée : classe entière commentée, plus aucun
// appelant (voir EffectRegistry.Execute).

// using UnityEngine;
// using DG.Tweening;

// // Command exécutée par la file de commandes du jeu (Command.AddToQueue / la boucle qui les
// // dépile une par une). Son rôle : déclencher l'apparition du popup, puis retenir la file
// // de commandes suivantes le temps qu'on ait pu le lire.
// public class RaiseEffectVisualCommand : Command
// {
//     readonly CardEffectData data;      // les données de l'effet (nom, trigger, EffectSO...)
//     readonly EffectContext context;    // qui a lancé l'effet, sur quelle cible, etc.
//
//     public RaiseEffectVisualCommand(CardEffectData data, EffectContext context)
//     {
//         this.data = data;
//         this.context = context;
//     }
//
//     // Appelée par la file de commandes quand c'est le tour de cette commande de s'exécuter.
//     public override void StartCommandExecution()
//     {
//         // Lève l'event global : c'est CET appel qui fait apparaître le popup
//         // (via CardPreviewUI.HandleAutoEffect, abonné à cet event).
//         TargetingVisualEvents.RaiseAutoEffectTriggered(data, context);
//
//         // Ne signale la fin de cette commande qu'après un délai — ça bloque l'exécution
//         // des commandes suivantes (dégâts, morts, etc.) pendant ce temps-là, pour laisser
//         // le popup visible avant que la suite ne s'enchaîne.
//         DOVirtual.DelayedCall(VisualManager.Instance.RaiseEffectVisualDelay, CommandExecutionComplete);
//     }
// }
