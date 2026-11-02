using Unity.Entities;

/// <summary>복사한 설정의 값 스냅샷이다. 배치 Command가 검증하고 현장에 보존하며 완공 시 새 건물에 전달한다.</summary>
public struct PlannedBuildingSettings : IComponentData
{
    public bool HasRecipe;
    public int RecipeId;
    public bool HasStorageFilter;
    public StorageFilter Filter;

    public bool HasValues => HasRecipe || HasStorageFilter;
}

/// <summary>Submit의 World 순번으로 일반 저장소 필터를 Command에서 변경하고 EndCommand에 소비한다. 기존 실물/출고는 보존한다.</summary>
public struct ChangeStorageFilterRequest : IComponentData
{
    public Entity Target;
    public StorageFilter Filter;
    public ulong Sequence;
}

/// <summary>이 World의 일반 저장소 필터 요청 접수 순번이다. 원본 필터나 처리 완료 상태가 아니다.</summary>
public struct StorageFilterRequestSequence : IComponentData
{
    public ulong LastIssued;
}
