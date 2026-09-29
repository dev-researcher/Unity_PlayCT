# Unity_PlayCT

Unity project for Meta Quest 3 VR research (stationary interaction, no locomotion).

- Unity: 6000.0.84f1 (Unity 6 LTS). Install the Android Build Support module (OpenJDK, Android SDK & NDK Tools).
- XR: OpenXR (Meta Quest Support feature, Android build target) + XR Interaction Toolkit 3.4.1.
- Render pipeline: Built-in, Vulkan, IL2CPP, ARM64.
- Scene: `Assets/Scenes/Laboratory.unity` (4 m x 4 m university laboratory: oak table, bookshelf, cabinet, window with daylight, XR Origin at room centre).

## Research infrastructure

`Assets/Scripts/Research` is shared by every task.

- `SessionManager` holds session ID, participant ID and condition. Set them in the inspector or in `session_config.json` inside `Application.persistentDataPath` (`{"participantId":"P012","condition":"PreAdapted","hanoiDiskCount":4}`).
- `EventLogger` appends one JSON object per line to `<persistentDataPath>/PlayCT/<sessionId>/events.jsonl`. Every line carries `session_id, participant_id, condition, task, event, timestamp_utc, t_session_s` followed by the task's own fields. Tasks implement no logging of their own; they send a `ResearchEvent` to the logger.
- `ConditionManager` gives a typed view (`Static` or `PreAdapted`) of the SessionManager's condition text; the legacy value `baseline` is read as `Static`. Any other text is invalid and the experiment will not start.
- `IExperimentTask` is what a task exposes to the experiment: `TaskId`, `StartTask(condition)`, `IsRunning`, `IsCompleted`, `Completed`, `EndTask(reason)`. `HanoiTask` implements it; a future task does the same and registers itself with `TaskOrchestrator` (inspector `tasks` list or `Register`).
- `TaskOrchestrator` runs the task IDs in its `taskSequence` list (currently only `Hanoi`) one after another and logs `experiment_started`, `task_started`, `task_ended`, `task_skipped` and `experiment_ended` (task name `Experiment`) to the same `events.jsonl`. Sequence logic lives in the pure class `ExperimentSequencer`. In the Laboratory scene the orchestrator starts Hanoi, so `HanoiTask.beginOnStart` is off there.

On Quest the folder is `/sdcard/Android/data/<package>/files/PlayCT/`.

## Tower of Hanoi (`Assets/Scripts/Tasks/Hanoi`)

The `Hanoi_Task` object in `Laboratory.unity` holds a wooden board with pegs `Origen`, `Apoyo`, `Destino` and five oak disks. Disks are picked up with XR Interaction Toolkit direct interactors on both controllers. The participant sees no counter, timer, score, solution or error text.

- Number of disks: 3, 4 or 5 (`HanoiTask.diskCount`, `SessionManager.hanoiDiskCount`, or `HanoiTask.Configure(n, condition)`). Optimal moves (7 / 15 / 31) are only used in the logged metrics.
- A release is accepted only over a peg; a larger disk on a smaller one, or a release away from the pegs, sends the disk back to its slot (animated).
- Events per trial: `trial_started`, `disk_grab`, `disk_release` (disk_id, source_peg, destination_peg, legal, outcome, move_number, resulting_state, completion_status), `trial_completed`, `trial_summary` (completion time, total moves, invalid attempts, efficiency = optimal / moves, move sequence, attempt sequence). A summary is also written as `hanoi_trial_NN_summary.json` next to the event log.
- Completion is detected when every disk is on `Destino`; the task then locks and raises `HanoiTask.TrialCompleted`.

## Tests

- Unity Test Runner: `Assets/Tests/EditMode` (pure logic, log format, mesh geometry) and `Assets/Tests/PlayMode` (Laboratory scene driven through the task).
- Without the Editor: `dotnet test Tools~/OffEngineTests` (.NET 8 SDK) runs the EditMode tests plus the real `HanoiTask`/`HanoiDisk`/`EventLogger` scripts on a small fake engine (87 tests). It complements, and does not replace, Play Mode.

## Opening the project on a new machine

1. Install Unity Hub and Unity **6000.0.84f1** (the repository root is the Unity project folder) with the modules for your platform. For Quest builds add **Android Build Support** including OpenJDK and Android SDK & NDK Tools.
2. Clone the repository and add the folder in Unity Hub. On first open Unity resolves `Packages/manifest.json`, creates `Library/` and `Packages/packages-lock.json` (commit the lock file after the first successful open), and may ask to import TextMeshPro essentials; accept.
3. Open `Assets/Scenes/Laboratory.unity` and press Play. Without a headset the XR Origin does not move; use the XR Device Simulator or connect the Quest (Meta Horizon Link / Air Link) and enable OpenXR for the Standalone platform (Project Settings > XR Plug-in Management > PC tab > OpenXR + Oculus Touch Controller Profile).
4. Quest build: switch platform to Android; OpenXR with Meta Quest Support and the Touch controller profiles is already configured for Android in `Assets/XR`.

Status: this project was authored without a Unity Editor. It has not yet been opened, compiled by Unity, or run in Play Mode. The first open on a real machine is the first real validation.
