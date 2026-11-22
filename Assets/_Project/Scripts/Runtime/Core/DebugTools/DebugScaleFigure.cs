using Revoxel.World;
using UnityEngine;
#if UNITY_EDITOR || REVOXEL_DEBUG
using Unity.Mathematics;
using UnityEngine.InputSystem;
#endif

namespace Revoxel.Core
{
    // Debug scale reference (D-045), on the camera: M places a 1.8 m figure on the ground
    // where the camera aims, facing the camera; pressing M again moves it. The figure is an
    // orange capsule 0.5 m wide with a dark bar at eye height (1.7 m) on its front. Aims
    // through the loaded voxels, not the colliders, so it reaches as far as the world is
    // loaded. Sizes are debug values, not the player's (STATUS Q-44).
    [RequireComponent(typeof(DebugFlyCamera))]
    public sealed class DebugScaleFigure : MonoBehaviour
    {
#if UNITY_EDITOR || REVOXEL_DEBUG
        [SerializeField]
        private VoxelWorld _world;

        [SerializeField]
        [Tooltip("Debug tool value, figure height in meters.")]
        private float _height = 1.8f;

        [SerializeField]
        [Tooltip("Debug tool value, figure width in meters.")]
        private float _width = 0.5f;

        [SerializeField]
        [Tooltip("Debug tool value, eye bar height above the feet in meters.")]
        private float _eyeHeight = 1.7f;

        [SerializeField]
        [Tooltip("Debug tool value, how far the aim reaches in meters.")]
        private float _reach = 64f;

        private Transform _figure;
        private Material _bodyMaterial;
        private Material _markerMaterial;

        // Null until placed once.
        public Transform Figure => _figure;

        // Places the figure on the ground under the first non-air voxel along the camera's
        // forward ray. False when nothing loaded is within reach.
        public bool PlaceAtAim()
        {
            if (_world == null || !TryAim(out var feet))
            {
                return false;
            }

            if (_figure == null)
            {
                _figure = Build();
            }

            var toCamera = transform.position - feet;
            toCamera.y = 0f;
            _figure.SetPositionAndRotation(
                feet,
                toCamera.sqrMagnitude > 1e-4f
                    ? Quaternion.LookRotation(toCamera.normalized, Vector3.up)
                    : Quaternion.identity
            );
            return true;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.mKey.wasPressedThisFrame)
            {
                PlaceAtAim();
            }
        }

        private void OnDestroy()
        {
            if (_figure != null)
            {
                Destroy(_figure.gameObject);
            }

            if (_bodyMaterial != null)
            {
                Destroy(_bodyMaterial);
            }

            if (_markerMaterial != null)
            {
                Destroy(_markerMaterial);
            }
        }

        // Voxel walk along the ray (Amanatides and Woo). The figure stands in the air cell
        // in front of the hit, dropped to the ground below it.
        private bool TryAim(out Vector3 feet)
        {
            feet = default;
            var origin = (float3)transform.position / WorldConstants.VoxelSize;
            var dir = math.normalize((float3)transform.forward);
            var cell = (int3)math.floor(origin);
            var step = (int3)math.sign(dir);
            var inverse = math.abs(1f / dir);
            var tMax = new float3(
                Boundary(origin.x, cell.x, dir.x),
                Boundary(origin.y, cell.y, dir.y),
                Boundary(origin.z, cell.z, dir.z)
            );
            var maxT = _reach / WorldConstants.VoxelSize;
            var previous = cell;
            var hasPrevious = false;
            var t = 0f;
            while (t <= maxT)
            {
                if (_world.GetVoxel(cell) != VoxelId.Air)
                {
                    var stand = hasPrevious ? previous : cell;
                    if (!hasPrevious)
                    {
                        while (
                            stand.y < WorldConstants.WorldHeightVoxels
                            && _world.GetVoxel(stand) != VoxelId.Air
                        )
                        {
                            stand.y++;
                        }
                    }

                    while (stand.y > 0 && _world.GetVoxel(stand - new int3(0, 1, 0)) == VoxelId.Air)
                    {
                        stand.y--;
                    }

                    feet = new Vector3(
                        (stand.x + 0.5f) * WorldConstants.VoxelSize,
                        stand.y * WorldConstants.VoxelSize,
                        (stand.z + 0.5f) * WorldConstants.VoxelSize
                    );
                    return true;
                }

                previous = cell;
                hasPrevious = true;
                if (tMax.x <= tMax.y && tMax.x <= tMax.z)
                {
                    t = tMax.x;
                    tMax.x += inverse.x;
                    cell.x += step.x;
                }
                else if (tMax.y <= tMax.z)
                {
                    t = tMax.y;
                    tMax.y += inverse.y;
                    cell.y += step.y;
                }
                else
                {
                    t = tMax.z;
                    tMax.z += inverse.z;
                    cell.z += step.z;
                }
            }

            return false;
        }

        // Ray distance, in voxels, to the first cell boundary along one axis.
        private static float Boundary(float origin, int cell, float dir)
        {
            if (dir > 0f)
            {
                return (cell + 1 - origin) / dir;
            }

            return dir < 0f ? (origin - cell) / -dir : float.PositiveInfinity;
        }

        private Transform Build()
        {
            var root = new GameObject("DebugScaleFigure").transform;

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(root, false);
            // The capsule primitive is 2 m tall and 1 m wide.
            body.transform.localScale = new Vector3(_width, _height * 0.5f, _width);
            body.transform.localPosition = new Vector3(0f, _height * 0.5f, 0f);
            var bodyRenderer = body.GetComponent<Renderer>();
            _bodyMaterial = new Material(bodyRenderer.sharedMaterial)
            {
                color = new Color(1f, 0.45f, 0.1f),
            };
            bodyRenderer.sharedMaterial = _bodyMaterial;

            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "EyeBar";
            Destroy(marker.GetComponent<Collider>());
            marker.transform.SetParent(root, false);
            marker.transform.localScale = new Vector3(_width * 0.6f, 0.08f, 0.1f);
            marker.transform.localPosition = new Vector3(0f, _eyeHeight, _width * 0.5f);
            var markerRenderer = marker.GetComponent<Renderer>();
            _markerMaterial = new Material(markerRenderer.sharedMaterial)
            {
                color = new Color(0.1f, 0.1f, 0.1f),
            };
            markerRenderer.sharedMaterial = _markerMaterial;

            return root;
        }
#else
        private void Awake()
        {
            Destroy(this);
        }
#endif
    }
}
