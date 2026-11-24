using UnityEngine;

namespace Revoxel.World
{
    // Definition of one voxel type: one asset per type, named Voxel_<Name> (docs/05).
    [CreateAssetMenu(menuName = "Revoxel/World/Voxel Type", fileName = "Voxel_")]
    public sealed class VoxelType : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Unique id stored in chunk data, below 1024. 0 is Air.")]
        private ushort _id;

        [SerializeField]
        [Tooltip("Localization key of the display name, voxel.<name>.name (D-047 rule).")]
        private string _nameKey = string.Empty;

        [SerializeField]
        [Tooltip("Solid voxels collide and are in the collision mesh.")]
        private bool _isSolid = true;

        [SerializeField]
        [Tooltip("Opaque voxels hide the faces of their neighbors.")]
        private bool _isOpaque = true;

        [SerializeField]
        [Tooltip("Liquid voxels (water) are drawn with the water shader, only toward air (D-073).")]
        private bool _isLiquid;

        [SerializeField]
        [Tooltip("sRGB true color of the material. Placeholder value (Q-12).")]
        private Color _baseColor = Color.white;

        [SerializeField]
        [Tooltip("sRGB color the surface pattern blends toward. Placeholder value (Q-12).")]
        private Color _secondaryColor = Color.gray;

        [SerializeField]
        [Tooltip("World-space pattern that makes adjacent voxels look continuous (D-051).")]
        private SurfacePattern _pattern = SurfacePattern.Flat;

        [SerializeField]
        [Tooltip("Pattern feature size in meters, 0 to 8. Placeholder value (Q-12).")]
        [Range(0f, 8f)]
        private float _patternScale = 1f;

        [SerializeField]
        [Tooltip(
            "How far the pattern blends toward the secondary color, 0 to 1. Placeholder value (Q-12)."
        )]
        [Range(0f, 1f)]
        private float _patternContrast;

        public VoxelId Id => new VoxelId(_id);

        public string NameKey => _nameKey;

        public bool IsSolid => _isSolid;

        public bool IsOpaque => _isOpaque;

        public bool IsLiquid => _isLiquid;

        public Color BaseColor => _baseColor;

        public Color SecondaryColor => _secondaryColor;

        public SurfacePattern Pattern => _pattern;

        public float PatternScale => _patternScale;

        public float PatternContrast => _patternContrast;

        internal void Configure(
            ushort id,
            string nameKey,
            bool isSolid,
            bool isOpaque,
            Color baseColor,
            Color secondaryColor,
            SurfacePattern pattern,
            float patternScale,
            float patternContrast,
            bool isLiquid = false
        )
        {
            _id = id;
            _nameKey = nameKey;
            _isSolid = isSolid;
            _isOpaque = isOpaque;
            _baseColor = baseColor;
            _secondaryColor = secondaryColor;
            _pattern = pattern;
            _patternScale = patternScale;
            _patternContrast = patternContrast;
            _isLiquid = isLiquid;
        }
    }
}
