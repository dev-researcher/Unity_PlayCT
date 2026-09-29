using System;
using System.Collections.Generic;
using PlayCT.Research;
using UnityEngine;

namespace PlayCT.Tasks.Hanoi
{
    /// <summary>
    /// Runs the Tower of Hanoi task in the scene. It connects the physical pegs and disks to the pure
    /// <see cref="HanoiTrial"/> logic, animates disks into place or back to where they came from, and logs through the
    /// shared <see cref="EventLogger"/>. The participant sees no counters, timers, scores or messages.
    /// </summary>
    public class HanoiTask : MonoBehaviour, IExperimentTask
    {
        [Header("Trial configuration")]
        [SerializeField, Range(HanoiState.MinDisks, HanoiState.MaxDisks)] int diskCount = 3;
        [Tooltip("When the SessionManager specifies a disk count (3-5) it overrides the value above.")]
        [SerializeField] bool useSessionDiskCount = true;
        [SerializeField] bool beginOnStart = true;

        [Header("Scene objects")]
        [SerializeField] HanoiPeg[] pegs = new HanoiPeg[0];
        [SerializeField] HanoiDisk[] disks = new HanoiDisk[0];
        [SerializeField] SessionManager session;
        [SerializeField] EventLogger eventLogger;

        [Header("Placement feel")]
        [Tooltip("Horizontal radius around a peg axis inside which a released disk is accepted as 'over the peg'.")]
        [SerializeField] float captureRadius = 0.09f;
        [SerializeField] float captureHeightAbove = 0.16f;
        [SerializeField] float captureHeightBelow = 0.05f;
        [SerializeField] float placeDuration = 0.20f;
        [SerializeField] float returnDuration = 0.45f;
        [SerializeField] float clearance = 0.03f;

        readonly IClock clock = new UnityClock();
        HanoiTrial trial;
        int trialCounter;
        HanoiSummary lastSummary;
        bool initialized;

        public HanoiTrial Trial => trial;
        public int DiskCount => trial != null ? trial.DiskCount : diskCount;
        public bool IsCompleted => trial != null && trial.IsCompleted;
        public bool IsRunning => trial != null && trial.IsRunning;
        public HanoiSummary LastSummary => lastSummary;
        public EventLogger EventLogger => eventLogger;
        public SessionManager Session => session;
        public IReadOnlyList<HanoiDisk> Disks => disks;
        public IReadOnlyList<HanoiPeg> Pegs => pegs;

        public event Action<HanoiTrial> TrialStarted;
        public event Action<HanoiSummary> TrialCompleted;

        public string TaskId => HanoiTrial.TaskName;
        public event Action<IExperimentTask> Completed;

        /// <summary>Common task entry point: begins a trial. The condition does not change how Hanoi behaves.</summary>
        public void StartTask(ExperimentCondition condition) => BeginTrial();

        /// <summary>Common task exit point: ends a trial that is still running and releases any held disk.</summary>
        public void EndTask(string reason)
        {
            if (trial == null) return;
            trial.End(reason);
            foreach (var disk in disks)
            {
                if (disk.IsHeld) disk.CancelSelection();
            }
            RefreshAccessibility();
        }

        void Awake()
        {
            Initialize();
        }

        void Start()
        {
            if (beginOnStart && trial == null) BeginTrial();
        }

        void OnDestroy()
        {
            foreach (var disk in disks)
            {
                if (disk == null) continue;
                disk.Grabbed -= HandleGrab;
                disk.Released -= HandleRelease;
                disk.GrabPolicy = null;
            }
        }

        void OnApplicationQuit()
        {
            trial?.End("application_quit");
        }

        void Initialize()
        {
            if (initialized) return;
            initialized = true;

            if (session == null) session = FindFirstObjectByType<SessionManager>();
            if (session == null) session = new GameObject("SessionManager").AddComponent<SessionManager>();
            if (eventLogger == null) eventLogger = FindFirstObjectByType<EventLogger>();
            if (eventLogger == null) eventLogger = new GameObject("EventLogger").AddComponent<EventLogger>();
            eventLogger.Session = session;

            foreach (var disk in disks)
            {
                disk.Grabbed += HandleGrab;
                disk.Released += HandleRelease;
                disk.GrabPolicy = CanGrab;
            }
        }

        /// <summary>Sets how many disks (3-5) the next trial uses and, optionally, the experimental condition label.</summary>
        public void Configure(int newDiskCount, string condition = null)
        {
            diskCount = Mathf.Clamp(newDiskCount, HanoiState.MinDisks, HanoiState.MaxDisks);
            useSessionDiskCount = false;
            if (!string.IsNullOrEmpty(condition)) session.SetCondition(condition);
        }

        /// <summary>Starts a new trial (ending the current one if it is still running) and resets the physical disks.</summary>
        public void BeginTrial()
        {
            Initialize();
            trial?.End("restarted");

            foreach (var disk in disks)
            {
                if (disk.IsHeld) disk.CancelSelection();
            }

            var count = diskCount;
            if (useSessionDiskCount && session.HanoiDiskCount >= HanoiState.MinDisks) count = session.HanoiDiskCount;
            count = Mathf.Clamp(count, HanoiState.MinDisks, HanoiState.MaxDisks);

            trial = new HanoiTrial(count, eventLogger, clock, ++trialCounter);
            lastSummary = null;
            LayoutDisks();
            trial.Begin();
            RefreshAccessibility();
            TrialStarted?.Invoke(trial);
        }

        public void ResetTrial() => BeginTrial();

        bool CanGrab(HanoiDisk disk)
        {
            return trial != null && trial.IsRunning && !trial.IsCompleted && trial.HeldDisk == 0;
        }

        void LayoutDisks()
        {
            var origin = FindPeg(HanoiPegs.Origen);
            foreach (var disk in disks)
            {
                var active = disk.Size <= trial.DiskCount;
                disk.gameObject.SetActive(active);
                if (!active) continue;
                var slot = trial.State.SlotIndexOf(disk.Size);
                disk.PlaceImmediately(origin.SlotPosition(slot, disk.Thickness));
            }
        }

        void RefreshAccessibility()
        {
            foreach (var disk in disks)
            {
                if (!disk.isActiveAndEnabled) continue;
                disk.Accessible = trial != null && trial.IsRunning && !trial.IsCompleted && trial.State.IsTop(disk.Size);
            }
        }

        void HandleGrab(HanoiDisk disk)
        {
            if (trial == null || !trial.OnGrab(disk.Size))
            {
                disk.CancelSelection();
                ReturnToCurrentSlot(disk, returnDuration);
            }
        }

        void HandleRelease(HanoiDisk disk)
        {
            if (trial == null) return;

            var pegName = FindPegAt(disk.transform.position);
            var outcome = trial.OnRelease(disk.Size, pegName);

            switch (outcome.Result)
            {
                case ReleaseResult.MovedToNewPeg:
                case ReleaseResult.ReturnedToSamePeg:
                    ReturnToCurrentSlot(disk, placeDuration);
                    break;
                default:
                    ReturnToCurrentSlot(disk, returnDuration);
                    break;
            }

            RefreshAccessibility();

            if (outcome.Completed) HandleCompleted();
        }

        void HandleCompleted()
        {
            lastSummary = trial.BuildSummary();
            eventLogger.WriteJsonFile($"hanoi_trial_{trial.TrialIndex:D2}_summary.json", lastSummary.ToJson(session));
            TrialCompleted?.Invoke(lastSummary);
            Completed?.Invoke(this);
        }

        void ReturnToCurrentSlot(HanoiDisk disk, float duration)
        {
            var peg = FindPeg(trial.State.PegOf(disk.Size));
            var slot = trial.State.SlotIndexOf(disk.Size);
            var target = peg.SlotPosition(slot, disk.Thickness);
            var start = disk.transform.position;

            var clearY = peg.TopPoint.y + disk.Thickness + clearance;
            var travelY = Mathf.Clamp(Mathf.Max(start.y, clearY), clearY, clearY + 0.15f);
            var path = new List<Vector3>
            {
                start,
                new Vector3(start.x, travelY, start.z),
                new Vector3(target.x, travelY, target.z),
                target,
            };
            disk.MoveAlong(path, duration, RefreshAccessibility);
        }

        HanoiPeg FindPeg(string pegName)
        {
            foreach (var peg in pegs)
            {
                if (peg != null && peg.PegName == pegName) return peg;
            }
            throw new InvalidOperationException($"HanoiTask has no peg named '{pegName}'.");
        }

        public HanoiDisk FindDisk(int size)
        {
            foreach (var disk in disks)
            {
                if (disk != null && disk.Size == size) return disk;
            }
            throw new InvalidOperationException($"HanoiTask has no disk of size {size}.");
        }

        /// <summary>Returns the name of the peg a disk released at this world position would be threaded on, or null.</summary>
        public string FindPegAt(Vector3 worldPosition)
        {
            HanoiPeg best = null;
            var bestDistance = float.MaxValue;
            foreach (var peg in pegs)
            {
                if (peg == null) continue;
                var distance = peg.HorizontalDistance(worldPosition);
                var height = worldPosition.y - peg.BasePoint.y;
                if (distance > captureRadius || height < -captureHeightBelow || height > peg.Height + captureHeightAbove) continue;
                if (distance < bestDistance)
                {
                    best = peg;
                    bestDistance = distance;
                }
            }
            return best != null ? best.PegName : null;
        }

        /// <summary>World position at which a disk is held "over" a peg, ready to be dropped on it.</summary>
        public Vector3 HoverPointAbove(string pegName, float extraHeight = 0.05f)
        {
            return FindPeg(pegName).TopPoint + Vector3.up * extraHeight;
        }

        /// <summary>
        /// Scripted interaction that runs through exactly the same path as the XR grab/release events.
        /// Used by automated tests and by researchers to replay a solution.
        /// </summary>
        public bool SimulateGrab(int diskSize)
        {
            var disk = FindDisk(diskSize);
            if (!disk.isActiveAndEnabled || disk.IsAnimating || !disk.Accessible || !CanGrab(disk)) return false;
            disk.RaiseGrabbed();
            return true;
        }

        public void SimulateRelease(int diskSize, Vector3 worldPosition)
        {
            var disk = FindDisk(diskSize);
            disk.PlaceImmediately(worldPosition);
            disk.RaiseReleased();
        }

        public bool SimulateMove(int diskSize, string pegName)
        {
            if (!SimulateGrab(diskSize)) return false;
            SimulateRelease(diskSize, HoverPointAbove(pegName));
            return true;
        }
    }
}
