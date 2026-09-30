using System.Collections.Generic;
using System.Linq;
using PlayCT.Tasks.Correo;
using PlayCT.Tasks.Cubo;
using PlayCT.Tasks.Gabinete;
using PlayCT.Tasks.Hanoi;

namespace PlayCT.App
{
    /// <summary>What the participant reads about one game: its name, a one-line summary and the instructions shown before it starts.</summary>
    public sealed class GameInfo
    {
        public string TaskId { get; }
        public string Title { get; }
        public string Summary { get; }
        public IReadOnlyList<string> Instructions { get; }

        public GameInfo(string taskId, string title, string summary, params string[] instructions)
        {
            TaskId = taskId;
            Title = title;
            Summary = summary;
            Instructions = instructions;
        }
    }

    /// <summary>
    /// The four games in menu order, with their Spanish instruction screens. The instructions say what to do, how to interact and what
    /// completing the task means; they do not give a solution, a count of moves or any strategy.
    /// </summary>
    public static class GameCatalog
    {
        public static readonly IReadOnlyList<GameInfo> All = new[]
        {
            new GameInfo(HanoiTrial.TaskName, "Torre de Hanói", "Traslada una torre de discos entre tres postes.",
                "Hay tres postes: Origen, Apoyo y Destino. Al empezar, todos los discos están en Origen.",
                "Objetivo: trasladar toda la torre al poste Destino.",
                "Toma un disco con el agarre o el gatillo y suéltalo sobre un poste.",
                "Solo puedes mover el disco de arriba. Un disco grande no puede ir sobre uno más pequeño.",
                "La actividad termina sola cuando la torre está completa."),

            new GameInfo(CuboTrial.TaskName, "Cubo de Relaciones", "Gira las caras de un cubo y observa qué cambia.",
                "Sobre la mesa hay un cubo de piezas pequeñas, con caras de distintos colores.",
                "Sujeta el cubo con una mano para estabilizarlo. Con la otra, toca una cara para seleccionarla y gírala.",
                "Cada giro es de 90°, en un sentido o en el otro.",
                "Observa qué cambia y qué permanece igual cuando giras una cara.",
                "Son varias pequeñas tareas seguidas. Algunas tienen un tiempo máximo, sin reloj visible."),

            new GameInfo(GabineteTrial.TaskName, "Gabinete de Formas", "Encuentra la abertura de cada pieza.",
                "Sobre la mesa hay un mueble con aberturas y una bandeja con piezas de madera.",
                "Toma una pieza con el agarre o el gatillo y llévala hasta su abertura.",
                "Cada pieza encaja en una sola abertura. Algunas deben girarse para encajar.",
                "Una pieza que no corresponde no encaja y vuelve a la bandeja.",
                "Hay varias rondas. Termina al colocar todas las piezas pedidas."),

            new GameInfo(CorreoTrial.TaskName, "El Correo", "Planifica el envío de paquetes entre estaciones.",
                "El mapa muestra cinco estaciones, de A a E, unidas por caminos de capacidad limitada.",
                "Hay tres paquetes: P1, P2 y P3. P1 tiene prioridad. Objetivo: que los tres lleguen a E.",
                "Toca la estación de salida, la de destino y los paquetes, y pulsa Enviar. Limpiar borra la selección.",
                "Hay reglas sobre qué paquetes pueden viajar juntos. Un envío que no las respeta no se realiza.",
                "Planifica los envíos antes de hacerlos. Hay un tiempo máximo, sin reloj visible."),
        };

        public static IReadOnlyList<string> Ids => All.Select(g => g.TaskId).ToList();

        public static bool TryGet(string taskId, out GameInfo info)
        {
            info = All.FirstOrDefault(g => g.TaskId == taskId);
            return info != null;
        }

        public static GameInfo Get(string taskId)
        {
            if (!TryGet(taskId, out var info)) throw new KeyNotFoundException($"There is no game '{taskId}'.");
            return info;
        }
    }
}
