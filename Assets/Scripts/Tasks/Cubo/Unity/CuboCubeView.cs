using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PlayCT.Tasks.Cubo
{
    /// <summary>
    /// The physical-looking cube: 27 wooden cubies with matte coloured stickers, built at runtime from the fixed
    /// <see cref="CubeLayout"/>. It only displays states given by the task and animates exact quarter turns; it holds no
    /// research logic. The 6 face handles and the body handle carry the XR interactables.
    /// </summary>
    public class CuboCubeView : MonoBehaviour, ICuboView
    {
        [SerializeField] CuboTask task;
        [SerializeField] float cubeSize = 0.38f;
        [SerializeField] float gap = 0.003f;
        [SerializeField] float stickerFill = 0.86f;
        [SerializeField] float stickerThickness = 0.003f;
        [SerializeField] float turnDuration = 0.3f;
        [SerializeField] float faceHandleSize = 0.24f;
        [SerializeField] float faceHandleDepth = 0.05f;
        [Tooltip("Wood used for the cubies.")]
        [SerializeField] Material woodMaterial;
        [Tooltip("Sticker materials in the order Up, Down, Front, Back, Left, Right.")]
        [SerializeField] Material[] faceMaterials = new Material[6];

        readonly Transform[] cubies = new Transform[27];
        readonly Vector3[] cubieHome = new Vector3[27];
        readonly Renderer[] stickers = new Renderer[CubeLayout.StickerCount];
        Transform cubeRoot;
        Coroutine turnRoutine;
        Transform pivot;
        Action pendingDone;
        CubeState pendingState;
        bool built;

        public bool IsAnimating => turnRoutine != null;
        public Transform CubeRoot
        {
            get
            {
                Build();
                return cubeRoot;
            }
        }

        void Awake()
        {
            Build();
            cubeRoot.gameObject.SetActive(false);
        }

        void OnDisable() => FinishTurn();

        public void SetVisible(bool visible)
        {
            Build();
            if (!visible) FinishTurn();
            cubeRoot.gameObject.SetActive(visible);
        }

        public void Show(CubeState state)
        {
            Build();
            FinishTurn();
            ApplyColors(state);
        }

        public void AnimateTurn(CubeMove move, CubeState result, Action done)
        {
            Build();
            FinishTurn();
            pendingDone = done;
            pendingState = result;

            pivot = new GameObject("Turn_Pivot").transform;
            pivot.SetParent(cubeRoot, false);
            for (var i = 0; i < cubies.Length; i++)
            {
                if (OnLayer(i, move.Face)) cubies[i].SetParent(pivot, true);
            }

            var (nx, ny, nz) = CubeFaces.Normal(move.Face);
            var axis = new Vector3(nx, ny, nz);
            // Positive rotation about the outward normal is clockwise seen from outside the face.
            var angle = move.Turn == CubeTurn.Clockwise ? CubeMove.AmountDegrees : -CubeMove.AmountDegrees;
            turnRoutine = StartCoroutine(Turn(axis, angle));
        }

        IEnumerator Turn(Vector3 axis, float angle)
        {
            var elapsed = 0f;
            while (elapsed < turnDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / turnDuration));
                pivot.localRotation = Quaternion.AngleAxis(angle * t, axis);
                yield return null;
            }
            turnRoutine = null;
            CompleteTurn();
        }

        void FinishTurn()
        {
            if (turnRoutine != null)
            {
                StopCoroutine(turnRoutine);
                turnRoutine = null;
            }
            if (pivot != null) CompleteTurn(false);
        }

        void CompleteTurn(bool notify = true)
        {
            for (var i = 0; i < cubies.Length; i++) cubies[i].SetParent(cubeRoot, false);
            for (var i = 0; i < cubies.Length; i++)
            {
                cubies[i].localPosition = cubieHome[i];
                cubies[i].localRotation = Quaternion.identity;
            }
            if (pivot != null) Destroy(pivot.gameObject);
            pivot = null;

            if (pendingState != null) ApplyColors(pendingState);
            var done = pendingDone;
            pendingDone = null;
            pendingState = null;
            if (notify) done?.Invoke();
        }

        void ApplyColors(CubeState state)
        {
            for (var i = 0; i < stickers.Length; i++)
                stickers[i].sharedMaterial = faceMaterials[state.ColorAt(i)];
        }

        bool OnLayer(int cubieIndex, CubeFace face)
        {
            var p = cubieHome[cubieIndex];
            var unit = cubeSize / 3f;
            var value = Mathf.RoundToInt((CubeFaces.Axis(face) == 'x' ? p.x : CubeFaces.Axis(face) == 'y' ? p.y : p.z) / unit);
            return value == CubeFaces.Sign(face);
        }

        void Build()
        {
            if (built) return;
            built = true;

            var unit = cubeSize / 3f;
            var cubieSize = unit - gap;
            var stickerSize = cubieSize * stickerFill;

            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var cubeMesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            Destroy(primitive);

            cubeRoot = new GameObject("Cubo_Cube").transform;
            cubeRoot.SetParent(transform, false);
            cubeRoot.localPosition = new Vector3(0f, cubeSize * 0.5f, 0f);

            var cubieIndex = new Dictionary<(int, int, int), int>();
            var next = 0;
            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    for (var z = -1; z <= 1; z++)
                    {
                        var go = new GameObject($"Cubie_{x}_{y}_{z}");
                        go.transform.SetParent(cubeRoot, false);
                        go.transform.localPosition = new Vector3(x, y, z) * unit;
                        go.transform.localScale = Vector3.one * cubieSize;
                        go.AddComponent<MeshFilter>().sharedMesh = cubeMesh;
                        go.AddComponent<MeshRenderer>().sharedMaterial = woodMaterial;
                        cubies[next] = go.transform;
                        cubieHome[next] = go.transform.localPosition;
                        cubieIndex[(x, y, z)] = next;
                        next++;
                    }
                }
            }

            foreach (var slot in CubeLayout.Slots)
            {
                var parent = cubies[cubieIndex[(slot.X, slot.Y, slot.Z)]];
                var (nx, ny, nz) = slot.Normal;
                var go = new GameObject($"Sticker_{CubeFaces.Letter(slot.Face)}{slot.Row}{slot.Col}");
                go.transform.SetParent(parent, false);
                // Local to the cubie, whose scale is cubieSize, so sizes are fractions of one cubie.
                go.transform.localPosition = new Vector3(nx, ny, nz) * (0.5f + stickerThickness * 0.5f / cubieSize - 0.0005f / cubieSize);
                go.transform.localScale = new Vector3(nx != 0 ? stickerThickness / cubieSize : stickerFill,
                    ny != 0 ? stickerThickness / cubieSize : stickerFill,
                    nz != 0 ? stickerThickness / cubieSize : stickerFill);
                go.AddComponent<MeshFilter>().sharedMesh = cubeMesh;
                stickers[slot.Index] = go.AddComponent<MeshRenderer>();
            }
            ApplyColors(CubeState.Solved);

            BuildHandles();
        }

        void BuildHandles()
        {
            var body = new GameObject("Handle_Body");
            body.transform.SetParent(cubeRoot, false);
            var bodyCollider = body.AddComponent<BoxCollider>();
            bodyCollider.size = Vector3.one * cubeSize;
            bodyCollider.isTrigger = true;
            AddKinematicBody(body);
            body.AddComponent<XRSimpleInteractable>();
            body.AddComponent<CuboBodyHandle>().Configure(task);

            foreach (var face in CubeFaces.All)
            {
                var (nx, ny, nz) = CubeFaces.Normal(face);
                var normal = new Vector3(nx, ny, nz);
                var handle = new GameObject("Handle_" + face);
                handle.transform.SetParent(cubeRoot, false);
                handle.transform.localPosition = normal * (cubeSize * 0.5f + faceHandleDepth * 0.5f);
                var collider = handle.AddComponent<BoxCollider>();
                collider.size = new Vector3(nx != 0 ? faceHandleDepth : faceHandleSize,
                    ny != 0 ? faceHandleDepth : faceHandleSize,
                    nz != 0 ? faceHandleDepth : faceHandleSize);
                collider.isTrigger = true;
                AddKinematicBody(handle);
                handle.AddComponent<XRSimpleInteractable>();
                handle.AddComponent<CuboFaceHandle>().Configure(face, task, cubeRoot);
            }
        }

        static void AddKinematicBody(GameObject go)
        {
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }
    }
}
