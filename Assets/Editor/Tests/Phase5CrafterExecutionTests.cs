using NUnit.Framework;
using PlanetMiner.Config;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Task 5.2: Crafter Decision & Execution System 통합 단위/파이프라인 검증 테스트.
/// - CrafterDecisionSystem: 레시피 유효성, 입력 재료 보유량, 출력 버퍼 수용 공간 판정
/// - CrafterExecutionSystem: 제작 착수 시 선소비(피드백 3번 완전 충족), 진행도 누적, ProductResult 기록(정책 B)
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
        => Entities.CreateCrafter(int2.zero, recipeId, speed);

    private Entity CreateStoredItem(Entity owner, ItemTypeEnum itemType, int slotIndex = 0)
        => Entities.CreateStoredItem(owner, itemType, slotIndex);

    private void RunDecisionPhase()
        => Simulation.Update(_crafterDecisionHandle);

    private void RunExecutionPhase(float deltaTime)
    {
        Simulation.Update(_crafterExecutionHandle, deltaTime);
        Simulation.Playback(_endStateApplyEcb);
    }

    private void RunLifecyclePhase()
    {
        Simulation.Update(_lifecycleHandle);
        Simulation.Playback(_endStateApplyEcb);
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

        // Execution 단계에서는 실제 Item을 만들지 않고 ProductResult만 기록합니다.
        var productResults = _entityManager.GetBuffer<ProductResult>(crafter);
        Assert.AreEqual(1, productResults.Length, "Crafter should record one primary ProductResult before StateApply.");
        Assert.AreEqual(ItemTypeEnum.Iron, productResults[0].ItemType);
        Assert.AreEqual(1, productResults[0].Count);
        Assert.AreEqual(0, productResults[0].SlotIndex);
        Assert.AreEqual(0, _entityManager.GetBuffer<ProductItemElement>(crafter).Length, "Product buffer must remain unchanged before StateApply.");

        RunLifecyclePhase();

        // 3. StateApply 이후 ProductResult가 소비되고 ProductItemElement에 완성품이 반영되는지 확인
        productResults = _entityManager.GetBuffer<ProductResult>(crafter);
        Assert.AreEqual(0, productResults.Length, "ProductResult must be consumed by ItemLifecycleApplySystem.");

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


}
