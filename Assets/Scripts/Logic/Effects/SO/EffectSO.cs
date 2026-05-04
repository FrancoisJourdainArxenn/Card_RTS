using System.Collections.Generic;
using UnityEngine;

public abstract class EffectSO : ScriptableObject
{
    public string Description = "";
    public abstract void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectParameters parameters
    );
    public virtual string GetDescription(EffectParameters parameters) => Description;

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    protected static void Log(string msg) => Debug.Log($"[Effects] {msg}");
}