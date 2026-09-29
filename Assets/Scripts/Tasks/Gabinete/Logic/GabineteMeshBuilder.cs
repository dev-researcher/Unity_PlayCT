using System;
using System.Collections.Generic;

namespace PlayCT.Tasks.Gabinete
{
    public sealed class PolygonMeshData
    {
        public float[] Vertices;
        public float[] Normals;
        public float[] Uvs;
        public int[][] Submeshes;

        public int VertexCount => Vertices.Length / 3;
    }

    /// <summary>Where one opening sits on the board and which shape it takes.</summary>
    public readonly struct BoardOpening
    {
        public GabineteShapeType Shape { get; }
        public Vec2 Center { get; }

        public BoardOpening(GabineteShapeType shape, Vec2 center)
        {
            Shape = shape;
            Center = center;
        }
    }

    /// <summary>
    /// Builds the meshes of the cabinet: each piece as an extruded footprint (optionally tapered), and the board as a
    /// plate with a matching pocket for each opening. Pure geometry with no Unity types, verified offline by area and
    /// volume checks. Triangles are wound so that the cross product of their edges points out of the visible side.
    /// Coordinates: x and z in the plane of the board, y up; the piece origin is the centre of its footprint at the bottom;
    /// the board origin is the centre of its top surface.
    /// </summary>
    public static class GabineteMeshBuilder
    {
        const float UvScale = 4f;
        const double SmoothCosine = 0.9;

        public static PolygonMeshData BuildPiece(GabineteShapeSpec shape)
        {
            var mesh = new MeshAccumulator(1);
            var footprint = shape.Footprint;

            var bottom = Ring(footprint, shape.BaseScale, 0f, 0f, 0f);
            var top = Ring(footprint, shape.TopScale, shape.Height, 0f, 0f);
            AddWalls(mesh, 0, bottom, top, false);
            AddCap(mesh, 0, footprint, shape.BaseScale, 0f, 0f, 0f, -1f);
            AddCap(mesh, 0, footprint, shape.TopScale, shape.Height, 0f, 0f, 1f);
            return mesh.ToData();
        }

        /// <summary>
        /// The board: submesh 0 is the plate (top with the openings cut out, sides, underside), submesh 1 the inside of the
        /// pockets (walls and floor), so the pockets can be given a darker material.
        /// </summary>
        public static PolygonMeshData BuildBoard(IReadOnlyList<BoardOpening> openings, float width, float depth, float thickness, float pocketDepth)
        {
            if (openings == null || openings.Count == 0) throw new ArgumentException("A board needs at least one opening.", nameof(openings));
            if (pocketDepth <= 0 || pocketDepth >= thickness) throw new ArgumentException("The pockets must be shallower than the plate.", nameof(pocketDepth));

            var mesh = new MeshAccumulator(2);
            var halfW = width * 0.5f;
            var halfD = depth * 0.5f;

            var ordered = new List<BoardOpening>(openings);
            ordered.Sort((a, b) => a.Center.X.CompareTo(b.Center.X));

            for (var i = 0; i < ordered.Count; i++)
            {
                var opening = ordered[i];
                var shape = GabineteShapes.Get(opening.Shape);
                var (topScale, bottomScale) = GabineteShapes.OpeningScales(shape, pocketDepth);
                var left = i == 0 ? -halfW : (ordered[i - 1].Center.X + opening.Center.X) * 0.5f;
                var right = i == ordered.Count - 1 ? halfW : (opening.Center.X + ordered[i + 1].Center.X) * 0.5f;

                var cell = new List<Vec2> { new Vec2(left, -halfD), new Vec2(right, -halfD), new Vec2(right, halfD), new Vec2(left, halfD) };
                var holeTop = Polygon2D.Scaled(shape.Footprint, topScale, opening.Center);
                var tris = PolygonTriangulator.Triangulate(cell, new IReadOnlyList<Vec2>[] { holeTop }, out var pts);
                AddTriangles(mesh, 0, pts, tris, 0f, 1f);

                var wallTop = Ring(shape.Footprint, topScale, 0f, opening.Center.X, opening.Center.Y);
                var wallBottom = Ring(shape.Footprint, bottomScale, -pocketDepth, opening.Center.X, opening.Center.Y);
                AddWalls(mesh, 1, wallBottom, wallTop, true);
                AddCap(mesh, 1, shape.Footprint, bottomScale, -pocketDepth, opening.Center.X, opening.Center.Y, 1f);
            }

            AddQuad(mesh, 0, new[] { -halfW, -thickness, -halfD }, new[] { halfW, -thickness, -halfD }, new[] { halfW, 0f, -halfD }, new[] { -halfW, 0f, -halfD }, 0, 0, -1);
            AddQuad(mesh, 0, new[] { halfW, -thickness, halfD }, new[] { -halfW, -thickness, halfD }, new[] { -halfW, 0f, halfD }, new[] { halfW, 0f, halfD }, 0, 0, 1);
            AddQuad(mesh, 0, new[] { -halfW, -thickness, halfD }, new[] { -halfW, -thickness, -halfD }, new[] { -halfW, 0f, -halfD }, new[] { -halfW, 0f, halfD }, -1, 0, 0);
            AddQuad(mesh, 0, new[] { halfW, -thickness, -halfD }, new[] { halfW, -thickness, halfD }, new[] { halfW, 0f, halfD }, new[] { halfW, 0f, -halfD }, 1, 0, 0);
            AddQuad(mesh, 0, new[] { -halfW, -thickness, halfD }, new[] { halfW, -thickness, halfD }, new[] { halfW, -thickness, -halfD }, new[] { -halfW, -thickness, -halfD }, 0, -1, 0);
            return mesh.ToData();
        }

        static float[][] Ring(IReadOnlyList<Vec2> footprint, float scale, float y, float ox, float oz)
        {
            var ccw = Polygon2D.CounterClockwise(footprint);
            var ring = new float[ccw.Count][];
            for (var i = 0; i < ccw.Count; i++) ring[i] = new[] { ccw[i].X * scale + ox, y, ccw[i].Y * scale + oz };
            return ring;
        }

        static void AddCap(MeshAccumulator mesh, int sub, IReadOnlyList<Vec2> footprint, float scale, float y, float ox, float oz, float normalY)
        {
            var tris = PolygonTriangulator.Triangulate(Polygon2D.Scaled(footprint, scale, new Vec2(ox, oz)), out var pts);
            AddTriangles(mesh, sub, pts, tris, y, normalY);
        }

        static void AddTriangles(MeshAccumulator mesh, int sub, List<Vec2> pts, List<int> tris, float y, float normalY)
        {
            for (var t = 0; t < tris.Count; t += 3)
            {
                var v = new int[3];
                for (var k = 0; k < 3; k++)
                {
                    var p = pts[tris[t + k]];
                    v[k] = mesh.Vertex(p.X, y, p.Y, 0f, normalY, 0f, p.X * UvScale, p.Y * UvScale);
                }
                mesh.Triangle(sub, v[0], v[1], v[2]);
            }
        }

        /// <summary>
        /// Side walls between two rings of the same polygon (counter-clockwise seen from above). Normals point away from the
        /// polygon, or into it for the walls of a pocket. Adjacent walls share a smooth normal only when the corner is shallow.
        /// </summary>
        static void AddWalls(MeshAccumulator mesh, int sub, float[][] bottom, float[][] top, bool inward)
        {
            var n = bottom.Length;
            var faceNormals = new double[n][];
            for (var i = 0; i < n; i++)
            {
                var j = (i + 1) % n;
                var edge = Sub(bottom[j], bottom[i]);
                var slope = Sub(top[i], bottom[i]);
                var normal = Normalize(Cross(edge, slope));
                // Outward for a counter-clockwise polygon seen from above is (dz, -dx).
                var outward = new[] { edge[2], 0.0, -edge[0] };
                if (Dot(normal, outward) < 0) normal = new[] { -normal[0], -normal[1], -normal[2] };
                if (inward) normal = new[] { -normal[0], -normal[1], -normal[2] };
                faceNormals[i] = normal;
            }

            var length = 0f;
            for (var i = 0; i < n; i++)
            {
                var j = (i + 1) % n;
                var prevNormal = faceNormals[(i + n - 1) % n];
                var thisNormal = faceNormals[i];
                var nextNormal = faceNormals[(i + 1) % n];
                var nStart = Dot(prevNormal, thisNormal) >= SmoothCosine ? Normalize(Add(prevNormal, thisNormal)) : thisNormal;
                var nEnd = Dot(thisNormal, nextNormal) >= SmoothCosine ? Normalize(Add(thisNormal, nextNormal)) : thisNormal;

                var edgeLength = (float)Math.Sqrt(Dot(Sub(bottom[j], bottom[i]), Sub(bottom[j], bottom[i])));
                var u0 = length * UvScale;
                var u1 = (length + edgeLength) * UvScale;
                length += edgeLength;

                var b0 = mesh.Vertex(bottom[i][0], bottom[i][1], bottom[i][2], nStart, u0, bottom[i][1] * UvScale);
                var b1 = mesh.Vertex(bottom[j][0], bottom[j][1], bottom[j][2], nEnd, u1, bottom[j][1] * UvScale);
                var t1 = mesh.Vertex(top[j][0], top[j][1], top[j][2], nEnd, u1, top[j][1] * UvScale);
                var t0 = mesh.Vertex(top[i][0], top[i][1], top[i][2], nStart, u0, top[i][1] * UvScale);
                mesh.Triangle(sub, b0, b1, t1);
                mesh.Triangle(sub, b0, t1, t0);
            }
        }

        static void AddQuad(MeshAccumulator mesh, int sub, float[] a, float[] b, float[] c, float[] d, float nx, float ny, float nz)
        {
            var normal = new double[] { nx, ny, nz };
            var v0 = mesh.Vertex(a[0], a[1], a[2], normal, a[0] * UvScale + a[2] * UvScale, a[1] * UvScale);
            var v1 = mesh.Vertex(b[0], b[1], b[2], normal, b[0] * UvScale + b[2] * UvScale, b[1] * UvScale);
            var v2 = mesh.Vertex(c[0], c[1], c[2], normal, c[0] * UvScale + c[2] * UvScale, c[1] * UvScale);
            var v3 = mesh.Vertex(d[0], d[1], d[2], normal, d[0] * UvScale + d[2] * UvScale, d[1] * UvScale);
            mesh.Triangle(sub, v0, v1, v2);
            mesh.Triangle(sub, v0, v2, v3);
        }

        static double[] Sub(float[] a, float[] b) => new double[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        static double[] Add(double[] a, double[] b) => new[] { a[0] + b[0], a[1] + b[1], a[2] + b[2] };
        static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        static double[] Cross(double[] a, double[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };

        static double[] Normalize(double[] v)
        {
            var length = Math.Sqrt(Dot(v, v));
            return length < 1e-12 ? new[] { 0.0, 1.0, 0.0 } : new[] { v[0] / length, v[1] / length, v[2] / length };
        }

        /// <summary>Collects vertices and triangles; each triangle is flipped if needed so it faces the same way as its vertex normals.</summary>
        sealed class MeshAccumulator
        {
            readonly List<float> vertices = new List<float>();
            readonly List<float> normals = new List<float>();
            readonly List<float> uvs = new List<float>();
            readonly List<int>[] submeshes;

            public MeshAccumulator(int submeshCount)
            {
                submeshes = new List<int>[submeshCount];
                for (var i = 0; i < submeshCount; i++) submeshes[i] = new List<int>();
            }

            public int Vertex(float x, float y, float z, float nx, float ny, float nz, float u, float v) =>
                Vertex(x, y, z, new double[] { nx, ny, nz }, u, v);

            public int Vertex(float x, float y, float z, double[] normal, float u, float v)
            {
                vertices.Add(x);
                vertices.Add(y);
                vertices.Add(z);
                normals.Add((float)normal[0]);
                normals.Add((float)normal[1]);
                normals.Add((float)normal[2]);
                uvs.Add(u);
                uvs.Add(v);
                return vertices.Count / 3 - 1;
            }

            public void Triangle(int sub, int a, int b, int c)
            {
                var pa = Position(a);
                var pb = Position(b);
                var pc = Position(c);
                var geometric = Cross(Sub(pb, pa), Sub(pc, pa));
                var wanted = new double[3];
                for (var k = 0; k < 3; k++) wanted[k] = normals[a * 3 + k] + normals[b * 3 + k] + normals[c * 3 + k];
                var list = submeshes[sub];
                if (Dot(geometric, wanted) >= 0)
                {
                    list.Add(a);
                    list.Add(b);
                    list.Add(c);
                }
                else
                {
                    list.Add(a);
                    list.Add(c);
                    list.Add(b);
                }
            }

            float[] Position(int index) => new[] { vertices[index * 3], vertices[index * 3 + 1], vertices[index * 3 + 2] };

            static double[] Sub(float[] a, float[] b) => new double[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };

            public PolygonMeshData ToData()
            {
                var data = new PolygonMeshData
                {
                    Vertices = vertices.ToArray(),
                    Normals = normals.ToArray(),
                    Uvs = uvs.ToArray(),
                    Submeshes = new int[submeshes.Length][],
                };
                for (var i = 0; i < submeshes.Length; i++) data.Submeshes[i] = submeshes[i].ToArray();
                return data;
            }
        }
    }
}
