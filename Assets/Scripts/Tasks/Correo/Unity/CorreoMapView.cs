using System;
using System.Collections;
using System.Collections.Generic;
using PlayCT.Tasks.Gabinete;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PlayCT.Tasks.Correo
{
    /// <summary>
    /// The physical postal network on the table: a wooden board with five turned-wood settlements A to E, flat wooden strips
    /// for the directed paths (an arrowhead at the destination end and one slot per unit of capacity), and three parcel boxes,
    /// P1 taller and tied with a band because it is the high-priority one. Two wooden buttons, Enviar and Limpiar, sit at the
    /// front edge. It is built at runtime, shows only what the task tells it, and holds no research logic.
    /// </summary>
    public class CorreoMapView : MonoBehaviour, ICorreoView
    {
        [SerializeField] CorreoTask task;
        [Tooltip("Board under the network.")]
        [SerializeField] Material boardMaterial;
        [Tooltip("Settlements and buttons.")]
        [SerializeField] Material woodMaterial;
        [Tooltip("Path strips.")]
        [SerializeField] Material pathMaterial;
        [Tooltip("Slots that show a path's capacity, arrowheads and lettering.")]
        [SerializeField] Material inkMaterial;
        [Tooltip("Low-priority parcels.")]
        [SerializeField] Material parcelMaterial;
        [Tooltip("High-priority parcel.")]
        [SerializeField] Material priorityParcelMaterial;
        [Tooltip("Band tied around the high-priority parcel.")]
        [SerializeField] Material bandMaterial;
        [Tooltip("Ring under the settlement chosen as source.")]
        [SerializeField] Material sourceRingMaterial;
        [Tooltip("Ring under the settlement chosen as destination.")]
        [SerializeField] Material destinationRingMaterial;
        [Tooltip("Existing Origen plaque, placed next to A.")]
        [SerializeField] Material originPlaqueMaterial;
        [Tooltip("Existing Destino plaque, placed next to E.")]
        [SerializeField] Material destinationPlaqueMaterial;
        [SerializeField] float shipmentDuration = 0.8f;
        [SerializeField] float textScale = 1f;

        const float BoardWidth = 1.0f;
        const float BoardDepth = 0.56f;
        const float BoardZCenter = 0.02f;
        const float BoardHeight = 0.02f;
        const float SettlementRadius = 0.055f;
        const float SettlementHeight = 0.014f;
        const float PathWidth = 0.03f;
        const float PathHeight = 0.004f;
        const float PathGap = 0.075f;
        const float SelectedLift = 0.022f;
        const float ButtonZ = -0.225f;

        static readonly Dictionary<Settlement, Vector2> Positions = new Dictionary<Settlement, Vector2>
        {
            { Settlement.A, new Vector2(-0.36f, 0.00f) },
            { Settlement.B, new Vector2(-0.13f, 0.13f) },
            { Settlement.C, new Vector2(-0.13f, -0.13f) },
            { Settlement.D, new Vector2(0.11f, 0.09f) },
            { Settlement.E, new Vector2(0.36f, -0.04f) },
        };

        static readonly Dictionary<CorreoPackage, Vector2> Slots = new Dictionary<CorreoPackage, Vector2>
        {
            { CorreoPackage.P1, new Vector2(-0.022f, 0.016f) },
            { CorreoPackage.P2, new Vector2(0.022f, 0.016f) },
            { CorreoPackage.P3, new Vector2(0f, -0.024f) },
        };

        readonly List<Mesh> meshes = new List<Mesh>();
        readonly Dictionary<CorreoPackage, Transform> parcels = new Dictionary<CorreoPackage, Transform>();
        readonly Dictionary<Settlement, Transform> settlements = new Dictionary<Settlement, Transform>();
        readonly HashSet<CorreoPackage> selected = new HashSet<CorreoPackage>();
        readonly Dictionary<CorreoPackage, Settlement> shown = new Dictionary<CorreoPackage, Settlement>();
        Transform root;
        Transform sourceRing;
        Transform destinationRing;
        Coroutine routine;
        Action pendingDone;
        CorreoState pendingState;
        Font font;
        bool built;

        public bool IsAnimating => routine != null;

        void Awake()
        {
            Build();
            root.gameObject.SetActive(false);
        }

        void OnDisable() => FinishAnimation(false);

        void OnDestroy()
        {
            foreach (var mesh in meshes)
                if (mesh != null) Destroy(mesh);
        }

        public void SetVisible(bool visible)
        {
            Build();
            if (!visible) FinishAnimation(false);
            root.gameObject.SetActive(visible);
        }

        public void Show(CorreoTrialConfig config, CorreoState state)
        {
            Build();
            FinishAnimation(false);
            selected.Clear();
            ApplyState(state);
            ShowSelection(null, null, new CorreoPackage[0]);
        }

        public void ShowSelection(Settlement? source, Settlement? destination, IReadOnlyList<CorreoPackage> packages)
        {
            Build();
            selected.Clear();
            foreach (var package in packages) selected.Add(package);

            PlaceRing(sourceRing, source);
            PlaceRing(destinationRing, destination);
            foreach (var package in CorreoPackages.All) Position(package, shown[package]);
        }

        public void AnimateShipment(CorreoShipment shipment, CorreoState result, Action done)
        {
            Build();
            FinishAnimation(false);
            pendingDone = done;
            pendingState = result;
            routine = StartCoroutine(Carry(shipment));
        }

        public void ShowNotShipped(CorreoShipment shipment)
        {
            Build();
            if (routine != null) return;
            routine = StartCoroutine(Nudge(shipment));
        }

        IEnumerator Carry(CorreoShipment shipment)
        {
            var starts = new Dictionary<CorreoPackage, Vector3>();
            var ends = new Dictionary<CorreoPackage, Vector3>();
            foreach (var package in shipment.Packages)
            {
                starts[package] = parcels[package].localPosition;
                ends[package] = RestingPosition(package, shipment.Destination, false);
            }

            var elapsed = 0f;
            while (elapsed < shipmentDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / shipmentDuration));
                foreach (var package in shipment.Packages)
                {
                    var p = Vector3.Lerp(starts[package], ends[package], t);
                    p.y += Mathf.Sin(t * Mathf.PI) * 0.06f;
                    parcels[package].localPosition = p;
                }
                yield return null;
            }
            routine = null;
            FinishAnimation(true);
        }

        IEnumerator Nudge(CorreoShipment shipment)
        {
            const float duration = 0.3f;
            var elapsed = 0f;
            var rests = new Dictionary<CorreoPackage, Vector3>();
            foreach (var package in shipment.Packages) rests[package] = parcels[package].localPosition;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var offset = Mathf.Sin(elapsed / duration * Mathf.PI * 4f) * 0.006f * (1f - elapsed / duration);
                foreach (var package in shipment.Packages) parcels[package].localPosition = rests[package] + Vector3.right * offset;
                yield return null;
            }
            foreach (var package in shipment.Packages) parcels[package].localPosition = rests[package];
            routine = null;
        }

        void FinishAnimation(bool notify)
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }
            if (pendingState != null)
            {
                ApplyState(pendingState);
                ShowSelection(null, null, new CorreoPackage[0]);
            }
            var done = pendingDone;
            pendingDone = null;
            pendingState = null;
            if (notify) done?.Invoke();
            else if (built) RestParcels();
        }

        void RestParcels()
        {
            foreach (var package in CorreoPackages.All)
                if (shown.ContainsKey(package)) Position(package, shown[package]);
        }

        void ApplyState(CorreoState state)
        {
            foreach (var package in CorreoPackages.All) shown[package] = state.Location(package);
            RestParcels();
        }

        void Position(CorreoPackage package, Settlement settlement) =>
            parcels[package].localPosition = RestingPosition(package, settlement, selected.Contains(package));

        Vector3 RestingPosition(CorreoPackage package, Settlement settlement, bool lifted)
        {
            var p = Positions[settlement] + Slots[package];
            return new Vector3(p.x, BoardHeight + SettlementHeight + (lifted ? SelectedLift : 0f), p.y);
        }

        void PlaceRing(Transform ring, Settlement? settlement)
        {
            ring.gameObject.SetActive(settlement.HasValue);
            if (!settlement.HasValue) return;
            var p = Positions[settlement.Value];
            ring.localPosition = new Vector3(p.x, BoardHeight + 0.0006f, p.y);
        }

        void Build()
        {
            if (built) return;
            built = true;

            root = new GameObject("PostalNetwork").transform;
            root.SetParent(transform, false);

            MakeSolid("Board", root, Polygon2D.Rectangle(BoardWidth, BoardDepth), BoardHeight, boardMaterial, new Vector3(0f, 0f, BoardZCenter));

            foreach (var settlement in Settlements.All) BuildSettlement(settlement);
            foreach (var path in CorreoNetwork.Standard.Paths) BuildPath(path);
            BuildPlaque("Origen", Settlement.A, originPlaqueMaterial);
            BuildPlaque("Destino", Settlement.E, destinationPlaqueMaterial);

            sourceRing = BuildRing("Ring_Source", sourceRingMaterial);
            destinationRing = BuildRing("Ring_Destination", destinationRingMaterial);

            foreach (var package in CorreoPackages.All) BuildParcel(package);
            BuildButton("Enviar", new Vector3(0.12f, BoardHeight, ButtonZ), CorreoHandleKind.Send);
            BuildButton("Limpiar", new Vector3(-0.12f, BoardHeight, ButtonZ), CorreoHandleKind.Clear);
        }

        void BuildSettlement(Settlement settlement)
        {
            var p = Positions[settlement];
            var go = MakeSolid("Settlement_" + settlement, root, Polygon2D.Regular(32, SettlementRadius), SettlementHeight, woodMaterial,
                new Vector3(p.x, BoardHeight, p.y));
            settlements[settlement] = go.transform;

            var collider = go.AddComponent<BoxCollider>();
            collider.size = new Vector3(SettlementRadius * 2f, 0.05f, SettlementRadius * 2f);
            collider.center = new Vector3(0f, 0.025f, 0f);
            collider.isTrigger = true;
            AddHandle(go, CorreoHandleKind.Settlement, settlement);

            MakeText(go.transform, settlement.ToString(), 0.045f, new Vector3(0f, 0.0005f, -SettlementRadius - 0.032f), FlatRotation, onBoard: true);
        }

        void BuildPath(CorreoPath path)
        {
            var from = Positions[path.From];
            var to = Positions[path.To];
            var direction = (to - from).normalized;
            var start = from + direction * PathGap;
            var end = to - direction * PathGap;
            var length = (end - start).magnitude;
            var middle = (start + end) * 0.5f;
            var yaw = Mathf.Atan2(-direction.y, direction.x) * Mathf.Rad2Deg;
            var rotation = Quaternion.Euler(0f, yaw, 0f);

            var strip = MakeSolid($"Path_{path.From}{path.To}", root, Polygon2D.Rectangle(length, PathWidth), PathHeight, pathMaterial,
                new Vector3(middle.x, BoardHeight, middle.y));
            strip.transform.localRotation = rotation;

            MakeSolid("Arrowhead", strip.transform,
                new List<Vec2> { new Vec2(0f, 0.021f), new Vec2(0.032f, 0f), new Vec2(0f, -0.021f) }, PathHeight + 0.0015f, inkMaterial,
                new Vector3(length * 0.5f - 0.03f, PathHeight, 0f), centroidOrigin: false);

            var slotSpacing = 0.034f;
            var slotsLength = slotSpacing * (path.Capacity - 1);
            for (var i = 0; i < path.Capacity; i++)
            {
                var x = -slotsLength * 0.5f - 0.02f + i * slotSpacing;
                MakeSolid("CapacitySlot_" + (i + 1), strip.transform, Polygon2D.Rectangle(0.024f, 0.016f), 0.0012f, inkMaterial,
                    new Vector3(x, PathHeight, 0f));
            }

            var labelOffset = new Vector2(-direction.y, direction.x) * 0.032f;
            MakeText(root, path.Capacity.ToString(), 0.035f,
                new Vector3(middle.x + labelOffset.x, BoardHeight + 0.0005f, middle.y + labelOffset.y), FlatRotation, onBoard: true);
        }

        void BuildPlaque(string name, Settlement settlement, Material material)
        {
            var p = Positions[settlement];
            var go = new GameObject("Plaque_" + name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(p.x, BoardHeight + 0.0015f, p.y + SettlementRadius + 0.045f);
            go.transform.localScale = new Vector3(0.14f, 1f, 0.035f);
            go.AddComponent<MeshFilter>().sharedMesh = FlatQuad();
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        Transform BuildRing(string name, Material material)
        {
            var mesh = Extrude(Polygon2D.Regular(40, SettlementRadius + 0.012f), 0.0008f);
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.SetActive(false);
            return go.transform;
        }

        void BuildParcel(CorreoPackage package)
        {
            var high = CorreoPackages.IsHighPriority(package);
            var size = high ? 0.040f : 0.032f;
            var height = high ? 0.036f : 0.026f;
            var go = MakeSolid("Parcel_" + package, root, Polygon2D.Rectangle(size, size), height, high ? priorityParcelMaterial : parcelMaterial, Vector3.zero);
            parcels[package] = go.transform;

            if (high)
            {
                MakeSolid("Band", go.transform, Polygon2D.Rectangle(size + 0.002f, 0.008f), height + 0.001f, bandMaterial, new Vector3(0f, 0f, 0f));
            }
            MakeText(go.transform, CorreoPackages.Id(package), 0.016f, new Vector3(0f, height + 0.0006f, high ? -0.0125f : -0.001f), FlatRotation);

            var collider = go.AddComponent<BoxCollider>();
            collider.size = new Vector3(size + 0.012f, height + 0.012f, size + 0.012f);
            collider.center = new Vector3(0f, height * 0.5f, 0f);
            collider.isTrigger = true;
            AddHandle(go, CorreoHandleKind.Package, Settlement.A, package);
        }

        void BuildButton(string label, Vector3 position, CorreoHandleKind kind)
        {
            var go = MakeSolid("Button_" + label, root, Polygon2D.Rectangle(0.12f, 0.05f), 0.014f, woodMaterial, position);
            var collider = go.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.12f, 0.04f, 0.05f);
            collider.center = new Vector3(0f, 0.02f, 0f);
            collider.isTrigger = true;
            AddHandle(go, kind, Settlement.A, CorreoPackage.P1);
            MakeText(go.transform, label, 0.026f, new Vector3(0f, 0.0148f, 0f), FlatRotation);
        }

        void AddHandle(GameObject go, CorreoHandleKind kind, Settlement settlement = Settlement.A, CorreoPackage package = CorreoPackage.P1)
        {
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            go.AddComponent<XRSimpleInteractable>();
            var handle = go.AddComponent<CorreoHandle>();
            handle.Configure(task, kind, settlement, package);
        }

        static readonly Quaternion FlatRotation = Quaternion.Euler(90f, 0f, 0f);

        GameObject MakeSolid(string name, Transform parent, IReadOnlyList<Vec2> footprint, float height, Material material, Vector3 localPosition, bool centroidOrigin = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.AddComponent<MeshFilter>().sharedMesh = Extrude(footprint, height, centroidOrigin);
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        Mesh Extrude(IReadOnlyList<Vec2> footprint, float height, bool centroidOrigin = true)
        {
            var spec = new GabineteShapeSpec(GabineteShapeType.Prism, "correo", footprint, height, 1f, 1f, 360, 0);
            var data = GabineteMeshBuilder.BuildPiece(spec);
            var count = data.VertexCount;
            var vertices = new Vector3[count];
            var normals = new Vector3[count];
            var uvs = new Vector2[count];
            var shift = Vector2.zero;
            if (!centroidOrigin)
            {
                var c = Polygon2D.Centroid(Polygon2D.CounterClockwise(footprint));
                shift = new Vector2(c.X, c.Y);
            }
            for (var i = 0; i < count; i++)
            {
                vertices[i] = new Vector3(data.Vertices[i * 3] + shift.x, data.Vertices[i * 3 + 1], data.Vertices[i * 3 + 2] + shift.y);
                normals[i] = new Vector3(data.Normals[i * 3], data.Normals[i * 3 + 1], data.Normals[i * 3 + 2]);
                uvs[i] = new Vector2(data.Uvs[i * 2], data.Uvs[i * 2 + 1]);
            }
            var mesh = new Mesh { name = "Correo_Solid", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.SetTriangles(data.Submeshes[0], 0);
            mesh.RecalculateBounds();
            meshes.Add(mesh);
            return mesh;
        }

        Mesh FlatQuad()
        {
            var mesh = new Mesh { name = "Correo_Plaque", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            meshes.Add(mesh);
            return mesh;
        }

        TextMesh MakeText(Transform parent, string text, float height, Vector3 localPosition, Quaternion rotation, bool onBoard = false)
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject("Text_" + text);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = rotation;
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = font;
            mesh.fontSize = 64;
            mesh.characterSize = height / 6.4f * textScale;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            var source = onBoard ? pathMaterial : inkMaterial;
            mesh.color = source != null ? source.color : Color.black;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            return mesh;
        }
    }
}
