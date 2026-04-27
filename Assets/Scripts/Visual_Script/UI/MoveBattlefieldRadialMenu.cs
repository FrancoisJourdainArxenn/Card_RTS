using UnityEngine;

public class MoveBattleFieldRadialMenu : RadialMenu
{
    [SerializeField]
    public float minAngleDeg;
    
    [SerializeField]
    public float maxDistance;

    protected override bool ZoomedOnly => true;

    public override void AddEntries()
    {
        var others = cameraController.CurrentAnchor.FindClosestWithAngle(minAngleDeg, maxDistance);
        for (int i = 0; i < others.Count; i++)
        {
            var anchor = others[i].anchor;
            var index = i;
            AddEntry(
                index,
                "",
                Icons[0],
                others[i].angleDeg,
                entry => MoveToAnchor(anchor, entry.GetIndex())
            );
        }
    }
    void MoveToAnchor(ZoneCameraAnchor anchor, int index)
    {
        cameraController.MoveCameraToAnchor(anchor);
        Close(index);
    }
}