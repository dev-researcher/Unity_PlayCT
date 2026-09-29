using UnityEngine;

namespace PlayCT.Tasks.Hanoi
{
    /// <summary>
    /// Generates the ring mesh (disk with a centre hole and chamfered edges) for a <see cref="HanoiDisk"/>
    /// and assigns it to the MeshFilter and the convex MeshCollider.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-200)]
    [RequireComponent(typeof(HanoiDisk), typeof(MeshFilter))]
    public class HanoiDiskMesh : MonoBehaviour
    {
        [SerializeField, Range(16, 96)] int segments = 40;
        [SerializeField] float chamfer = 0.003f;

        Mesh mesh;

        void OnEnable() => Rebuild();

        void OnDisable()
        {
            var filter = GetComponent<MeshFilter>();
            if (filter != null) filter.sharedMesh = null;
            var meshCollider = GetComponent<MeshCollider>();
            if (meshCollider != null) meshCollider.sharedMesh = null;
            DestroyMesh();
        }

        public void Rebuild()
        {
            var disk = GetComponent<HanoiDisk>();
            var data = RingMeshBuilder.Build(disk.OuterRadius, disk.HoleRadius, disk.Thickness, chamfer, segments);

            DestroyMesh();
            mesh = new Mesh { name = $"HanoiDisk_{disk.Size}", hideFlags = HideFlags.HideAndDontSave };
            var vertices = new Vector3[data.VertexCount];
            var normals = new Vector3[data.VertexCount];
            var uvs = new Vector2[data.VertexCount];
            for (var i = 0; i < vertices.Length; i++)
            {
                vertices[i] = new Vector3(data.Vertices[i * 3], data.Vertices[i * 3 + 1], data.Vertices[i * 3 + 2]);
                normals[i] = new Vector3(data.Normals[i * 3], data.Normals[i * 3 + 1], data.Normals[i * 3 + 2]);
                uvs[i] = new Vector2(data.Uvs[i * 2], data.Uvs[i * 2 + 1]);
            }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = data.Triangles;
            mesh.RecalculateBounds();

            GetComponent<MeshFilter>().sharedMesh = mesh;
            var meshCollider = GetComponent<MeshCollider>();
            if (meshCollider != null)
            {
                meshCollider.convex = true;
                meshCollider.sharedMesh = mesh;
            }
        }

        void DestroyMesh()
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
            mesh = null;
        }
    }
}
