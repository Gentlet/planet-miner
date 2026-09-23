using NUnit.Framework;
using PlanetMiner.Config;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Phase 5 Crafter 레시피 변경 및 입고 차단/복귀 파이프라인 검증 테스트 (피드백 1번).
/// - 레시피 변경 시 CommandGroup에서 원자적 처리 (Filter 갱신, Byproduct to ProductBuffer, 진행도 리셋)
/// - ProductItemElement에 잔여물이 있는 동안 CrafterStatusEnum.WaitingForByproductOutput 상태 진입
/// - WaitingForByproductOutput 동안 BuildingItemInputDecisionSystem에서 재료 입고 완전 차단 (방안 A)
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
        var filter = new StorageFilter(StorageFilterMode.Whitelist);
        filter.Mask.Set((byte)ItemTypeEnum.Iron_Ore, true);
        return Entities.CreateCrafter(position, recipeId, filter: filter);
    }

    private Entity CreateBelt(int2 position, DirectionEnum direction)
        => Entities.CreateBelt(position, direction);

    private Entity CreateBeltItem(int2 position, DirectionEnum direction, float progress, ItemTypeEnum itemType)
    {
        Entities.CreateBelt(position, direction);
        return Entities.CreateBeltItem(position, direction, progress, 0.0f, itemType);
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
    public void Test01_RecipeChange_EntersWaitingForByproductOutput_BlocksBeltDeposit()
    {
        // Arrange: (1, 0)에 Recipe 1(Iron) Crafter 배치.
        // 기존 CrafterExecution 레시피 변경 테스트의 검증도 이 파이프라인 테스트로 통합합니다.
        var crafter = CreateCrafter(new int2(1, 0), recipeId: 1);
        Entities.CreateStoredItem(crafter, ItemTypeEnum.Iron_Ore, 0);
        Entities.CreateStoredItem(crafter, ItemTypeEnum.Iron_Ore, 0);
        Entities.CreateStoredItem(crafter, ItemTypeEnum.Copper_Ore, 1);

        var storedBuffer = _entityManager.GetBuffer<StoredItemElement>(crafter);
        Assert.AreEqual(3, storedBuffer.Length);

        // (0, 0) 벨트에서 (1, 0) Crafter로 향하는 아이템(새 레시피 재료인 Copper_Ore) 배치 (Progress = 1.0f)
        var beltItem = CreateBeltItem(new int2(0, 0), DirectionEnum.Right, 1.0f, ItemTypeEnum.Copper_Ore);

        SyncSpatialIndices();

        // Act: Recipe 2 (Copper)로 변경 요청 발행
        var reqEntity = _entityManager.CreateEntity(typeof(ChangeCrafterRecipeRequest));
        _entityManager.SetComponentData(reqEntity, new ChangeCrafterRecipeRequest(crafter, newRecipeId: 2));

        // Phase 1 CommandGroup 실행
        _crafterCommandHandle.Update(_world.Unmanaged);
        _endStateApplyEcb.Update();

        // Assert 1: 기존 Iron_Ore가 ProductBuffer로 배출되고, Status가 WaitingForByproductOutput으로 변경되었는지 확인
        var storedBufferAfter = _entityManager.GetBuffer<StoredItemElement>(crafter);
        var productBuffer = _entityManager.GetBuffer<ProductItemElement>(crafter);
        Assert.AreEqual(0, storedBufferAfter.Length, "Stored items must be emptied clean.");
        Assert.AreEqual(3, productBuffer.Length, "All byproduct items must move to ProductItemElement.");

        int ironCount = 0;
        int copperCount = 0;
        for (int i = 0; i < productBuffer.Length; i++)
        {
            Assert.GreaterOrEqual(productBuffer[i].SlotIndex, 1, "Byproduct items must use byproduct slots (>= 1).");
            if (productBuffer[i].ItemType == ItemTypeEnum.Iron_Ore) ironCount++;
            if (productBuffer[i].ItemType == ItemTypeEnum.Copper_Ore) copperCount++;
        }

        Assert.AreEqual(2, ironCount);
        Assert.AreEqual(1, copperCount);

        var state = _entityManager.GetComponentData<CrafterState>(crafter);
        Assert.AreEqual(2, state.ActiveRecipeId);
        Assert.AreEqual(2, state.SelectedRecipeId);
        Assert.AreEqual(CrafterStatusEnum.WaitingForByproductOutput, state.Status);

        // Assert 2: StorageFilter는 Copper_Ore 허용으로 즉시 갱신되었는지 확인
        var filter = _entityManager.GetComponentData<StorageFilter>(crafter);
        Assert.AreEqual(StorageFilterMode.Whitelist, filter.Mode);
        Assert.IsTrue(filter.IsItemAllowed(ItemTypeEnum.Copper_Ore));
        Assert.IsFalse(filter.IsItemAllowed(ItemTypeEnum.Iron_Ore));
        Assert.IsFalse(_entityManager.Exists(reqEntity), "Recipe change request must be consumed.");

        // Act 2: Phase 2 DecisionGroup 실행 (BuildingItemInputDecisionSystem)
        _inputDecisionHandle.Update(_world.Unmanaged);

        // Assert 3: 새 레시피의 재료(Copper_Ore)이고 필터가 허용하더라도,
        // Crafter가 WaitingForByproductOutput 상태이므로 입고가 완전 차단(CanDeposit == false)되어야 함!
        var inputDecision = _entityManager.GetComponentData<BuildingItemInputDecision>(beltItem);
        Assert.IsFalse(inputDecision.CanDeposit, "Input must be blocked while Crafter is WaitingForByproductOutput.");
        Assert.AreEqual(crafter, inputDecision.TargetBuilding);
    }

    [Test]
    public void Test02_WhenProductBufferBecomesEmpty_CrafterReturnsToIdle_AndAcceptsNewIngredient()
    {
        // Arrange: (1, 0) Crafter에 WaitingForByproductOutput 상태 설정 및 ProductItem 1개 존재
        var crafter = CreateCrafter(new int2(1, 0), recipeId: 2);
        var state = _entityManager.GetComponentData<CrafterState>(crafter);
        state.SelectedRecipeId = 2;
        state.ActiveRecipeId = 2;
        state.Status = CrafterStatusEnum.WaitingForByproductOutput;
        _entityManager.SetComponentData(crafter, state);

        var filter = new StorageFilter(StorageFilterMode.Whitelist);
        filter.Mask.Set((byte)ItemTypeEnum.Copper_Ore, true);
        _entityManager.SetComponentData(crafter, filter);

        var productBuffer = _entityManager.GetBuffer<ProductItemElement>(crafter);
        productBuffer.Add(new ProductItemElement(Entity.Null, ItemTypeEnum.Iron_Ore, 1));

        var beltItem = CreateBeltItem(new int2(0, 0), DirectionEnum.Right, 1.0f, ItemTypeEnum.Copper_Ore);
        SyncSpatialIndices();

        // 1. 출력 버퍼가 차 있는 동안 CrafterDecisionSystem 실행 -> 상태 유지
        _crafterDecisionHandle.Update(_world.Unmanaged);
        state = _entityManager.GetComponentData<CrafterState>(crafter);
        Assert.AreEqual(CrafterStatusEnum.WaitingForByproductOutput, state.Status);

        // 2. 출력 버퍼가 완전히 비워짐 (방안 A: productItems.Length == 0)
        productBuffer.Clear();

        // 3. CrafterDecisionSystem 실행 -> Idle 복귀
        _crafterDecisionHandle.Update(_world.Unmanaged);
        state = _entityManager.GetComponentData<CrafterState>(crafter);
        Assert.AreEqual(CrafterStatusEnum.WaitingForInput, state.Status, "Crafter should transition out of WaitingForByproductOutput to WaitingForInput (ingredients not yet deposited).");

        // 4. BuildingItemInputDecisionSystem 실행 -> 입고 허용!
        _inputDecisionHandle.Update(_world.Unmanaged);
        var inputDecision = _entityManager.GetComponentData<BuildingItemInputDecision>(beltItem);
        Assert.IsTrue(inputDecision.CanDeposit, "Belt item should now be accepted into Crafter!");
        Assert.AreEqual(crafter, inputDecision.TargetBuilding);
    }
}
