using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Revoxel.World;
using Unity.Collections;
using UnityEngine;

namespace Revoxel.Tests.EditMode
{
    public sealed class ChunkMeshPipelineTests
    {
        [Test]
        public void MeshPipeline_ResultsOlderThanThreeFrames_AreAppliedDespiteBudget()
        {
            // Writable mesh data may live 4 frames; results must not wait longer even when
            // the budget is spent every frame.
            var host = new GameObject("ChunkMeshPipelineTests");
            var pipeline = new ChunkMeshPipeline(
                WorldAssets.LoadRegistry(),
                4,
                WorldAssets.LoadRenderSettings().SkyLightCanopy
            );
            var views = new ChunkViewPool(host.transform, null);
            var air = ChunkVoxels.CreateUniform(VoxelId.Air, Allocator.Persistent);
            var chunks = new List<Chunk>();
            var applied = new List<Chunk>();
            try
            {
                var neighborhood = pipeline.Neighborhood;
                for (var i = 0; i < 27; i++)
                {
                    neighborhood[i] = air;
                }

                for (var i = 0; i < 3; i++)
                {
                    var chunk = new Chunk();
                    chunk.Reset(
                        new ChunkCoord(i, 0, 0),
                        ChunkVoxels.CreateUniform(new VoxelId(5), Allocator.Persistent),
                        null
                    );
                    chunks.Add(chunk);
                    neighborhood[13] = chunk.Voxels;
                    pipeline.Schedule(chunk, false, 10);
                }

                var spent = Stopwatch.StartNew();
                pipeline.ApplyFinished(views, applied, spent, 0, 13);

                Assert.That(applied.Count, Is.EqualTo(3));
                Assert.That(pipeline.Busy, Is.Zero);
                foreach (var chunk in chunks)
                {
                    Assert.That(chunk.View, Is.Not.Null);
                    Assert.That(chunk.MeshVersion, Is.EqualTo(1));
                }
            }
            finally
            {
                pipeline.Dispose();
                views.Dispose();
                air.Dispose();
                foreach (var chunk in chunks)
                {
                    chunk.Dispose();
                }

                Object.DestroyImmediate(host);
            }
        }
    }
}
