using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 청크 자원 생성의 완료 Map과 준비/ECB 반영 대기 Pending을 소유하여 중복 생성을 막는다. 공간 점유 인덱스가 아니다.
/// 부착 엔티티: GeneratedChunkReadyElement/GeneratedChunkCompletedElement가 함께 있는 World 단일 수명주기 엔티티다.
/// 생성: ChunkLoadCommandSystem.OnCreate가 두 Persistent 집합과 빈 버퍼를 생성한다.
/// 이용: ChunkLoadCommandSystem(Command)이 요청을 Pending에 넣고 다음 Command에 완료 알림을 받아 Map으로 확정한다. ResourceGenerationCommandSystem(Command)은 Ready 항목의 자원을 생성한다.
/// 제거: 완료 시 Pending의 해당 좌표를 제거하며 완료 Map은 유지한다. ChunkLoadCommandSystem.OnDestroy가 두 Native 집합을 Dispose한다.
/// </summary>
public struct GeneratedChunkTracker : IComponentData
{
    public NativeParallelHashSet<int2> Map;
    public NativeParallelHashSet<int2> Pending;
}

/// <summary>
/// 역할·목적: 중복 제거 후 자원 프리팹/설정 준비를 기다리는 청크 생성 작업을 전달한다.
/// 부착 엔티티: GeneratedChunkTracker가 있는 World 수명주기 엔티티의 버퍼다.
/// 생성: ChunkLoadCommandSystem(Command)이 신규 Pending 청크 좌표를 추가한다.
/// 이용: 같은 Command의 ResourceGenerationCommandSystem이 준비 조건 충족 시 자원 스폰과 완료 알림을 EndCommand ECB에 기록한다.
/// 제거: 준비되지 않으면 항목을 유지한다. 스폰/완료 명령 기록 뒤 Ready 버퍼를 Clear하며 버퍼 자체는 tracker 수명을 따른다.
/// </summary>
public struct GeneratedChunkReadyElement : IBufferElementData
{
    public int2 ChunkCoord;

    public GeneratedChunkReadyElement(int2 chunkCoord)
    {
        ChunkCoord = chunkCoord;
    }
}

/// <summary>
/// 역할·목적: 청크 스폰 명령이 실제 재생된 뒤 생성 완료를 확정할 수 있도록 전달하는 알림이다.
/// 부착 엔티티: GeneratedChunkTracker가 있는 동일 World 수명주기 엔티티의 버퍼다.
/// 생성: ResourceGenerationCommandSystem(Command)이 자원 스폰 뒤 같은 EndCommand ECB에 항목 추가를 기록한다.
/// 이용: 다음 틱 ChunkLoadCommandSystem(Command)이 좌표를 Map에 추가하고 Pending에서 제거한다. 명령 기록만으로 즉시 완료 처리하지 않는다.
/// 제거: ChunkLoadCommandSystem이 알림 반영 후 즉시 Clear한다. 버퍼 자체는 tracker 수명을 따른다.
/// </summary>
public struct GeneratedChunkCompletedElement : IBufferElementData
{
    public int2 ChunkCoord;
}
