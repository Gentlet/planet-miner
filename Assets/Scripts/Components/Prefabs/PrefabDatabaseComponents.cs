using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 건물 종류별 프리팹·방향 적용 전 정적 크기 매핑의 단일 DB 소유자를 식별한다.
/// 부착 엔티티: BuildingPrefabElement 버퍼가 함께 있는 별도 베이킹 DB 엔티티다.
/// 생성: BuildingPrefabDatabaseAuthoring.Baker가 등록된 Authoring 항목을 베이킹한다.
/// 이용: PrefabDatabaseInitializationSystem(Initialization)이 DB 유일성·필수 항목·프리팹 생존/태그를 검증하고 BuildingLifecycleApplySystem/ConstructionLifecycleApplySystem(StateApply)의 완공 생성이 읽는다.
/// 제거: 생성 요청으로 DB를 소비하지 않는다. 검증 후 런타임 불변이며 DB 엔티티/World 수명을 따른다. 누락 시 대체 생성하지 않는다.
/// </summary>
public struct BuildingPrefabDatabase : IComponentData
{
}

/// <summary>
/// 역할·목적: 건물 종류별 프리팹·방향 적용 전 정적 크기 매핑을 제공한다. Prefab은 실제 런타임 인스턴스와 구분한다.
/// 부착 엔티티: BuildingPrefabDatabase 태그가 있는 DB 엔티티의 버퍼다.
/// 생성: BuildingPrefabDatabaseAuthoring.Baker가 각 항목의 Prefab 참조를 등록한다.
/// 이용: PrefabDatabaseInitializationSystem(Initialization)이 항목 중복·타입·Prefab/LocalTransform 등을 검증하고 BuildingLifecycleApplySystem/ConstructionLifecycleApplySystem(StateApply)의 완공 생성이 타입으로 조회하여 인스턴스화한다.
/// 제거: 인스턴스 생성/삭제 때 매핑을 소비하지 않는다. 검증 후 변경하지 않으며 DB 엔티티/World 수명을 따른다.
/// </summary>
[InternalBufferCapacity(16)]
public struct BuildingPrefabElement : IBufferElementData
{
    public BuildingTypeEnum Type;
    public Entity Prefab;
    public int2 FootprintSize;

    public BuildingPrefabElement(BuildingTypeEnum type, Entity prefab, int2 footprintSize)
    {
        Type = type;
        Prefab = prefab;
        FootprintSize = footprintSize;
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
/// 이용: PrefabDatabaseInitializationSystem(Initialization)이 항목 중복·타입·Prefab/LocalTransform 등을 검증하고 ItemLifecycleApplySystem(StateApply)의 Spawn·생산과 BuildingLifecycleApplySystem(StateApply)의 철거 환급이 타입으로 조회하여 인스턴스화한다.
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
/// 이용: PrefabDatabaseInitializationSystem(Initialization)이 항목 중복·타입·Prefab/LocalTransform 등을 검증하고 ResourceGenerationCommandSystem(Command)의 자원 생성이 타입으로 조회하여 인스턴스화한다.
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
/// 역할·목적: 향후 드론 프리팹 DB를 식별하기 위한 태그 계약이다.
/// 부착 엔티티: 드론 수행자가 아니라 DronePrefab을 가진 별도 DB 엔티티를 의도한다.
/// 생성·이용: 현재 제품 코드에 Authoring/Baker·게시 Producer·드론 생성 Consumer가 없다. 건물/아이템/자원 DB 검증에 드론 DB를 포함하지 않는다.
/// 제거: 현재 관리된 생성/제거 경계가 없으며 외부에서 붙인 데이터는 해당 엔티티/World 수명을 따른다.
/// </summary>
public struct DronePrefabDatabase : IComponentData
{
}

/// <summary>
/// 역할·목적: 향후 드론 인스턴스 생성에 사용할 단일 Prefab 엔티티 참조 계약이다.
/// 부착 엔티티: DronePrefabDatabase가 있는 별도 DB 엔티티를 의도한다. 실제 DroneWorker 상태가 아니다.
/// 생성·이용: 현재 제품 코드에 게시 Producer나 이 참조로 드론을 생성하는 Consumer는 없다. 필드가 존재한다는 것과 수행부 구현을 구분한다.
/// 제거: 현재 관리된 생성/제거 경계가 없으며 외부 등록 엔티티/World 수명을 따른다.
/// </summary>
public struct DronePrefab : IComponentData
{
    public Entity Prefab;

    public DronePrefab(Entity prefab)
    {
        Prefab = prefab;
    }
}
