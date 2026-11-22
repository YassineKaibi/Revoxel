using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Revoxel.World
{
    // The voxels of one chunk (D-050): either one id for the whole chunk (uniform, no heap
    // memory) or a palette of ids plus palette indices bit-packed little-endian in 32-bit
    // words, at 1, 2, 4, 8 or 16 bits per voxel. With a power-of-two width a value never
    // straddles two words. Unmanaged, so it can live in NativeArrays and jobs.
    public struct ChunkVoxels : IDisposable
    {
        private const int Count = WorldConstants.VoxelsPerChunk;

        private UnsafeList<ushort> _palette;
        private UnsafeList<uint> _words;
        private AllocatorManager.AllocatorHandle _allocator;
        private ushort _uniformId;

        // log2 of the bits per index; 0xFF when uniform.
        private byte _bitsLog2;
        private bool _isCreated;

        private const byte UniformBits = 0xFF;

        public bool IsCreated => _isCreated;

        public bool IsUniform => _bitsLog2 == UniformBits;

        // Valid when IsUniform.
        public VoxelId UniformId => new VoxelId(_uniformId);

        public int BitsPerIndex => IsUniform ? 0 : 1 << _bitsLog2;

        public int PaletteCount => IsUniform ? 1 : _palette.Length;

        public long MemoryBytes =>
            UnsafeUtility.SizeOf<ChunkVoxels>()
            + (_palette.IsCreated ? (long)_palette.Capacity * sizeof(ushort) : 0)
            + (_words.IsCreated ? (long)_words.Capacity * sizeof(uint) : 0);

        public static ChunkVoxels CreateUniform(VoxelId id, Allocator allocator) =>
            new ChunkVoxels
            {
                _allocator = allocator,
                _uniformId = id.Value,
                _bitsLog2 = UniformBits,
                _isCreated = true,
            };

        // Uniform when every value is equal; otherwise the smallest width that holds the
        // palette. Burst-callable.
        public static ChunkVoxels FromDense(NativeArray<ushort> dense, Allocator allocator)
        {
            var first = dense[0];
            var allSame = true;
            for (var i = 1; i < Count; i++)
            {
                if (dense[i] != first)
                {
                    allSame = false;
                    break;
                }
            }

            if (allSame)
            {
                return CreateUniform(new VoxelId(first), allocator);
            }

            var voxels = new ChunkVoxels
            {
                _allocator = allocator,
                _isCreated = true,
                _palette = new UnsafeList<ushort>(16, allocator),
            };

            // First pass: the palette, with a one-entry cache for runs of the same id.
            var lastId = first;
            voxels._palette.Add(first);
            for (var i = 1; i < Count; i++)
            {
                var id = dense[i];
                if (id != lastId && voxels.FindInPalette(id) < 0)
                {
                    voxels._palette.Add(id);
                }

                lastId = id;
            }

            voxels._bitsLog2 = BitsLog2For(voxels._palette.Length);
            voxels.AllocateWords(voxels._bitsLog2);

            // Second pass: the indices.
            lastId = first;
            var lastIndex = 0;
            for (var i = 0; i < Count; i++)
            {
                var id = dense[i];
                if (id != lastId)
                {
                    lastIndex = voxels.FindInPalette(id);
                    lastId = id;
                }

                voxels.WriteIndex(i, lastIndex);
            }

            return voxels;
        }

        public VoxelId Get(int index) =>
            IsUniform ? new VoxelId(_uniformId) : new VoxelId(_palette[ReadIndex(index)]);

        // Grows the bits per index when the palette overflows, preserving every value.
        public void Set(int index, VoxelId id)
        {
            if (IsUniform)
            {
                if (id.Value == _uniformId)
                {
                    return;
                }

                _palette = new UnsafeList<ushort>(16, _allocator);
                _palette.Add(_uniformId);
                _palette.Add(id.Value);
                _bitsLog2 = 0;
                AllocateWords(0);
                WriteIndex(index, 1);
                return;
            }

            var paletteIndex = FindInPalette(id.Value);
            if (paletteIndex < 0)
            {
                paletteIndex = _palette.Length;
                _palette.Add(id.Value);
                var needed = BitsLog2For(_palette.Length);
                if (needed != _bitsLog2)
                {
                    Repack(needed);
                }
            }

            WriteIndex(index, paletteIndex);
        }

        public void CopyTo(NativeArray<ushort> dense)
        {
            if (IsUniform)
            {
                for (var i = 0; i < Count; i++)
                {
                    dense[i] = _uniformId;
                }

                return;
            }

            for (var i = 0; i < Count; i++)
            {
                dense[i] = _palette[ReadIndex(i)];
            }
        }

        public void Dispose()
        {
            if (_palette.IsCreated)
            {
                _palette.Dispose();
            }

            if (_words.IsCreated)
            {
                _words.Dispose();
            }

            _palette = default;
            _words = default;
            _isCreated = false;
        }

        // 2 entries need 1 bit, up to 4 need 2, 16 need 4, 256 need 8, more need 16.
        private static byte BitsLog2For(int paletteCount)
        {
            if (paletteCount <= 2)
            {
                return 0;
            }

            if (paletteCount <= 4)
            {
                return 1;
            }

            if (paletteCount <= 16)
            {
                return 2;
            }

            return paletteCount <= 256 ? (byte)3 : (byte)4;
        }

        private int FindInPalette(ushort id)
        {
            for (var i = 0; i < _palette.Length; i++)
            {
                if (_palette[i] == id)
                {
                    return i;
                }
            }

            return -1;
        }

        private void AllocateWords(byte bitsLog2)
        {
            // 32 / bits values per word.
            var words = Count >> (5 - bitsLog2);
            _words = new UnsafeList<uint>(words, _allocator);
            _words.Resize(words, NativeArrayOptions.ClearMemory);
        }

        private void Repack(byte bitsLog2)
        {
            var old = _words;
            var oldLog2 = _bitsLog2;
            AllocateWords(bitsLog2);
            _bitsLog2 = bitsLog2;
            for (var i = 0; i < Count; i++)
            {
                WriteIndex(i, ReadIndex(old, oldLog2, i));
            }

            old.Dispose();
        }

        private int ReadIndex(int index) => ReadIndex(_words, _bitsLog2, index);

        private static int ReadIndex(UnsafeList<uint> words, byte bitsLog2, int index)
        {
            var perWordLog2 = 5 - bitsLog2;
            var shift = (index & ((1 << perWordLog2) - 1)) << bitsLog2;
            var mask = (1u << (1 << bitsLog2)) - 1u;
            return (int)((words[index >> perWordLog2] >> shift) & mask);
        }

        private void WriteIndex(int index, int paletteIndex)
        {
            var perWordLog2 = 5 - _bitsLog2;
            var shift = (index & ((1 << perWordLog2) - 1)) << _bitsLog2;
            var mask = ((1u << (1 << _bitsLog2)) - 1u) << shift;
            ref var word = ref _words.ElementAt(index >> perWordLog2);
            word = (word & ~mask) | (((uint)paletteIndex << shift) & mask);
        }
    }
}
