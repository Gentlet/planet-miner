using NUnit.Framework;
using PlanetMiner.Config;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 제작 판단/진행·다중 부산물·출력 정체에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: 레시피/아이템 설정·재료/출력 슬롯을 준비해 비활성 Decision·진행/생산 결과·슬롯별 실물과 차단을 검사한다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class Phase5CrafterExecutionTests : EcsWorldTestFixture
{
    private Entity _itemConfig;
    private SystemHandle _crafterDecisionHandle;
    private SystemHandle _crafterExecutionHandle;
    private SystemHandle _crafterStateApplyHandle;
    private SystemHandle _lifecycleHandle;
    private EndStateApplyEntityCommandBufferSystem _endStateApplyEcb;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        CreateGameplayPrefabDatabases();

        // 레시피 설정은 각 테스트가 처음 게시할 때 선택한다.
        _itemConfig = ItemConfigInitSystem.InitializeItemRegistry(_entityManager);

        // 2. 시스템 핸들 획득
        _crafterDecisionHandle = _world.GetOrCreateSystem(typeof(CrafterDecisionSystem));
        _crafterExecutionHandle = _world.GetOrCreateSystem(typeof(CrafterExecutionSystem));
        _crafterStateApplyHandle = _world.GetOrCreateSystem(typeof(CrafterStateApplySystem));
        _lifecycleHandle = _world.GetOrCreateSystem(typeof(ItemLifecycleApplySystem));
        _endStateApplyEcb = _world.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>();
    }

    private Entity CreateCrafter(int recipeId = 1, float speed = 1.0f)
        => Entities.CreateCrafter(int2.zero, recipeId, speed);

    private Entity CreateStoredItem(Entity owner, ItemTypeEnum itemType, int slotIndex = 0)
        => Entities.CreateStoredItem(owner, itemType, slotIndex);

    private void RunDecisionPhase()
        => Simulation.Update(_crafterDecisionHandle);

    private void RunExecutionPhase(float deltaTime)
    {
        // 재료 소비/결과 기록의 ECB 경계를 진행한다. 생산 실물 생성은 뒤의 Lifecycle 호출과 구분한다.
        Simulation.Update(_crafterExecutionHandle, deltaTime);
        Simulation.Playback(_endStateApplyEcb);
    }

    private void RunStatusApplyPhase()
    {
        Simulation.Update(_crafterStateApplyHandle);
        Simulation.Playback(_endStateApplyEcb);
    }

    private void RunStateApplyPhase()
    {
        Simulation.Update(_crafterStateApplyHandle);
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
            RunStatusApplyPhase();
            elapsed += currentDt;
        }
    }

    [Test]
    public void Test02_DisabledCrafterDecision_IsExcludedFromExecution()
    {
        RecipeInitSystem.InitializeRecipeRegistry(_entityManager);
        var crafter = CreateCrafter(recipeId: 1);
        CreateStoredItem(crafter, ItemTypeEnum.Iron_Ore);

        RunDecisionPhase();

        Assert.IsTrue(_entityManager.IsComponentEnabled<CrafterDecision>(crafter));
        _entityManager.SetComponentEnabled<CrafterDecision>(crafter, false);

        RunExecutionPhase(deltaTime: 0.1f);

        var state = _entityManager.GetComponentData<CrafterState>(crafter);
        Assert.IsFalse(state.IsCraftingActive);
        Assert.AreEqual(0.0f, state.Progress, 0.0001f);
        Assert.AreEqual(1, _entityManager.GetBuffer<StoredItemElement>(crafter).Length);
    }

    [Test]
    public void Test07_MultipleByproducts_SpawnsAllOutputsToRespectiveSlots()
    {
        // Arrange: 레시피 10 (주완성품 Iron 1 + 부산품1 Stone 1 + 부산품2 Copper 1)
        string customJson = @"
        {
          ""recipes"": [
            {
              ""id"": 10,
              ""outputItemType"": ""Iron"",
              ""outputAmount"": 1,
              ""craftTime"": 1.0,
              ""ingredients"": [
                { ""itemType"": ""Iron_Ore"", ""amount"": 2 }
              ],
              ""byproducts"": [
                { ""itemType"": ""Stone"", ""amount"": 1 },
                { ""itemType"": ""Copper"", ""amount"": 1 }
              ]
            }
          ]
        }";

        RecipeInitSystem.InitializeRecipeRegistry(_entityManager, customJson);

        var crafter = CreateCrafter(recipeId: 10);
        CreateStoredItem(crafter, ItemTypeEnum.Iron_Ore);
        CreateStoredItem(crafter, ItemTypeEnum.Iron_Ore);

        // 1. 제작 착수 및 1.0s 경과 (진행도 1.0f 도달)
        StepSimulation(1.0f, stepDt: 0.1f);

        // 2. 완료 감지 및 배출 (Decision -> Execution -> StateApply)
        RunDecisionPhase();
        var decisionComp = _entityManager.GetComponentData<CrafterDecision>(crafter);
        Assert.IsTrue(decisionComp.CanProduceOutput, "CanProduceOutput must be true when progress reaches 1.0f.");

        RunExecutionPhase(deltaTime: 0.05f);
        RunStateApplyPhase();

        // 2. 검증: ProductBuffer에 주생산품 1개 + 부산품 2개 = 총 3개 생성
        var productBuffer = _entityManager.GetBuffer<ProductItemElement>(crafter);
        Assert.AreEqual(3, productBuffer.Length, "ProductBuffer must contain 3 outputs (1 primary + 2 byproducts).");

        // Slot 0: Iron (Primary)
        Assert.AreEqual(ItemTypeEnum.Iron, productBuffer[0].ItemType);
        Assert.AreEqual(0, productBuffer[0].SlotIndex);

        // Slot 1: Stone (Byproduct 1)
        Assert.AreEqual(ItemTypeEnum.Stone, productBuffer[1].ItemType);
        Assert.AreEqual(1, productBuffer[1].SlotIndex);

        // Slot 2: Copper (Byproduct 2)
        Assert.AreEqual(ItemTypeEnum.Copper, productBuffer[2].ItemType);
        Assert.AreEqual(2, productBuffer[2].SlotIndex);

        // 상태 리셋 확인
        var state = _entityManager.GetComponentData<CrafterState>(crafter);
        Assert.IsFalse(state.IsCraftingActive);
        Assert.AreEqual(0.0f, state.Progress, 0.0001f);
    }

    [Test]
    public void Test08_MultipleByproducts_Backpressure_AllOrNothing_WaitsIfAnySlotFull()
    {
        // Arrange: 레시피 10 (주완성품 Iron 1 + 부산품1 Stone 1 + 부산품2 Copper 1)
        string customJson = @"
        {
          ""recipes"": [
            {
              ""id"": 10,
              ""outputItemType"": ""Iron"",
              ""outputAmount"": 1,
              ""craftTime"": 1.0,
              ""ingredients"": [
                { ""itemType"": ""Iron_Ore"", ""amount"": 2 }
              ],
              ""byproducts"": [
                { ""itemType"": ""Stone"", ""amount"": 1 },
                { ""itemType"": ""Copper"", ""amount"": 1 }
              ]
            }
          ]
        }";

        RecipeInitSystem.InitializeRecipeRegistry(_entityManager, customJson);

        var crafter = CreateCrafter(recipeId: 10);
        CreateStoredItem(crafter, ItemTypeEnum.Iron_Ore);
        CreateStoredItem(crafter, ItemTypeEnum.Iron_Ore);

        // 1. 착수 및 1.0s까지 진행 완료
        StepSimulation(1.0f, stepDt: 0.1f);

        var stateMid = _entityManager.GetComponentData<CrafterState>(crafter);
        Assert.AreEqual(1.0f, stateMid.Progress, 0.0001f);

        var itemRegistry = _entityManager.GetComponentData<ItemRegistry>(_itemConfig);
        int maxStack = itemRegistry.GetMaxStack(
            _entityManager.GetBuffer<ItemConfigElement>(_itemConfig, true), ItemTypeEnum.Copper);
        // 2. 부산품2(Slot 2: Copper)만 MaxStack만큼 채움 (Slot 0, 1은 여유 공간 있음)
        NativeArray<Entity> dummyItems = new NativeArray<Entity>(maxStack, Allocator.Temp);
        for (int i = 0; i < maxStack; i++)
        {
            var dummyItem = _entityManager.CreateEntity(typeof(ItemIdentity), typeof(ItemOwnership));
            _entityManager.SetComponentData(dummyItem, new ItemIdentity(ItemTypeEnum.Copper));
            _entityManager.SetComponentData(dummyItem, ItemOwnership.Stored(crafter));
            dummyItems[i] = dummyItem;
        }

        var productBuffer = _entityManager.GetBuffer<ProductItemElement>(crafter);
        for (int i = 0; i < maxStack; i++)
        {
            productBuffer.Add(new ProductItemElement(dummyItems[i], ItemTypeEnum.Copper, slotIndex: 2));
        }
        dummyItems.Dispose();

        // 3. Decision Phase 실행: All-or-Nothing 정책에 의해 Slot 2 만석으로 인해 배출 거부 확인
        RunDecisionPhase();

        var decisionWaiting = _entityManager.GetComponentData<CrafterDecision>(crafter);
        var stateDecisionWaiting = _entityManager.GetComponentData<CrafterStateDecision>(crafter);

        Assert.AreEqual(CrafterStatusEnum.WaitingForOutput, stateDecisionWaiting.NextStatus);
        Assert.IsTrue(_entityManager.IsComponentEnabled<CrafterStateDecision>(crafter));
        Assert.IsFalse(decisionWaiting.CanProduceOutput, "CanProduceOutput must be false when any byproduct slot is full (All-or-Nothing).");

        RunStatusApplyPhase();

        var stateWaiting = _entityManager.GetComponentData<CrafterState>(crafter);
        Assert.AreEqual(CrafterStatusEnum.WaitingForOutput, stateWaiting.Status, "Status must be applied during StateApply when any byproduct slot is full.");

        // Execution 실행해도 추가 생산 없어야 함
        RunExecutionPhase(deltaTime: 0.1f);
        var stateAfterExec = _entityManager.GetComponentData<CrafterState>(crafter);
        Assert.AreEqual(1.0f, stateAfterExec.Progress, 0.0001f);
        productBuffer = _entityManager.GetBuffer<ProductItemElement>(crafter);
        Assert.AreEqual(maxStack, productBuffer.Length);

        // 4. Slot 2에서 1개 제거하여 공간 확보 후 재검사
        productBuffer.RemoveAt(0);
        Assert.AreEqual(maxStack - 1, productBuffer.Length);

        RunDecisionPhase();
        var decisionResume = _entityManager.GetComponentData<CrafterDecision>(crafter);
        Assert.IsTrue(decisionResume.CanProduceOutput, "CanProduceOutput must become true once all output slots have space.");

        RunExecutionPhase(deltaTime: 0.05f);
        RunStateApplyPhase();

        // 5. 제작 완료되어 Iron(Slot 0), Stone(Slot 1), Copper(Slot 2)가 각각 추가됨 (기존 적재량 - 1 + 3개)
        productBuffer = _entityManager.GetBuffer<ProductItemElement>(crafter);
        Assert.AreEqual(maxStack + 2, productBuffer.Length, "All 3 outputs must be produced once space is available.");
    }
}
