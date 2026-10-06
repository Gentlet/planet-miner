using NUnit.Framework;
using Unity.Entities;

namespace PlanetMiner.Tests
{
    /// <summary>
    /// 역할·목적: NUnit ECS 사례마다 독립 World와 EntityManager/엔티티 Factory/실행 Driver를 제공한다.
    /// 준비·수명: NUnit SetUp이 World를 생성하고 TearDown이 Dispose하여 시스템·엔티티·NativeContainer 수명을 종료한다.
    /// 이용: 하위 사례가 시스템/입력만 추가한다. Ready helper는 선택 phase의 전제이며 제품 DB 시작 검증 통과를 뜻하지 않는다.
    /// </summary>
    public abstract class EcsWorldTestFixture
    {
        protected World _world;
        protected EntityManager _entityManager;
        protected TestEntityFactory Entities;
        protected TestSimulationDriver Simulation;

        protected void CreateGameplayPrefabDatabases()
        {
            // 선택 phase의 Instantiate에 필요한 DB/Ready 전제만 준비하며 제품 시작 검증을 실행하지 않는다.
            TestPrefabDatabaseFactory.CreateBuildings(_entityManager);
            TestPrefabDatabaseFactory.CreateItems(_entityManager);
            // 선택한 Phase만 실행하는 격리 테스트의 입력 전제. 시작 검증은 별도 통합 테스트에서 실행한다.
            _entityManager.CreateEntity(typeof(PrefabDatabaseReady));
        }

        [SetUp]
        public virtual void SetUp()
        {
            _world = new World(GetType().Name);
            _entityManager = _world.EntityManager;
            Entities = new TestEntityFactory(_entityManager);
            Simulation = new TestSimulationDriver(_world);
        }

        [TearDown]
        public virtual void TearDown()
        {
            if (_world != null && _world.IsCreated)
            {
                _world.Dispose();
                _world = null;
                _entityManager = default;
                Entities = null;
                Simulation = null;
            }
        }
    }
}
