using System;
using System.Collections.Generic;

namespace PlayCT.Tasks.Hanoi
{
    public sealed class RingMeshData
    {
        public float[] Vertices;
        public float[] Normals;
        public float[] Uvs;
        public int[] Triangles;

        public int VertexCount => Vertices.Length / 3;
    }

    /// <summary>
    /// Builds a flat ring (a disk with a central hole and chamfered outer edges), centred on the origin
    /// with its axis along Y. Pure geometry so it can be verified without the Unity engine.
    /// </summary>
    public static class RingMeshBuilder
    {
        public static RingMeshData Build(float outerRadius, float innerRadius, float thickness, float chamfer, int segments)
        {
            if (segments < 8) throw new ArgumentOutOfRangeException(nameof(segments));
            if (innerRadius <= 0f || innerRadius >= outerRadius) throw new ArgumentException("Inner radius must be between 0 and the outer radius.");
            chamfer = Math.Min(chamfer, Math.Min(thickness * 0.5f - 1e-4f, outerRadius - innerRadius - 1e-4f));
            if (chamfer < 0f) chamfer = 0f;

            var h = thickness * 0.5f;
            // Counter-clockwise cross-section in the (radius, height) plane.
            var profile = new List<float[]>
            {
                new[] { innerRadius, -h },
                new[] { outerRadius - chamfer, -h },
                new[] { outerRadius, -h + chamfer },
                new[] { outerRadius, h - chamfer },
                new[] { outerRadius - chamfer, h },
                new[] { innerRadius, h },
            };
            if (chamfer <= 0f)
            {
                profile.RemoveAt(4);
                profile.RemoveAt(1);
            }

            var edgeCount = profile.Count;
            var totalLength = 0f;
            var edgeLengths = new float[edgeCount];
            for (var e = 0; e < edgeCount; e++)
            {
                var a = profile[e];
                var b = profile[(e + 1) % edgeCount];
                edgeLengths[e] = (float)Math.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[1] - a[1]) * (b[1] - a[1]));
                totalLength += edgeLengths[e];
            }

            var ringVertices = (segments + 1) * 2;
            var vertexCount = ringVertices * edgeCount;
            var data = new RingMeshData
            {
                Vertices = new float[vertexCount * 3],
                Normals = new float[vertexCount * 3],
                Uvs = new float[vertexCount * 2],
                Triangles = new int[segments * edgeCount * 6],
            };

            var vi = 0;
            var ti = 0;
            var vAccum = 0f;
            for (var e = 0; e < edgeCount; e++)
            {
                var a = profile[e];
                var b = profile[(e + 1) % edgeCount];
                var dr = b[0] - a[0];
                var dy = b[1] - a[1];
                var len = edgeLengths[e];
                var nr = dy / len;
                var ny = -dr / len;
                var baseIndex = vi;

                for (var s = 0; s <= segments; s++)
                {
                    var t = (float)s / segments;
                    var angle = t * (float)(2.0 * Math.PI);
                    var cos = (float)Math.Cos(angle);
                    var sin = (float)Math.Sin(angle);
                    for (var end = 0; end < 2; end++)
                    {
                        var p = end == 0 ? a : b;
                        data.Vertices[vi * 3 + 0] = p[0] * cos;
                        data.Vertices[vi * 3 + 1] = p[1];
                        data.Vertices[vi * 3 + 2] = p[0] * sin;
                        data.Normals[vi * 3 + 0] = nr * cos;
                        data.Normals[vi * 3 + 1] = ny;
                        data.Normals[vi * 3 + 2] = nr * sin;
                        data.Uvs[vi * 2 + 0] = t * 2f;
                        data.Uvs[vi * 2 + 1] = (vAccum + (end == 0 ? 0f : len)) / totalLength;
                        vi++;
                    }
                }

                for (var s = 0; s < segments; s++)
                {
                    var i0 = baseIndex + s * 2;
                    var i1 = i0 + 1;
                    var i2 = i0 + 2;
                    var i3 = i0 + 3;
                    AddOutwardTriangle(data, ref ti, i0, i1, i2);
                    AddOutwardTriangle(data, ref ti, i2, i1, i3);
                }
                vAccum += len;
            }

            return data;
        }

        // Unity front faces are wound clockwise seen from outside, which is the order whose cross product
        // (b - a) x (c - a) points along the outward normal. Pick that order whatever the revolve direction is.
        static void AddOutwardTriangle(RingMeshData d, ref int ti, int a, int b, int c)
        {
            var ax = d.Vertices[a * 3]; var ay = d.Vertices[a * 3 + 1]; var az = d.Vertices[a * 3 + 2];
            var bx = d.Vertices[b * 3]; var by = d.Vertices[b * 3 + 1]; var bz = d.Vertices[b * 3 + 2];
            var cx = d.Vertices[c * 3]; var cy = d.Vertices[c * 3 + 1]; var cz = d.Vertices[c * 3 + 2];
            var ux = bx - ax; var uy = by - ay; var uz = bz - az;
            var vx = cx - ax; var vy = cy - ay; var vz = cz - az;
            var nx = uy * vz - uz * vy;
            var ny = uz * vx - ux * vz;
            var nz = ux * vy - uy * vx;
            var dot = nx * d.Normals[a * 3] + ny * d.Normals[a * 3 + 1] + nz * d.Normals[a * 3 + 2];
            if (dot < 0f)
            {
                d.Triangles[ti++] = a; d.Triangles[ti++] = c; d.Triangles[ti++] = b;
            }
            else
            {
                d.Triangles[ti++] = a; d.Triangles[ti++] = b; d.Triangles[ti++] = c;
            }
        }
    }
}
