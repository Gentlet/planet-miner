using NUnit.Framework;
using PlanetMiner.Config;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Task 5.2: Crafter Decision & Execution System 통합 단위/파이프라인 검증 테스트.
/// - CrafterDecisionSystem: 레시피 유효성, 입력 재료 보유량, 출력 버퍼 수용 공간 판정
/// - CrafterExecutionSystem: 제작 착수 시 선소비(피드백 3번 완전 충족), 진행도 누적, 스폰(정책 B)
/// - 레시피 변경 시 재료 배출(Purge to Output) 및 StorageFilter 자동 동기화
/// </summary>
public class Phase5CrafterExecutionTests : EcsWorldTestFixture
{
    private BlobAssetReference<RecipeRegistryBlob> _recipeBlob;
    private SystemHandle _crafterDecisionHandle;
    private SystemHandle _crafterExecutionHandle;
    private SystemHandle _lifecycleHandle;
    private EndStateApplyEntityCommandBufferSystem _endStateApplyEcb;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();

        // 1. 레시피 레지스트리 전역 싱글톤 초기화
        _recipeBlob = RecipeInitSystem.InitializeRecipeRegistry(_entityManager);

        // 2. 시스템 핸들 획득
        _crafterDecisionHandle = _world.GetOrCreateSystem(typeof(CrafterDecisionSystem));
        _crafterExecutionHandle = _world.GetOrCreateSystem(typeof(CrafterExecutionSystem));
        _lifecycleHandle = _world.GetOrCreateSystem(typeof(ItemLifecycleApplySystem));
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

    private Entity CreateCrafter(int recipeId = 1, float speed = 1.0f)
    {
        var entity = _entityManager.CreateEntity(
            typeof(BuildingType),
            typeof(CrafterState),
            typeof(CrafterDecision),
            typeof(Storage),
            typeof(StorageFilter));

        _entityManager.SetComponentData(entity, new BuildingType(BuildingTypeEnum.Crafter));
        _entityManager.SetComponentData(entity, new CrafterState(recipeId, speed));
        _entityManager.SetComponentData(entity, new CrafterDecision(false, recipeId));
        _entityManager.SetComponentEnabled<CrafterDecision>(entity, false);
        _entityManager.SetComponentData(entity, new Storage(slotCount: 4));
        _entityManager.SetComponentData(entity, new StorageFilter(StorageFilterMode.Whitelist));

        // 재료 버퍼와 출력 버퍼 물리 분리 부착
        _entityManager.AddBuffer<StoredItemElement>(entity);
        _entityManager.AddBuffer<ProductItemElement>(entity);

        return entity;
    }

    private Entity CreateStoredItem(Entity owner, ItemTypeEnum itemType, int slotIndex = 0)
    {
        var itemEntity = _entityManager.CreateEntity(
            typeof(ItemIdentity),
            typeof(ItemOwnership),
            typeof(GridPosition),
            typeof(BeltMovementState),
            typeof(BeltMovementDecision),
            typeof(BuildingItemInputDecision),
            typeof(DestroyItemRequest),
            typeof(TransferOwnershipRequest));

        _entityManager.SetComponentData(itemEntity, new ItemIdentity(itemType));
        _entityManager.SetComponentData(itemEntity, ItemOwnership.Stored(owner));
        _entityManager.SetComponentData(itemEntity, new GridPosition(int2.zero));

        _entityManager.SetComponentEnabled<BeltMovementState>(itemEntity, false);
        _entityManager.SetComponentEnabled<BeltMovementDecision>(itemEntity, false);
        _entityManager.SetComponentEnabled<BuildingItemInputDecision>(itemEntity, false);
        _entityManager.SetComponentEnabled<DestroyItemRequest>(itemEntity, false);
        _entityManager.SetComponentEnabled<TransferOwnershipRequest>(itemEntity, false);

        var storedBuffer = _entityManager.GetBuffer<StoredItemElement>(owner);
        storedBuffer.Add(new StoredItemElement(itemEntity, itemType, slotIndex));

        return itemEntity;
    }

    private void RunDecisionPhase()
    {
        _crafterDecisionHandle.Update(_world.Unmanaged);
    }

    private void RunExecutionPhase(float deltaTime)
    {
        _world.SetTime(new Unity.Core.TimeData(0.1, deltaTime));
        _crafterExecutionHandle.Update(_world.Unmanaged);
        _endStateApplyEcb.Update();
    }

    private void RunLifecyclePhase()
    {
        _lifecycleHandle.Update(_world.Unmanaged);
        _endStateApplyEcb.Update();
    }

    private void StepSimulation(float totalDuration, float stepDt = 0.1f)
    {
        float elapsed = 0.0f;
        while (elapsed < totalDuration - 0.0001f)
        {
            float currentDt = math.min(stepDt, totalDuration - elapsed);
            RunDecisionPhase();
            RunExecutionPhase(currentDt);
            elapsed += currentDt;
        }
    }

    [Test]
    public void Test01_NoRecipe_Or_InsufficientIngredients_SetsWaitingStatus()
    {
        // 1. 레시피 없는 제작기
        var noRecipeCrafter = CreateCrafter(recipeId: 0);
        RunDecisionPhase();

        var stateNo = _entityManager.GetComponentData<CrafterState>(noRecipeCrafter);
        var decisionNo = _entityManager.GetComponentData<CrafterDecision>(noRecipeCrafter);
        Assert.AreEqual(CrafterStatusEnum.NoRecipe, stateNo.Status);
        Assert.IsFalse(decisionNo.CanCraft);

        // 2. Recipe 1(Iron_Ore 1 -> Iron 1), 하지만 재료 없음
        var crafter = CreateCrafter(recipeId: 1);
        RunDecisionPhase();

        var state = _entityManager.GetComponentData<CrafterState>(crafter);
        var decision = _entityManager.GetComponentData<CrafterDecision>(crafter);
        Assert.AreEqual(CrafterStatusEnum.WaitingForInput, state.Status);
        Assert.IsFalse(decision.CanCraft);
        Assert.IsFalse(decision.CanStartCraft);
    }

    [Test]
    public void Test02_IngredientsSupplied_StartsCraftingAndConsumesMaterialsImmediately()
    {
        // Arrange: Recipe 1 (Iron_Ore 1개 필요)
        var crafter = CreateCrafter(recipeId: 1);
        var oreItem = CreateStoredItem(crafter, ItemTypeEnum.Iron_Ore, slotIndex: 0);

        Assert.AreEqual(1, _entityManager.GetBuffer<StoredItemElement>(crafter).Length);

        // Act 1: Decision Phase
        RunDecisionPhase();

        var decision = _entityManager.GetComponentData<CrafterDecision>(crafter);
        Assert.IsTrue(decision.CanCraft);
        Assert.IsTrue(decision.CanStartCraft);

        // Act 2: Execution Phase (제작 착수 및 재료 선소비)
        RunExecutionPhase(deltaTime: 0.1f);

        // Assert: StoredItemElement에서 아이템이 즉시 제거되었는지 확인 (ECB 반영 후 재조회)
        var storedBuffer = _entityManager.GetBuffer<StoredItemElement>(crafter);
        Assert.AreEqual(0, storedBuffer.Length, "StoredItemElement must be cleared immediately upon craft start.");

        var state = _entityManager.GetComponentData<CrafterState>(crafter);
        Assert.IsTrue(state.IsCraftingActive, "IsCraftingActive must be true after consuming ingredients.");
        Assert.AreEqual(0.1f, state.Progress, 0.0001f);

        // Lifecycle Phase 실행 시 파괴 요청 소비 확인
        RunLifecyclePhase();
        Assert.IsFalse(_entityManager.Exists(oreItem), "Consumed ingredient entity must be destroyed.");
    }

    [Test]
    public void Test03_CraftingProgress_AdvancesOverTime()
    {
        // Arrange: Recipe 1 (CraftTime = 1.0s)
        var crafter = CreateCrafter(recipeId: 1);
        CreateStoredItem(crafter, ItemTypeEnum.Iron_Ore);

        // Frame 1~3: 0.1s씩 3프레임 진행 (총 0.3s)
        StepSimulation(0.3f, stepDt: 0.1f);

        var state1 = _entityManager.GetComponentData<CrafterState>(crafter);
        Assert.IsTrue(state1.IsCraftingActive);
        Assert.AreEqual(0.3f, state1.Progress, 0.0001f);

        // Frame 4~7: 추가 0.4s 진행 (누적 0.7s)
        StepSimulation(0.4f, stepDt: 0.1f);

        var state2 = _entityManager.GetComponentData<CrafterState>(crafter);
        Assert.AreEqual(0.7f, state2.Progress, 0.0001f);

        var decision2 = _entityManager.GetComponentData<CrafterDecision>(crafter);
        Assert.IsTrue(decision2.CanAdvance);
        Assert.IsFalse(decision2.CanStartCraft); // 이미 진행 중이므로 중복 시작 안함
    }

    [Test]
    public void Test04_CraftingComplete_SpawnsProductToProductBuffer()
    {
        // Arrange: Recipe 1 (CraftTime = 1.0s, Output: Iron 1개)
        var crafter = CreateCrafter(recipeId: 1);
        CreateStoredItem(crafter, ItemTypeEnum.Iron_Ore);

        // 1. 착수 및 1.0s까지 진행 완료
        StepSimulation(1.0f, stepDt: 0.1f);

        var stateMid = _entityManager.GetComponentData<CrafterState>(crafter);
        Assert.AreEqual(1.0f, stateMid.Progress, 0.0001f);

        // 2. 완료 감지 및 배출 (Decision -> Execution -> Lifecycle)
        RunDecisionPhase();
        var decisionComp = _entityManager.GetComponentData<CrafterDecision>(crafter);
        Assert.IsTrue(decisionComp.CanProduceOutput, "CanProduceOutput must be true when progress reaches 1.0f.");

        RunExecutionPhase(deltaTime: 0.05f);
        RunLifecyclePhase();

        // 3. ProductItemElement 버퍼에 완성품이 스폰되었는지 확인
        var productBuffer = _entityManager.GetBuffer<ProductItemElement>(crafter);
        Assert.AreEqual(1, productBuffer.Length, "Product buffer must contain 1 finished product.");
        Assert.AreEqual(ItemTypeEnum.Iron, productBuffer[0].ItemType);
        Assert.AreEqual(0, productBuffer[0].SlotIndex, "Primary product must have SlotIndex 0.");

        // 제작 상태 리셋 확인
        var stateEnd = _entityManager.GetComponentData<CrafterState>(crafter);
        Assert.IsFalse(stateEnd.IsCraftingActive, "IsCraftingActive must be reset to false.");
        Assert.AreEqual(0.0f, stateEnd.Progress, 0.0001f, "Progress must be reset to 0.0f.");
    }

    [Test]
    public void Test05_Backpressure_ProductBufferFull_WaitsForOutputAtProgress1()
    {
        // Arrange: Recipe 1, 출력 버퍼에 이미 MaxStack(50개) 적재되어 있는 상황
        var crafter = CreateCrafter(recipeId: 1);
        CreateStoredItem(crafter, ItemTypeEnum.Iron_Ore);

        // 1. 착수 및 1.0s까지 진행 완료
        StepSimulation(1.0f, stepDt: 0.1f);

        var stateMid = _entityManager.GetComponentData<CrafterState>(crafter);
        Assert.AreEqual(1.0f, stateMid.Progress, 0.0001f);

        // 2. 출력 버퍼를 인위적으로 50개(MaxStack) 채움 (구조적 변경 후 버퍼 획득)
        NativeArray<Entity> dummyItems = new NativeArray<Entity>(50, Allocator.Temp);
        for (int i = 0; i < 50; i++)
        {
            var dummyItem = _entityManager.CreateEntity(typeof(ItemIdentity), typeof(ItemOwnership));
            _entityManager.SetComponentData(dummyItem, new ItemIdentity(ItemTypeEnum.Iron));
            _entityManager.SetComponentData(dummyItem, ItemOwnership.Stored(crafter));
            dummyItems[i] = dummyItem;
        }

        var productBuffer = _entityManager.GetBuffer<ProductItemElement>(crafter);
        for (int i = 0; i < 50; i++)
        {
            productBuffer.Add(new ProductItemElement(dummyItems[i], ItemTypeEnum.Iron, 0));
        }
        dummyItems.Dispose();

        // 3. 만석 상태에서 Decision Phase 실행
        RunDecisionPhase();

        var stateWaiting = _entityManager.GetComponentData<CrafterState>(crafter);
        var decisionWaiting = _entityManager.GetComponentData<CrafterDecision>(crafter);

        // 정책 B: 1.0f 유지 및 WaitingForOutput 상태로 대기 확인
        Assert.AreEqual(CrafterStatusEnum.WaitingForOutput, stateWaiting.Status);
        Assert.IsFalse(decisionWaiting.CanProduceOutput, "CanProduceOutput must be false when output is full.");

        RunExecutionPhase(deltaTime: 0.1f);
        var stateAfterExec = _entityManager.GetComponentData<CrafterState>(crafter);
        Assert.AreEqual(1.0f, stateAfterExec.Progress, 0.0001f, "Progress must remain at 1.0f while waiting for output.");
        
        productBuffer = _entityManager.GetBuffer<ProductItemElement>(crafter);
        Assert.AreEqual(50, productBuffer.Length, "No additional product must be spawned while full.");

        // 4. 출력 버퍼에서 1개 제거 (방출 시뮬레이션) 후 다시 실행
        productBuffer.RemoveAt(0);
        Assert.AreEqual(49, productBuffer.Length);

        RunDecisionPhase();
        var decisionResume = _entityManager.GetComponentData<CrafterDecision>(crafter);
        Assert.IsTrue(decisionResume.CanProduceOutput, "CanProduceOutput must become true once space is freed.");

        RunExecutionPhase(deltaTime: 0.05f);
        RunLifecyclePhase();

        productBuffer = _entityManager.GetBuffer<ProductItemElement>(crafter);
        Assert.AreEqual(50, productBuffer.Length, "Product must be spawned once space becomes available.");
    }

    [Test]
    public void Test06_RecipeChange_PurgesStoredItemsToProductBufferAndUpdatesFilter()
    {
        // Arrange: Recipe 1 (Iron)으로 설정된 제작기
        var crafter = CreateCrafter(recipeId: 1);

        // StoredItemElement에 아이템 2종(Iron_Ore 2개, Copper_Ore 1개) 보관
        CreateStoredItem(crafter, ItemTypeEnum.Iron_Ore, 0);
        CreateStoredItem(crafter, ItemTypeEnum.Iron_Ore, 0);
        CreateStoredItem(crafter, ItemTypeEnum.Copper_Ore, 1);

        var storedBuffer = _entityManager.GetBuffer<StoredItemElement>(crafter);
        var productBuffer = _entityManager.GetBuffer<ProductItemElement>(crafter);
        Assert.AreEqual(3, storedBuffer.Length);
        Assert.AreEqual(0, productBuffer.Length);

        // Act: 레시피를 1에서 2(Copper)로 변경
        var state = _entityManager.GetComponentData<CrafterState>(crafter);
        state.SelectedRecipeId = 2; // Copper_Ore -> Copper
        _entityManager.SetComponentData(crafter, state);

        // ExecutionGroup 실행 -> 레시피 변경 감지 및 배출 이관 동작
        RunExecutionPhase(deltaTime: 0.05f);

        // Assert 1: StoredItemElement가 완전히 비워졌는지 확인
        Assert.AreEqual(0, storedBuffer.Length, "StoredItemElement must be purged clean on recipe change.");

        // Assert 2: ProductItemElement로 3개 아이템이 모두 이관되었는지 확인
        Assert.AreEqual(3, productBuffer.Length, "Purged items must be transferred to ProductItemElement.");

        // Slot 2, 3으로 분리 배정되어 단일 품목(Slot pollution 방지) 규칙을 준수하는지 확인
        int ironCount = 0;
        int copperCount = 0;
        for (int i = 0; i < productBuffer.Length; i++)
        {
            Assert.GreaterOrEqual(productBuffer[i].SlotIndex, 2, "Purged items must use purge slots (>= 2).");
            if (productBuffer[i].ItemType == ItemTypeEnum.Iron_Ore) ironCount++;
            if (productBuffer[i].ItemType == ItemTypeEnum.Copper_Ore) copperCount++;
        }
        Assert.AreEqual(2, ironCount);
        Assert.AreEqual(1, copperCount);

        // Assert 3: StorageFilter가 새 레시피(Copper_Ore)로 자동 갱신되었는지 확인
        var filter = _entityManager.GetComponentData<StorageFilter>(crafter);
        Assert.AreEqual(StorageFilterMode.Whitelist, filter.Mode);
        Assert.IsTrue(filter.IsItemAllowed(ItemTypeEnum.Copper_Ore), "Copper_Ore must be allowed by whitelist.");
        Assert.IsFalse(filter.IsItemAllowed(ItemTypeEnum.Iron_Ore), "Iron_Ore must be blocked by whitelist.");

        // Assert 4: ActiveRecipeId가 2로 동기화되었는지 확인
        var stateEnd = _entityManager.GetComponentData<CrafterState>(crafter);
        Assert.AreEqual(2, stateEnd.ActiveRecipeId);
    }
}
