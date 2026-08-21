using UnityEngine;

// Partagé avec CondCounter — regroupé ici plutôt que dans un fichier à part, les deux
// classes qui l'utilisent sont juste à côté.
public enum CountComparison
{
    AtLeast,
    Exactly,
    AtMost,
}

// Condition générique : compte les entités (créatures/bâtiments/zones/joueurs) correspondant à
// une CountQuery (même struct que le scaling des effets, cf. EffectInfo.scalingQuery) et compare
// ce compte à un seuil. Remplace les anciennes conditions à un seul axe (CondMelee, CondMyZone,
// CondSubType, CondCardInZone) : team/zone/statut/carte se règlent tous depuis l'inspecteur via
// les TargetQuery de la CountQuery, sans nouvelle classe C# par combinaison.
[CreateAssetMenu(menuName = "Effects/Conditions/Condition:Entity Count")]
public class CondEntityCount : ConditionSO
{
    public CountQuery query;
    public CountComparison comparison = CountComparison.AtLeast;
    public int threshold = 1;

    // Retire la source elle-même du compte si elle matche le filtre (ex: "combien de Soldiers
    // dans ma zone" sur une carte qui est elle-même un Soldier). Désactivable si on veut
    // explicitement inclure la source (ex: repartition/scaling qui l'incluait déjà).
    public bool excludeSource = true;

    public override bool Evaluate(EffectContext context)
    {
        int count = context.GetTargetCount(query.targetType, query.queries, excludeSource);
        return comparison switch
        {
            CountComparison.AtLeast => count >= threshold,
            CountComparison.Exactly => count == threshold,
            CountComparison.AtMost  => count <= threshold,
            _ => false,
        };
    }
}
