using Unity.Entities;

namespace PlanetMiner.Tests
{
    /// <summary>
    /// 테스트에서 반복되는 시간 설정, 시스템 실행, Dependency 완료 및 ECB Playback을
    /// 한 곳에서 표현하기 위한 경량 Driver.
    ///
    /// 각 테스트는 도메인별 Phase 순서를 그대로 명시하고,
    /// 저수준 World/SystemHandle 호출만 이 Driver에 위임.
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
            Update(systemHandle);
            ref var state = ref _world.Unmanaged.ResolveSystemStateRef(systemHandle);
            state.Dependency.Complete();
        }

        public void Playback(EndStateApplyEntityCommandBufferSystem ecbSystem)
        {
            ecbSystem.Update();
        }

        /// <summary>자원 생성/채굴 통합 테스트용 실제 6단계 실행 경계. 추가 Playback은 사용하지 않는다.</summary>
        public GameSimulationGroup CreateResourceGenerationPipeline(bool includeMining = false)
        {
            var simulation = _world.GetOrCreateSystemManaged<GameSimulationGroup>();
            var command = _world.GetOrCreateSystemManaged<CommandGroup>();
            var decision = _world.GetOrCreateSystemManaged<DecisionGroup>();
            var reservation = _world.GetOrCreateSystemManaged<ReservationGroup>();
            var execution = _world.GetOrCreateSystemManaged<ExecutionGroup>();
            var apply = _world.GetOrCreateSystemManaged<StateApplyGroup>();
            var sync = _world.GetOrCreateSystemManaged<SynchronizationGroup>();
            simulation.AddSystemToUpdateList(command);
            simulation.AddSystemToUpdateList(decision);
            simulation.AddSystemToUpdateList(reservation);
            simulation.AddSystemToUpdateList(execution);
            simulation.AddSystemToUpdateList(apply);
            simulation.AddSystemToUpdateList(sync);
            command.AddSystemToUpdateList(_world.GetOrCreateSystem<ChunkLoadCommandSystem>());
            command.AddSystemToUpdateList(_world.GetOrCreateSystem<ResourceGenerationCommandSystem>());
            apply.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>());
            sync.AddSystemToUpdateList(_world.GetOrCreateSystem<ResourceSpatialSyncSystem>());
            if (includeMining)
            {
                decision.AddSystemToUpdateList(_world.GetOrCreateSystem<MinerDecisionSystem>());
                execution.AddSystemToUpdateList(_world.GetOrCreateSystem<MinerExecutionSystem>());
                apply.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemLifecycleApplySystem>());
            }
            command.SortSystems();
            decision.SortSystems();
            execution.SortSystems();
            apply.SortSystems();
            sync.SortSystems();
            simulation.SortSystems();
            return simulation;
        }
    }
}
