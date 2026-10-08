using Unity.Entities;

namespace PlanetMiner.Tests
{
    /// <summary>
    /// 역할·목적: 테스트가 시간·Update·Job 완료·ECB 재생을 명시적으로 제어할 실행 helper.
    /// 호출·입출력: 테스트 World의 SystemHandle/그룹을 실행해 상태를 남긴다. 단일 Update는 Job 완료/ECB 재생을 자동 수행하지 않는다.
    /// 수명·범위: World는 Fixture가 소유/해제하고 Driver는 참조만 보관한다. 그룹 builder는 선택 시스템의 실제 SortSystems/Command·건물 ECB 경계를 구성한다.
    /// </summary>
    public sealed class TestSimulationDriver
    {
        private readonly World _world;

        public TestSimulationDriver(World world)
        {
            _world = world;
        }

        public void SetDeltaTime(float deltaTime, double elapsedTime = 0.1)
        {
            _world.SetTime(new Unity.Core.TimeData(elapsedTime, deltaTime));
        }

        public void Update(SystemHandle systemHandle)
        {
            systemHandle.Update(_world.Unmanaged);
        }

        public void Update(SystemHandle systemHandle, float deltaTime, double elapsedTime = 0.1)
        {
            SetDeltaTime(deltaTime, elapsedTime);
            Update(systemHandle);
        }

        public void UpdateAndComplete(SystemHandle systemHandle)
        {
            // 개별 Update는 Job 예약으로 끝날 수 있으므로 상태를 즉시 읽을 테스트는 Dependency 완료를 명시한다.
            Update(systemHandle);
            ref var state = ref _world.Unmanaged.ResolveSystemStateRef(systemHandle);
            state.Dependency.Complete();
        }

        public void Playback(EndBuildingEntityCommandBufferSystem ecbSystem)
        {
            ecbSystem.Update();
        }

        public void Playback(EndSimulationEntityCommandBufferSystem ecbSystem)
        {
            ecbSystem.Update();
        }

        public void Playback(EndCommandEntityCommandBufferSystem ecbSystem)
        {
            ecbSystem.Update();
        }

        /// <summary>제작기 생성·물류 통합 검증. 건물 하위 그룹의 실제 정렬과 Command·건물 ECB 경계를 사용한다.</summary>
        public GameSimulationGroup CreateCrafterPipeline()
        {
            // 등록 순서만으로 phase/선후 관계를 추정하지 않는다. 각 그룹과 루트의 SortSystems로 실제 정렬을 적용한다.
            var simulation = _world.GetOrCreateSystemManaged<GameSimulationGroup>();
            var command = _world.GetOrCreateSystemManaged<CommandGroup>();
            var building = _world.GetOrCreateSystemManaged<BuildingSimulationGroup>();
            var decision = _world.GetOrCreateSystemManaged<BuildingDecisionGroup>();
            var reservation = _world.GetOrCreateSystemManaged<BuildingReservationGroup>();
            var execution = _world.GetOrCreateSystemManaged<BuildingExecutionGroup>();
            var apply = _world.GetOrCreateSystemManaged<BuildingStateApplyGroup>();
            var sync = _world.GetOrCreateSystemManaged<SynchronizationGroup>();
            simulation.AddSystemToUpdateList(command);
            simulation.AddSystemToUpdateList(building);
            simulation.AddSystemToUpdateList(sync);
            building.AddSystemToUpdateList(decision);
            building.AddSystemToUpdateList(reservation);
            building.AddSystemToUpdateList(execution);
            building.AddSystemToUpdateList(apply);

            command.AddSystemToUpdateList(_world.GetOrCreateSystem<CrafterRecipeCommandSystem>());
            command.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingDemolitionCommandSystem>());
            command.AddSystemToUpdateList(_world.GetOrCreateSystem<ConstructionCancelCommandSystem>());
            command.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>());
            decision.AddSystemToUpdateList(_world.GetOrCreateSystem<BeltMovementDecisionSystem>());
            decision.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingItemInputDecisionSystem>());
            decision.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemSpawnAdmissionDecisionSystem>());
            decision.AddSystemToUpdateList(_world.GetOrCreateSystem<StorageItemOutputDecisionSystem>());
            decision.AddSystemToUpdateList(_world.GetOrCreateSystem<ProductItemOutputDecisionSystem>());
            decision.AddSystemToUpdateList(_world.GetOrCreateSystem<CrafterDecisionSystem>());
            reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingStorageInputReservationSystem>());
            reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<BeltDestinationReservationSystem>());
            execution.AddSystemToUpdateList(_world.GetOrCreateSystem<BeltMovementExecutionSystem>());
            execution.AddSystemToUpdateList(_world.GetOrCreateSystem<CrafterExecutionSystem>());
            apply.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingItemStorageApplySystem>());
            apply.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemOwnershipApplySystem>());
            apply.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemLifecycleApplySystem>());
            apply.AddSystemToUpdateList(_world.GetOrCreateSystem<CrafterStateApplySystem>());
            apply.AddSystemToUpdateList(_world.GetOrCreateSystem<ConstructionLifecycleApplySystem>());
            apply.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingLifecycleApplySystem>());
            building.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>());
            sync.AddSystemToUpdateList(_world.GetOrCreateSystem<BeltSpatialSyncSystem>());
            sync.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingSpatialSyncSystem>());
            sync.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemSpatialSyncSystem>());
            sync.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<WorldInvariantValidationSystem>());

            command.SortSystems();
            decision.SortSystems();
            reservation.SortSystems();
            execution.SortSystems();
            apply.SortSystems();
            building.SortSystems();
            sync.SortSystems();
            simulation.SortSystems();
            return simulation;
        }

        /// <summary>자원 생성/채굴 통합 테스트용 Command→건물→Synchronization 실행 경계. 추가 Playback은 사용하지 않는다.</summary>
        public GameSimulationGroup CreateResourceGenerationPipeline(bool includeMining = false)
        {
            new TestEntityFactory(_world.EntityManager).PrepareSimulationConfiguration();
            var simulation = _world.GetOrCreateSystemManaged<GameSimulationGroup>();
            var command = _world.GetOrCreateSystemManaged<CommandGroup>();
            var building = _world.GetOrCreateSystemManaged<BuildingSimulationGroup>();
            var decision = _world.GetOrCreateSystemManaged<BuildingDecisionGroup>();
            var reservation = _world.GetOrCreateSystemManaged<BuildingReservationGroup>();
            var execution = _world.GetOrCreateSystemManaged<BuildingExecutionGroup>();
            var apply = _world.GetOrCreateSystemManaged<BuildingStateApplyGroup>();
            var sync = _world.GetOrCreateSystemManaged<SynchronizationGroup>();
            simulation.AddSystemToUpdateList(command);
            simulation.AddSystemToUpdateList(building);
            simulation.AddSystemToUpdateList(sync);
            building.AddSystemToUpdateList(decision);
            building.AddSystemToUpdateList(reservation);
            building.AddSystemToUpdateList(execution);
            building.AddSystemToUpdateList(apply);
            command.AddSystemToUpdateList(_world.GetOrCreateSystem<ChunkLoadCommandSystem>());
            command.AddSystemToUpdateList(_world.GetOrCreateSystem<ResourceGenerationCommandSystem>());
            command.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>());
            building.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>());
            sync.AddSystemToUpdateList(_world.GetOrCreateSystem<ResourceSpatialSyncSystem>());
            if (includeMining)
            {
                decision.AddSystemToUpdateList(_world.GetOrCreateSystem<MinerDecisionSystem>());
                execution.AddSystemToUpdateList(_world.GetOrCreateSystem<MinerExecutionSystem>());
                apply.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemLifecycleApplySystem>());
            }
            command.SortSystems();
            decision.SortSystems();
            reservation.SortSystems();
            execution.SortSystems();
            apply.SortSystems();
            building.SortSystems();
            sync.SortSystems();
            simulation.SortSystems();
            return simulation;
        }
    }
}
