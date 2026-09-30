using System.Collections.Generic;
using System.Linq;

namespace PlayCT.Research
{
    /// <summary>
    /// The standard guide messages every task falls back on. They ask the participant to observe, to notice a constraint or to
    /// think about what changes and what stays the same; none of them names a move, a piece or an answer.
    /// </summary>
    public static class GuideCatalog
    {
        public static readonly IReadOnlyList<GuideMessage> Common = new[]
        {
            new GuideMessage("common.task_start.1", GuideMoment.TaskStart, "Antes de empezar, observa con calma el material. ¿Qué observas?"),
            new GuideMessage("common.task_start.2", GuideMoment.TaskStart, "Tómate el tiempo que necesites. ¿Qué crees que puedes hacer con lo que tienes delante?"),

            new GuideMessage("common.trial_start.1", GuideMoment.TrialStart, "¿Qué observas?"),
            new GuideMessage("common.trial_start.2", GuideMoment.TrialStart, "¿Qué podrías probar?"),

            new GuideMessage("common.help.1", GuideMoment.HelpRequest, "¿Qué observas?"),
            new GuideMessage("common.help.2", GuideMoment.HelpRequest, "¿Qué restricción estás observando?"),
            new GuideMessage("common.help.3", GuideMoment.HelpRequest, "¿Qué cambia cuando realizas esta acción?"),
            new GuideMessage("common.help.4", GuideMoment.HelpRequest, "¿Qué crees que permanecerá igual?"),
            new GuideMessage("common.help.5", GuideMoment.HelpRequest, "¿Qué podrías probar?"),

            new GuideMessage("common.trial_end.completed.1", GuideMoment.TrialEnd, "Has terminado este ensayo. ¿Qué has notado mientras lo hacías?", GuideOutcome.Completed),
            new GuideMessage("common.trial_end.incomplete.1", GuideMoment.TrialEnd, "Este ensayo ha terminado. ¿Qué probarías de otra manera?", GuideOutcome.Incomplete),

            new GuideMessage("common.task_end.1", GuideMoment.TaskEnd, "Has terminado esta actividad. ¿Qué es lo que más te ha llamado la atención?"),
        };

        public static IReadOnlyList<GuideMessage> ForMoment(IEnumerable<GuideMessage> messages, GuideMoment moment, GuideOutcome outcome) =>
            messages.Where(m => m.Moment == moment && (m.Outcome == GuideOutcome.Any || m.Outcome == outcome)).ToList();
    }
}
