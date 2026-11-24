using Revoxel.World;
using UnityEngine;
#if UNITY_EDITOR || REVOXEL_DEBUG
using UnityEngine.InputSystem;
#endif

namespace Revoxel.Core
{
    // Debug camera (PLAN-02 "Debug tools", D-045), flying or walking. Flying: WASD to
    // move, E and Q up and down, Shift to go faster. Walking: a 1.8 m body with the eyes at
    // 1.7 m, WASD, Shift to run, Space to jump, gravity, steps up to 0.3 m, standing on the
    // chunk colliders. F switches between the two. Mouse to look; click the game view to
    // capture the mouse, Escape to release it. Starts 2 m above the surface at the origin.
    // Keeps a horizontal field of view whatever the screen shape. Body and FOV values are
    // debug values, not the player's (STATUS Q-44).
    // Reads devices directly: the real input actions belong to WP-03.
    public sealed class DebugFlyCamera : MonoBehaviour
    {
#if UNITY_EDITOR || REVOXEL_DEBUG
        // Below this vertical speed with no collider under the body, it fell through the
        // terrain (colliders not built yet) and is put back on the generated surface.
        private const float LostFallSpeed = -15f;
        private const float MaxFallSpeed = -50f;
        private const float GroundProbeMeters = 200f;

        [SerializeField]
        private VoxelWorld _world;

        [SerializeField]
        [Tooltip("Debug tool value, meters above the surface at the start.")]
        private float _startHeight = 2f;

        [SerializeField]
        [Tooltip("Debug tool value, meters per second.")]
        private float _speed = 12f;

        [SerializeField]
        [Tooltip("Debug tool value, speed multiplier while Shift is held.")]
        private float _fastMultiplier = 4f;

        [SerializeField]
        [Tooltip("Debug tool value, degrees per pixel of mouse movement.")]
        private float _lookSensitivity = 0.1f;

        [SerializeField]
        [Tooltip("Debug tool value, horizontal field of view in degrees.")]
        private float _horizontalFov = 90f;

        [SerializeField]
        [Tooltip("Debug tool value, walking body height in meters.")]
        private float _bodyHeight = 1.8f;

        [SerializeField]
        [Tooltip("Debug tool value, eye height above the feet in meters.")]
        private float _eyeHeight = 1.7f;

        [SerializeField]
        [Tooltip("Debug tool value, walking body radius in meters.")]
        private float _bodyRadius = 0.3f;

        [SerializeField]
        [Tooltip("Debug tool value, highest step climbed without jumping, meters.")]
        private float _stepHeight = 0.3f;

        [SerializeField]
        [Tooltip("Debug tool value, walking speed in meters per second.")]
        private float _walkSpeed = 4.5f;

        [SerializeField]
        [Tooltip("Debug tool value, walking speed multiplier while Shift is held.")]
        private float _runMultiplier = 1.5f;

        [SerializeField]
        [Tooltip("Debug tool value, jump height in meters.")]
        private float _jumpHeight = 1f;

        private Camera _camera;
        private CharacterController _body;
        private float _yaw;
        private float _pitch;
        private float _verticalSpeed;

        public bool IsCapturingMouse => Cursor.lockState == CursorLockMode.Locked;

        public bool IsWalking { get; private set; }

        public bool IsGrounded => IsWalking && _body.isGrounded;

        public float EyeHeight => _eyeHeight;

        public float HorizontalFov => _horizontalFov;

        // Walking puts the feet on the ground under the camera.
        public void SetWalking(bool walking)
        {
            if (walking == IsWalking)
            {
                return;
            }

            IsWalking = walking;
            _verticalSpeed = 0f;
            if (!walking)
            {
                _body.enabled = false;
                return;
            }

            if (_body == null)
            {
                _body = gameObject.AddComponent<CharacterController>();
                _body.enabled = false;
            }

            _body.height = _bodyHeight;
            _body.radius = _bodyRadius;
            KeepBodyUnderEyes();
            _body.stepOffset = _stepHeight;
            _body.slopeLimit = 50f;
            _body.skinWidth = 0.03f;
            _body.minMoveDistance = 0f;
            PlaceOnGround();
            _body.enabled = true;
        }

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private void OnEnable()
        {
            var angles = transform.eulerAngles;
            _yaw = angles.y;
            _pitch = angles.x > 180f ? angles.x - 360f : angles.x;
        }

        // Placed once the water plan is built: a query before would wait for it (D-077).
        private void Start()
        {
            if (_world == null)
            {
                return;
            }

            if (_world.IsWaterPlanReady)
            {
                PlaceAtStart();
            }
            else
            {
                _world.WaterPlanReady += PlaceAtStart;
            }
        }

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.WaterPlanReady -= PlaceAtStart;
            }
        }

        private void PlaceAtStart()
        {
            _world.WaterPlanReady -= PlaceAtStart;
            if (_world.TryGetSurfaceHeightMeters(0, 0, out var surface))
            {
                // Over water the camera starts above its surface (WP-05).
                if (_world.TryGetWaterColumn(0, 0, out var water) && water.IsWet)
                {
                    surface = Mathf.Max(surface, water.SurfaceMeters);
                }

                transform.position = new Vector3(0.0625f, surface + _startHeight, 0.0625f);
            }
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && mouse != null)
            {
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

                if (keyboard.fKey.wasPressedThisFrame)
                {
                    SetWalking(!IsWalking);
                }

                if (IsCapturingMouse)
                {
                    var delta = mouse.delta.ReadValue();
                    _yaw += delta.x * _lookSensitivity;
                    _pitch = Mathf.Clamp(_pitch - delta.y * _lookSensitivity, -89f, 89f);
                    transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
                }
            }

            if (IsWalking)
            {
                Walk(keyboard);
            }
            else if (keyboard != null)
            {
                Fly(keyboard);
            }
        }

        private void LateUpdate()
        {
            if (_camera != null)
            {
                _camera.fieldOfView = Camera.HorizontalToVerticalFieldOfView(
                    _horizontalFov,
                    _camera.aspect
                );
            }
        }

        private void Fly(Keyboard keyboard)
        {
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

        // No keyboard (tests, batch mode) still falls and lands.
        private void Walk(Keyboard keyboard)
        {
            var dt = Time.deltaTime;
            var input = Vector3.zero;
            var run = false;
            var jump = false;
            if (keyboard != null)
            {
                input.x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                input.z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
                run = keyboard.shiftKey.isPressed;
                jump = keyboard.spaceKey.wasPressedThisFrame;
            }

            KeepBodyUnderEyes();
            var speed = _walkSpeed * (run ? _runMultiplier : 1f);
            var horizontal = Quaternion.Euler(0f, _yaw, 0f) * Vector3.ClampMagnitude(input, 1f);
            if (_body.isGrounded)
            {
                // Pressed onto the ground so isGrounded holds on the next frame.
                _verticalSpeed = jump ? Mathf.Sqrt(-2f * Physics.gravity.y * _jumpHeight) : -2f;
            }

            _verticalSpeed = Mathf.Max(_verticalSpeed + Physics.gravity.y * dt, MaxFallSpeed);
            var flags = _body.Move((horizontal * speed + Vector3.up * _verticalSpeed) * dt);
            if ((flags & CollisionFlags.Above) != 0 && _verticalSpeed > 0f)
            {
                _verticalSpeed = 0f;
            }

            if (
                _verticalSpeed < LostFallSpeed
                && !Physics.Raycast(Feet(), Vector3.down, GroundProbeMeters)
            )
            {
                _body.enabled = false;
                PlaceOnGround();
                _verticalSpeed = 0f;
                _body.enabled = true;
            }
        }

        // On the first collider under the camera, or on the generated surface when there is
        // none (colliders exist only near the focus). The body must be disabled.
        private void PlaceOnGround()
        {
            var position = transform.position;
            float ground;
            if (Physics.Raycast(position, Vector3.down, out var hit, GroundProbeMeters))
            {
                ground = hit.point.y;
            }
            else if (
                _world != null
                && _world.TryGetSurfaceHeightMeters(
                    Mathf.FloorToInt(position.x / WorldConstants.VoxelSize),
                    Mathf.FloorToInt(position.z / WorldConstants.VoxelSize),
                    out var surface
                )
            )
            {
                ground = surface;
            }
            else
            {
                return;
            }

            transform.position = new Vector3(position.x, ground + _eyeHeight + 0.05f, position.z);
        }

        // The capsule stays upright but its center turns with the camera; counter-rotate it
        // so the body stands under the eyes whatever the pitch.
        private void KeepBodyUnderEyes() =>
            _body.center =
                Quaternion.Inverse(transform.rotation)
                * new Vector3(0f, _bodyHeight * 0.5f - _eyeHeight, 0f);

        private Vector3 Feet() => transform.position + Vector3.down * (_eyeHeight - 0.05f);
#else
        private void Awake()
        {
            Destroy(this);
        }
#endif
    }
}
