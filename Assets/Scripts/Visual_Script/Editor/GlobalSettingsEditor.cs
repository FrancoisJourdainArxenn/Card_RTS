using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Groups GlobalSettings' Inspector fields into collapsible foldouts, one per [Header(...)]
// (e.g. "Pop Animation", "Camera Shake", "Prefabs and Assets"), instead of one long flat list.
[CustomEditor(typeof(GlobalSettings))]
public class GlobalSettingsEditor : Editor
{
    class Group
    {
        public string Header; // null = fields declared before the first [Header]
        public readonly List<string> FieldNames = new List<string>();
    }

    List<Group> groups;

    void OnEnable()
    {
        BuildGroups();
    }

    void BuildGroups()
    {
        groups = new List<Group>();
        Group current = new Group { Header = null };
        groups.Add(current);

        SerializedProperty prop = serializedObject.GetIterator();
        bool enterChildren = true;
        while (prop.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (prop.name == "m_Script")
                continue;

            FieldInfo field = target.GetType().GetField(prop.name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            HeaderAttribute header = field?.GetCustomAttribute<HeaderAttribute>();
            if (header != null)
            {
                current = new Group { Header = header.header };
                groups.Add(current);
            }
            current.FieldNames.Add(prop.name);
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        foreach (Group group in groups)
        {
            if (group.FieldNames.Count == 0)
                continue;

            if (group.Header == null)
            {
                DrawFields(group);
                continue;
            }

            string prefKey = "GlobalSettingsEditor.Foldout." + group.Header;
            bool expanded = EditorPrefs.GetBool(prefKey, true);
            bool newExpanded = EditorGUILayout.Foldout(expanded, group.Header, true, EditorStyles.foldoutHeader);
            if (newExpanded != expanded)
                EditorPrefs.SetBool(prefKey, newExpanded);

            if (newExpanded)
            {
                EditorGUI.indentLevel++;
                DrawFields(group);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);
        }

        serializedObject.ApplyModifiedProperties();
    }

    void DrawFields(Group group)
    {
        foreach (string fieldName in group.FieldNames)
        {
            SerializedProperty property = serializedObject.FindProperty(fieldName);
            if (property != null)
                EditorGUILayout.PropertyField(property, true);
        }
    }
}
