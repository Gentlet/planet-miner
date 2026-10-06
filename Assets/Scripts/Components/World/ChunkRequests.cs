using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 로드할 청크 좌표 큐의 World 단일 소유자 태그다.
/// 부착 엔티티: ChunkLoadRequestElement 버퍼가 함께 있는 독립 큐 엔티티다.
/// 생성: InitialChunkLoadBootstrapSystem(Initialization)이 없으면 큐와 버퍼를 생성한다.
/// 이용: 초기 부트스트랩이 좌표를 추가하고 ChunkLoadCommandSystem(Command)이 기존 완료/대기 청크와 중복을 병합하여 생성 대기 버퍼로 넘긴다. 카메라 기반 Producer는 미구현이다.
/// 제거: 요청 처리 때는 버퍼 내용만 비우고 큐 태그/엔티티는 World 종료까지 유지한다.
/// </summary>
public struct ChunkLoadRequestQueue : IComponentData
{
}

/// <summary>
/// 역할·목적: 자원 생성이 필요한 청크 좌표를 큐에 전달한다. 좌표는 월드 셀과 구분한다.
/// 부착 엔티티: ChunkLoadRequestQueue가 있는 World 단일 큐 엔티티의 버퍼다.
/// 생성: InitialChunkLoadBootstrapSystem(Initialization)이 초기 영역의 좌표를 추가한다. 외부 청크 로드 Producer도 이 큐를 사용하는 계약이다.
/// 이용: ChunkLoadCommandSystem(Command)이 완료/대기 중복을 제외하고 GeneratedChunkTracker.Pending 및 GeneratedChunkReadyElement에 등록한다.
/// 제거: Command가 큐 항목을 처리한 뒤 즉시 Clear한다. 버퍼 자체는 큐 엔티티와 함께 유지한다.
/// </summary>
public struct ChunkLoadRequestElement : IBufferElementData
{
    public int2 ChunkCoord;

    public ChunkLoadRequestElement(int2 chunkCoord)
    {
        ChunkCoord = chunkCoord;
    }
}
