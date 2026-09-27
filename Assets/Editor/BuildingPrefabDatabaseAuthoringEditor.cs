using UnityEditor;
using UnityEngine;

/// <summary>
/// BuildingPrefabDatabaseAuthoring 전용 인스펙터 GUI 에디터.
/// </summary>
[CustomEditor(typeof(BuildingPrefabDatabaseAuthoring))]
public class BuildingPrefabDatabaseAuthoringEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var authoring = (BuildingPrefabDatabaseAuthoring)target;

        EditorGUILayout.Space(10);
        if (GUILayout.Button("Populate From Resources", GUILayout.Height(30)))
        {
            Undo.RecordObject(authoring, "Populate Building Prefabs From Resources");
            authoring.PopulateFromResources();
            EditorUtility.SetDirty(authoring);
        }
    }
}
