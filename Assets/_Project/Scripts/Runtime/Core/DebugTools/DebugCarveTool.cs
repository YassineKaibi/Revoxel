using Revoxel.World;
using UnityEngine;
#if UNITY_EDITOR || REVOXEL_DEBUG
using Unity.Mathematics;
using UnityEngine.InputSystem;
#endif

namespace Revoxel.Core
{
    // Debug voxel edits (PLAN-02 Task 9, D-045), on the camera: left click carves a sphere
    // at the aimed point, right click fills a sphere of the fill type on the surface, the
    // mouse wheel changes the radius, G drops a test cube with a rigidbody to check the
    // colliders. Aims with Physics.Raycast against the chunk colliders; the real tools are
    // WP-07 and WP-13.
    [RequireComponent(typeof(DebugFlyCamera))]
    public sealed class DebugCarveTool : MonoBehaviour
    {
#if UNITY_EDITOR || REVOXEL_DEBUG
        [SerializeField]
        private VoxelWorld _world;

        [SerializeField]
        private VoxelType _fill;

        [SerializeField]
        [Tooltip("Debug tool value, sphere radius in meters.")]
        private float _radius = 0.5f;

        [SerializeField]
        [Tooltip("Debug tool value, smallest radius in meters.")]
        private float _minRadius = 0.125f;

        [SerializeField]
        [Tooltip("Debug tool value, largest radius in meters.")]
        private float _maxRadius = 2f;

        [SerializeField]
        [Tooltip("Debug tool value, radius change per wheel notch in meters.")]
        private float _radiusStep = 0.125f;

        [SerializeField]
        [Tooltip("Debug tool value, reach in meters.")]
        private float _reach = 8f;

        [SerializeField]
        [Tooltip("Debug tool value, seconds before a test cube is removed.")]
        private float _testCubeLifetime = 20f;

        private DebugFlyCamera _flyCamera;

        public float Radius => _radius;

        private void Awake()
        {
            _flyCamera = GetComponent<DebugFlyCamera>();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (_world == null || keyboard == null || mouse == null || !_flyCamera.IsCapturingMouse)
            {
                return;
            }

            var scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f)
            {
                _radius = Mathf.Clamp(
                    _radius + Mathf.Sign(scroll) * _radiusStep,
                    _minRadius,
                    _maxRadius
                );
            }

            if (mouse.leftButton.wasPressedThisFrame && TryAim(out var hit))
            {
                _world.CarveSphere(hit.point, _radius, VoxelId.Air);
            }

            if (mouse.rightButton.wasPressedThisFrame && _fill != null && TryAim(out hit))
            {
                var center = (float3)(hit.point + hit.normal * _radius);
                if (math.distance(center, (float3)transform.position) > _radius + 0.5f)
                {
                    _world.CarveSphere(center, _radius, _fill.Id);
                }
            }

            if (keyboard.gKey.wasPressedThisFrame)
            {
                DropTestCube();
            }
        }

        private bool TryAim(out RaycastHit hit) =>
            Physics.Raycast(transform.position, transform.forward, out hit, _reach)
            && hit.collider.transform.parent == _world.transform;

        private void DropTestCube()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "DebugTestCube";
            cube.transform.position = transform.position + transform.forward * 2f;
            cube.transform.localScale = Vector3.one * 0.5f;
            cube.AddComponent<Rigidbody>();
            Destroy(cube, _testCubeLifetime);
        }
#else
        private void Awake()
        {
            Destroy(this);
        }
#endif
    }
}
