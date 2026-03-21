using UnityEngine;
using UnityEditor;

static class FactionUnityIntegration {

	[MenuItem("Assets/Create/FactionAsset")]
	public static void CreateYourScriptableObject() {
		ScriptableObjectUtility2.CreateAsset<FactionAsset>();
	}

}
