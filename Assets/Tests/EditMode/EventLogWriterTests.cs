using System.IO;
using System.Linq;
using NUnit.Framework;
using PlayCT.Research;
using PlayCT.Tasks.Hanoi;

namespace PlayCT.Tests
{
    public class EventLogWriterTests
    {
        [Test]
        public void Writer_AddsSessionEnvelope_ToEveryLine()
        {
            var clock = new FakeClock();
            var text = new StringWriter();
            var writer = new EventLogWriter(new SessionInfo("S-1", "P-07", "cond-A"), text, clock);
            var trial = new HanoiTrial(3, writer, clock);
            trial.Begin();
            clock.Advance(1.25);
            trial.OnGrab(1);
            trial.OnRelease(1, HanoiPegs.Destino);

            var lines = text.ToString().Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
            Assert.AreEqual(3, lines.Count);
            Assert.AreEqual(3, writer.EventCount);
            foreach (var line in lines)
            {
                StringAssert.StartsWith("{\"session_id\":\"S-1\",\"participant_id\":\"P-07\",\"condition\":\"cond-A\",\"task\":\"Hanoi\",\"event\":\"", line);
                StringAssert.Contains("\"timestamp_utc\":\"2026-01-15T09:00:", line);
                StringAssert.EndsWith("}", line);
            }

            StringAssert.Contains("\"event\":\"disk_release\"", lines[2]);
            StringAssert.Contains("\"disk_id\":\"D1\"", lines[2]);
            StringAssert.Contains("\"source_peg\":\"Origen\"", lines[2]);
            StringAssert.Contains("\"destination_peg\":\"Destino\"", lines[2]);
            StringAssert.Contains("\"legal\":true", lines[2]);
            StringAssert.Contains("\"move_number\":1", lines[2]);
            StringAssert.Contains("\"resulting_state\":\"Origen:[3,2];Apoyo:[];Destino:[1]\"", lines[2]);
            StringAssert.Contains("\"completion_status\":\"in_progress\"", lines[2]);
            StringAssert.Contains("\"t_session_s\":1.25", lines[2]);
        }

        [Test]
        public void JsonLine_EscapesAndHandlesSpecialValues()
        {
            var e = new ResearchEvent("t", "e")
                .Add("text", "a\"b\\c\nd")
                .Add("nan", double.NaN)
                .Add("none", null)
                .Add("list", new[] { "x", "y" })
                .Add("decimal", 0.5);
            var json = JsonLine.Serialize(e.Fields);

            Assert.AreEqual("{\"text\":\"a\\\"b\\\\c\\nd\",\"nan\":null,\"none\":null,\"list\":[\"x\",\"y\"],\"decimal\":0.5}", json);
        }

        [Test]
        public void Summary_ToJson_ContainsResearchMetrics()
        {
            var clock = new FakeClock();
            var trial = new HanoiTrial(3, new MemorySink(), clock);
            trial.Begin();
            clock.Advance(5);
            HanoiSolver.Play(trial, 3);

            var json = trial.BuildSummary().ToJson(new SessionInfo("S", "P", "C"));

            StringAssert.Contains("\"task\":\"Hanoi\"", json);
            StringAssert.Contains("\"total_moves\":7", json);
            StringAssert.Contains("\"optimal_moves\":7", json);
            StringAssert.Contains("\"invalid_attempts\":0", json);
            StringAssert.Contains("\"efficiency\":1", json);
            StringAssert.Contains("\"completion_status\":\"completed\"", json);
            StringAssert.Contains("\"move_sequence\":[\"D1:Origen->Destino\"", json);
        }
    }
}
