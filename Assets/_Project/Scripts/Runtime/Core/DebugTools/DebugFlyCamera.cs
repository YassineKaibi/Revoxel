using Revoxel.World;
using UnityEngine;
#if UNITY_EDITOR || REVOXEL_DEBUG
using UnityEngine.InputSystem;
#endif

namespace Revoxel.Core
{
    // Debug fly camera (PLAN-02 "Debug tools", D-045): WASD to move, E and Q up and down,
    // Shift to go faster, mouse to look. Click the game view to capture the mouse, Escape
    // to release it. Starts at the spawn point once the island is loaded.
    // Reads devices directly: the real input actions belong to WP-03.
    public sealed class DebugFlyCamera : MonoBehaviour
    {
        [SerializeField]
        private VoxelWorld _world;

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

#if UNITY_EDITOR || REVOXEL_DEBUG
        private void OnEnable()
        {
            var angles = transform.eulerAngles;
            _yaw = angles.y;
            _pitch = angles.x > 180f ? angles.x - 360f : angles.x;
            if (_world == null)
            {
                return;
            }

            _world.Loaded += MoveToSpawn;
            if (_world.IsLoaded)
            {
                MoveToSpawn();
            }
        }

        private void OnDisable()
        {
            if (_world != null)
            {
                _world.Loaded -= MoveToSpawn;
            }
        }

        private void MoveToSpawn()
        {
            var spawn = _world.SpawnPoint;
            var eye = new Vector3(spawn.x + 0.5f, spawn.y + 1.6f, spawn.z + 0.5f);
            var center = new Vector3(
                WorldConstants.WorldSizeX * 0.5f,
                eye.y,
                WorldConstants.WorldSizeZ * 0.5f
            );
            var look = Quaternion.LookRotation(center - eye, Vector3.up).eulerAngles;
            _yaw = look.y;
            _pitch = 0f;
            transform.SetPositionAndRotation(eye, Quaternion.Euler(_pitch, _yaw, 0f));
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
