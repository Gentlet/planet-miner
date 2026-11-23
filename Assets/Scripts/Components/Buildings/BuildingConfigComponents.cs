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

/// <summary>건물 ID별 기능 설정과 해금 상태. 공통 생성과 배치가 읽으며 런타임 작업 상태는 소유하지 않는다.</summary>
public struct BuildingConfigElement : IBufferElementData
{
    public int BuildingId;
    public int Tier;
    public FixedString128Bytes DisplayName;
    public bool IsUnlocked;
    public int RequiredResearch;
    public int2 Footprint;

    public BuildingSpeedConfig BeltTransport;
    public BuildingSpeedConfig Mining;
    public BuildingSpeedConfig Crafting;
    public BuildingSpeedConfig Research;
    public BeltRoutingMode BeltRouting;
    public BuildingItemStorageConfig ItemStorage;
    public BuildingPowerConfig Power;
    public BuildingDroneStationConfig DroneStation;
    public bool DroneCharging;
    public BuildingPlacementConfig Placement;
}

public struct BuildingSpeedConfig
{
    public bool IsPresent;
    public float Speed;

    public BuildingSpeedConfig(float speed)
    {
        IsPresent = true;
        Speed = speed;
    }
}

public enum BeltRoutingMode : byte
{
    None,
    Split,
    Merge
}

public struct BuildingItemStorageConfig
{
    public bool IsPresent;
    public int Slots;
    public bool FilterEditable;
}

public struct BuildingDroneStationConfig
{
    public bool IsPresent;
    public int RangeInChunks;
    public int DroneSlots;
}

public struct BuildingPlacementConfig
{
    public bool BootstrapOnly;
    public bool Indestructible;
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

public readonly struct BuildingFuelConfig
{
    public readonly ItemTypeEnum ItemType;
    public readonly float EnergyPerItem;

    public BuildingFuelConfig(ItemTypeEnum itemType, float energyPerItem)
    {
        ItemType = itemType;
        EnergyPerItem = energyPerItem;
    }
}
