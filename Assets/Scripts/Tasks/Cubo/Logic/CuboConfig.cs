using System;
using System.Collections.Generic;

namespace PlayCT.Tasks.Cubo
{
    public enum CuboMiniTask
    {
        CruzClara,
        CorregirUnaPieza,
        ElegirUnaSecuencia,
        QuePermanece,
    }

    public static class CuboMiniTasks
    {
        public static string Id(CuboMiniTask task)
        {
            switch (task)
            {
                case CuboMiniTask.CruzClara: return "cruz_clara";
                case CuboMiniTask.CorregirUnaPieza: return "corregir_una_pieza";
                case CuboMiniTask.ElegirUnaSecuencia: return "elegir_una_secuencia";
                default: return "que_permanece";
            }
        }

        public static bool TryParse(string text, out CuboMiniTask task)
        {
            task = CuboMiniTask.CruzClara;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var key = text.Replace("_", "").Replace("-", "").Replace(" ", "").ToLowerInvariant();
            foreach (CuboMiniTask candidate in Enum.GetValues(typeof(CuboMiniTask)))
            {
                if (Id(candidate).Replace("_", "") == key || candidate.ToString().ToLowerInvariant() == key)
                {
                    task = candidate;
                    return true;
                }
            }
            return false;
        }
    }

    public enum CuboGoalKind
    {
        /// <summary>The centre and the four edge stickers of one face show a single colour.</summary>
        CrossOnFace,
        /// <summary>All 54 stickers match a target state.</summary>
        ExactState,
        /// <summary>After at least one rotation, the participant touches a face whose nine stickers did not change.</summary>
        UnchangedFace,
    }

    public sealed class CuboGoal
    {
        public CuboGoalKind Kind { get; private set; }
        public CubeFace Face { get; private set; }
        public CubeState Target { get; private set; }

        public static CuboGoal Cross(CubeFace face) => new CuboGoal { Kind = CuboGoalKind.CrossOnFace, Face = face };
        public static CuboGoal Exact(CubeState target) => new CuboGoal { Kind = CuboGoalKind.ExactState, Target = target ?? throw new ArgumentNullException(nameof(target)) };
        public static CuboGoal Unchanged() => new CuboGoal { Kind = CuboGoalKind.UnchangedFace };

        public bool IsSatisfiedBy(CubeState state)
        {
            switch (Kind)
            {
                case CuboGoalKind.CrossOnFace: return state.IsCrossOn(Face);
                case CuboGoalKind.ExactState: return state.Equals(Target);
                default: return false;
            }
        }
    }

    /// <summary>Everything that defines one mini-task trial. The interaction layer reads none of these values directly.</summary>
    public sealed class CuboTrialConfig
    {
        public CuboMiniTask MiniTask { get; set; }
        public CubeState InitialState { get; set; } = CubeState.Solved;
        public CuboGoal Goal { get; set; }

        /// <summary>The rotations the participant may perform. Empty means all twelve quarter turns.</summary>
        public IReadOnlyList<CubeMove> AllowedMoves { get; set; } = new CubeMove[0];

        /// <summary>Maximum trial duration in seconds; 0 means no limit. The participant is never shown a timer.</summary>
        public double DurationSeconds { get; set; }

        /// <summary>Length of a known solution (0 when not defined). It is reported, not enforced.</summary>
        public int ReferenceMoves { get; set; }

        /// <summary>For <see cref="CuboGoalKind.UnchangedFace"/>: valid rotations needed before an answer counts.</summary>
        public int MinRotations { get; set; } = 1;

        public bool IsAllowed(CubeMove move)
        {
            if (AllowedMoves == null || AllowedMoves.Count == 0) return true;
            foreach (var allowed in AllowedMoves)
                if (allowed.Equals(move)) return true;
            return false;
        }

        public void Validate()
        {
            if (Goal == null) throw new InvalidOperationException("A trial needs a goal.");
            if (InitialState == null) throw new InvalidOperationException("A trial needs an initial state.");
            if (DurationSeconds < 0) throw new InvalidOperationException("Duration cannot be negative.");
            if (MinRotations < 1) throw new InvalidOperationException("MinRotations must be at least 1.");
            if (Goal.Kind != CuboGoalKind.UnchangedFace && Goal.IsSatisfiedBy(InitialState))
                throw new InvalidOperationException($"The initial state of '{CuboMiniTasks.Id(MiniTask)}' already satisfies its goal.");
        }
    }

    /// <summary>
    /// Text form of a trial that can be edited in the inspector or in a config file. Move lists use the notation
    /// "R U' F" applied to the solved cube; a 54-letter state overrides them.
    /// </summary>
    [Serializable]
    public class CuboTrialSpec
    {
        public string miniTask = "cruz_clara";
        public string initialMoves = "";
        public string initialState = "";
        public string goalFace = "U";
        public string goalState = "";
        public string allowedMoves = "";
        public float durationSeconds;
        public int referenceMoves;
        public int minRotations = 1;

        public CuboTrialConfig ToConfig()
        {
            if (!CuboMiniTasks.TryParse(miniTask, out var task))
                throw new FormatException($"Unknown mini-task '{miniTask}'.");

            var initial = string.IsNullOrWhiteSpace(initialState)
                ? CubeState.FromMoves(CubeNotation.Parse(initialMoves))
                : CubeState.FromSnapshot(initialState.Trim());

            CuboGoal goal;
            switch (task)
            {
                case CuboMiniTask.CruzClara:
                    if (string.IsNullOrWhiteSpace(goalFace) || !CubeFaces.TryFromLetter(goalFace.Trim()[0], out var face))
                        throw new FormatException($"Unknown goal face '{goalFace}'.");
                    goal = CuboGoal.Cross(face);
                    break;
                case CuboMiniTask.QuePermanece:
                    goal = CuboGoal.Unchanged();
                    break;
                default:
                    goal = CuboGoal.Exact(string.IsNullOrWhiteSpace(goalState) ? CubeState.Solved : CubeState.FromSnapshot(goalState.Trim()));
                    break;
            }

            var config = new CuboTrialConfig
            {
                MiniTask = task,
                InitialState = initial,
                Goal = goal,
                AllowedMoves = CubeNotation.Parse(allowedMoves),
                DurationSeconds = durationSeconds,
                ReferenceMoves = referenceMoves,
                MinRotations = minRotations < 1 ? 1 : minRotations,
            };
            config.Validate();
            return config;
        }
    }

    public static class CuboProtocol
    {
        /// <summary>
        /// The four mini-tasks in order. Every start state is a short, fixed move sequence from the solved cube, so the
        /// protocol is identical for every participant. No task asks for a full solve.
        /// </summary>
        public static IReadOnlyList<CuboTrialSpec> DefaultSpecs() => new[]
        {
            // A clear cross on the top face has been broken by two turns.
            new CuboTrialSpec { miniTask = "cruz_clara", initialMoves = "F R'", goalFace = "U", durationSeconds = 240, referenceMoves = 2 },

            // One layer has been turned away from an otherwise complete cube; only three faces may be turned.
            new CuboTrialSpec { miniTask = "corregir_una_pieza", initialMoves = "F'", allowedMoves = "U U' R R' F F'", durationSeconds = 240, referenceMoves = 1 },

            // Three turns have been applied; the participant chooses which turns undo them from six allowed ones.
            new CuboTrialSpec { miniTask = "elegir_una_secuencia", initialMoves = "R U F", allowedMoves = "U U' R R' F F'", durationSeconds = 300, referenceMoves = 3 },

            // Only the right layer can be turned; the participant then touches the face that did not change.
            new CuboTrialSpec { miniTask = "que_permanece", initialMoves = "F R' U", allowedMoves = "R R'", durationSeconds = 180, minRotations = 1 },
        };

        public static List<CuboTrialConfig> Default()
        {
            var configs = new List<CuboTrialConfig>();
            foreach (var spec in DefaultSpecs()) configs.Add(spec.ToConfig());
            return configs;
        }
    }
}
