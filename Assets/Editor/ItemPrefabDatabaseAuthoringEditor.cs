using UnityEditor;
using UnityEngine;

/// <summary>
/// ItemPrefabDatabaseAuthoring 전용 인스펙터 GUI 에디터.
/// </summary>
[CustomEditor(typeof(ItemPrefabDatabaseAuthoring))]
public class ItemPrefabDatabaseAuthoringEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var authoring = (ItemPrefabDatabaseAuthoring)target;

        EditorGUILayout.Space(10);
        if (GUILayout.Button("Populate From Resources", GUILayout.Height(30)))
        {
            Undo.RecordObject(authoring, "Populate Item Prefabs From Resources");
            authoring.PopulateFromResources();
            EditorUtility.SetDirty(authoring);
        }
    }
}
