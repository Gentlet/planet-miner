using NUnit.Framework;
using Unity.Entities;

namespace PlanetMiner.Tests
{
    /// <summary>
    /// ECS 격리 테스트를 위한 기본 픽스처.
    /// 각 테스트마다 독립된 World를 생성하고 종료 시 자동으로 정리합니다.
    /// </summary>
    public abstract class EcsWorldTestFixture
    {
        protected World _world;
        protected EntityManager _entityManager;

        [SetUp]
        public virtual void SetUp()
        {
            _world = new World(GetType().Name);
            _entityManager = _world.EntityManager;
        }

        [TearDown]
        public virtual void TearDown()
        {
            if (_world != null && _world.IsCreated)
            {
                _world.Dispose();
                _world = null;
                _entityManager = default;
            }
        }
    }
}
