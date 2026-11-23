using System;
using System.IO;
using Unity.Entities;
using Unity.Scenes;
using UnityEngine.LowLevel;

/// <summary>복원과 검증을 마친 World를 공개한다. 공개 전에는 현재 게임 World와 PlayerLoop를 유지한다.</summary>
internal sealed class GameLoadWorldPreparation : IDisposable
{
    private World _world;
    private readonly Entity _startGate;
    private readonly InitializationSystemGroup _initialization;
    private readonly EntityQuery _ready;
    private readonly EntityQuery _fatal;

    public GameLoadWorldPreparation(Unity.Entities.Hash128 prefabSceneGuid)
    {
        if (!prefabSceneGuid.IsValid)
        {
            throw new ArgumentException("복원용 프리팹 SubScene GUID가 필요합니다.", nameof(prefabSceneGuid));
        }

        _world = new World("Planet Miner Load", WorldFlags.Game);
        try
        {
            var manager = _world.EntityManager;
            _startGate = manager.CreateEntity(typeof(GameSessionStartPending));
            // Unity의 TypeManager가 다른 World와 공유하는 시스템 타입 목록이다.
            var systems = DefaultWorldInitialization.GetAllSystemTypeIndices(WorldSystemFilterFlags.Default);
            DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(_world, systems);
            _initialization = _world.GetExistingSystemManaged<InitializationSystemGroup>();
            _ready = manager.CreateEntityQuery(ComponentType.ReadOnly<PrefabDatabaseReady>());
            _fatal = manager.CreateEntityQuery(ComponentType.ReadOnly<SimulationFatalError>());
            SceneSystem.LoadSceneAsync(_world.Unmanaged, prefabSceneGuid);
        }
        catch
        {
            _world.Dispose();
            throw;
        }
    }

    public bool IsReady => !_ready.IsEmptyIgnoreFilter;
    public bool HasFailed => !_fatal.IsEmptyIgnoreFilter;

    public void AdvanceInitialization()
    {
        _initialization.Update();
    }

    public World RestoreAndActivate(GameWorldSnapshot snapshot, World previous)
    {
        if (!ReferenceEquals(previous, World.DefaultGameObjectInjectionWorld))
        {
            throw new InvalidOperationException("현재 세션 또는 복원용 World가 준비되지 않았습니다.");
        }
        RestoreSnapshot(snapshot);

        // 복원된 원본과 큐가 이 두 일회성 초기화의 결과를 이미 제공한다.
        _world.GetExistingSystemManaged<GameBootstrapInitializationSystem>().Enabled = false;
        var initialChunks = _world.GetExistingSystem<InitialChunkLoadBootstrapSystem>();
        _world.Unmanaged.ResolveSystemStateRef(initialChunks).Enabled = false;
        _world.EntityManager.DestroyEntity(_startGate);

        var loop = PlayerLoop.GetCurrentPlayerLoop();
        ScriptBehaviourUpdateOrder.RemoveWorldFromPlayerLoop(previous, ref loop);
        ScriptBehaviourUpdateOrder.AppendWorldToPlayerLoop(_world, ref loop);
        PlayerLoop.SetPlayerLoop(loop);
        World.DefaultGameObjectInjectionWorld = _world;

        World restored = _world;
        _world = null;
        return restored;
    }

    private void RestoreSnapshot(GameWorldSnapshot snapshot)
    {
        if (!IsReady || HasFailed)
        {
            throw new InvalidOperationException("복원용 World가 준비되지 않았습니다.");
        }
        GameWorldRestore.Apply(_world, snapshot);
        if (HasFailed || _world.GetExistingSystemManaged<WorldInvariantValidationSystem>().TotalViolationCount != 0)
        {
            throw new InvalidDataException("복원한 월드의 정합성 검사에 실패했습니다.");
        }
    }

#if UNITY_EDITOR
    // 기존 형식은 공개하거나 진행시키지 않은 World에서 현재 스키마로 다시 캡처한다.
    internal GameWorldSnapshot CaptureForMigration(GameWorldSnapshot snapshot)
    {
        RestoreSnapshot(snapshot);
        return GameWorldCapture.Capture(_world.EntityManager);
    }

    internal void VerifyMigrationSnapshot(GameWorldSnapshot snapshot) => RestoreSnapshot(snapshot);
#endif

    public void Dispose()
    {
        _world?.Dispose();
        _world = null;
    }
}
