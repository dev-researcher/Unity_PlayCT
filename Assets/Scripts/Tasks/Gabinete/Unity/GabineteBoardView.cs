using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PlayCT.Tasks.Gabinete
{
    /// <summary>
    /// The wooden fitting cabinet: a board with a pocket for each opening, a tray in front of it and the pieces, all built
    /// at runtime from the shapes and layout of the trial. It only shows pieces and moves them when told; it holds no
    /// research logic. Every piece carries its own XR grab interactable.
    /// </summary>
    public class GabineteBoardView : MonoBehaviour, IGabineteView
    {
        [SerializeField] GabineteTask task;
        [Tooltip("Wood of the pieces.")]
        [SerializeField] Material pieceMaterial;
        [Tooltip("Wood of the cabinet board.")]
        [SerializeField] Material boardMaterial;
        [Tooltip("Lining of the openings, so they read as recesses.")]
        [SerializeField] Material pocketMaterial;
        [Tooltip("Tray the pieces wait on.")]
        [SerializeField] Material trayMaterial;
        [SerializeField] float seatDuration = 0.25f;
        [SerializeField] float returnDuration = 0.3f;

        readonly Dictionary<GabineteShapeType, Mesh> pieceMeshes = new Dictionary<GabineteShapeType, Mesh>();
        readonly Dictionary<string, GabinetePieceView> pieces = new Dictionary<string, GabinetePieceView>();
        readonly Dictionary<string, GabinetePieceSpec> specs = new Dictionary<string, GabinetePieceSpec>();
        Transform root;
        Transform boardTransform;
        Transform piecesRoot;
        Mesh boardMesh;
        Mesh trayMesh;
        int shapeCount;
        bool built;

        public Transform BoardFrame
        {
            get
            {
                Build();
                return boardTransform;
            }
        }

        void Awake()
        {
            Build();
            root.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            foreach (var mesh in pieceMeshes.Values)
                if (mesh != null) Destroy(mesh);
            if (boardMesh != null) Destroy(boardMesh);
            if (trayMesh != null) Destroy(trayMesh);
        }

        void Build()
        {
            if (built) return;
            built = true;
            root = new GameObject("Cabinet").transform;
            root.SetParent(transform, false);
            boardTransform = new GameObject("Board").transform;
            boardTransform.SetParent(root, false);
            boardTransform.localPosition = new Vector3(0f, GabineteLayout.BoardThickness, 0f);
            piecesRoot = new GameObject("Pieces").transform;
            piecesRoot.SetParent(root, false);
        }

        public void SetVisible(bool visible)
        {
            Build();
            root.gameObject.SetActive(visible);
        }

        public void Show(GabineteTrialConfig config)
        {
            Build();
            ClearPieces();
            shapeCount = config.ShapeCount;

            var openings = new List<BoardOpening>();
            foreach (var piece in config.Pieces)
                openings.Add(new BoardOpening(piece.Shape, GabineteLayout.OpeningCenter(piece.OpeningIndex, shapeCount)));

            RebuildBoard(openings);
            RebuildTray();
            foreach (var spec in config.Pieces) CreatePiece(spec);
        }

        void RebuildBoard(List<BoardOpening> openings)
        {
            var data = GabineteMeshBuilder.BuildBoard(openings, GabineteLayout.BoardWidth(shapeCount), GabineteLayout.BoardDepth,
                GabineteLayout.BoardThickness, GabineteLayout.PocketDepth);
            if (boardMesh != null) Destroy(boardMesh);
            boardMesh = ToMesh(data, "Gabinete_Board");

            var go = boardTransform.gameObject;
            var filter = go.GetComponent<MeshFilter>() ?? go.AddComponent<MeshFilter>();
            var renderer = go.GetComponent<MeshRenderer>() ?? go.AddComponent<MeshRenderer>();
            filter.sharedMesh = boardMesh;
            renderer.sharedMaterials = new[] { boardMaterial, pocketMaterial };
        }

        void RebuildTray()
        {
            var name = "Tray";
            var existing = root.Find(name);
            if (existing != null) Destroy(existing.gameObject);
            if (trayMesh != null) Destroy(trayMesh);

            var footprint = new GabineteShapeSpec(GabineteShapeType.Prism, "tray", Polygon2D.Rectangle(GabineteLayout.BoardWidth(shapeCount), GabineteLayout.TrayDepth),
                GabineteLayout.TrayThickness, 1f, 1f, 180, 0);
            trayMesh = ToMesh(GabineteMeshBuilder.BuildPiece(footprint), "Gabinete_Tray");
            var tray = new GameObject(name);
            tray.transform.SetParent(root, false);
            tray.transform.localPosition = new Vector3(0f, 0f, GabineteLayout.TrayZ);
            tray.AddComponent<MeshFilter>().sharedMesh = trayMesh;
            tray.AddComponent<MeshRenderer>().sharedMaterial = trayMaterial;
        }

        void CreatePiece(GabinetePieceSpec spec)
        {
            var shape = spec.ShapeSpec;
            var home = TrayHome(spec);

            var go = new GameObject("Piece_" + spec.Id);
            go.SetActive(false);
            go.transform.SetParent(piecesRoot, false);
            go.transform.localPosition = home;
            go.transform.localRotation = Quaternion.Euler(0f, (float)spec.StartYawDegrees, 0f);

            var mesh = PieceMesh(spec.Shape);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = pieceMaterial;
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = true;
            collider.isTrigger = true;

            var attach = new GameObject("Attach").transform;
            attach.SetParent(go.transform, false);
            attach.localPosition = new Vector3(0f, shape.Height * 0.5f, 0f);

            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var grab = go.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.trackPosition = true;
            grab.trackRotation = true;
            grab.throwOnDetach = false;
            grab.useDynamicAttach = false;
            grab.matchAttachPosition = true;
            grab.matchAttachRotation = false;
            grab.attachTransform = attach;

            var view = go.AddComponent<GabinetePieceView>();
            view.Configure(task, spec.Id, boardTransform, home, (float)spec.StartYawDegrees);
            go.SetActive(true);

            pieces[spec.Id] = view;
            specs[spec.Id] = spec;
        }

        Vector3 TrayHome(GabinetePieceSpec spec)
        {
            var c = GabineteLayout.TrayCenter(spec.TraySlot, shapeCount);
            return new Vector3(c.X, GabineteLayout.TrayThickness, c.Y);
        }

        public void SeatPiece(string pieceId, int openingIndex, Action done)
        {
            if (!pieces.TryGetValue(pieceId, out var piece))
            {
                done?.Invoke();
                return;
            }
            var pose = piece.CurrentPose();
            var shape = specs[pieceId].ShapeSpec;
            var period = shape.SymmetryDegrees;
            var yaw = period <= 0 ? 0.0 : Math.Round(pose.YawDegrees / period) * period;

            var centre = GabineteLayout.OpeningCenter(openingIndex, shapeCount);
            var world = boardTransform.TransformPoint(new Vector3(centre.X, -GabineteLayout.PocketDepth, centre.Y));
            piece.Seat(world, (float)yaw, seatDuration, done);
        }

        public void ReturnPiece(string pieceId)
        {
            if (pieces.TryGetValue(pieceId, out var piece)) piece.ReturnHome(returnDuration);
        }

        public void LockPieces()
        {
            foreach (var piece in pieces.Values)
            {
                piece.Accessible = false;
                piece.CancelSelection();
            }
        }

        void ClearPieces()
        {
            foreach (var piece in pieces.Values)
            {
                if (piece == null) continue;
                piece.gameObject.SetActive(false);
                Destroy(piece.gameObject);
            }
            pieces.Clear();
            specs.Clear();
        }

        Mesh PieceMesh(GabineteShapeType type)
        {
            if (pieceMeshes.TryGetValue(type, out var cached) && cached != null) return cached;
            var mesh = ToMesh(GabineteMeshBuilder.BuildPiece(GabineteShapes.Get(type)), "Gabinete_" + GabineteShapes.Id(type));
            pieceMeshes[type] = mesh;
            return mesh;
        }

        static Mesh ToMesh(PolygonMeshData data, string name)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            var count = data.VertexCount;
            var vertices = new Vector3[count];
            var normals = new Vector3[count];
            var uvs = new Vector2[count];
            for (var i = 0; i < count; i++)
            {
                vertices[i] = new Vector3(data.Vertices[i * 3], data.Vertices[i * 3 + 1], data.Vertices[i * 3 + 2]);
                normals[i] = new Vector3(data.Normals[i * 3], data.Normals[i * 3 + 1], data.Normals[i * 3 + 2]);
                uvs[i] = new Vector2(data.Uvs[i * 2], data.Uvs[i * 2 + 1]);
            }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.subMeshCount = data.Submeshes.Length;
            for (var s = 0; s < data.Submeshes.Length; s++) mesh.SetTriangles(data.Submeshes[s], s);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
