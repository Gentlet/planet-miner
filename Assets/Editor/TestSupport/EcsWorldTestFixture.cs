using NUnit.Framework;
using Unity.Entities;

public abstract class EcsWorldTestFixture
{
    protected World _world;
    protected EntityManager _entityManager;

    [SetUp]
    public void SetUpEcsWorld()
    {
        _world = new World(GetType().Name);
        _entityManager = _world.EntityManager;
    }

    [TearDown]
    public void TearDownEcsWorld()
    {
        if (_world == null)
            return;

        if (!_world.IsCreated)
            return;

        _world.Dispose();
        _world = null;
        _entityManager = default;
    }
}
