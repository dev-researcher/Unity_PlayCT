// A very small stand-in for the parts of UnityEngine and XR Interaction Toolkit that the PlayCT scripts use.
// It lets the MonoBehaviours (HanoiTask, HanoiDisk, HanoiPeg, EventLogger, SessionManager) run outside the Unity Editor,
// so their orchestration logic can be executed and asserted in a normal `dotnet test`. It is NOT a replacement for Play Mode.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float f) => new Vector3(a.x * f, a.y * f, a.z * f);
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { t = Mathf.Clamp01(t); return a + (b - a) * t; }
        public override string ToString() => $"({x:0.###}, {y:0.###}, {z:0.###})";
    }

    public static class Mathf
    {
        public static float Clamp(float v, float min, float max) => Math.Max(min, Math.Min(max, v));
        public static int Clamp(int v, int min, int max) => Math.Max(min, Math.Min(max, v));
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float SmoothStep(float from, float to, float t) { t = Clamp01(t); t = -2f * t * t * t + 3f * t * t; return to * t + from * (1f - t); }
    }

    public static class Time
    {
        public static float deltaTime = 1f / 72f;
        public static double realtimeSinceStartupAsDouble = 0;
    }

    public static class Debug
    {
        public static readonly List<string> Messages = new List<string>();
        public static void Log(object m) => Messages.Add("L:" + m);
        public static void LogWarning(object m) => Messages.Add("W:" + m);
        public static void LogError(object m) => Messages.Add("E:" + m);
    }

    public enum RuntimePlatform { LinuxPlayer }
    public static class Application
    {
        public static string persistentDataPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "playct_offengine");
        public static string version = "0.0-test";
        public static string unityVersion = "fake";
        public static RuntimePlatform platform = RuntimePlatform.LinuxPlayer;
    }
    public static class SystemInfo { public static string deviceModel = "fake-device"; }

    public static class JsonUtility
    {
        public static T FromJson<T>(string json) => throw new NotSupportedException("JsonUtility is not available off-engine.");
    }

    public struct Color { public static Color cyan => default; }
    public static class Gizmos { public static Color color; public static void DrawLine(Vector3 a, Vector3 b) { } }

    public class SerializeFieldAttribute : Attribute { }
    public class HeaderAttribute : Attribute { public HeaderAttribute(string s) { } }
    public class TooltipAttribute : Attribute { public TooltipAttribute(string s) { } }
    public class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    public class DefaultExecutionOrderAttribute : Attribute { public DefaultExecutionOrderAttribute(int i) { } }
    public class DisallowMultipleComponentAttribute : Attribute { }
    public class RequireComponent : Attribute { public RequireComponent(params Type[] t) { } }
    public class ExecuteAlwaysAttribute : Attribute { }

    namespace Events
    {
        public class UnityEvent<T>
        {
            readonly List<Action<T>> listeners = new List<Action<T>>();
            public void AddListener(Action<T> a) => listeners.Add(a);
            public void RemoveListener(Action<T> a) => listeners.Remove(a);
            public void Invoke(T arg) { foreach (var l in listeners.ToArray()) l(arg); }
        }
    }

    public class Object
    {
        public string name;
        public static T FindFirstObjectByType<T>() where T : Object => Engine.All.OfType<T>().FirstOrDefault();
        public static void Destroy(Object o) { }
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !ReferenceEquals(a, b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => base.GetHashCode();
    }

    public class Transform : Component
    {
        public Vector3 position;
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : class => gameObject.Components.OfType<T>().FirstOrDefault();
        public bool TryGetComponent<T>(out T c) where T : class { c = GetComponent<T>(); return c != null; }
    }

    public class Behaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeSelf;
    }

    public class Coroutine { public IEnumerator Routine; public bool Done; }

    public class MonoBehaviour : Behaviour
    {
        public Coroutine StartCoroutine(IEnumerator r)
        {
            var c = new Coroutine { Routine = r };
            Engine.Coroutines.Add(c);
            if (!r.MoveNext()) { c.Done = true; Engine.Coroutines.Remove(c); }
            return c;
        }
        public void StopCoroutine(Coroutine c) { if (c != null) { c.Done = true; Engine.Coroutines.Remove(c); } }
    }

    public class Rigidbody : Component { public bool isKinematic; public bool useGravity = true; }

    public class GameObject : Object
    {
        public readonly List<Component> Components = new List<Component>();
        public Transform transform { get; }
        public bool activeSelf { get; private set; } = true;

        public GameObject(string name)
        {
            this.name = name;
            transform = new Transform { gameObject = this };
            Engine.Register(this);
        }

        public T AddComponent<T>() where T : Component, new() => AddComponent<T>(null);

        /// <summary>Like AddComponent, but lets the caller fill serialized fields first, as Unity does before Awake.</summary>
        public T AddComponent<T>(Action<T> serialized) where T : Component, new()
        {
            var c = new T { gameObject = this, name = typeof(T).Name };
            Components.Add(c);
            Engine.Register(c);
            serialized?.Invoke(c);
            if (c is Rigidbody) return c;
            Engine.Call(c, "Awake");
            if (activeSelf) Engine.Call(c, "OnEnable");
            return c;
        }

        public void SetActive(bool active)
        {
            if (active == activeSelf) return;
            activeSelf = active;
            foreach (var c in Components.ToArray())
                Engine.Call(c, active ? "OnEnable" : "OnDisable");
        }
    }

    public static class Engine
    {
        public static readonly List<Object> All = new List<Object>();
        public static readonly List<Coroutine> Coroutines = new List<Coroutine>();

        public static void Register(Object o) => All.Add(o);
        public static void Reset() { All.Clear(); Coroutines.Clear(); Debug.Messages.Clear(); Time.realtimeSinceStartupAsDouble = 0; }

        public static void Call(object target, string method)
        {
            var m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            m?.Invoke(target, null);
        }

        public static void Set(object target, string field, object value)
        {
            var t = target.GetType();
            FieldInfo f = null;
            while (t != null && f == null) { f = t.GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); t = t.BaseType; }
            if (f == null) throw new MissingFieldException(target.GetType().Name, field);
            f.SetValue(target, value);
        }

        public static void Tick(int frames = 1)
        {
            for (var i = 0; i < frames; i++)
            {
                Time.realtimeSinceStartupAsDouble += Time.deltaTime;
                foreach (var c in Coroutines.ToArray())
                {
                    if (c.Done) continue;
                    if (!c.Routine.MoveNext()) { c.Done = true; Coroutines.Remove(c); }
                }
            }
        }

        public static void TickSeconds(double seconds) => Tick((int)Math.Ceiling(seconds / Time.deltaTime));
    }

    namespace SceneManagement
    {
        public struct Scene { public string name => "OffEngine"; }
        public static class SceneManager { public static Scene GetActiveScene() => new Scene(); }
    }

    namespace XR.Interaction.Toolkit
    {
        public class BaseInteractionEventArgs { }
        public class SelectEnterEventArgs : BaseInteractionEventArgs { }
        public class SelectExitEventArgs : BaseInteractionEventArgs { }
        public sealed class SelectEnterEvent : Events.UnityEvent<SelectEnterEventArgs> { }
        public sealed class SelectExitEvent : Events.UnityEvent<SelectExitEventArgs> { }
        public class XRInteractionManager : MonoBehaviour
        {
            public virtual void CancelInteractableSelection(Interactables.IXRSelectInteractable interactable)
            {
                if (interactable is Interactables.XRGrabInteractable g) g.ForceExit();
            }
        }
    }
    namespace XR.Interaction.Toolkit.Filtering
    {
        public interface IXRFilterList<T> { int count { get; } void Add(T item); bool Remove(T item); }
        public interface IXRSelectFilter { bool canProcess { get; } bool Process(Interactors.IXRSelectInteractor interactor, Interactables.IXRSelectInteractable interactable); }
        public class FilterList : IXRFilterList<IXRSelectFilter>
        {
            public readonly List<IXRSelectFilter> Items = new List<IXRSelectFilter>();
            public int count => Items.Count;
            public void Add(IXRSelectFilter item) => Items.Add(item);
            public bool Remove(IXRSelectFilter item) => Items.Remove(item);
        }
    }
    namespace XR.Interaction.Toolkit.Interactors { public interface IXRSelectInteractor { } public class FakeHand : IXRSelectInteractor { } }
    namespace XR.Interaction.Toolkit.Interactables
    {
        public interface IXRSelectInteractable { }
        public abstract class XRBaseInteractable : MonoBehaviour, IXRSelectInteractable
        {
            protected readonly Filtering.FilterList filters = new Filtering.FilterList();
            public XRInteractionManager interactionManager { get; set; }
            public SelectEnterEvent firstSelectEntered { get; } = new SelectEnterEvent();
            public SelectExitEvent lastSelectExited { get; } = new SelectExitEvent();
            public Filtering.IXRFilterList<Filtering.IXRSelectFilter> selectFilters => filters;
        }

        /// <summary>Emulates what an XR hand does to a grab interactable: filters decide, then events fire.</summary>
        public class XRGrabInteractable : XRBaseInteractable
        {
            public bool IsSelected { get; private set; }

            public bool TryGrab(Interactors.IXRSelectInteractor hand)
            {
                if (!isActiveAndEnabled || IsSelected) return false;
                foreach (var f in filters.Items)
                    if (f.canProcess && !f.Process(hand, this)) return false;
                IsSelected = true;
                firstSelectEntered.Invoke(new SelectEnterEventArgs());
                return true;
            }

            public void ReleaseAt(Vector3 position)
            {
                if (!IsSelected) return;
                transform.position = position;
                ForceExit();
            }

            public void ForceExit()
            {
                if (!IsSelected) return;
                IsSelected = false;
                lastSelectExited.Invoke(new SelectExitEventArgs());
            }
        }
    }
}
