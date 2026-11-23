using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Revoxel.World
{
    // The scene object of one coarse chunk: renderer only, scaled by 2^level.
    internal sealed class DetailView
    {
        public GameObject GameObject;
        public MeshRenderer Renderer;
        public Mesh Mesh;
        public long MeshBytes;
    }

    // Reuses coarse chunk GameObjects and meshes, so the detail creates no objects once
    // the pool is warm. Views come out hidden; the swap step shows them.
    internal sealed class DetailViewPool : IDisposable
    {
        private readonly Stack<DetailView> _free = new Stack<DetailView>();
        private readonly List<DetailView> _all = new List<DetailView>();
        private readonly Transform _parent;
        private readonly Material[] _materials;

        // One material per render submesh: voxels, then liquids (D-073).
        public DetailViewPool(Transform parent, Material[] materials)
        {
            _parent = parent;
            _materials = materials;
        }

        public int ActiveCount { get; private set; }

        // Min corner of a level-L chunk, meters.
        public static float3 Origin(int level, ChunkCoord coord) =>
            (float3)(coord.Value * (WorldConstants.ChunkSize << level)) * WorldConstants.VoxelSize;

        public DetailView Get(int level, ChunkCoord coord)
        {
            var view = _free.Count > 0 ? _free.Pop() : Create();
            var origin = Origin(level, coord);
            var t = view.GameObject.transform;
            t.position = new Vector3(origin.x, origin.y, origin.z);
            t.localScale = Vector3.one * (1 << level);
            view.Renderer.enabled = false;
            view.GameObject.SetActive(true);
            ActiveCount++;
            return view;
        }

        public void Release(DetailView view)
        {
            view.Mesh.Clear();
            view.MeshBytes = 0;
            view.GameObject.SetActive(false);
            _free.Push(view);
            ActiveCount--;
        }

        public void Dispose()
        {
            foreach (var view in _all)
            {
                DestroyObject(view.GameObject);
                DestroyObject(view.Mesh);
            }

            _all.Clear();
            _free.Clear();
            ActiveCount = 0;
        }

        private DetailView Create()
        {
            var go = new GameObject("DetailChunk");
            go.layer = _parent.gameObject.layer;
            go.transform.SetParent(_parent, false);
            var view = new DetailView
            {
                GameObject = go,
                Renderer = go.AddComponent<MeshRenderer>(),
                Mesh = new Mesh { name = "DetailChunk" },
            };
            if (_materials != null)
            {
                view.Renderer.sharedMaterials = _materials;
            }

            go.AddComponent<MeshFilter>().sharedMesh = view.Mesh;
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
