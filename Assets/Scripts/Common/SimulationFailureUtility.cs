using Unity.Collections;
using Unity.Entities;

/// <summary>
/// 역할·목적: 복구되지 않는 생성/설정 오류를 로그와 SimulationFatalError로 게시해 다음 게임 틱을 차단한다.
/// 입력·출력: 메시지와 호출자의 ECB를 받아 오류 엔티티 생성만 기록한다. 현재 틱의 이미 기록된 변경을 rollback하지 않는다.
/// 이용·수명: 프리팹 기반 생성/환급 등의 실패 경계가 사용하며 오류의 실체화 시점은 전달한 ECB Playback이 정한다.
/// </summary>
public static class SimulationFailureUtility
{
    /// <summary>현재 ECB 경계에서 오류를 게시한다. 다음 게임 틱은 실행하지 않는다. Rollback은 아니다.</summary>
    public static void Record(ref EntityCommandBuffer ecb, in FixedString128Bytes message)
    {
        UnityEngine.Debug.LogError(message);
        Entity error = ecb.CreateEntity();
        ecb.AddComponent(error, new SimulationFatalError { Message = message });
    }
}
