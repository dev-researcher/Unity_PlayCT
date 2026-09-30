using System;
using System.Collections.Generic;
using NUnit.Framework;
using PlayCT.Tasks.Hanoi;

namespace PlayCT.Tests
{
    public class RingMeshBuilderTests
    {
        static readonly float[] Sizes = { 0.05f, 0.065f, 0.08f, 0.095f, 0.11f };

        [TestCaseSource(nameof(Sizes))]
        public void Mesh_StaysWithinTheDiskEnvelope(float outer)
        {
            var mesh = RingMeshBuilder.Build(outer, 0.014f, 0.022f, 0.003f, 40);
            for (var i = 0; i < mesh.VertexCount; i++)
            {
                var x = mesh.Vertices[i * 3];
                var y = mesh.Vertices[i * 3 + 1];
                var z = mesh.Vertices[i * 3 + 2];
                var r = (float)Math.Sqrt(x * x + z * z);
                Assert.LessOrEqual(r, outer + 1e-5f);
                Assert.GreaterOrEqual(r, 0.014f - 1e-5f);
                Assert.LessOrEqual(Math.Abs(y), 0.011f + 1e-6f);
            }
            Assert.AreEqual(0, mesh.Triangles.Length % 3);
            Assert.Less(mesh.VertexCount, 65000);
        }

        [TestCaseSource(nameof(Sizes))]
        public void Triangles_AreWoundClockwiseSeenFromOutside_AsUnityExpects(float outer)
        {
            var mesh = RingMeshBuilder.Build(outer, 0.014f, 0.022f, 0.003f, 40);
            for (var t = 0; t < mesh.Triangles.Length; t += 3)
            {
                int a = mesh.Triangles[t], b = mesh.Triangles[t + 1], c = mesh.Triangles[t + 2];
                var ab = Sub(P(mesh, b), P(mesh, a));
                var ac = Sub(P(mesh, c), P(mesh, a));
                var n = Cross(ab, ac);
                var vn = Add(Add(N(mesh, a), N(mesh, b)), N(mesh, c));
                Assert.Greater(Dot(n, vn), 0f, $"triangle {t / 3} faces against its normals");
            }
        }

        [Test]
        public void Surface_IsClosed_EveryEdgeIsSharedByExactlyTwoTriangles()
        {
            var mesh = RingMeshBuilder.Build(0.08f, 0.014f, 0.022f, 0.003f, 40);
            var edges = new Dictionary<(string, string), int>();
            for (var t = 0; t < mesh.Triangles.Length; t += 3)
            {
                for (var e = 0; e < 3; e++)
                {
                    var k0 = Key(mesh, mesh.Triangles[t + e]);
                    var k1 = Key(mesh, mesh.Triangles[t + (e + 1) % 3]);
                    var key = string.CompareOrdinal(k0, k1) < 0 ? (k0, k1) : (k1, k0);
                    edges.TryGetValue(key, out var count);
                    edges[key] = count + 1;
                }
            }
            foreach (var kv in edges)
                Assert.AreEqual(2, kv.Value, $"open edge {kv.Key}");
        }

        static string Key(RingMeshData m, int i) =>
            $"{Math.Round(m.Vertices[i * 3], 5)}|{Math.Round(m.Vertices[i * 3 + 1], 5)}|{Math.Round(m.Vertices[i * 3 + 2], 5)}";
        static float[] P(RingMeshData m, int i) => new[] { m.Vertices[i * 3], m.Vertices[i * 3 + 1], m.Vertices[i * 3 + 2] };
        static float[] N(RingMeshData m, int i) => new[] { m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2] };
        static float[] Sub(float[] a, float[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        static float[] Add(float[] a, float[] b) => new[] { a[0] + b[0], a[1] + b[1], a[2] + b[2] };
        static float Dot(float[] a, float[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        static float[] Cross(float[] a, float[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
    }
}
