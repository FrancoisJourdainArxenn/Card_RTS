using UnityEngine;

// Non-bloquante : la file continue sans attendre la fin du beam (AssimilateBeamVisual se détruit
// tout seul en fin de coroutine). Racine instanciée SUR la target, orientée vers la source ;
// aucun scale n'est appliqué à la racine (la Spirale/Particules gardent leur taille d'origine),
// seul Assimilation_Cone est étiré — voir AssimilateBeamVisual.Play.
public class PlayAssimilateVFXCommand : Command
{
    private readonly GameObject beamPrefab;
    private readonly Vector3 sourcePosition;
    private readonly Vector3 targetPosition;

    public PlayAssimilateVFXCommand(GameObject beamPrefab, Vector3 sourcePosition, Vector3 targetPosition)
    {
        this.beamPrefab = beamPrefab;
        this.sourcePosition = sourcePosition;
        this.targetPosition = targetPosition;
    }

    public override void StartCommandExecution()
    {
        if (beamPrefab == null)
        {
            CommandExecutionComplete();
            return;
        }

        Vector3 dir = targetPosition - sourcePosition;
        Quaternion rot = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir) : Quaternion.identity;

        GameObject beam = Object.Instantiate(beamPrefab, targetPosition, rot);

        AssimilateBeamVisual visual = beam.GetComponent<AssimilateBeamVisual>();
        if (visual != null)
            visual.Play(dir.magnitude);

        CommandExecutionComplete();
    }
}
