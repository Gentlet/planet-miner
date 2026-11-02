using UnityEngine;

/// <summary>WorldInputController가 허용한 입력으로 카메라 위치와 직교 크기만 변경한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class CameraMovementController : MonoBehaviour
{
    [SerializeField, Min(0f)] private float _moveSpeed = 8f;
    [SerializeField, Min(1f)] private float _speedBoostMultiplier = 2f;
    [SerializeField, Min(0.01f)] private float _zoomSpeed = 1f;
    [SerializeField, Min(0.01f)] private float _minOrthographicSize = 2f;
    [SerializeField, Min(0.01f)] private float _maxOrthographicSize = 20f;

    private Camera _camera;

    public bool CanProcessInput
    {
        get
        {
            if (_camera == null)
            {
                return false;
            }

            return isActiveAndEnabled && _camera.isActiveAndEnabled && _camera.orthographic;
        }
    }

    private void OnEnable()
    {
        _camera = GetComponent<Camera>();
        if (!_camera.orthographic)
        {
            RejectConfiguration("직교 카메라가 필요합니다.");
            return;
        }

        if (!GetSettings().TryValidate(out string error))
        {
            RejectConfiguration(error);
        }
    }

    public CameraMovementSettings GetSettings()
    {
        return new CameraMovementSettings
        {
            MoveSpeed = _moveSpeed,
            SpeedBoostMultiplier = _speedBoostMultiplier,
            ZoomSpeed = _zoomSpeed,
            MinOrthographicSize = _minOrthographicSize,
            MaxOrthographicSize = _maxOrthographicSize
        };
    }

    /// <summary>유효한 수치를 함께 적용한다. 범위 변경은 현재 줌을 새 범위에 맞추며 위치/진행 중 월드 명령은 소유하지 않는다.</summary>
    public bool TryApplySettings(CameraMovementSettings settings, out string error)
    {
        if (!settings.TryValidate(out error))
        {
            return false;
        }
        ApplyValidatedSettings(settings);
        return true;
    }

    /// <summary>호출자가 전체 설정 검증을 끝낸 뒤 사용하는 반영 경계다.</summary>
    internal void ApplyValidatedSettings(CameraMovementSettings settings)
    {
        _moveSpeed = settings.MoveSpeed;
        _speedBoostMultiplier = settings.SpeedBoostMultiplier;
        _zoomSpeed = settings.ZoomSpeed;
        _minOrthographicSize = settings.MinOrthographicSize;
        _maxOrthographicSize = settings.MaxOrthographicSize;
        var camera = GetComponent<Camera>();
        camera.orthographicSize = Mathf.Clamp(camera.orthographicSize, _minOrthographicSize, _maxOrthographicSize);
    }

    public void ApplyInput(Vector2 movement, bool speedBoost, float scrollSteps, Vector2 pointer, float deltaTime)
    {
        if (!CanProcessInput)
        {
            return;
        }

        movement = Vector2.ClampMagnitude(movement, 1f);
        float speed = speedBoost ? _moveSpeed * _speedBoostMultiplier : _moveSpeed;
        transform.position += new Vector3(movement.x, movement.y, 0f) * (speed * deltaTime);
        if (Mathf.Approximately(scrollSteps, 0f))
        {
            return;
        }

        if (!WorldPointerUtility.TryGetWorldPosition(_camera, pointer, out Vector3 beforeZoom))
        {
            return;
        }

        float size = Mathf.Clamp(_camera.orthographicSize - scrollSteps * _zoomSpeed,
            _minOrthographicSize, _maxOrthographicSize);
        _camera.orthographicSize = size;
        if (WorldPointerUtility.TryGetWorldPosition(_camera, pointer, out Vector3 afterZoom))
        {
            transform.position += beforeZoom - afterZoom;
        }
    }

    private void RejectConfiguration(string reason)
    {
        Debug.LogError($"{nameof(CameraMovementController)}: {reason}", this);
        enabled = false;
    }
}
