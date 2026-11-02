using System;

/// <summary>카메라 조작 수치의 전달 데이터다. 기본값/현재값 조회와 검증은 UI/저장 방식에 의존하지 않는다.</summary>
[Serializable]
public struct CameraMovementSettings
{
    public float MoveSpeed;
    public float SpeedBoostMultiplier;
    public float ZoomSpeed;
    public float MinOrthographicSize;
    public float MaxOrthographicSize;

    public static CameraMovementSettings Default => new CameraMovementSettings
    {
        MoveSpeed = 8f,
        SpeedBoostMultiplier = 2f,
        ZoomSpeed = 1f,
        MinOrthographicSize = 2f,
        MaxOrthographicSize = 20f
    };

    public bool TryValidate(out string error)
    {
        error = null;
        if (!float.IsFinite(MoveSpeed) || MoveSpeed < 0f ||
            !float.IsFinite(SpeedBoostMultiplier) || SpeedBoostMultiplier < 1f ||
            !float.IsFinite(MoveSpeed * SpeedBoostMultiplier) ||
            !float.IsFinite(ZoomSpeed) || ZoomSpeed <= 0f ||
            !float.IsFinite(MinOrthographicSize) || MinOrthographicSize <= 0f ||
            !float.IsFinite(MaxOrthographicSize) || MaxOrthographicSize < MinOrthographicSize)
        {
            error = "이동/가속/줌 설정은 유한한 값과 올바른 범위가 필요합니다.";
            return false;
        }
        return true;
    }
}

/// <summary>원본 Input Actions의 안정된 binding ID와 교체할 단일 버튼 경로다. null 경로는 기본 키, 빈 경로는 키 해제다.</summary>
[Serializable]
public struct WorldInputBindingOverride
{
    public string BindingId;
    public string OverridePath;
}

/// <summary>설정 화면/저장 계층이 전달할 전체 설정이다. 컨트롤러는 이 객체나 배열을 소유하지 않고 복사해 적용한다.</summary>
[Serializable]
public sealed class WorldInputSettings
{
    public CameraMovementSettings Camera = CameraMovementSettings.Default;
    public WorldInputBindingOverride[] BindingOverrides = Array.Empty<WorldInputBindingOverride>();
}

/// <summary>설정 UI용 바인딩 설명이다. 입력/게임 상태를 소유하지 않는다.</summary>
public readonly struct WorldInputBindingDescriptor
{
    public readonly string BindingId;
    public readonly string ActionName;
    public readonly string Name;
    public readonly string DefaultPath;
    public readonly string EffectivePath;

    public WorldInputBindingDescriptor(string bindingId, string actionName, string name, string defaultPath, string effectivePath)
    {
        BindingId = bindingId;
        ActionName = actionName;
        Name = name;
        DefaultPath = defaultPath;
        EffectivePath = effectivePath;
    }
}
