using Unity.Entities;

/// <summary>
/// 역할·목적: 통합 BuildingConfig 게시 때 함께 생성되는 기존 런타임 스펙 호환 버퍼를 식별한다.
/// 부착 엔티티: BuildingConfig와 같은 World 설정 엔티티다. 별도의 실제 건물 상태가 아니다.
/// 생성: BuildingConfigInitSystem(Initialization)이 호출하는 BuildingConfigLoader.PublishConfig가 함께 붙인다.
/// 이용: 현재 제품 시스템의 직접 Reader는 없으며 실제 생성/배치 Reader는 BuildingConfigElement를 사용한다.
/// 제거: 개별 요청으로 소비하지 않고 설정 엔티티/World 종료 시 함께 사라진다. 존재만으로 별도 설정 파이프라인이 실행되는 것은 아니다.
/// </summary>
public struct BuildingRuntimeConfig : IComponentData
{
}

/// <summary>
/// 역할·목적: 건물 종류별 속도·저장 용량의 기존 호환 표현이다. 통합 설정 게시 시 BuildingConfigElement에서 복사한다.
/// 부착 엔티티: BuildingRuntimeConfig와 BuildingConfig가 함께 있는 동일 설정 엔티티의 버퍼다.
/// 생성: BuildingConfigInitSystem(Initialization)의 BuildingConfigLoader.PublishConfig가 항목을 채운다.
/// 이용: BuildingConfigLookupUtility의 조회 API가 있지만 현재 제품 시스템의 직접 호출 Reader는 없다. 실제 생성은 BuildingConfigElement를 읽는다.
/// 제거: 요청/건설로 소비하지 않고 설정 엔티티/World 종료 시 함께 사라진다. 독립적으로 갱신하는 Writer는 없다.
/// </summary>
[InternalBufferCapacity(8)]
public struct BuildingRuntimeConfigElement : IBufferElementData
{
    public BuildingTypeEnum BuildingType;
    public float Speed;
    public int StorageCapacity;

    public BuildingRuntimeConfigElement(BuildingTypeEnum buildingType, float speed, int storageCapacity = 0)
    {
        BuildingType = buildingType;
        Speed = speed;
        StorageCapacity = storageCapacity;
    }
}
