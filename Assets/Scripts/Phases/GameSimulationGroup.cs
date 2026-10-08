using Unity.Entities;

/// <summary>
/// 역할·목적: Unity SimulationSystemGroup 안에서 Command→Building→Drone→Commit→Synchronization을 실행한다.
/// 입력·생성자: 프리팹 DB Ready, 각 설정 소유자의 게시 엔티티/버퍼와 초기화/실행 경계의 SimulationFatalError.
/// 실행 조건: 틱 시작에 DB와 필수 설정이 준비되고 중단 오류가 없어야 전체 그룹을 한 번 실행한다.
/// 출력·정리: Command/Building/Simulation 종료 ECB를 재생한다. 중간 오류도 현재 틱을 마치고 다음 틱부터 차단한다.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class GameSimulationGroup : ComponentSystemGroup
{
    private EntityQuery _readyQuery;
    private EntityQuery _fatalErrorQuery;
    private EntityQuery _buildingConfigQuery;
    private EntityQuery _itemConfigQuery;
    private EntityQuery _recipeConfigQuery;
    private EntityQuery _worldConfigQuery;

    protected override void OnCreate()
    {
        base.OnCreate();
        _readyQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabDatabaseReady>());
        _fatalErrorQuery = GetEntityQuery(ComponentType.ReadOnly<SimulationFatalError>());
        _buildingConfigQuery = GetEntityQuery(ComponentType.ReadOnly<BuildingConfig>(),
            ComponentType.ReadOnly<BuildingConfigElement>(), ComponentType.ReadOnly<BuildingConstructionMaterialElement>());
        _itemConfigQuery = GetEntityQuery(ComponentType.ReadOnly<ItemRegistry>(), ComponentType.ReadOnly<ItemConfigElement>());
        _recipeConfigQuery = GetEntityQuery(ComponentType.ReadOnly<RecipeRegistry>(), ComponentType.ReadOnly<RecipeConfigElement>(),
            ComponentType.ReadOnly<RecipeIngredientElement>(), ComponentType.ReadOnly<RecipeOutputElement>());
        _worldConfigQuery = GetEntityQuery(ComponentType.ReadOnly<ResourceGenerationSettings>(),
            ComponentType.ReadOnly<ResourceGenerationConfigElement>(), ComponentType.ReadOnly<FloorGenerationSettings>(),
            ComponentType.ReadOnly<FloorBiomeElement>(), ComponentType.ReadOnly<FloorVariantElement>());
    }

    protected override void OnUpdate()
    {
        // 준비 전 또는 중단 오류 후에는 일부 phase만 진행하지 않고 게임 그룹 전체를 건너뛴다.
        if (_readyQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        if (!_fatalErrorQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        // DB Ready는 설정 준비 태그가 아니다. 각 게시 소유자의 원본 엔티티/버퍼가 준비된 뒤에만 시작한다.
        if (_buildingConfigQuery.IsEmptyIgnoreFilter || _itemConfigQuery.IsEmptyIgnoreFilter ||
            _recipeConfigQuery.IsEmptyIgnoreFilter || _worldConfigQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        base.OnUpdate();
    }
}
