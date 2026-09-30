using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;

public class Phase4ChunkLifecycleTests : EcsWorldTestFixture
{
    [Test]
    public void Test01_ChunkUtility_CoordinateConversions_HandleNegativeAndPositiveCoordinates()
    {
        // 1. 양수 영역 변환 검증
        int2 cellZero = new int2(0, 0);
        Assert.AreEqual(new int2(0, 0), ChunkUtility.ToChunkPosition(cellZero));
        Assert.AreEqual(new int2(0, 0), ChunkUtility.ToLocalCell(cellZero));
        Assert.AreEqual(0, ChunkUtility.ToCellIndex(new int2(0, 0)));

        int2 cellChunkEdge = new int2(15, 15);
        Assert.AreEqual(new int2(0, 0), ChunkUtility.ToChunkPosition(cellChunkEdge));
        Assert.AreEqual(new int2(15, 15), ChunkUtility.ToLocalCell(cellChunkEdge));
        Assert.AreEqual(255, ChunkUtility.ToCellIndex(new int2(15, 15)));

        int2 cellNextChunk = new int2(16, 0);
        Assert.AreEqual(new int2(1, 0), ChunkUtility.ToChunkPosition(cellNextChunk));
        Assert.AreEqual(new int2(0, 0), ChunkUtility.ToLocalCell(cellNextChunk));

        // 2. 음수 영역 변환 검증 (FloorDiv 정합성)
        int2 cellNegOne = new int2(-1, -1);
        Assert.AreEqual(new int2(-1, -1), ChunkUtility.ToChunkPosition(cellNegOne));
        Assert.AreEqual(new int2(15, 15), ChunkUtility.ToLocalCell(cellNegOne));
        Assert.AreEqual(255, ChunkUtility.ToCellIndex(ChunkUtility.ToLocalCell(cellNegOne)));

        int2 cellNegChunkOrigin = new int2(-16, -16);
        Assert.AreEqual(new int2(-1, -1), ChunkUtility.ToChunkPosition(cellNegChunkOrigin));
        Assert.AreEqual(new int2(0, 0), ChunkUtility.ToLocalCell(cellNegChunkOrigin));
        Assert.AreEqual(0, ChunkUtility.ToCellIndex(ChunkUtility.ToLocalCell(cellNegChunkOrigin)));

        int2 cellNegFurther = new int2(-17, -1);
        Assert.AreEqual(new int2(-2, -1), ChunkUtility.ToChunkPosition(cellNegFurther));
        Assert.AreEqual(new int2(15, 15), ChunkUtility.ToLocalCell(cellNegFurther));

        // 3. 청크 바운드(타일 경계) 검증
        ChunkUtility.GetChunkBounds(new int2(-1, -1), out int2 minCell, out int2 maxCell);
        Assert.AreEqual(new int2(-16, -16), minCell);
        Assert.AreEqual(new int2(-1, -1), maxCell);

        ChunkUtility.GetChunkBounds(new int2(0, 0), out minCell, out maxCell);
        Assert.AreEqual(new int2(0, 0), minCell);
        Assert.AreEqual(new int2(15, 15), maxCell);
    }

    [Test]
    public void Test04_ChunkLoadCommandSystem_DeduplicatesPendingChunksWithoutCompletingThem()
    {
        var loadSystem = _world.GetOrCreateSystem(typeof(ChunkLoadCommandSystem));

        // 요청 큐에 중복 좌표 포함 3건 인큐
        Entity queueEntity = _entityManager.CreateEntity(typeof(ChunkLoadRequestQueue));
        var requestBuffer = _entityManager.AddBuffer<ChunkLoadRequestElement>(queueEntity);
        requestBuffer.Add(new ChunkLoadRequestElement(new int2(0, 0)));
        requestBuffer.Add(new ChunkLoadRequestElement(new int2(1, 0)));
        requestBuffer.Add(new ChunkLoadRequestElement(new int2(0, 0))); // 중복 요청

        loadSystem.Update(_world.Unmanaged);

        // 중복 제거되어 2개만 등록되었는지 확인
        Entity trackerEntity = _entityManager.CreateEntityQuery(typeof(GeneratedChunkTracker)).GetSingletonEntity();
        var tracker = _entityManager.GetComponentData<GeneratedChunkTracker>(trackerEntity);
        Assert.AreEqual(0, tracker.Map.Count());
        Assert.AreEqual(2, tracker.Pending.Count());
        Assert.IsTrue(tracker.Pending.Contains(new int2(0, 0)));
        Assert.IsTrue(tracker.Pending.Contains(new int2(1, 0)));

        // 신규 전달 버퍼(GeneratedChunkReadyElement)에 2개만 기록되었는지 확인
        var readyBuffer = _entityManager.GetBuffer<GeneratedChunkReadyElement>(trackerEntity);
        Assert.AreEqual(2, readyBuffer.Length);

        // Consume-on-Apply: 요청 버퍼는 비워졌는지 확인
        Assert.IsTrue(requestBuffer.IsEmpty);
        requestBuffer.Add(new ChunkLoadRequestElement(new int2(0, 0)));
        loadSystem.Update(_world.Unmanaged);
        Assert.AreEqual(2, readyBuffer.Length, "대기 중 재요청은 합쳐야 한다.");
        Assert.AreEqual(0, tracker.Map.Count());
    }

    [Test]
    public void Test05_ChunkLoadCommandSystem_AlreadyGeneratedChunk_IsQuietlyDropped()
    {
        var loadSystem = _world.GetOrCreateSystem(typeof(ChunkLoadCommandSystem));

        Entity trackerEntity = _entityManager.CreateEntityQuery(typeof(GeneratedChunkTracker)).GetSingletonEntity();
        var tracker = _entityManager.GetComponentData<GeneratedChunkTracker>(trackerEntity);
        tracker.Map.Add(new int2(5, 5)); // 이미 생성 완료된 청크 시뮬레이션

        Entity queueEntity = _entityManager.CreateEntity(typeof(ChunkLoadRequestQueue));
        var requestBuffer = _entityManager.AddBuffer<ChunkLoadRequestElement>(queueEntity);
        requestBuffer.Add(new ChunkLoadRequestElement(new int2(5, 5))); // 재요청

        loadSystem.Update(_world.Unmanaged);

        // 이미 생성된 청크는 무시되므로 readyBuffer는 비어있어야 함
        var readyBuffer = _entityManager.GetBuffer<GeneratedChunkReadyElement>(trackerEntity);
        Assert.AreEqual(0, readyBuffer.Length);

        // 요청 버퍼는 정상적으로 비워져야 함 (Consume)
        Assert.IsTrue(requestBuffer.IsEmpty);
    }

    [Test]
    public void Test06_FullPipeline_BootstrapToChunkLoad_RegistersInitialChunks()
    {
        // 1. 설정 등록
        Entity settingsEntity = _entityManager.CreateEntity(typeof(ResourceGenerationSettings));
        _entityManager.SetComponentData(settingsEntity, new ResourceGenerationSettings(worldSeed: 1, initialChunkSize: 3));

        // 2. 부트스트랩 시스템 실행 (요청 인큐)
        var bootstrapSystem = _world.GetOrCreateSystem(typeof(InitialChunkLoadBootstrapSystem));
        bootstrapSystem.Update(_world.Unmanaged);

        // 3. 로드 시스템 실행 (요청 소비 및 청크 등록)
        var loadSystem = _world.GetOrCreateSystem(typeof(ChunkLoadCommandSystem));
        loadSystem.Update(_world.Unmanaged);

        Entity trackerEntity = _entityManager.CreateEntityQuery(typeof(GeneratedChunkTracker)).GetSingletonEntity();
        var tracker = _entityManager.GetComponentData<GeneratedChunkTracker>(trackerEntity);
        Assert.AreEqual(0, tracker.Map.Count());
        Assert.AreEqual(9, tracker.Pending.Count(), "초기 청크는 생성 완료가 아니라 대기 상태여야 한다.");

        var readyBuffer = _entityManager.GetBuffer<GeneratedChunkReadyElement>(trackerEntity);
        Assert.AreEqual(9, readyBuffer.Length, "첫 프레임에는 9개 신규 청크가 후속 파이프라인에 전달되어야 함");

        // 4. 소비자가 아직 처리하지 않았으므로 다음 프레임에도 요청을 유지한다.
        loadSystem.Update(_world.Unmanaged);
        Assert.AreEqual(9, readyBuffer.Length);
        Assert.AreEqual(9, tracker.Pending.Count());
        Assert.AreEqual(0, tracker.Map.Count());
    }
}
