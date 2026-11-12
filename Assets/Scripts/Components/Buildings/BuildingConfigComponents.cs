using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 건물 종류·티어별 생성 스펙·현재 해금 상태·건설 비용의 통합 설정 소유자를 식별한다.
/// 부착 엔티티: BuildingConfigElement/BuildingConstructionMaterialElement 및 기존 호환 버퍼를 함께 가진 World 단일 설정 엔티티다.
/// 생성: BuildingConfigInitSystem(Initialization)이 BuildingConfigLoader.PublishValidatedConfig로 검증된 설정을 한 번 게시한다.
/// 이용: BuildingPlacementCommandSystem(Command)이 해금·크기·비용을 읽고 BuildingLifecycleApplySystem/ConstructionLifecycleApplySystem(StateApply)은 공통 생성과 철거 비용 환급에 사용한다.
/// 제거: 요청 처리로 소비하지 않는 World 설정이며 World 종료 시 함께 사라진다. 연구 초기화/완료 Apply가 IsUnlocked와 별도 ResearchStatBonuses를 게시한다.
/// </summary>
public struct BuildingConfig : IComponentData
{
}

/// <summary>
/// 역할·목적: 건물 종류·티어별 속도·저장 슬롯 수·해금 상태·연구 식별자·방향 적용 전 기본 크기를 제공한다.
/// 부착 엔티티: BuildingConfig 통합 설정 엔티티의 버퍼다.
/// 생성: 외부 TableBuilder가 설정값을 검증하고 BuildingConfigInitSystem이 생성 JSON을 해석하여 게시한다.
/// 이용: BuildingPlacementCommandSystem(Command)이 배치 해금·기본 크기를 읽고 BuildingLifecycleUtility가 StateApply 건물 생성의 타입별 상태를 초기화한다.
/// 제거: 생성/배치 요청으로 항목을 소비하지 않는다. 기본 스펙은 보존하고 제품 연구 경로는 초기화/완료 Apply에서 IsUnlocked를 갱신한다. 버퍼는 설정 엔티티/World 수명을 따른다.
/// </summary>
public struct BuildingConfigElement : IBufferElementData
{
    /// <summary>설정 게시와 배치/생성에서 사용하는 양수 건물 식별자.</summary>
    public int BuildingId;
    public BuildingTypeEnum BuildingType;
    public int Tier;
    /// <summary>설정의 표시 이름. 종류·티어 식별과 독립적이며 중복을 허용한다.</summary>
    public FixedString128Bytes DisplayName;
    public float Speed;
    public int StorageCapacity;
    public bool IsUnlocked;
    public int RequiredResearch;
    /// <summary>이 종류·티어의 방향 적용 전 고정 점유 크기. 명시적 양수 설정이 필수이며 기본값으로 보충하지 않는다.</summary>
    public int2 Footprint;
    /// <summary>이 종류·Tier의 전력 능력 원본. 미대상 종류는 default이며 런타임 수요/공급은 별도 컴포넌트다.</summary>
    public BuildingPowerConfig Power;
    /// <summary>주 시설·드론 정거장의 정사각형 활동 영역을 확장할 청크 수. 음수는 0으로 사용한다.</summary>
    public int DroneStationRangeInChunks;
    /// <summary>일반 storageCapacity와 별개인 드론 전용 슬롯 수. 정거장 종류에만 양수로 설정한다.</summary>
    public int DroneStationStorageCapacity;

    public BuildingConfigElement(
        BuildingTypeEnum buildingType,
        int tier,
        FixedString128Bytes displayName,
        float speed,
        int storageCapacity,
        bool isUnlocked,
        int requiredResearch = default,
        int2 footprint = default,
        BuildingPowerConfig power = default,
        int droneStationRangeInChunks = 0,
        int droneStationStorageCapacity = 0,
        int buildingId = 0)
    {
        BuildingId = buildingId;
        BuildingType = buildingType;
        Tier = tier;
        DisplayName = displayName;
        Speed = speed;
        StorageCapacity = storageCapacity;
        IsUnlocked = isUnlocked;
        RequiredResearch = requiredResearch;
        Footprint = footprint;
        Power = power;
        DroneStationRangeInChunks = math.max(0, droneStationRangeInChunks);
        DroneStationStorageCapacity = droneStationStorageCapacity;
    }
}

/// <summary>
/// 역할·목적: 건물 ID와 품목별 건설에 필요한 개별 아이템 수량을 정의한다.
/// 부착 엔티티: BuildingConfig 통합 설정 엔티티의 비용 버퍼다. 현장의 도착량/예약량 원본은 별도 ConstructionMaterialRequirementElement다.
/// 생성: 외부 TableBuilder가 자재 행을 검증하고 Unity 초기화가 건물 ID로 연결한 값을 게시한다.
/// 이용: BuildingPlacementCommandSystem(Command)이 현장 요구량을 만들고 BuildingLifecycleApplySystem(StateApply)이 완공 건물 철거의 비용 환급을 생성한다.
/// 제거: 건설·철거 시 설정 수량을 소비하지 않는다. 버퍼는 설정 엔티티/World 수명을 따른다.
/// </summary>
public struct BuildingConstructionMaterialElement : IBufferElementData
{
    public int BuildingId;
    public ItemTypeEnum ItemType;
    public int Quantity;

    public BuildingConstructionMaterialElement(int buildingId, ItemTypeEnum itemType, int quantity)
    {
        BuildingId = buildingId;
        ItemType = itemType;
        Quantity = quantity;
    }
}
