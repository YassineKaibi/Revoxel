using System;
using System.Collections.Generic;
using UnityEngine;

namespace Revoxel.World
{
    // The scene object of a chunk with geometry, with its two meshes.
    public sealed class ChunkView
    {
        public GameObject GameObject;
        public MeshFilter Filter;
        public MeshRenderer Renderer;
        public MeshCollider Collider;
        public Mesh RenderMesh;
        public Mesh CollisionMesh;
    }

    // Reuses chunk GameObjects with their MeshFilter, MeshRenderer, MeshCollider and meshes,
    // so streaming creates no objects once the pool is warm.
    internal sealed class ChunkViewPool : IDisposable
    {
        private readonly Stack<ChunkView> _free = new Stack<ChunkView>();
        private readonly List<ChunkView> _all = new List<ChunkView>();
        private readonly Transform _parent;
        private readonly Material[] _materials;

        // One material per render submesh: voxels, then liquids (D-073).
        public ChunkViewPool(Transform parent, Material[] materials)
        {
            _parent = parent;
            _materials = materials;
        }

        public int ActiveCount { get; private set; }

        // Voxel coordinates are world coordinates: the world sits at the origin.
        public ChunkView Get(ChunkCoord coord)
        {
            var view = _free.Count > 0 ? _free.Pop() : Create();
            var origin = Coords.VoxelToWorldPosition(Coords.ChunkOrigin(coord));
            view.GameObject.transform.position = new Vector3(origin.x, origin.y, origin.z);
            view.GameObject.SetActive(true);
            view.Renderer.enabled = true;
            ActiveCount++;
            return view;
        }

        public void Release(ChunkView view)
        {
            view.Collider.sharedMesh = null;
            view.GameObject.SetActive(false);
            _free.Push(view);
            ActiveCount--;
        }

        public void Dispose()
        {
            foreach (var view in _all)
            {
                DestroyObject(view.GameObject);
                DestroyObject(view.RenderMesh);
                DestroyObject(view.CollisionMesh);
            }

            _all.Clear();
            _free.Clear();
            ActiveCount = 0;
        }

        private ChunkView Create()
        {
            var go = new GameObject("Chunk");
            go.layer = _parent.gameObject.layer;
            go.transform.SetParent(_parent, false);
            var view = new ChunkView
            {
                GameObject = go,
                Filter = go.AddComponent<MeshFilter>(),
                Collider = go.AddComponent<MeshCollider>(),
                RenderMesh = new Mesh { name = "ChunkRender" },
                CollisionMesh = new Mesh { name = "ChunkCollision" },
            };
            view.Renderer = go.AddComponent<MeshRenderer>();
            if (_materials != null)
            {
                view.Renderer.sharedMaterials = _materials;
            }

            view.Filter.sharedMesh = view.RenderMesh;
            _all.Add(view);
            return view;
        }

        private static void DestroyObject(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(obj);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }
    }
}
