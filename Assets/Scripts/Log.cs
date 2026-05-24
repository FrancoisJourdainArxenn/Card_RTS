public static class Log
{
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void Info(string msg) => UnityEngine.Debug.Log(msg);

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void Warning(string msg) => UnityEngine.Debug.LogWarning(msg);

    // LogError reste actif en build — les erreurs réseau doivent toujours être visibles.
    public static void Error(string msg) => UnityEngine.Debug.LogError(msg);
}
