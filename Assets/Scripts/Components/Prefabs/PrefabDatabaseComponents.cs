using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 건물 종류·티어별 프리팹 매핑의 단일 DB 소유자를 식별한다. 점유 크기는 BuildingConfig가 소유한다.
/// 부착 엔티티: BuildingPrefabElement 버퍼가 함께 있는 별도 베이킹 DB 엔티티다.
/// 생성: BuildingPrefabDatabaseAuthoring.Baker가 등록된 Authoring 항목을 베이킹한다.
/// 이용: PrefabDatabaseInitializationSystem(Initialization)이 DB 유일성·필수 항목·프리팹 생존/태그를 검증하고 BuildingLifecycleApplySystem/ConstructionLifecycleApplySystem(StateApply)의 완공 생성이 읽는다.
/// 제거: 생성 요청으로 DB를 소비하지 않는다. 검증 후 런타임 불변이며 DB 엔티티/World 수명을 따른다. 누락 시 대체 생성하지 않는다.
/// </summary>
public struct BuildingPrefabDatabase : IComponentData
{
}

/// <summary>
/// 역할·목적: 건물 종류·티어별 프리팹 매핑을 제공한다. Prefab은 실제 런타임 인스턴스와 구분하며 크기는 담지 않는다.
/// 부착 엔티티: BuildingPrefabDatabase 태그가 있는 DB 엔티티의 버퍼다.
/// 생성: BuildingPrefabDatabaseAuthoring.Baker가 각 항목의 Prefab 참조를 등록한다.
/// 이용: PrefabDatabaseInitializationSystem(Initialization)이 항목 중복·타입·Prefab/LocalTransform 등을 검증하고 BuildingLifecycleApplySystem/ConstructionLifecycleApplySystem(StateApply)의 완공 생성이 종류·티어로 조회하여 인스턴스화한다.
/// 제거: 인스턴스 생성/삭제 때 매핑을 소비하지 않는다. 검증 후 변경하지 않으며 DB 엔티티/World 수명을 따른다.
/// </summary>
[InternalBufferCapacity(16)]
public struct BuildingPrefabElement : IBufferElementData
{
    public BuildingTypeEnum Type;
    public int Tier;
    public Entity Prefab;
    public BuildingPrefabElement(BuildingTypeEnum type, int tier, Entity prefab)
    {
        Type = type;
        Tier = tier;
        Prefab = prefab;
    }
}

/// <summary>
/// 역할·목적: 품목별 아이템 프리팹 매핑의 단일 DB 소유자를 식별한다.
/// 부착 엔티티: ItemPrefabElement 버퍼가 함께 있는 별도 베이킹 DB 엔티티다.
/// 생성: ItemPrefabDatabaseAuthoring.Baker가 등록된 Authoring 항목을 베이킹한다.
/// 이용: PrefabDatabaseInitializationSystem(Initialization)이 DB 유일성·필수 항목·프리팹 생존/태그를 검증하고 ItemLifecycleApplySystem(StateApply)의 Spawn·생산과 BuildingLifecycleApplySystem(StateApply)의 철거 환급이 읽는다.
/// 제거: 생성 요청으로 DB를 소비하지 않는다. 검증 후 런타임 불변이며 DB 엔티티/World 수명을 따른다. 누락 시 대체 생성하지 않는다.
/// </summary>
public struct ItemPrefabDatabase : IComponentData
{
}

/// <summary>
/// 역할·목적: 품목별 아이템 프리팹 매핑을 제공한다. Prefab은 실제 런타임 인스턴스와 구분한다.
/// 부착 엔티티: ItemPrefabDatabase 태그가 있는 DB 엔티티의 버퍼다.
/// 생성: ItemPrefabDatabaseAuthoring.Baker가 각 항목의 Prefab 참조를 등록한다.
/// 이용: PrefabDatabaseInitializationSystem(Initialization)이 항목 중복/타입/Prefab/LocalTransform 등을 검증하고 ItemLifecycleApplySystem(StateApply)의 Spawn/생산과 BuildingLifecycleApplySystem(StateApply)의 철거 환급이 품목 타입(Type)으로 조회하여 인스턴스화한다.
/// 제거: 인스턴스 생성/삭제 때 매핑을 소비하지 않는다. 검증 후 변경하지 않으며 DB 엔티티/World 수명을 따른다.
/// </summary>
[InternalBufferCapacity(16)]
public struct ItemPrefabElement : IBufferElementData
{
    public ItemTypeEnum Type;
    public Entity Prefab;

    public ItemPrefabElement(ItemTypeEnum type, Entity prefab)
    {
        Type = type;
        Prefab = prefab;
    }
}

/// <summary>
/// 역할·목적: 자원 품목별 노드 프리팹 매핑의 단일 DB 소유자를 식별한다.
/// 부착 엔티티: ResourcePrefabElement 버퍼가 함께 있는 별도 베이킹 DB 엔티티다.
/// 생성: ResourcePrefabDatabaseAuthoring.Baker가 등록된 Authoring 항목을 베이킹한다.
/// 이용: PrefabDatabaseInitializationSystem(Initialization)이 DB 유일성·필수 항목·프리팹 생존/태그를 검증하고 ResourceGenerationCommandSystem(Command)의 자원 생성이 읽는다.
/// 제거: 생성 요청으로 DB를 소비하지 않는다. 검증 후 런타임 불변이며 DB 엔티티/World 수명을 따른다. 누락 시 대체 생성하지 않는다.
/// </summary>
public struct ResourcePrefabDatabase : IComponentData
{
}

/// <summary>
/// 역할·목적: 자원 품목별 노드 프리팹 매핑을 제공한다. Prefab은 실제 런타임 인스턴스와 구분한다.
/// 부착 엔티티: ResourcePrefabDatabase 태그가 있는 DB 엔티티의 버퍼다.
/// 생성: ResourcePrefabDatabaseAuthoring.Baker가 각 항목의 Prefab 참조를 등록한다.
/// 이용: PrefabDatabaseInitializationSystem(Initialization)이 항목 중복/타입/Prefab/LocalTransform 등을 검증하고 ResourceGenerationCommandSystem(Command)의 자원 생성이 자원 품목(ResourceType)으로 조회하여 인스턴스화한다.
/// 제거: 인스턴스 생성/삭제 때 매핑을 소비하지 않는다. 검증 후 변경하지 않으며 DB 엔티티/World 수명을 따른다.
/// </summary>
[InternalBufferCapacity(8)]
public struct ResourcePrefabElement : IBufferElementData
{
    public ItemTypeEnum ResourceType;
    public Entity Prefab;

    public ResourcePrefabElement(ItemTypeEnum resourceType, Entity prefab)
    {
        ResourceType = resourceType;
        Prefab = prefab;
    }
}

/// <summary>
/// 역할·목적: 드론 표시 원형 DB를 식별한다. DronePrefabDatabaseAuthoring의 Baker가 단독 참조와 함께 게시한다.
/// 부착 엔티티: DronePrefab을 가진 별도 DB 엔티티다. 수행자 Entity가 아니다.
/// 이용: PrefabDatabaseInitializationSystem이 유일성/원형을 검증하고 DroneSpawnUtility가 Ready 이후 읽는다.
/// 제거: 준비 이후 불변이며 DB 엔티티/World 수명을 따른다.
/// </summary>
public struct DronePrefabDatabase : IComponentData
{
}

/// <summary>
/// 역할·목적: 활성 드론 생성에 사용할 단일 표시 원형 참조다. 런타임 수행자 상태를 원형에 저장하지 않는다.
/// 부착 엔티티: DronePrefabDatabase가 있는 DB 엔티티다. Baker가 Dynamic 원형 참조를 게시한다.
/// 이용: DroneSpawnUtility는 원형을 복제하고 위치·관측·배터리·빈 화물/배정 상태를 EndSimulation ECB에 초기화한다.
/// 제거: 준비 이후 불변이며 DB 엔티티/World 수명을 따른다. 자동 배차·이동·충전은 후속이다.
/// </summary>
public struct DronePrefab : IComponentData
{
    public Entity Prefab;

    public DronePrefab(Entity prefab)
    {
        Prefab = prefab;
    }
}
