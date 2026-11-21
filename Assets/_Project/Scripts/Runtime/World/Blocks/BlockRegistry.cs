using Unity.Collections;
using UnityEngine;

namespace Revoxel.World
{
    // Lists every BlockType. Valid when ids are unique and id 0 is a non-solid,
    // non-opaque Air block.
    [CreateAssetMenu(menuName = "Revoxel/World/Block Registry", fileName = "BlockRegistry")]
    public sealed class BlockRegistry : ScriptableObject
    {
        [SerializeField]
        private BlockType[] _blocks = new BlockType[0];

        public int Count => _blocks.Length;

        public BlockType this[int index] => _blocks[index];

        public bool TryValidate(out string error)
        {
            var seen = new bool[WorldConstants.MaxBlockTypes];
            var hasAir = false;
            for (var i = 0; i < _blocks.Length; i++)
            {
                var block = _blocks[i];
                if (block == null)
                {
                    error = $"entry {i} is empty";
                    return false;
                }

                var id = block.Id.Value;
                if (seen[id])
                {
                    error = $"id {id} is used twice ({block.name})";
                    return false;
                }

                seen[id] = true;
                if (string.IsNullOrWhiteSpace(block.NameKey))
                {
                    error = $"{block.name} has no name key";
                    return false;
                }

                if (id == 0)
                {
                    if (block.IsSolid || block.IsOpaque)
                    {
                        error = $"id 0 ({block.name}) must be Air: not solid, not opaque";
                        return false;
                    }

                    hasAir = true;
                }
            }

            if (!hasAir)
            {
                error = "no block with id 0 (Air)";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public bool TryGet(BlockId id, out BlockType block)
        {
            foreach (var candidate in _blocks)
            {
                if (candidate != null && candidate.Id == id)
                {
                    block = candidate;
                    return true;
                }
            }

            block = null;
            return false;
        }

        // Lookup table indexed by block id, for the meshing job. Unknown ids stay default
        // (not solid, not opaque, black).
        public NativeArray<BlockRenderData> CreateRenderData(Allocator allocator)
        {
            var data = new NativeArray<BlockRenderData>(WorldConstants.MaxBlockTypes, allocator);
            foreach (var block in _blocks)
            {
                if (block == null)
                {
                    continue;
                }

                data[block.Id.Value] = BlockRenderData.FromSrgb(
                    block.IsSolid,
                    block.IsOpaque,
                    block.CorruptedColor,
                    block.RestoredColor
                );
            }

            return data;
        }

        internal void SetBlocks(BlockType[] blocks)
        {
            _blocks = blocks;
        }

        private void OnValidate()
        {
            if (!TryValidate(out var error))
            {
                Debug.LogWarning($"[World] Block registry '{name}' is invalid: {error}", this);
            }
        }
    }
}
