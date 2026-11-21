using UnityEngine;

namespace Revoxel.World
{
    // Definition of one block type: one asset per type, named Block_<Name> (docs/05).
    [CreateAssetMenu(menuName = "Revoxel/World/Block Type", fileName = "Block_")]
    public sealed class BlockType : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Unique id stored in chunk data. 0 is Air.")]
        private byte _id;

        [SerializeField]
        [Tooltip("Localization key of the display name, block.<id>.name (D-047).")]
        private string _nameKey = string.Empty;

        [SerializeField]
        [Tooltip("Solid blocks collide and are in the collision mesh.")]
        private bool _isSolid = true;

        [SerializeField]
        [Tooltip("Opaque blocks hide the faces of their neighbors.")]
        private bool _isOpaque = true;

        [SerializeField]
        [Tooltip("sRGB. Placeholder palette value (D-021, Q-12).")]
        private Color _corruptedColor = Color.gray;

        [SerializeField]
        [Tooltip("sRGB. Placeholder palette value (D-021, Q-12).")]
        private Color _restoredColor = Color.white;

        public BlockId Id => new BlockId(_id);

        public string NameKey => _nameKey;

        public bool IsSolid => _isSolid;

        public bool IsOpaque => _isOpaque;

        public Color CorruptedColor => _corruptedColor;

        public Color RestoredColor => _restoredColor;

        internal void Configure(
            byte id,
            string nameKey,
            bool isSolid,
            bool isOpaque,
            Color corruptedColor,
            Color restoredColor
        )
        {
            _id = id;
            _nameKey = nameKey;
            _isSolid = isSolid;
            _isOpaque = isOpaque;
            _corruptedColor = corruptedColor;
            _restoredColor = restoredColor;
        }
    }
}
