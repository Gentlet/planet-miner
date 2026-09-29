using NUnit.Framework;
using Unity.Entities;

namespace PlanetMiner.Tests
{
    /// <summary>
    /// ECS 격리 테스트를 위한 기본 픽스처.
    /// 각 테스트마다 독립된 World를 생성하고 종료 시 자동으로 정리.
    /// </summary>
    public abstract class EcsWorldTestFixture
    {
        protected World _world;
        protected EntityManager _entityManager;
        protected TestEntityFactory Entities;
        protected TestSimulationDriver Simulation;

        protected void CreateGameplayPrefabDatabases()
        {
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
