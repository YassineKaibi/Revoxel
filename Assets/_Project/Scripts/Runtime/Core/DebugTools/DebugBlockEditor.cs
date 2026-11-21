using UnityEngine;
#if UNITY_EDITOR || REVOXEL_DEBUG
using UnityEngine.InputSystem;
#endif

namespace Revoxel.Core
{
    // Debug tool on the camera (D-045): G drops a test cube with a rigidbody to check the
    // colliders. Voxel carving is replaced by DebugCarveTool (PLAN-02 Task 9).
    [RequireComponent(typeof(DebugFlyCamera))]
    public sealed class DebugBlockEditor : MonoBehaviour
    {
#if UNITY_EDITOR || REVOXEL_DEBUG
        [SerializeField]
        [Tooltip("Debug tool value, seconds before a test cube is removed.")]
        private float _testCubeLifetime = 20f;

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.gKey.wasPressedThisFrame)
            {
                DropTestCube();
            }
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
