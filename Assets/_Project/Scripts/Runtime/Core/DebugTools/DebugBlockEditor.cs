using Revoxel.World;
using UnityEngine;
#if UNITY_EDITOR || REVOXEL_DEBUG
using Unity.Mathematics;
using UnityEngine.InputSystem;
#endif

namespace Revoxel.Core
{
    // Debug block edits (PLAN-02 "Debug tools", D-045), on the camera: left click breaks
    // the block under the crosshair, right click places a Recycled block on the face under
    // it, G drops a test cube with a rigidbody to check the colliders. Aims with
    // Physics.Raycast against the chunk colliders; the real targeting (D-032) is WP-03.
    [RequireComponent(typeof(DebugFlyCamera))]
    public sealed class DebugBlockEditor : MonoBehaviour
    {
        [SerializeField]
        private VoxelWorld _world;

        [SerializeField]
        private BlockType _placeBlock;

        [SerializeField]
        [Tooltip("Debug tool value, reach in blocks.")]
        private float _reach = 8f;

        [SerializeField]
        [Tooltip("Debug tool value, seconds before a test cube is removed.")]
        private float _testCubeLifetime = 20f;

#if UNITY_EDITOR || REVOXEL_DEBUG
        private DebugFlyCamera _flyCamera;

        private void Awake()
        {
            _flyCamera = GetComponent<DebugFlyCamera>();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (
                _world == null
                || !_world.IsLoaded
                || keyboard == null
                || mouse == null
                || !_flyCamera.IsCapturingMouse
            )
            {
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame && TryAim(out var hit))
            {
                _world.TrySetBlock(
                    Coords.WorldPositionToBlock(hit.point - hit.normal * 0.5f),
                    BlockId.Air
                );
            }

            if (mouse.rightButton.wasPressedThisFrame && _placeBlock != null && TryAim(out hit))
            {
                var target = Coords.WorldPositionToBlock(hit.point + hit.normal * 0.5f);
                if (!Overlaps(target))
                {
                    _world.TrySetBlock(target, _placeBlock.Id);
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

        private bool Overlaps(int3 block)
        {
            var bounds = new Bounds(
                new Vector3(block.x + 0.5f, block.y + 0.5f, block.z + 0.5f),
                Vector3.one
            );
            return bounds.Contains(transform.position);
        }

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
