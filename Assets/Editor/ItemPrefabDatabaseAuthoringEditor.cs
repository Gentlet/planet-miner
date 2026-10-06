using UnityEditor;
using UnityEngine;

/// <summary>
/// 역할·목적: 아이템 DB Authoring Inspector에 Resources 목록 채우기 버튼을 제공한다.
/// 호출·입출력: Unity Inspector가 기본 직렬화 필드/버튼을 표시하며 대상 목록을 편집한다.
/// 수명·정리: Editor 수명은 Unity가 관리한다. 변경 전 Undo/후 dirty를 기록하며 이 호출이 파일 저장·베이킹·런타임 DB 게시를 실행하지는 않는다.
/// </summary>
[CustomEditor(typeof(ItemPrefabDatabaseAuthoring))]
public class ItemPrefabDatabaseAuthoringEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Undo 가능한 목록 편집과 dirty 표시만 수행한다. 저장/베이킹은 이 호출에서 실행하지 않는다.
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
