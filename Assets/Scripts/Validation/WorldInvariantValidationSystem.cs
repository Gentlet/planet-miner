#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Text;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Architecture V2 전용 데이터 무결성(Invariant) 검증 시스템.
/// 모든 6대 Phase의 상태 반영 및 동기화가 완전히 끝난 프레임 맨 마지막(SynchronizationGroup, OrderLast = true)에 실행됩니다.
/// 위반 감지 시 콘솔 에러 출력 없이 파일로 진단 로그를 저장하고 Debug.Break()로 에디터를 일시정지합니다.
/// </summary>
[UpdateInGroup(typeof(SynchronizationGroup), OrderLast = true)]
public partial class WorldInvariantValidationSystem : SystemBase
{
    /// <summary>
    /// 검증 주기 (기본값: 매 1프레임마다 검사, N프레임 간격 조절 가능)
    /// </summary>
    public int CheckIntervalFrames = 1;
    private int _frameCounter;

    private string _logDirectory;

    protected override void OnCreate()
    {
        base.OnCreate();
        // Logs/InvariantErrors/ 경로 설정 (프로젝트 루트 기준)
        _logDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "InvariantErrors"));
    }

    protected override void OnUpdate()
    {
        _frameCounter++;
        if (_frameCounter < CheckIntervalFrames)
        {
            return;
        }
        _frameCounter = 0;

        // Invariant 검증 실행
        ValidateInvariants();
    }

    private void ValidateInvariants()
    {
        // Phase 0: 뼈대 상태
        // 향후 Phase 1(Item/Spatial), Phase 2(Belt) 등 도메인 추가 시 각 검증 로직이 여기에 등록됩니다.
    }

    /// <summary>
    /// 무결성 위반 발생 시 호출하는 리포팅 메서드.
    /// 콘솔 에러 출력 없이 진단 로그 파일을 생성하고 에디터를 일시정지(Debug.Break)합니다.
    /// </summary>
    /// <param name="category">위반 항목 카테고리 (예: Item, Drone, Spatial, Building)</param>
    /// <param name="message">위반 상세 내용 (기대값 vs 실제값 등)</param>
    /// <param name="entity">위반 대상 엔티티 (선택 사항)</param>
    public void ReportViolation(string category, string message, Entity entity = default)
    {
        try
        {
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"invariant_error_{timestamp}.txt";
            string filePath = Path.Combine(_logDirectory, fileName);

            var sb = new StringBuilder();
            sb.AppendLine("=== PlanetMiner Invariant Violation Report ===");
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"Frame Count: {UnityEngine.Time.frameCount}");
            sb.AppendLine($"Category: {category}");
            if (entity != Entity.Null)
            {
                sb.AppendLine($"Entity: Index={entity.Index}, Version={entity.Version}");
            }
            sb.AppendLine("Message:");
            sb.AppendLine(message);
            sb.AppendLine("==============================================");

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            // 파일 쓰기 예외 발생 시 최소한의 디버그 정보 보존을 위한 처리
            System.Diagnostics.Debug.WriteLine($"[InvariantReport Error] Failed to write log file: {ex.Message}");
        }

        // 에디터 일시정지
        Debug.Break();
    }
}
#endif
