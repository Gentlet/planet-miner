using Unity.Entities;

namespace PlanetMiner.Tests
{
    /// <summary>
    /// 테스트에서 반복되는 시간 설정, 시스템 실행, Dependency 완료 및 ECB Playback을
    /// 한 곳에서 표현하기 위한 경량 Driver입니다.
    ///
    /// 각 테스트는 도메인별 Phase 순서를 그대로 명시하고,
    /// 저수준 World/SystemHandle 호출만 이 Driver에 위임합니다.
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
    }
}
