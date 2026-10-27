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

        if (!float.IsFinite(_moveSpeed) || _moveSpeed < 0f ||
            !float.IsFinite(_speedBoostMultiplier) || _speedBoostMultiplier < 1f ||
            !float.IsFinite(_zoomSpeed) || _zoomSpeed <= 0f ||
            !float.IsFinite(_minOrthographicSize) || _minOrthographicSize <= 0f ||
            !float.IsFinite(_maxOrthographicSize) || _maxOrthographicSize < _minOrthographicSize)
        {
            RejectConfiguration("이동/가속/줌 설정 범위를 확인하세요.");
        }
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
