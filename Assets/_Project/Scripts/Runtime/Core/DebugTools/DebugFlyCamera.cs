using UnityEngine;
#if UNITY_EDITOR || REVOXEL_DEBUG
using UnityEngine.InputSystem;
#endif

namespace Revoxel.Core
{
    // Debug fly camera (PLAN-02 "Debug tools", D-045): WASD to move, E and Q up and down,
    // Shift to go faster, mouse to look. Click the game view to capture the mouse, Escape
    // to release it.
    // Reads devices directly: the real input actions belong to WP-03.
    public sealed class DebugFlyCamera : MonoBehaviour
    {
#if UNITY_EDITOR || REVOXEL_DEBUG
        [SerializeField]
        [Tooltip("Debug tool value, blocks per second.")]
        private float _speed = 12f;

        [SerializeField]
        [Tooltip("Debug tool value, speed multiplier while Shift is held.")]
        private float _fastMultiplier = 4f;

        [SerializeField]
        [Tooltip("Debug tool value, degrees per pixel of mouse movement.")]
        private float _lookSensitivity = 0.1f;

        private float _yaw;
        private float _pitch;

        public bool IsCapturingMouse => Cursor.lockState == CursorLockMode.Locked;

        private void OnEnable()
        {
            var angles = transform.eulerAngles;
            _yaw = angles.y;
            _pitch = angles.x > 180f ? angles.x - 360f : angles.x;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || mouse == null)
            {
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (!IsCapturingMouse && mouse.leftButton.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                return;
            }

            if (IsCapturingMouse)
            {
                var delta = mouse.delta.ReadValue();
                _yaw += delta.x * _lookSensitivity;
                _pitch = Mathf.Clamp(_pitch - delta.y * _lookSensitivity, -89f, 89f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            var move = Vector3.zero;
            move += keyboard.wKey.isPressed ? transform.forward : Vector3.zero;
            move -= keyboard.sKey.isPressed ? transform.forward : Vector3.zero;
            move += keyboard.dKey.isPressed ? transform.right : Vector3.zero;
            move -= keyboard.aKey.isPressed ? transform.right : Vector3.zero;
            move += keyboard.eKey.isPressed ? Vector3.up : Vector3.zero;
            move -= keyboard.qKey.isPressed ? Vector3.up : Vector3.zero;
            var speed = _speed * (keyboard.shiftKey.isPressed ? _fastMultiplier : 1f);
            transform.position += move * (speed * Time.unscaledDeltaTime);
        }
#else
        private void Awake()
        {
            Destroy(this);
        }
#endif
    }
}
