using System.Collections.Generic;

namespace PlayCT.App
{
    /// <summary>Builds the content of each screen from the flow's current state. Returns null for the states where the participant is playing and no menu is shown.</summary>
    public static class ScreenCatalog
    {
        public const string PointerHint = "Apunta con el controlador y pulsa el gatillo para elegir.";

        public static ScreenSpec Build(ApplicationFlow flow)
        {
            switch (flow.State)
            {
                case AppState.Welcome: return Welcome(flow);
                case AppState.GameSelection: return GameSelection(flow);
                case AppState.GameInstructions: return Instructions(flow, "Instrucciones", GameCatalog.Get(flow.SelectedGameId), "Comenzar", "Volver a juegos", flow.BeginSelectedGame, flow.BackToGames);
                case AppState.GameCompleted: return GameCompleted(flow);
                case AppState.ExperimentIntroduction: return ExperimentIntroduction(flow);
                case AppState.ParticipantSetup: return ParticipantSetup(flow);
                case AppState.ExperimentReady: return ExperimentReady(flow);
                case AppState.ExperimentTaskInstructions:
                    return Instructions(flow, $"Actividad {flow.ExperimentTaskIndex + 1} de {flow.ExperimentTaskCount}", GameCatalog.Get(flow.ExperimentTaskId),
                        "Comenzar tarea", null, flow.BeginExperimentTask, null);
                case AppState.ExperimentTaskCompleted: return ExperimentTaskCompleted(flow);
                case AppState.ExperimentCompleted: return ExperimentCompleted(flow);
                default: return null;
            }
        }

        static ScreenSpec Welcome(ApplicationFlow flow)
        {
            var spec = new ScreenSpec { State = flow.State, Kicker = "Bienvenido/a", Title = "PlayCT", Footer = PointerHint };
            spec.Paragraphs.Add("Plataforma de tareas de realidad virtual para interacción, resolución de problemas y estudio de transferencia cognitiva.");
            spec.ButtonRows.Add(new List<ButtonSpec>
            {
                new ButtonSpec("play", "JUGAR", ButtonKind.Hero, flow.ChooseFreePlay)
                {
                    Description = "Permite utilizar los juegos de manera independiente, sin iniciar una sesión experimental.",
                },
                new ButtonSpec("experiment", "EXPERIMENTO", ButtonKind.Hero, flow.ChooseExperiment)
                {
                    Description = "Inicia el flujo experimental y el tracking de investigación.",
                },
            });
            return spec;
        }

        static ScreenSpec GameSelection(ApplicationFlow flow)
        {
            var spec = new ScreenSpec { State = flow.State, Title = "Juegos", Footer = PointerHint };
            spec.Paragraphs.Add("Elige una actividad. Puedes jugar a cualquiera.");
            var cards = new List<ButtonSpec>();
            foreach (var game in GameCatalog.All)
            {
                var id = game.TaskId;
                cards.Add(new ButtonSpec("game:" + id, game.Title, ButtonKind.Card, () => flow.SelectGame(id)) { Description = game.Summary });
            }
            spec.ButtonRows.Add(cards.GetRange(0, 2));
            spec.ButtonRows.Add(cards.GetRange(2, 2));
            spec.ButtonRows.Add(new List<ButtonSpec> { new ButtonSpec("home", "Volver al inicio", ButtonKind.Secondary, flow.BackToStart) });
            return spec;
        }

        static ScreenSpec Instructions(ApplicationFlow flow, string kicker, GameInfo game, string startLabel, string backLabel,
            System.Action start, System.Action back)
        {
            var spec = new ScreenSpec { State = flow.State, Kicker = kicker, Title = game.Title };
            spec.Bullets.AddRange(game.Instructions);
            var row = new List<ButtonSpec>();
            if (backLabel != null) row.Add(new ButtonSpec("back", backLabel, ButtonKind.Secondary, back));
            row.Add(new ButtonSpec("start", startLabel, ButtonKind.Primary, start));
            spec.ButtonRows.Add(row);
            return spec;
        }

        static ScreenSpec GameCompleted(ApplicationFlow flow)
        {
            var spec = new ScreenSpec { State = flow.State, Title = "Tarea completada" };
            spec.Paragraphs.Add("Has completado esta actividad.");
            spec.ButtonRows.Add(new List<ButtonSpec>
            {
                new ButtonSpec("home", "Volver al inicio", ButtonKind.Secondary, flow.BackToStart),
                new ButtonSpec("games", "Volver a juegos", ButtonKind.Primary, flow.BackToGames),
            });
            return spec;
        }

        static ScreenSpec ExperimentIntroduction(ApplicationFlow flow)
        {
            var spec = new ScreenSpec { State = flow.State, Title = "Experimento", Footer = PointerHint };
            spec.Paragraphs.Add("Este es un estudio sobre la interacción y la resolución de tareas en realidad virtual.");
            spec.Bullets.Add("Realizarás varias actividades, una tras otra.");
            spec.Bullets.Add("Algunas actividades pueden presentarse en condiciones distintas.");
            spec.Bullets.Add("Tus interacciones se registrarán con fines de investigación, asociadas solo a un código anónimo.");
            spec.Bullets.Add("Sigue las instrucciones de cada actividad. No necesitas conocer las soluciones de antemano.");
            spec.ButtonRows.Add(new List<ButtonSpec>
            {
                new ButtonSpec("home", "Volver al inicio", ButtonKind.Secondary, flow.BackToStart),
                new ButtonSpec("continue", "Continuar", ButtonKind.Primary, flow.ContinueFromIntroduction),
            });
            return spec;
        }

        static ScreenSpec ParticipantSetup(ApplicationFlow flow)
        {
            var spec = new ScreenSpec
            {
                State = flow.State,
                Title = "Identificación",
                FieldLabel = "ID del participante",
                FieldValue = flow.ParticipantId,
            };
            spec.Paragraphs.Add("Introduce tu código anónimo. No escribas tu nombre.");

            var keys = new List<ButtonSpec>();
            foreach (var c in "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-")
            {
                var key = c;
                keys.Add(new ButtonSpec("key:" + c, c.ToString(), ButtonKind.Key, () => flow.AppendParticipantCharacter(key)));
            }
            keys.Add(new ButtonSpec("key:back", "Borrar", ButtonKind.Key, flow.BackspaceParticipant));
            keys.Add(new ButtonSpec("key:clear", "Limpiar", ButtonKind.Key, flow.ClearParticipant));
            for (var i = 0; i < keys.Count; i += 10) spec.KeyRows.Add(keys.GetRange(i, System.Math.Min(10, keys.Count - i)));

            spec.ButtonRows.Add(new List<ButtonSpec>
            {
                new ButtonSpec("back", "Atrás", ButtonKind.Secondary, flow.BackToIntroduction),
                new ButtonSpec("continue", "Continuar", ButtonKind.Primary, flow.ConfirmParticipant) { Enabled = flow.IsParticipantIdValid },
            });
            return spec;
        }

        static ScreenSpec ExperimentReady(ApplicationFlow flow)
        {
            var spec = new ScreenSpec { State = flow.State, Title = "El experimento está listo", Notice = flow.StartError };
            spec.Paragraphs.Add("A continuación realizarás varias actividades. Lee las instrucciones de cada una antes de comenzar.");
            spec.Info = $"Participante: {flow.ParticipantId}     Sesión: {flow.SessionId}";
            spec.ButtonRows.Add(new List<ButtonSpec>
            {
                new ButtonSpec("back", "Atrás", ButtonKind.Secondary, flow.BackToParticipantSetup),
                new ButtonSpec("begin", "Comenzar experimento", ButtonKind.Primary, flow.BeginExperiment),
            });
            return spec;
        }

        static ScreenSpec ExperimentTaskCompleted(ApplicationFlow flow)
        {
            var spec = new ScreenSpec
            {
                State = flow.State,
                Kicker = $"Actividad {flow.ExperimentTaskIndex + 1} de {flow.ExperimentTaskCount}",
                Title = "Tarea completada",
            };
            spec.Paragraphs.Add("Has completado esta actividad.");
            spec.Paragraphs.Add(flow.ExperimentTaskIndex + 1 < flow.ExperimentTaskCount
                ? "Cuando estés listo/a, continúa con la siguiente actividad."
                : "Cuando estés listo/a, continúa.");
            spec.ButtonRows.Add(new List<ButtonSpec> { new ButtonSpec("continue", "Continuar", ButtonKind.Primary, flow.ContinueAfterTask) });
            return spec;
        }

        static ScreenSpec ExperimentCompleted(ApplicationFlow flow)
        {
            var spec = new ScreenSpec { State = flow.State, Title = "Experimento completado" };
            spec.Paragraphs.Add("Has completado todas las actividades. Gracias por participar.");
            spec.ButtonRows.Add(new List<ButtonSpec> { new ButtonSpec("finish", "Finalizar", ButtonKind.Primary, flow.FinishExperiment) });
            return spec;
        }
    }
}
