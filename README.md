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
- `TaskOrchestrator` runs the task IDs in its `taskSequence` list (in the Laboratory scene `Hanoi`, `CuboRelaciones`, then `GabineteFormas`) one after another and logs `experiment_started`, `task_started`, `task_ended`, `task_skipped` and `experiment_ended` (task name `Experiment`) to the same `events.jsonl`. Sequence logic lives in the pure class `ExperimentSequencer`. In the Laboratory scene the orchestrator starts Hanoi, so `HanoiTask.beginOnStart` is off there.

On Quest the folder is `/sdcard/Android/data/<package>/files/PlayCT/`.

## Tower of Hanoi (`Assets/Scripts/Tasks/Hanoi`)

The `Hanoi_Task` object in `Laboratory.unity` holds a wooden board with pegs `Origen`, `Apoyo`, `Destino` and five oak disks. Disks are picked up with XR Interaction Toolkit direct interactors on both controllers. The participant sees no counter, timer, score, solution or error text.

- Number of disks: 3, 4 or 5 (`HanoiTask.diskCount`, `SessionManager.hanoiDiskCount`, or `HanoiTask.Configure(n, condition)`). Optimal moves (7 / 15 / 31) are only used in the logged metrics.
- A release is accepted only over a peg; a larger disk on a smaller one, or a release away from the pegs, sends the disk back to its slot (animated).
- Events per trial: `trial_started`, `disk_grab`, `disk_release` (disk_id, source_peg, destination_peg, legal, outcome, move_number, resulting_state, completion_status), `trial_completed`, `trial_summary` (completion time, total moves, invalid attempts, efficiency = optimal / moves, move sequence, attempt sequence). A summary is also written as `hanoi_trial_NN_summary.json` next to the event log.
- Completion is detected when every disk is on `Destino`; the task then locks and raises `HanoiTask.TrialCompleted`.

## Cubo de Relaciones (`Assets/Scripts/Tasks/Cubo`)

The `Cubo_Task` object in `Laboratory.unity` builds, when it runs, a 38 cm cube of 27 dark-oak cubies with six matte sticker colours (Up linen, Down ochre, Front sage, Back slate, Left clay, Right plum) in the middle of the table. It implements `IExperimentTask` (`TaskId` `CuboRelaciones`); the orchestrator starts it after Hanoi, and while it runs it switches off `Hanoi_Task` so the table is clear (restored when the task ends).

- Interaction: one hand grips the cube by its body or edges to stabilize it (it never moves); the other hand grips the middle of a face and lets go. A short movement selects the face; a clear twist around the face centre asks for a quarter turn clockwise or counterclockwise as seen from outside. Only exact 90 degree turns of a face layer exist, so there is no free rotation. Turns that are not in the trial's allowed set, or attempted without the stabilizing hand, are refused: the cube does not move, the attempt is logged, and the participant continues. Nothing is displayed to the participant.
- Logic (`Logic/`, no Unity types): `CubeState` (54 stickers, immutable, deterministic quarter turns), `CubeMove`, `CuboTrialConfig` (mini-task, initial state, goal, allowed rotations, duration), `CuboTrial` (rules, events, metrics), `CuboSummary`. A trial is configured through `CuboTrialSpec` entries (`CuboTask.trials` in the inspector); an empty list uses `CuboProtocol.Default()`.
- Mini-tasks, none of which asks for a full solve: `cruz_clara` (form a single-colour cross on a face), `corregir_una_pieza` (undo one displaced layer using only three faces), `elegir_una_secuencia` (choose which allowed turns undo a three-turn change), `que_permanece` (after turning, touch the face whose nine stickers did not change). Each trial has an optional time limit that only ends the trial; no timer is shown.
- Events (task `CuboRelaciones`): `trial_started`, `stabilizer_changed`, `face_selected`, `rotation` (selected_face, rotation_axis, rotation_direction, rotation_amount_deg, legal, outcome, move_number, previous_state, resulting_state), `face_answer`, `trial_completed`, `trial_summary`. A summary per trial is also written as `cubo_trial_NN_summary.json`. The condition is recorded in every event and summary and does not change the rules.

## Gabinete de Formas (`Assets/Scripts/Tasks/Gabinete`)

The `Gabinete_Task` object in `Laboratory.unity` builds, when it runs, a wooden fitting cabinet on the table: a dark-oak board with one recessed opening per shape (lined in slate) and a linen tray in front holding the pieces, which are oak. It implements `IExperimentTask` (`TaskId` `GabineteFormas`); the orchestrator starts it after Cubo, and while it runs it switches off `Hanoi_Task` (restored when the task ends). The task manages only its own trials; it never starts or ends the session.

- Shapes: prism, cylinder, truncated cone, wedge, L-shaped block and hexagonal piece, plus a half cylinder and a T-shaped block that exist only so the 8-shape configuration has eight different pieces. Each opening is the exact footprint of its piece with 4% clearance. Prism and L-block start turned 90 degrees from the orientation their opening accepts (the hexagon 30 degrees); the cylinder and truncated cone (whose openings differ in size and taper) fit at any yaw, the prism after a half turn, the hexagon every 60 degrees.
- Interaction: each piece is an `XRGrabInteractable` that tracks position and rotation. When released, its pose in board space goes to the logic. Over an opening it is a placement attempt: it is accepted only if the opening belongs to that piece, the piece is upright, within `positionTolerance` of the opening centre and within `orientationTolerance` of an accepted yaw (folded by the shape's symmetry). Nothing snaps the piece to an opening beforehand; an accepted piece is only lowered into the opening it already matches. A rejected piece (and one released away from the openings) returns to its tray slot in its starting orientation. Nothing is displayed to the participant: no messages, hints or highlights. Pieces can be placed in any order and there is no move-order rule.
- Logic (`Logic/`, no Unity types): `GabineteShapes` (catalog and symmetry), `GabineteTrialConfig`/`GabineteTrialSpec`/`GabineteProtocol` (configurations), `GabineteTrial` (state, rules, events, metrics), `GabineteSummary`, and pure mesh geometry (`PolygonTriangulator`, `GabineteMeshBuilder`). `GabineteTask.trials` in the inspector takes `GabineteTrialSpec` entries (shape count 4, 6 or 8, or an explicit shape list, tray slots, start yaws, duration); an empty list uses `GabineteProtocol.Default()` (4, 6 and 8 shapes). Tolerances are in `GabineteTask.rules` and are placeholders until calibrated in headset.
- Events (task `GabineteFormas`): `trial_started`, `piece_picked_up`, `piece_released` (off-target releases), `placement_attempt` (piece_id, piece_type, target_opening, attempted_opening, attempted_opening_type, starting_orientation_deg, attempted_orientation_deg, orientation_error_deg, position_offset_m, placement_result, valid, completed_pieces, resulting_state), `trial_completed`, `trial_summary`. A summary per trial is also written as `gabinete_trial_NN_summary.json` (placement attempts, valid and invalid attempts by reason, off-target releases, rotation count, completion time, placement and action sequences). The condition is recorded in every event and summary and does not change the rules.

## Tests

- Unity Test Runner: `Assets/Tests/EditMode` (pure logic, log format, mesh geometry) and `Assets/Tests/PlayMode` (Laboratory scene driven through the task).
- Without the Editor: `dotnet test Tools~/OffEngineTests` (.NET 8 SDK) runs the EditMode tests plus the real `HanoiTask`/`HanoiDisk`/`CuboTask`/`GabineteTask`/`TaskOrchestrator`/`EventLogger` scripts on a small fake engine (209 tests). It complements, and does not replace, Play Mode.

## Opening the project on a new machine

1. Install Unity Hub and Unity **6000.0.84f1** (the repository root is the Unity project folder) with the modules for your platform. For Quest builds add **Android Build Support** including OpenJDK and Android SDK & NDK Tools.
2. Clone the repository and add the folder in Unity Hub. On first open Unity resolves `Packages/manifest.json`, creates `Library/` and `Packages/packages-lock.json` (commit the lock file after the first successful open), and may ask to import TextMeshPro essentials; accept.
3. Open `Assets/Scenes/Laboratory.unity` and press Play. Without a headset the XR Origin does not move; use the XR Device Simulator or connect the Quest (Meta Horizon Link / Air Link) and enable OpenXR for the Standalone platform (Project Settings > XR Plug-in Management > PC tab > OpenXR + Oculus Touch Controller Profile).
4. Quest build: switch platform to Android; OpenXR with Meta Quest Support and the Touch controller profiles is already configured for Android in `Assets/XR`.

Status: this project was authored without a Unity Editor. It has not yet been opened, compiled by Unity, or run in Play Mode. The first open on a real machine is the first real validation.
