using NUnit.Framework;
using PlanetMiner.Config;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Phase 5 Crafter 레시피 변경 및 입고 차단/복귀 파이프라인 검증 테스트 (피드백 1번).
/// - 레시피 변경 시 CommandGroup에서 원자적 처리 (Filter 갱신, Purge to ProductBuffer, 진행도 리셋)
/// - ProductItemElement에 잔여물이 있는 동안 CrafterStatusEnum.WaitingForPurgeOutput 상태 진입
/// - WaitingForPurgeOutput 동안 BuildingItemInputDecisionSystem에서 재료 입고 완전 차단 (방안 A)
/// - ProductItemElement가 완전히 비워지면 Idle로 복귀하고 새 레시피 재료 입고 개시
/// </summary>
public class Phase5RecipeChangePipelineTests : EcsWorldTestFixture
{
    private BlobAssetReference<RecipeRegistryBlob> _recipeBlob;
    private SystemHandle _crafterCommandHandle;
    private SystemHandle _crafterDecisionHandle;
    private SystemHandle _inputDecisionHandle;
    private SystemHandle _buildingSpatialSyncHandle;
    private SystemHandle _beltSpatialSyncHandle;
    private EndStateApplyEntityCommandBufferSystem _endStateApplyEcb;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();

        // 1. 레시피 레지스트리 초기화
        _recipeBlob = RecipeInitSystem.InitializeRecipeRegistry(_entityManager);

        // 2. 시스템 핸들 획득
        _crafterCommandHandle = _world.GetOrCreateSystem(typeof(CrafterRecipeCommandSystem));
        _crafterDecisionHandle = _world.GetOrCreateSystem(typeof(CrafterDecisionSystem));
        _inputDecisionHandle = _world.GetOrCreateSystem(typeof(BuildingItemInputDecisionSystem));
        _buildingSpatialSyncHandle = _world.GetOrCreateSystem(typeof(BuildingSpatialSyncSystem));
        _beltSpatialSyncHandle = _world.GetOrCreateSystem(typeof(BeltSpatialSyncSystem));
        _endStateApplyEcb = _world.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>();
    }

    [TearDown]
    public override void TearDown()
    {
        if (_recipeBlob.IsCreated)
        {
            _recipeBlob.Dispose();
        }
        base.TearDown();
    }

    private Entity CreateCrafter(int2 position, int recipeId = 1)
    {
        var entity = _entityManager.CreateEntity(
            typeof(BuildingType),
            typeof(BuildingFootprint),
            typeof(GridPosition),
            typeof(Direction),
            typeof(CrafterState),
            typeof(CrafterDecision),
            typeof(Storage),
            typeof(StorageFilter));

        _entityManager.SetComponentData(entity, new BuildingType(BuildingTypeEnum.Crafter));
        _entityManager.SetComponentData(entity, new BuildingFootprint(new int2(1, 1)));
        _entityManager.SetComponentData(entity, new GridPosition(position));
        _entityManager.SetComponentData(entity, new Direction(DirectionEnum.Up));
        _entityManager.SetComponentData(entity, new CrafterState(recipeId));
        _entityManager.SetComponentData(entity, new CrafterDecision(false, recipeId));
        _entityManager.SetComponentEnabled<CrafterDecision>(entity, false);
        _entityManager.SetComponentData(entity, new Storage(slotCount: 4));

        // 초기 필터 설정 (Recipe 1: Iron_Ore -> Iron)
        var filter = new StorageFilter(StorageFilterMode.Whitelist);
        filter.Mask.Set((byte)ItemTypeEnum.Iron_Ore, true);
        _entityManager.SetComponentData(entity, filter);

        _entityManager.AddBuffer<StoredItemElement>(entity);
        _entityManager.AddBuffer<ProductItemElement>(entity);

        return entity;
    }

    private Entity CreateBelt(int2 position, DirectionEnum direction)
    {
        var entity = _entityManager.CreateEntity(
            typeof(GridPosition),
            typeof(Direction),
            typeof(BeltComponent));

        _entityManager.SetComponentData(entity, new GridPosition(position));
        _entityManager.SetComponentData(entity, new Direction(direction));
        _entityManager.SetComponentData(entity, new BeltComponent(2.0f));
        return entity;
    }

    private Entity CreateBeltItem(int2 position, DirectionEnum direction, float progress, ItemTypeEnum itemType)
    {
        CreateBelt(position, direction);

        var entity = _entityManager.CreateEntity(
            typeof(ItemIdentity),
            typeof(ItemOwnership),
            typeof(GridPosition),
            typeof(BeltMovementState),
            typeof(BuildingItemInputDecision));

        _entityManager.SetComponentData(entity, new ItemIdentity(itemType));
        _entityManager.SetComponentData(entity, ItemOwnership.WorldItem);
        _entityManager.SetComponentData(entity, new GridPosition(position));
        _entityManager.SetComponentData(entity, new BeltMovementState(progress));
        _entityManager.SetComponentData(entity, new BuildingItemInputDecision(Entity.Null, false, -1));
        _entityManager.SetComponentEnabled<BuildingItemInputDecision>(entity, false);

        return entity;
    }

    private void SyncSpatialIndices()
    {
        _buildingSpatialSyncHandle.Update(_world.Unmanaged);
        var buildingFence = _world.EntityManager.CreateEntityQuery(typeof(BuildingSpatialIndexFence)).GetSingletonRW<BuildingSpatialIndexFence>();
        buildingFence.ValueRW.Complete();

        _beltSpatialSyncHandle.Update(_world.Unmanaged);
        var beltFence = _world.EntityManager.CreateEntityQuery(typeof(BeltSpatialIndexFence)).GetSingletonRW<BeltSpatialIndexFence>();
        beltFence.ValueRW.Complete();
    }

    [Test]
    public void Test01_RecipeChange_PurgesAndEntersWaitingForPurgeOutput_BlocksBeltDeposit()
    {
        // Arrange: (1, 0)에 Recipe 1(Iron) Crafter 배치, StoredItemElement에 Iron_Ore 1개 보관
        var crafter = CreateCrafter(new int2(1, 0), recipeId: 1);
        var storedBuffer = _entityManager.GetBuffer<StoredItemElement>(crafter);
        storedBuffer.Add(new StoredItemElement(Entity.Null, ItemTypeEnum.Iron_Ore, 0));

        // (0, 0) 벨트에서 (1, 0) Crafter로 향하는 아이템(새 레시피 재료인 Copper_Ore) 배치 (Progress = 1.0f)
        var beltItem = CreateBeltItem(new int2(0, 0), DirectionEnum.Right, 1.0f, ItemTypeEnum.Copper_Ore);

        SyncSpatialIndices();

        // Act: Recipe 2 (Copper)로 변경 요청 발행
        var reqEntity = _entityManager.CreateEntity(typeof(ChangeCrafterRecipeRequest));
        _entityManager.SetComponentData(reqEntity, new ChangeCrafterRecipeRequest(crafter, newRecipeId: 2));

        // Phase 1 CommandGroup 실행
        _crafterCommandHandle.Update(_world.Unmanaged);
        _endStateApplyEcb.Update();

        // Assert 1: 기존 Iron_Ore가 ProductBuffer로 배출되고, Status가 WaitingForPurgeOutput으로 변경되었는지 확인
        var storedBufferAfter = _entityManager.GetBuffer<StoredItemElement>(crafter);
        var productBuffer = _entityManager.GetBuffer<ProductItemElement>(crafter);
        Assert.AreEqual(0, storedBufferAfter.Length, "Stored items must be purged clean.");
        Assert.AreEqual(1, productBuffer.Length, "Item must be in product buffer.");
        var state = _entityManager.GetComponentData<CrafterState>(crafter);
        Assert.AreEqual(CrafterStatusEnum.WaitingForPurgeOutput, state.Status);

        // Assert 2: StorageFilter는 Copper_Ore 허용으로 즉시 갱신되었는지 확인
        var filter = _entityManager.GetComponentData<StorageFilter>(crafter);
        Assert.IsTrue(filter.IsItemAllowed(ItemTypeEnum.Copper_Ore));

        // Act 2: Phase 2 DecisionGroup 실행 (BuildingItemInputDecisionSystem)
        _inputDecisionHandle.Update(_world.Unmanaged);

        // Assert 3: 새 레시피의 재료(Copper_Ore)이고 필터가 허용하더라도,
        // Crafter가 WaitingForPurgeOutput 상태이므로 입고가 완전 차단(CanDeposit == false)되어야 함!
        var inputDecision = _entityManager.GetComponentData<BuildingItemInputDecision>(beltItem);
        Assert.IsFalse(inputDecision.CanDeposit, "Input must be blocked while Crafter is WaitingForPurgeOutput.");
        Assert.AreEqual(crafter, inputDecision.TargetBuilding);
    }

    [Test]
    public void Test02_WhenProductBufferBecomesEmpty_CrafterReturnsToIdle_AndAcceptsNewIngredient()
    {
        // Arrange: (1, 0) Crafter에 WaitingForPurgeOutput 상태 설정 및 ProductItem 1개 존재
        var crafter = CreateCrafter(new int2(1, 0), recipeId: 2);
        var state = _entityManager.GetComponentData<CrafterState>(crafter);
        state.SelectedRecipeId = 2;
        state.ActiveRecipeId = 2;
        state.Status = CrafterStatusEnum.WaitingForPurgeOutput;
        _entityManager.SetComponentData(crafter, state);

        var filter = new StorageFilter(StorageFilterMode.Whitelist);
        filter.Mask.Set((byte)ItemTypeEnum.Copper_Ore, true);
        _entityManager.SetComponentData(crafter, filter);

        var productBuffer = _entityManager.GetBuffer<ProductItemElement>(crafter);
        productBuffer.Add(new ProductItemElement(Entity.Null, ItemTypeEnum.Iron_Ore, 2));

        var beltItem = CreateBeltItem(new int2(0, 0), DirectionEnum.Right, 1.0f, ItemTypeEnum.Copper_Ore);
        SyncSpatialIndices();

        // 1. 출력 버퍼가 차 있는 동안 CrafterDecisionSystem 실행 -> 상태 유지
        _crafterDecisionHandle.Update(_world.Unmanaged);
        state = _entityManager.GetComponentData<CrafterState>(crafter);
        Assert.AreEqual(CrafterStatusEnum.WaitingForPurgeOutput, state.Status);

        // 2. 출력 버퍼가 완전히 비워짐 (방안 A: productItems.Length == 0)
        productBuffer.Clear();

        // 3. CrafterDecisionSystem 실행 -> Idle 복귀
        _crafterDecisionHandle.Update(_world.Unmanaged);
        state = _entityManager.GetComponentData<CrafterState>(crafter);
        Assert.AreEqual(CrafterStatusEnum.WaitingForInput, state.Status, "Crafter should transition out of WaitingForPurgeOutput to WaitingForInput (ingredients not yet deposited).");

        // 4. BuildingItemInputDecisionSystem 실행 -> 입고 허용!
        _inputDecisionHandle.Update(_world.Unmanaged);
        var inputDecision = _entityManager.GetComponentData<BuildingItemInputDecision>(beltItem);
        Assert.IsTrue(inputDecision.CanDeposit, "Belt item should now be accepted into Crafter!");
        Assert.AreEqual(crafter, inputDecision.TargetBuilding);
    }
}
