using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 완공 건물과 ConstructionSite의 종류 번호를 정의한다.
/// 부착 엔티티: 열거형 자체는 붙이지 않고 BuildingType·배치/생성 요청·설정·프리팹 매핑 값으로 사용한다.
/// 생성·이용: Authoring/설정/배치 Producer가 선택하고 생성·철거 Command/StateApply 및 BuildingSpatialSyncSystem이 종류별 동작을 구분한다.
/// 제거: 값을 담은 데이터의 수명을 따른다. enum·프리팹 존재만으로 전력·연구·드론 수행 기능이 구현된 것은 아니다. None/Count는 일반 생성 대상이 아니다.
/// </summary>
public enum BuildingTypeEnum : byte
{
    None,
    Storage,
    Miner,
    Crafter,
    PowerPole,
    CoalGenerator,
    ResearchBuilding,
    MainFacility,
    DroneStation,
    Splitter,
    Merger,
    Belt,
    ConstructionSite,
    Count
}

/// <summary>
/// 역할·목적: 건물/현장의 종류를 식별하여 생성·철거·공간 등록의 분기를 결정한다.
/// 부착 엔티티: 런타임 완공 건물 및 공사 현장. 현장은 ConstructionSite 종류를 사용하며 프리팹 DB의 종류 정보와 구분한다.
/// 생성: BuildingLifecycleUtility의 완공 건물 초기화와 BuildingPlacementCommandSystem의 현장 생성에서 설정한다.
/// 이용: BuildingDemolitionCommandSystem(Command), BuildingLifecycleApplySystem(StateApply), BuildingSpatialSyncSystem(Synchronization) 등이 읽는다.
/// 제거: 해당 건물/현장 엔티티가 삭제될 때 함께 제거한다. 종류만 바꾸어 현장을 완공 건물로 전환하지 않는다.
/// </summary>
public struct BuildingType : IComponentData
{
    public BuildingTypeEnum Type;

    public BuildingType(BuildingTypeEnum type)
    {
        Type = type;
    }

    public static implicit operator BuildingTypeEnum(BuildingType b) => b.Type;
    public static implicit operator BuildingType(BuildingTypeEnum type) => new BuildingType(type);
}

/// <summary>
/// 역할·목적: 건물/현장의 방향 적용 전 기본 타일 크기를 보관하여 점유 영역 계산의 기준을 제공한다.
/// 부착 엔티티: 완공 건물 또는 공사 현장. 위치는 GridPosition의 좌하단 기준점과 결합한다.
/// 생성: BuildingPlacementCommandSystem은 승인 후보의 기본 크기, BuildingLifecycleUtility는 생성 건물의 크기를 설정한다.
/// 이용: ConstructionLifecycleApplySystem(StateApply)의 바닥 차단과 BuildingSpatialSyncSystem(Synchronization)의 점유 등록 등이 읽는다.
/// Reader는 BuildingFootprintUtility.GetEffectiveSize로 방향을 한 번 적용한다. 이미 회전한 크기를 이 필드에 저장하지 않는다.
/// 제거: 건물/현장 삭제 시 함께 제거한다. footprint 갱신이 공간 인덱스의 즉시 갱신을 뜻하지는 않는다.
/// </summary>
public struct BuildingFootprint : IComponentData
{
    public int2 Size;

    public BuildingFootprint(int width, int height)
    {
        Size = new int2(width, height);
    }

    public BuildingFootprint(int2 size)
    {
        Size = size;
    }
}

/// <summary>
/// 역할·목적: 완공 건물의 사용자 철거를 막는 보호 태그.
/// 부착 엔티티: 철거를 금지할 완공 건물. BuildingLifecycleUtility는 MainFacility 생성 시 부착한다.
/// 생성·이용: 생성 경계에서 설정하며 BuildingDemolitionCommandSystem(Command)이 철거 가능성을 검사할 때 읽는다.
/// 제거: 건물/World 수명 종료 시 함께 제거한다. 현재 사용자 요청으로 보호를 해제하는 Writer는 없다.
/// </summary>
public struct IndestructibleBuilding : IComponentData
{
}

/// <summary>
/// 역할·목적: 철거 승인을 대상 건물의 상태로 유지하여 요청 엔티티 삭제 후에도 해당 틱의 동작을 중단한다.
/// 부착 엔티티: Command에서 철거가 승인된 완공 건물. 요청 엔티티나 현장에는 붙이지 않는다.
/// 생성: BuildingDemolitionCommandSystem이 EndCommand에 부착한다. 승인된 대상과 철거 조건은 StateApply까지 유지하는 계약이다.
/// 이용: 입고·생산·출고·벨트/라우팅 Decision과 드론 판단/검증이 읽어 실행 후보를 막는다.
/// BuildingLifecycleApplySystem(StateApply)이 이 상태로 철거 대상을 조회하여 내용물 반환·비용 환급·건물 삭제를 기록한다.
/// 제거: EndBuilding에서 건물과 함께 삭제한다. BuildingType이 소실된 경우에는 상태만 제거하는 방어 경로가 있다.
/// </summary>
public struct PendingBuildingDemolition : IComponentData
{
}
