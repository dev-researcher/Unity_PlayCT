using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PlayCT.App;
using PlayCT.Research;
using PlayCT.Tasks.Correo;
using PlayCT.Tasks.Cubo;
using PlayCT.Tasks.Gabinete;
using PlayCT.Tasks.Hanoi;

namespace PlayCT.Tests
{
    /// <summary>A scripted task: it starts, and the test decides when it completes.</summary>
    public sealed class ScriptedTask : IExperimentTask
    {
        public string TaskId { get; }
        public bool IsRunning { get; private set; }
        public bool IsCompleted { get; private set; }
        public int Starts;
        public readonly List<string> Ends = new List<string>();
        public ExperimentCondition? StartedWith;

        public event Action<IExperimentTask> Completed;

        public ScriptedTask(string id) { TaskId = id; }

        public void StartTask(ExperimentCondition condition)
        {
            Starts++;
            StartedWith = condition;
            IsRunning = true;
            IsCompleted = false;
        }

        public void EndTask(string reason)
        {
            Ends.Add(reason);
            IsRunning = false;
        }

        public void Finish()
        {
            IsCompleted = true;
            Completed?.Invoke(this);
        }
    }

    /// <summary>
    /// Stands in for the Unity controller: it implements the host and gate the same way (free play starts tasks directly, the
    /// experiment runs the real <see cref="ExperimentSequencer"/> over <see cref="GatedExperimentTask"/> wrappers) so the real flow,
    /// gate and sequencer can be exercised together.
    /// </summary>
    public sealed class FakeAppHost : IAppHost, IExperimentTaskGate
    {
        public readonly List<string> Calls = new List<string>();
        public readonly MemorySink Sink = new MemorySink();
        public readonly Dictionary<string, ScriptedTask> Tasks = new Dictionary<string, ScriptedTask>();
        public readonly Dictionary<string, GatedExperimentTask> Gated = new Dictionary<string, GatedExperimentTask>();
        public readonly FakeClock Clock = new FakeClock();
        public ApplicationFlow Flow { get; }
        public ExperimentSequencer Sequencer { get; private set; }
        public bool StartSucceeds = true;
        public bool Recording;

        public FakeAppHost()
        {
            foreach (var id in GameCatalog.Ids)
            {
                var task = new ScriptedTask(id);
                Tasks[id] = task;
                Gated[id] = new GatedExperimentTask(task, this);
            }
            Flow = new ApplicationFlow(this, Sink);
        }

        public void StartFreePlayGame(string taskId)
        {
            Calls.Add("free_start:" + taskId);
            var task = Tasks[taskId];
            task.Completed += OnFreePlayCompleted;
            task.StartTask(ExperimentCondition.Static);
        }

        void OnFreePlayCompleted(IExperimentTask task) => Flow.NotifyGameCompleted(task.TaskId);

        public void StopFreePlayGame(string taskId, string reason)
        {
            Calls.Add($"free_stop:{taskId}:{reason}");
            Tasks[taskId].Completed -= OnFreePlayCompleted;
            Tasks[taskId].EndTask(reason);
        }

        public string PrepareSession(string participantId)
        {
            Calls.Add("prepare:" + participantId);
            return "20260115_090000_ab12";
        }

        public bool StartExperiment()
        {
            Calls.Add("experiment_start");
            if (!StartSucceeds) return false;
            Recording = true;
            Sequencer = new ExperimentSequencer(GameCatalog.Ids, id => Gated[id], ExperimentCondition.Static, Sink, Clock);
            Sequencer.Finished += s => Flow.NotifyExperimentFinished(s.State == ExperimentState.Completed);
            Sequencer.Start();
            return true;
        }

        public void StartExperimentTask(string taskId) => Gated[taskId].BeginInner();

        public void AcknowledgeExperimentTask(string taskId) => Gated[taskId].Release();

        public void EndExperiment()
        {
            Calls.Add("experiment_end");
            Sequencer?.Abort("application_reset");
            Recording = false;
        }

        public void TaskOffered(string taskId)
        {
            Calls.Add("offered:" + taskId);
            Flow.OfferExperimentTask(taskId, Sequencer.CurrentIndex, Sequencer.Sequence.Count);
        }

        public void InnerStarting(string taskId) => Calls.Add("inner_starting:" + taskId);

        public void InnerCompleted(string taskId)
        {
            Calls.Add("inner_completed:" + taskId);
            Flow.NotifyExperimentTaskCompleted(taskId);
        }

        public void TaskClosed(string taskId) => Calls.Add("closed:" + taskId);

        /// <summary>Presses the button with this label on the current screen, as the participant would.</summary>
        public void Press(string label)
        {
            var spec = ScreenCatalog.Build(Flow);
            Assert.IsNotNull(spec, $"There is no menu in state {Flow.State}.");
            var button = spec.AllButtons().FirstOrDefault(b => b.Label == label);
            Assert.IsNotNull(button, $"State {Flow.State} has no button '{label}'. Buttons: {string.Join(", ", spec.AllButtons().Select(b => b.Label))}");
            Assert.IsTrue(button.Enabled, $"Button '{label}' is disabled.");
            button.OnPress();
        }

        public void Type(string text)
        {
            foreach (var c in text.ToUpperInvariant()) Press(c.ToString());
        }
    }

    public class ApplicationFlowTests
    {
        FakeAppHost host;
        ApplicationFlow flow;

        [SetUp]
        public void SetUp()
        {
            host = new FakeAppHost();
            flow = host.Flow;
        }

        // ---- Welcome -----------------------------------------------------------------------------------------------

        [Test]
        public void TheApplicationOpensOnTheWelcomeScreenWithTheTwoOptions()
        {
            Assert.AreEqual(AppState.Welcome, flow.State);
            var spec = ScreenCatalog.Build(flow);
            Assert.AreEqual("Bienvenido/a", spec.Kicker);
            Assert.AreEqual("PlayCT", spec.Title);
            Assert.AreEqual("Plataforma de tareas de realidad virtual para interacción, resolución de problemas y estudio de transferencia cognitiva.", spec.Paragraphs.Single());

            var buttons = spec.AllButtons().ToList();
            Assert.AreEqual(new[] { "JUGAR", "EXPERIMENTO" }, buttons.Select(b => b.Label).ToArray());
            Assert.AreEqual("Permite utilizar los juegos de manera independiente, sin iniciar una sesión experimental.", buttons[0].Description);
            Assert.AreEqual("Inicia el flujo experimental y el tracking de investigación.", buttons[1].Description);
            Assert.IsTrue(flow.MenuVisible);
            CollectionAssert.IsEmpty(host.Calls, "opening the app must not start anything");
        }

        // ---- Free play ---------------------------------------------------------------------------------------------

        [Test]
        public void Play_ShowsTheFourGames()
        {
            host.Press("JUGAR");
            Assert.AreEqual(AppState.GameSelection, flow.State);
            var spec = ScreenCatalog.Build(flow);
            Assert.AreEqual("Juegos", spec.Title);
            CollectionAssert.AreEqual(new[] { "Torre de Hanói", "Cubo de Relaciones", "Gabinete de Formas", "El Correo", "Volver al inicio" },
                spec.AllButtons().Select(b => b.Label).ToArray());
        }

        [Test]
        public void EveryGame_CanBePlayedAloneAndLeavesNoExperimentTrace()
        {
            foreach (var game in GameCatalog.All)
            {
                host = new FakeAppHost();
                flow = host.Flow;
                host.Press("JUGAR");
                host.Press(game.Title);
                Assert.AreEqual(AppState.GameInstructions, flow.State);
                Assert.AreEqual(game.TaskId, flow.SelectedGameId);
                Assert.AreEqual(game.Title, ScreenCatalog.Build(flow).Title);
                Assert.AreEqual(0, host.Tasks[game.TaskId].Starts, "instructions come before the game starts");

                host.Press("Comenzar");
                Assert.AreEqual(AppState.FreePlay, flow.State);
                Assert.IsFalse(flow.MenuVisible);
                Assert.AreEqual(1, host.Tasks[game.TaskId].Starts);
                Assert.AreEqual(1, host.Tasks.Values.Sum(t => t.Starts), "only the chosen game runs");

                host.Tasks[game.TaskId].Finish();
                Assert.AreEqual(AppState.GameCompleted, flow.State);
                host.Press("Volver a juegos");
                Assert.AreEqual(AppState.GameSelection, flow.State);

                CollectionAssert.DoesNotContain(host.Calls, "experiment_start");
                Assert.IsFalse(host.Calls.Any(c => c.StartsWith("prepare:")), "free play never creates a session");
                Assert.IsNull(host.Sequencer);
                Assert.IsFalse(host.Recording);
                CollectionAssert.IsEmpty(host.Sink.Events, "free play logs nothing through the flow");
            }
        }

        [Test]
        public void GamesCanBePlayedOneAfterAnotherInAnyOrder()
        {
            host.Press("JUGAR");
            host.Press("Torre de Hanói");
            host.Press("Comenzar");
            host.Tasks[HanoiTrial.TaskName].Finish();
            Assert.AreEqual(AppState.GameCompleted, flow.State);
            host.Press("Volver a juegos");
            host.Press("El Correo");
            host.Press("Comenzar");
            Assert.AreEqual(CorreoTrial.TaskName, flow.SelectedGameId);
            host.Tasks[CorreoTrial.TaskName].Finish();
            host.Press("Volver al inicio");
            Assert.AreEqual(AppState.Welcome, flow.State);

            CollectionAssert.Contains(host.Calls, $"free_stop:{HanoiTrial.TaskName}:completed");
            CollectionAssert.Contains(host.Calls, $"free_stop:{CorreoTrial.TaskName}:completed");
            Assert.AreEqual(1, host.Tasks[HanoiTrial.TaskName].Starts);
            Assert.AreEqual(1, host.Tasks[CorreoTrial.TaskName].Starts);
        }

        [Test]
        public void TheSameGameCanBePlayedAgainAndAGameCanBeLeftBeforeItEnds()
        {
            host.Press("JUGAR");
            host.Press("Cubo de Relaciones");
            host.Press("Comenzar");
            flow.LeaveGame();
            Assert.AreEqual(AppState.GameSelection, flow.State);
            CollectionAssert.Contains(host.Calls, $"free_stop:{CuboTrial.TaskName}:exited");

            host.Press("Cubo de Relaciones");
            host.Press("Comenzar");
            Assert.AreEqual(2, host.Tasks[CuboTrial.TaskName].Starts);
        }

        [Test]
        public void FromTheInstructionsYouCanGoBackWithoutStartingTheGame()
        {
            host.Press("JUGAR");
            host.Press("Gabinete de Formas");
            host.Press("Volver a juegos");
            Assert.AreEqual(AppState.GameSelection, flow.State);
            Assert.AreEqual(0, host.Tasks.Values.Sum(t => t.Starts));
        }

        [Test]
        public void TheFreePlayCompletionScreenShowsNoResults()
        {
            host.Press("JUGAR");
            host.Press("Torre de Hanói");
            host.Press("Comenzar");
            host.Tasks[HanoiTrial.TaskName].Finish();

            var spec = ScreenCatalog.Build(flow);
            Assert.AreEqual("Tarea completada", spec.Title);
            Assert.AreEqual("Has completado esta actividad.", spec.Paragraphs.Single());
            CollectionAssert.AreEquivalent(new[] { "Volver a juegos", "Volver al inicio" }, spec.AllButtons().Select(b => b.Label).ToArray());
            CollectionAssert.IsEmpty(spec.Bullets);
            Assert.IsNull(spec.Info);
        }

        [Test]
        public void ACompletionFromAnotherGameOrStateIsIgnored()
        {
            flow.NotifyGameCompleted(HanoiTrial.TaskName);
            Assert.AreEqual(AppState.Welcome, flow.State);

            host.Press("JUGAR");
            host.Press("Torre de Hanói");
            host.Press("Comenzar");
            flow.NotifyGameCompleted(CuboTrial.TaskName);
            Assert.AreEqual(AppState.FreePlay, flow.State);
        }

        // ---- Instructions ------------------------------------------------------------------------------------------

        [Test]
        public void EveryGameHasShortInstructionsWithoutSolutionsOrCounters()
        {
            Assert.AreEqual(4, GameCatalog.All.Count);
            CollectionAssert.AreEqual(new[] { "Hanoi", "CuboRelaciones", "GabineteFormas", "ElCorreo" }, GameCatalog.Ids);
            var forbidden = new[] { "óptim", "mínimo de", "solución es", "puntos", "puntuación", "eficiencia", "vidas", "combo", "estrella" };
            foreach (var game in GameCatalog.All)
            {
                Assert.That(game.Instructions.Count, Is.InRange(4, 6), game.Title);
                foreach (var line in game.Instructions)
                {
                    Assert.That(line.Length, Is.LessThanOrEqualTo(115), $"{game.Title}: '{line}' is too long for a short instruction");
                    foreach (var word in forbidden) StringAssert.DoesNotContain(word, line.ToLowerInvariant(), game.Title);
                }
            }
        }

        [Test]
        public void TheHanoiInstructionsExplainTheRulesAndTheGoal()
        {
            var text = string.Join(" ", GameCatalog.Get(HanoiTrial.TaskName).Instructions);
            StringAssert.Contains("tres postes", text);
            StringAssert.Contains("Destino", text);
            StringAssert.Contains("disco de arriba", text);
            StringAssert.Contains("grande no puede ir sobre uno más pequeño", text);
        }

        [Test]
        public void TheGameInstructionsCoverTheRequiredTopics()
        {
            StringAssert.Contains("90°", string.Join(" ", GameCatalog.Get(CuboTrial.TaskName).Instructions));
            var gabinete = string.Join(" ", GameCatalog.Get(GabineteTrial.TaskName).Instructions);
            StringAssert.Contains("abertura", gabinete);
            StringAssert.Contains("girarse", gabinete);
            StringAssert.Contains("no encaja", gabinete);
            var correo = string.Join(" ", GameCatalog.Get(CorreoTrial.TaskName).Instructions);
            StringAssert.Contains("estaciones", correo);
            StringAssert.Contains("capacidad", correo);
            StringAssert.Contains("prioridad", correo);
        }

        // ---- Experiment --------------------------------------------------------------------------------------------

        [Test]
        public void Experiment_StartsWithAnIntroductionThatExplainsTheStudyWithoutRevealingConditions()
        {
            host.Press("EXPERIMENTO");
            Assert.AreEqual(AppState.ExperimentIntroduction, flow.State);
            var spec = ScreenCatalog.Build(flow);
            Assert.AreEqual("Experimento", spec.Title);
            var text = string.Join(" ", spec.Paragraphs.Concat(spec.Bullets));
            StringAssert.Contains("realidad virtual", text);
            StringAssert.Contains("varias actividades", text);
            StringAssert.Contains("condiciones distintas", text);
            StringAssert.Contains("con fines de investigación", text);
            StringAssert.Contains("No necesitas conocer las soluciones", text);
            foreach (var word in new[] { "Static", "PreAdapted", "mejor", "peor", "te tocará", "grupo" })
                StringAssert.DoesNotContain(word, text);
            CollectionAssert.DoesNotContain(host.Calls, "experiment_start");
        }

        [Test]
        public void ParticipantId_IsAnonymousValidatedAndCreatesTheSessionOnlyWhenConfirmed()
        {
            host.Press("EXPERIMENTO");
            host.Press("Continuar");
            Assert.AreEqual(AppState.ParticipantSetup, flow.State);

            var spec = ScreenCatalog.Build(flow);
            Assert.AreEqual("ID del participante", spec.FieldLabel);
            var next = spec.AllButtons().Single(b => b.Label == "Continuar");
            Assert.IsFalse(next.Enabled, "an empty code cannot continue");

            host.Type("p-07");
            Assert.AreEqual("P-07", flow.ParticipantId);
            Assert.IsTrue(ScreenCatalog.Build(flow).AllButtons().Single(b => b.Label == "Continuar").Enabled);
            Assert.IsFalse(host.Calls.Any(c => c.StartsWith("prepare:")));

            host.Press("Borrar");
            Assert.AreEqual("P-0", flow.ParticipantId);
            host.Press("Limpiar");
            Assert.AreEqual(string.Empty, flow.ParticipantId);

            host.Type("P07");
            host.Press("Continuar");
            Assert.AreEqual(AppState.ExperimentReady, flow.State);
            CollectionAssert.Contains(host.Calls, "prepare:P07");
            Assert.AreEqual("20260115_090000_ab12", flow.SessionId);
        }

        [Test]
        public void ParticipantId_RejectsNamesWithSpacesAndSymbolsAndLimitsItsLength()
        {
            host.Press("EXPERIMENTO");
            host.Press("Continuar");
            flow.SetParticipantId("Ana María López");
            Assert.AreEqual("ANAMARALPEZ", flow.ParticipantId);
            Assert.IsFalse(ParticipantIdRules.IsValid("Ana María"));
            Assert.IsTrue(ParticipantIdRules.IsValid("P-07_b"));
            flow.SetParticipantId(new string('A', 40));
            Assert.AreEqual(ParticipantIdRules.MaxLength, flow.ParticipantId.Length);
            flow.AppendParticipantCharacter('B');
            Assert.AreEqual(ParticipantIdRules.MaxLength, flow.ParticipantId.Length);
            flow.AppendParticipantCharacter(' ');
            flow.AppendParticipantCharacter('@');
            Assert.AreEqual(ParticipantIdRules.MaxLength, flow.ParticipantId.Length);
        }

        [Test]
        public void TheParticipantCannotChooseTheCondition()
        {
            host.Press("EXPERIMENTO");
            host.Press("Continuar");
            host.Type("A1");
            host.Press("Continuar");
            var experimentStates = new[] { AppState.ExperimentIntroduction, AppState.ParticipantSetup, AppState.ExperimentReady };
            foreach (var spec in AllScreens().Where(sc => experimentStates.Contains(sc.State)))
            {
                var text = ScreenText(spec).ToLowerInvariant();
                foreach (var word in new[] { "static", "preadapted", "condición" })
                    StringAssert.DoesNotContain(word, text, spec.State.ToString());
            }
            var ready = ScreenCatalog.Build(flow);
            Assert.AreEqual("El experimento está listo", ready.Title);
            Assert.AreEqual("A continuación realizarás varias actividades. Lee las instrucciones de cada una antes de comenzar.", ready.Paragraphs.Single());
            StringAssert.Contains("A1", ready.Info);
            CollectionAssert.AreEqual(new[] { "Atrás", "Comenzar experimento" }, ready.AllButtons().Select(b => b.Label).ToArray());
        }

        [Test]
        public void TheExperimentRunsTheFourTasksInOrderWithInstructionsBeforeAndAContinueScreenAfterEach()
        {
            StartExperimentAs("P01");
            Assert.IsTrue(host.Recording);

            var expected = new[] { HanoiTrial.TaskName, CuboTrial.TaskName, GabineteTrial.TaskName, CorreoTrial.TaskName };
            for (var i = 0; i < expected.Length; i++)
            {
                var id = expected[i];
                Assert.AreEqual(AppState.ExperimentTaskInstructions, flow.State, $"task {i}");
                Assert.AreEqual(id, flow.ExperimentTaskId);
                Assert.AreEqual(i, flow.ExperimentTaskIndex);
                Assert.AreEqual(4, flow.ExperimentTaskCount);
                Assert.AreEqual($"Actividad {i + 1} de 4", ScreenCatalog.Build(flow).Kicker);
                Assert.AreEqual(GameCatalog.Get(id).Title, ScreenCatalog.Build(flow).Title);
                Assert.AreEqual(0, host.Tasks[id].Starts, "the task must not start before Comenzar tarea");
                Assert.AreEqual(id, host.Sequencer.CurrentTaskId);

                host.Press("Comenzar tarea");
                Assert.AreEqual(AppState.ExperimentRunning, flow.State);
                Assert.IsFalse(flow.MenuVisible);
                Assert.AreEqual(1, host.Tasks[id].Starts);
                Assert.AreEqual(ExperimentCondition.Static, host.Tasks[id].StartedWith, "the condition comes from the experiment");
                Assert.AreEqual(i, host.Sequencer.CurrentIndex);

                host.Tasks[id].Finish();
                Assert.AreEqual(AppState.ExperimentTaskCompleted, flow.State);
                Assert.AreEqual(id, host.Sequencer.CurrentTaskId, "the experiment waits for Continuar before moving on");
                Assert.AreEqual(0, host.Tasks[id].Ends.Count, "the task is not ended until the participant continues");
                var done = ScreenCatalog.Build(flow);
                Assert.AreEqual("Tarea completada", done.Title);
                CollectionAssert.AreEqual(new[] { "Continuar" }, done.AllButtons().Select(b => b.Label).ToArray());

                host.Press("Continuar");
                CollectionAssert.AreEqual(new[] { "completed" }, host.Tasks[id].Ends);
            }

            Assert.AreEqual(AppState.ExperimentCompleted, flow.State);
            Assert.AreEqual(ExperimentState.Completed, host.Sequencer.State);
            var end = ScreenCatalog.Build(flow);
            Assert.AreEqual("Experimento completado", end.Title);
            Assert.AreEqual("Has completado todas las actividades. Gracias por participar.", end.Paragraphs.Single());
            CollectionAssert.AreEqual(new[] { "Finalizar" }, end.AllButtons().Select(b => b.Label).ToArray());

            host.Press("Finalizar");
            Assert.AreEqual(AppState.Welcome, flow.State);
            Assert.AreEqual(string.Empty, flow.ParticipantId);
            Assert.IsNull(flow.SessionId);
            Assert.IsFalse(host.Recording);
            Assert.AreEqual(1, host.Calls.Count(c => c == "experiment_start"));
        }

        [Test]
        public void TheExistingSequencerStillLogsTheExperimentAndTaskBoundariesOncePerTask()
        {
            StartExperimentAs("P01");
            foreach (var id in GameCatalog.Ids)
            {
                host.Press("Comenzar tarea");
                host.Tasks[id].Finish();
                host.Press("Continuar");
            }

            Assert.AreEqual(1, host.Sink.OfType("experiment_started").Count());
            Assert.AreEqual(1, host.Sink.OfType("experiment_ended").Count());
            Assert.AreEqual(4, host.Sink.OfType("task_started").Count());
            Assert.AreEqual(4, host.Sink.OfType("task_ended").Count());
            CollectionAssert.AreEqual(GameCatalog.Ids, host.Sink.OfType("task_started").Select(e => (string)MemorySink.Field(e, "task_id")).ToList());
            Assert.IsTrue(host.Sink.OfType("task_ended").All(e => (string)MemorySink.Field(e, "status") == "completed"));
            Assert.AreEqual("all_tasks_completed", MemorySink.Field(host.Sink.OfType("experiment_ended").Single(), "end_reason"));
        }

        [Test]
        public void TheFlowAddsOneEventPerStepSoEveryTaskBoundaryCanBeTimedExactly()
        {
            StartExperimentAs("P01");
            host.Press("Comenzar tarea");
            host.Tasks[HanoiTrial.TaskName].Finish();
            host.Press("Continuar");

            var flowEvents = host.Sink.Events.Where(e => e.EventType.StartsWith("task_") && e.EventType != "task_started" && e.EventType != "task_ended").ToList();
            CollectionAssert.AreEqual(
                new[] { "task_instructions_shown", "task_begin_confirmed", "task_completion_shown", "task_continue_confirmed", "task_instructions_shown" },
                flowEvents.Select(e => e.EventType).ToArray());
            Assert.IsTrue(flowEvents.All(e => e.Task == ExperimentSequencer.TaskName));
            Assert.AreEqual(HanoiTrial.TaskName, MemorySink.Field(flowEvents[0], "task_id"));
            Assert.AreEqual(CuboTrial.TaskName, MemorySink.Field(flowEvents[4], "task_id"));

            var order = host.Sink.Events.Select(e => e.EventType).ToList();
            Assert.Less(order.IndexOf("task_started"), order.IndexOf("task_instructions_shown"));
            Assert.Less(order.IndexOf("task_instructions_shown"), order.IndexOf("task_begin_confirmed"));
            Assert.Less(order.IndexOf("task_begin_confirmed"), order.IndexOf("task_completion_shown"));
            Assert.Less(order.IndexOf("task_completion_shown"), order.IndexOf("task_continue_confirmed"));
            Assert.Less(order.IndexOf("task_continue_confirmed"), order.IndexOf("task_ended"));
        }

        [Test]
        public void IfTheExperimentCannotStartTheParticipantStaysOnTheReadyScreenWithAMessage()
        {
            host.StartSucceeds = false;
            host.Press("EXPERIMENTO");
            host.Press("Continuar");
            host.Type("P01");
            host.Press("Continuar");
            host.Press("Comenzar experimento");

            Assert.AreEqual(AppState.ExperimentReady, flow.State);
            StringAssert.Contains("No se pudo iniciar", ScreenCatalog.Build(flow).Notice);
            host.StartSucceeds = true;
            host.Press("Comenzar experimento");
            Assert.AreEqual(AppState.ExperimentTaskInstructions, flow.State);
            Assert.IsNull(flow.StartError);
        }

        [Test]
        public void IfTheExperimentIsAbortedTheApplicationReturnsToTheStart()
        {
            StartExperimentAs("P01");
            host.Press("Comenzar tarea");
            host.Sequencer.Abort("researcher_stop");

            Assert.AreEqual(AppState.Welcome, flow.State);
            Assert.AreEqual(ExperimentState.Aborted, host.Sequencer.State);
            CollectionAssert.AreEqual(new[] { "researcher_stop" }, host.Tasks[HanoiTrial.TaskName].Ends);
            Assert.IsFalse(host.Recording);
        }

        [Test]
        public void ANewExperimentCanRunAfterTheFirstOne()
        {
            StartExperimentAs("P01");
            foreach (var id in GameCatalog.Ids)
            {
                host.Press("Comenzar tarea");
                host.Tasks[id].Finish();
                host.Press("Continuar");
            }
            host.Press("Finalizar");

            StartExperimentAs("P02");
            Assert.AreEqual(AppState.ExperimentTaskInstructions, flow.State);
            Assert.AreEqual(HanoiTrial.TaskName, flow.ExperimentTaskId);
            Assert.AreEqual(2, host.Calls.Count(c => c == "experiment_start"));
        }

        [Test]
        public void TheExperimentCannotBeSkippedIntoFromFreePlayScreens()
        {
            flow.BeginExperiment();
            flow.BeginExperimentTask();
            flow.ContinueAfterTask();
            flow.ConfirmParticipant();
            Assert.AreEqual(AppState.Welcome, flow.State);
            CollectionAssert.IsEmpty(host.Calls);
        }

        [Test]
        public void AGatedTaskCompletingInsideItsStartIsStillShownOnce()
        {
            StartExperimentAs("P01");
            var inner = host.Tasks[HanoiTrial.TaskName];
            host.Press("Comenzar tarea");
            inner.Finish();
            inner.Finish();
            Assert.AreEqual(1, host.Calls.Count(c => c == "inner_completed:" + HanoiTrial.TaskName));
        }

        [Test]
        public void NoScreenShowsScoresMetricsOrEnglish()
        {
            var banned = new[] { "puntos", "puntaje", "puntuación", "score", "movimientos", "errores", "eficiencia", "tiempo total", "racha", "combo", "vidas", "monedas", "estrellas" };
            var english = new System.Text.RegularExpressions.Regex(@"\b(start|play|next|back|continue|welcome|game|experiment|participant)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (var spec in AllScreens())
            {
                var text = ScreenText(spec);
                foreach (var word in banned)
                    StringAssert.DoesNotContain(word.ToLowerInvariant(), text.ToLowerInvariant(), $"{spec.State}: '{word}'");
                Assert.IsFalse(english.IsMatch(text.Replace("PlayCT", "")), $"{spec.State} has English text: {text}");
            }
        }

        [Test]
        public void EveryScreenHasATitleAndAtLeastOneButton()
        {
            foreach (var spec in AllScreens())
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(spec.Title), spec.State.ToString());
                Assert.Greater(spec.AllButtons().Count(), 0, spec.State.ToString());
                foreach (var button in spec.AllButtons())
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(button.Label));
                    Assert.IsNotNull(button.OnPress);
                }
            }
        }

        [Test]
        public void NoMenuIsShownWhileATaskIsBeingPlayed()
        {
            host.Press("JUGAR");
            host.Press("Torre de Hanói");
            host.Press("Comenzar");
            Assert.IsNull(ScreenCatalog.Build(flow));
            Assert.AreEqual(AppState.FreePlay, flow.State);

            host.Tasks[HanoiTrial.TaskName].Finish();
            host.Press("Volver al inicio");
            StartExperimentAs("P01");
            host.Press("Comenzar tarea");
            Assert.IsNull(ScreenCatalog.Build(flow));
        }

        // ---- Helpers -----------------------------------------------------------------------------------------------

        void StartExperimentAs(string participant)
        {
            host.Press("EXPERIMENTO");
            host.Press("Continuar");
            host.Type(participant);
            host.Press("Continuar");
            host.Press("Comenzar experimento");
        }

        /// <summary>Every screen of the application, reached by driving the flow through both modes.</summary>
        public static List<ScreenSpec> AllScreens()
        {
            var screens = new List<ScreenSpec>();
            var host = new FakeAppHost();
            var flow = host.Flow;
            void Capture() => screens.Add(ScreenCatalog.Build(flow));

            Capture();
            host.Press("JUGAR");
            Capture();
            foreach (var game in GameCatalog.All)
            {
                host.Press(game.Title);
                Capture();
                host.Press("Volver a juegos");
            }
            host.Press("Torre de Hanói");
            host.Press("Comenzar");
            host.Tasks[HanoiTrial.TaskName].Finish();
            Capture();
            host.Press("Volver al inicio");

            host.Press("EXPERIMENTO");
            Capture();
            host.Press("Continuar");
            Capture();
            host.Type("PARTICIPANTE-01");
            Capture();
            host.Press("Continuar");
            Capture();
            host.Press("Comenzar experimento");
            foreach (var id in GameCatalog.Ids)
            {
                Capture();
                host.Press("Comenzar tarea");
                host.Tasks[id].Finish();
                Capture();
                host.Press("Continuar");
            }
            Capture();
            return screens;
        }

        public static string ScreenText(ScreenSpec spec)
        {
            var parts = new List<string> { spec.Kicker, spec.Title, spec.Notice, spec.Info, spec.FieldLabel, spec.Footer };
            parts.AddRange(spec.Paragraphs);
            parts.AddRange(spec.Bullets);
            foreach (var button in spec.AllButtons())
            {
                parts.Add(button.Label);
                parts.Add(button.Description);
            }
            return string.Join(" | ", parts.Where(p => !string.IsNullOrEmpty(p)));
        }
    }
}
