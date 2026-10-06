using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 건물 종류별 생성 스펙·현재 해금 상태·건설 비용의 통합 설정 소유자를 식별한다.
/// 부착 엔티티: BuildingConfigElement/BuildingConstructionMaterialElement 및 기존 호환 버퍼를 함께 가진 World 단일 설정 엔티티다.
/// 생성: BuildingConfigInitSystem(Initialization)이 BuildingConfigLoader.PublishConfig로 검증된 설정을 한 번 게시한다.
/// 이용: BuildingPlacementCommandSystem(Command)이 해금·크기·비용을 읽고 BuildingLifecycleApplySystem/ConstructionLifecycleApplySystem(StateApply)은 공통 생성과 철거 비용 환급에 사용한다.
/// 제거: 요청 처리로 소비하지 않는 World 설정이며 World 종료 시 함께 사라진다. 연구 완료로 IsUnlocked를 갱신하는 Writer는 아직 없다.
/// </summary>
public struct BuildingConfig : IComponentData
{
}

/// <summary>
/// 역할·목적: 건물 종류별 속도·저장 슬롯 수·해금 상태·연구 식별자·방향 적용 전 기본 크기를 제공한다.
/// 부착 엔티티: BuildingConfig 통합 설정 엔티티의 버퍼다.
/// 생성: BuildingConfigInitSystem(Initialization)이 BuildingConfigLoader로 로드·검증하여 게시한다.
/// 이용: BuildingPlacementCommandSystem(Command)이 배치 해금·기본 크기를 읽고 BuildingLifecycleUtility가 StateApply 건물 생성의 타입별 상태를 초기화한다.
/// 제거: 생성/배치 요청으로 항목을 소비하지 않는다. 버퍼는 설정 엔티티/World 수명을 따른다. 현재 연구 해금 Writer는 미구현이다.
/// </summary>
public struct BuildingConfigElement : IBufferElementData
{
    public BuildingTypeEnum BuildingType;
    public float Speed;
    public int StorageCapacity;
    public bool IsUnlocked;
    public FixedString32Bytes RequiredResearch;
    public int2 Footprint;

    public BuildingConfigElement(
        BuildingTypeEnum buildingType,
        float speed,
        int storageCapacity,
        bool isUnlocked,
        FixedString32Bytes requiredResearch = default,
        int2 footprint = default)
    {
        BuildingType = buildingType;
        Speed = speed;
        StorageCapacity = storageCapacity;
        IsUnlocked = isUnlocked;
        RequiredResearch = requiredResearch;
        Footprint = footprint.Equals(int2.zero) ? new int2(1, 1) : footprint;
    }
}

/// <summary>
/// 역할·목적: 건물 종류·품목별 건설에 필요한 개별 아이템 수량을 정의한다.
/// 부착 엔티티: BuildingConfig 통합 설정 엔티티의 비용 버퍼다. 현장의 도착량/예약량 원본은 별도 ConstructionMaterialRequirementElement다.
/// 생성: BuildingConfigInitSystem(Initialization)이 BuildingConfigLoader로 검증된 자재 목록을 게시한다.
/// 이용: BuildingPlacementCommandSystem(Command)이 현장 요구량을 만들고 BuildingLifecycleApplySystem(StateApply)이 완공 건물 철거의 비용 환급을 생성한다.
/// 제거: 건설·철거 시 설정 수량을 소비하지 않는다. 버퍼는 설정 엔티티/World 수명을 따른다.
/// </summary>
public struct BuildingConstructionMaterialElement : IBufferElementData
{
    public BuildingTypeEnum BuildingType;
    public ItemTypeEnum ItemType;
    public int Quantity;

    public BuildingConstructionMaterialElement(BuildingTypeEnum buildingType, ItemTypeEnum itemType, int quantity)
    {
        BuildingType = buildingType;
        ItemType = itemType;
        Quantity = quantity;
    }
}
