using Unity.Entities;

/// <summary>
/// 공통 생성 경로가 복제한 런타임 루트에 붙이고, 같은 End* 재생 경계가 장면 소유권을 해제한 뒤 소비한다.
/// 프리팹 원본과 장면에 직접 배치된 엔티티는 이 표식을 부착하지 않는다.
/// </summary>
public struct PendingPrefabSceneDetach : IComponentData
{
}
