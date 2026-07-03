using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Camera))]
public class CameraMovementController : MonoBehaviour
{
    [SerializeField]
    private InputActionAsset _inputActions;

    [SerializeField]
    private string _actionMapName = "Player";

    [SerializeField]
    private string _moveActionName = "Move";

    [SerializeField]
    private string _scrollActionName = "ScrollWheel";

    [SerializeField]
    private string _speedBoostActionName = "Sprint";

    [SerializeField]
    private float _moveSpeed = 8f;

    [SerializeField]
    private float _speedBoostMultiplier = 2f;

    [SerializeField]
    private float _zoomSpeed = 1f;

    [SerializeField]
    private float _minOrthographicSize = 2f;

    [SerializeField]
    private float _maxOrthographicSize = 20f;

    [SerializeField]
    private bool _normalizeDiagonalMovement = true;

    private Camera _camera;
    private InputAction _moveAction;
    private InputAction _scrollAction;
    private InputAction _speedBoostAction;
    private bool _enabledMoveAction;
    private bool _enabledScrollAction;
    private bool _enabledSpeedBoostAction;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    private void OnEnable()
    {
        _moveAction = FindAction(_actionMapName, _moveActionName);
        _scrollAction = FindAction(_actionMapName, _scrollActionName);
        _speedBoostAction = FindAction(_actionMapName, _speedBoostActionName);

        if (_moveAction != null && !_moveAction.enabled)
        {
            _moveAction.Enable();
            _enabledMoveAction = true;
        }

        if (_scrollAction != null && !_scrollAction.enabled)
        {
            _scrollAction.Enable();
            _enabledScrollAction = true;
        }

        if (_speedBoostAction != null && !_speedBoostAction.enabled)
        {
            _speedBoostAction.Enable();
            _enabledSpeedBoostAction = true;
        }
    }

    private void OnDisable()
    {
        if (_moveAction != null && _enabledMoveAction)
            _moveAction.Disable();

        if (_scrollAction != null && _enabledScrollAction)
            _scrollAction.Disable();

        if (_speedBoostAction != null && _enabledSpeedBoostAction)
            _speedBoostAction.Disable();

        _enabledMoveAction = false;
        _enabledScrollAction = false;
        _enabledSpeedBoostAction = false;
    }

    private void Update()
    {
        MoveCamera();
        ZoomCamera();
    }

    private void MoveCamera()
    {
        if (_moveAction == null)
            return;

        Vector2 input = _moveAction.ReadValue<Vector2>();

        if (_normalizeDiagonalMovement && input.sqrMagnitude > 1f)
            input.Normalize();

        float moveSpeed = IsSpeedBoostPressed() ? _moveSpeed * _speedBoostMultiplier : _moveSpeed;
        Vector3 movement = new Vector3(input.x, input.y, 0f) * (moveSpeed * Time.deltaTime);
        transform.position += movement;
    }

    private void ZoomCamera()
    {
        if (_camera == null || !_camera.orthographic || _scrollAction == null)
            return;

        float scrollY = _scrollAction.ReadValue<Vector2>().y;
        if (Mathf.Approximately(scrollY, 0f))
            return;

        float scrollSteps = scrollY / 120f;
        float targetSize = _camera.orthographicSize - scrollSteps * _zoomSpeed;
        _camera.orthographicSize = Mathf.Clamp(targetSize, _minOrthographicSize, _maxOrthographicSize);
    }

    private bool IsSpeedBoostPressed()
    {
        return _speedBoostAction != null && _speedBoostAction.IsPressed();
    }

    private InputAction FindAction(string actionMapName, string actionName)
    {
        if (_inputActions == null)
        {
            Debug.LogWarning($"{nameof(CameraMovementController)} needs an input action asset.", this);
            return null;
        }

        InputActionMap actionMap = _inputActions.FindActionMap(actionMapName);
        if (actionMap == null)
        {
            Debug.LogWarning($"Input action map '{actionMapName}' was not found.", this);
            return null;
        }

        InputAction action = actionMap.FindAction(actionName);
        if (action == null)
            Debug.LogWarning($"Input action '{actionMapName}/{actionName}' was not found.", this);

        return action;
    }
}
