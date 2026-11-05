using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 중간 포인터 위치를 보존하는 공유 입력 설정의 수명 소유자다.
/// 메인 스레드의 첫 소유자가 설정 복제본을 게시하고 마지막 소유자가 이전 인스턴스를 복원한다.
/// </summary>
public sealed class WorldInputEventScope : IDisposable
{
    private static int _ownerCount;
    private static InputSettings _previousSettings;
    private static InputSettings _ownedSettings;
    private bool _disposed;

    public WorldInputEventScope()
    {
        if (_ownerCount == 0)
        {
            _previousSettings = InputSystem.settings;
            _ownedSettings = UnityEngine.Object.Instantiate(_previousSettings);
            _ownedSettings.hideFlags = HideFlags.HideAndDontSave;
            _ownedSettings.disableRedundantEventsMerging = true;
            InstallOwnedSettings(_previousSettings, _ownedSettings);
        }

        _ownerCount++;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ownerCount--;
        if (_ownerCount != 0)
        {
            return;
        }

        if (InputSystem.settings == _ownedSettings)
        {
            InputSystem.settings = _previousSettings;
        }
        else
        {
            Debug.LogError("WorldInputEventScope: 활성 입력 수명 중 InputSettings 교체 소유권이 변경됐습니다.");
        }

        // Input System 1.20은 이전 설정을 복원할 때 현재 복제본을 파괴할 수 있다.
        if (_ownedSettings != null)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(_ownedSettings);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(_ownedSettings);
            }
        }

        _ownedSettings = null;
        _previousSettings = null;
    }

    private static void InstallOwnedSettings(InputSettings previousSettings, InputSettings ownedSettings)
    {
        HideFlags originalFlags = previousSettings.hideFlags;
        if (originalFlags != HideFlags.HideAndDontSave)
        {
            InputSystem.settings = ownedSettings;
            return;
        }

        // Input System 1.20은 현재 설정의 플래그가 정확히 HideAndDontSave이면 교체 시 파괴한다.
        // 원본을 복원할 수 있도록 저장/언로드 플래그를 유지한 채 NotEditable만 잠시 해제한다.
        previousSettings.hideFlags = originalFlags & ~HideFlags.NotEditable;
        try
        {
            InputSystem.settings = ownedSettings;
        }
        finally
        {
            previousSettings.hideFlags = originalFlags;
        }
    }
}
