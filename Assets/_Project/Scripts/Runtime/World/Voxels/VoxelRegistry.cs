using System;
using Unity.Collections;
using UnityEngine;

namespace Revoxel.World
{
    // Every voxel type of the game, looked up by id (D-029, D-050).
    [CreateAssetMenu(menuName = "Revoxel/World/Voxel Registry", fileName = "VoxelRegistry")]
    public sealed class VoxelRegistry : ScriptableObject
    {
        // Rows of the material table read by the Revoxel/Voxel shader.
        public const int BaseColorRow = 0;
        public const int SecondaryColorRow = 1;
        public const int PatternRow = 2;
        public const int MaterialTableRows = 3;

        // The pattern scale is stored as scale / MaxPatternScale in one byte.
        public const float MaxPatternScale = 8f;

        private const float MaxPatternValue = (float)SurfacePattern.Patches;

        [SerializeField]
        private VoxelType[] _types = Array.Empty<VoxelType>();

        [NonSerialized]
        private VoxelType[] _byId;

        public bool TryValidate(out string error)
        {
            var seen = new bool[WorldConstants.MaxVoxelTypes];
            var hasAir = false;
            for (var i = 0; i < _types.Length; i++)
            {
                var type = _types[i];
                if (type == null)
                {
                    error = $"Entry {i} is empty.";
                    return false;
                }

                var id = type.Id.Value;
                if (id >= WorldConstants.MaxVoxelTypes)
                {
                    error = $"{type.name}: id {id} is not below {WorldConstants.MaxVoxelTypes}.";
                    return false;
                }

                if (seen[id])
                {
                    error = $"{type.name}: id {id} is used twice.";
                    return false;
                }

                if (string.IsNullOrEmpty(type.NameKey))
                {
                    error = $"{type.name}: name key is empty.";
                    return false;
                }

                if (id == 0)
                {
                    if (type.IsSolid || type.IsOpaque)
                    {
                        error = $"{type.name}: Air (id 0) must be neither solid nor opaque.";
                        return false;
                    }

                    hasAir = true;
                }

                seen[id] = true;
            }

            if (!hasAir)
            {
                error = "No Air type (id 0).";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public bool TryGet(VoxelId id, out VoxelType type)
        {
            var byId = ById();
            type = id.Value < byId.Length ? byId[id.Value] : null;
            return type != null;
        }

        public bool IsRegistered(VoxelId id) => TryGet(id, out _);

        // One entry per possible id; unregistered ids are neither solid nor opaque.
        public NativeArray<VoxelRenderData> CreateRenderData(Allocator allocator)
        {
            var data = new NativeArray<VoxelRenderData>(WorldConstants.MaxVoxelTypes, allocator);
            foreach (var type in _types)
            {
                if (type != null && type.Id.Value < WorldConstants.MaxVoxelTypes)
                {
                    data[type.Id.Value] = new VoxelRenderData
                    {
                        IsSolid = type.IsSolid,
                        IsOpaque = type.IsOpaque,
                    };
                }
            }

            return data;
        }

        // Width highest id + 1, three rows: base color and secondary color (linear), then
        // (pattern / 3, scale / 8, contrast, 1). The caller owns the texture.
        public Texture2D CreateMaterialTable()
        {
            var width = 1;
            foreach (var type in _types)
            {
                if (type != null)
                {
                    width = Math.Max(width, type.Id.Value + 1);
                }
            }

            var table = new Texture2D(
                width,
                MaterialTableRows,
                TextureFormat.RGBA32,
                mipChain: false,
                linear: true
            )
            {
                name = "RevoxelMaterialTable",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[width * MaterialTableRows];
            foreach (var type in _types)
            {
                if (type == null || type.Id.Value >= width)
                {
                    continue;
                }

                var x = type.Id.Value;
                pixels[BaseColorRow * width + x] = type.BaseColor.linear;
                pixels[SecondaryColorRow * width + x] = type.SecondaryColor.linear;
                pixels[PatternRow * width + x] = new Color(
                    (float)type.Pattern / MaxPatternValue,
                    Mathf.Clamp01(type.PatternScale / MaxPatternScale),
                    Mathf.Clamp01(type.PatternContrast),
                    1f
                );
            }

            table.SetPixels32(pixels);
            table.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return table;
        }

        internal void SetTypes(VoxelType[] types)
        {
            _types = types;
            _byId = null;
        }

        private void OnEnable()
        {
            _byId = null;
        }

        private void OnValidate()
        {
            _byId = null;
            if (!TryValidate(out var error))
            {
                Debug.LogWarning($"[World] {name}: {error}", this);
            }
        }

        private VoxelType[] ById()
        {
            if (_byId != null)
            {
                return _byId;
            }

            var highest = -1;
            foreach (var type in _types)
            {
                if (type != null && type.Id.Value < WorldConstants.MaxVoxelTypes)
                {
                    highest = Math.Max(highest, type.Id.Value);
                }
            }

            _byId = new VoxelType[highest + 1];
            foreach (var type in _types)
            {
                if (
                    type != null
                    && type.Id.Value < WorldConstants.MaxVoxelTypes
                    && _byId[type.Id.Value] == null
                )
                {
                    _byId[type.Id.Value] = type;
                }
            }

            return _byId;
        }
    }
}
