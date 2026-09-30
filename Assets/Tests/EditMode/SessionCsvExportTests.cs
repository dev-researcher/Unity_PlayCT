using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PlayCT.Research;

namespace PlayCT.Tests
{
    public class SessionCsvExportTests
    {
        static SessionSummary Aggregate(SessionFixture fixture)
        {
            var events = EventLogReader.Read(fixture.Lines(), out var skipped);
            return ResearchTaskCatalog.CreateAggregator().Aggregate(events, skipped);
        }

        /// <summary>A small RFC 4180 reader, independent of the writer, so the tests check the file and not the writer against itself.</summary>
        static List<List<string>> Parse(string csv)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < csv.Length; i++)
            {
                var c = csv[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < csv.Length && csv[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else cell.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); }
                else if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(row);
                    row = new List<string>();
                    i++;
                }
                else cell.Append(c);
            }
            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
            return rows;
        }

        static string Cell(List<List<string>> table, int row, string column) => table[row][table[0].IndexOf(column)];

        static List<List<string>> FullTable() => Parse(SessionCsvExporter.BuildSessionSummaryCsv(Aggregate(SessionFixture.FullSession())));

        [Test]
        public void Header_StartsWithTheCommonColumnsInAFixedOrder()
        {
            var table = FullTable();
            CollectionAssert.AreEqual(SessionCsvExporter.CommonColumns, table[0].Take(SessionCsvExporter.CommonColumns.Count).ToList());
            CollectionAssert.AreEqual(new[]
            {
                "session_id", "participant_id", "condition", "task_id", "trial", "start_time", "end_time", "duration", "completed",
                "completion_status", "completion_time_s", "attempts", "valid_actions", "invalid_actions", "moves", "efficiency",
                "action_sequence", "record_source",
            }, SessionCsvExporter.CommonColumns.ToList());
        }

        [Test]
        public void Header_HasNoDuplicatesAndEveryRowHasOneCellPerColumn()
        {
            var table = FullTable();
            CollectionAssert.AllItemsAreUnique(table[0]);
            foreach (var row in table) Assert.AreEqual(table[0].Count, row.Count);
        }

        [Test]
        public void Header_DoesNotDependOnWhichTasksRan()
        {
            var full = FullTable()[0];

            var hanoiOnly = new SessionFixture();
            hanoiOnly.StartExperiment("Hanoi");
            hanoiOnly.StartTask("Hanoi", 0);
            hanoiOnly.PlayHanoi(3);
            hanoiOnly.EndTask("Hanoi", 0, "completed", 10);
            var partial = Parse(SessionCsvExporter.BuildSessionSummaryCsv(Aggregate(hanoiOnly)))[0];

            CollectionAssert.AreEqual(full, partial);
        }

        [Test]
        public void ARunOfFourTasks_GivesOneRowPerTaskTrial()
        {
            var table = FullTable();
            Assert.AreEqual(5, table.Count, "header and four tasks");
            CollectionAssert.AreEqual(new[] { "Hanoi", "CuboRelaciones", "GabineteFormas", "ElCorreo" },
                Enumerable.Range(1, 4).Select(i => Cell(table, i, "task_id")).ToArray());
            for (var i = 1; i <= 4; i++)
            {
                Assert.AreEqual("S-1", Cell(table, i, "session_id"));
                Assert.AreEqual("P-07", Cell(table, i, "participant_id"));
                Assert.AreEqual("PreAdapted", Cell(table, i, "condition"));
                Assert.AreEqual("1", Cell(table, i, "trial"));
            }
        }

        [Test]
        public void SeveralTrialsOfATask_GiveSeveralRows()
        {
            var f = new SessionFixture();
            f.StartExperiment("Hanoi");
            f.StartTask("Hanoi", 0);
            f.PlayHanoi(3, 1);
            f.PlayHanoi(4, 2);
            f.EndTask("Hanoi", 0, "completed", 10);

            var table = Parse(SessionCsvExporter.BuildSessionSummaryCsv(Aggregate(f)));
            Assert.AreEqual(3, table.Count);
            Assert.AreEqual("1", Cell(table, 1, "trial"));
            Assert.AreEqual("2", Cell(table, 2, "trial"));
        }

        [Test]
        public void ValuesAreWrittenFromTheAggregatedRecord()
        {
            var table = FullTable();
            var cubo = Enumerable.Range(1, 4).Single(i => Cell(table, i, "task_id") == "CuboRelaciones");
            Assert.AreEqual("true", Cell(table, cubo, "completed"));
            Assert.AreEqual("completed", Cell(table, cubo, "completion_status"));
            Assert.AreEqual("19.5", Cell(table, cubo, "completion_time_s"));
            Assert.AreEqual("6", Cell(table, cubo, "attempts"));
            Assert.AreEqual("4", Cell(table, cubo, "valid_actions"));
            Assert.AreEqual("1", Cell(table, cubo, "invalid_actions"));
            Assert.AreEqual("3", Cell(table, cubo, "CuboRelaciones.reference_moves"));
            StringAssert.Contains("sel:U", Cell(table, cubo, "action_sequence"));
            StringAssert.EndsWith("Z", Cell(table, cubo, "start_time"));
            StringAssert.EndsWith("Z", Cell(table, cubo, "end_time"));
        }

        [Test]
        public void NotApplicableValues_AreEmptyCells()
        {
            var table = FullTable();
            var cubo = Enumerable.Range(1, 4).Single(i => Cell(table, i, "task_id") == "CuboRelaciones");
            var hanoi = Enumerable.Range(1, 4).Single(i => Cell(table, i, "task_id") == "Hanoi");

            Assert.AreEqual(string.Empty, Cell(table, cubo, "efficiency"), "Cubo logs no efficiency");
            Assert.AreEqual(string.Empty, Cell(table, cubo, "Hanoi.disk_count"), "a Hanoi column on a Cubo row");
            Assert.AreEqual(string.Empty, Cell(table, hanoi, "CuboRelaciones.reference_moves"));
            Assert.AreNotEqual(string.Empty, Cell(table, hanoi, "efficiency"));
            Assert.AreNotEqual(string.Empty, Cell(table, hanoi, "Hanoi.disk_count"));
        }

        [Test]
        public void IncompleteTrial_HasNoCompletionTime()
        {
            var f = new SessionFixture();
            f.StartExperiment("CuboRelaciones");
            f.StartTask("CuboRelaciones", 0);
            f.LogCuboTrial(1, completed: false);
            f.EndTask("CuboRelaciones", 0, "incomplete", 20);

            var table = Parse(SessionCsvExporter.BuildSessionSummaryCsv(Aggregate(f)));
            Assert.AreEqual("false", Cell(table, 1, "completed"));
            Assert.AreEqual(string.Empty, Cell(table, 1, "completion_time_s"));
        }

        [Test]
        public void Escape_QuotesCommasQuotesAndNewlines()
        {
            Assert.AreEqual("plain", CsvWriter.Escape("plain"));
            Assert.AreEqual(string.Empty, CsvWriter.Escape(null));
            Assert.AreEqual("\"a,b\"", CsvWriter.Escape("a,b"));
            Assert.AreEqual("\"say \"\"hi\"\"\"", CsvWriter.Escape("say \"hi\""));
            Assert.AreEqual("\"line1\nline2\"", CsvWriter.Escape("line1\nline2"));
            Assert.AreEqual("\"a\r\nb\"", CsvWriter.Escape("a\r\nb"));
        }

        [Test]
        public void SpecialCharactersInAValue_SurviveARoundTrip()
        {
            const string tricky = "Ana, \"la\" niña\nñandú ¿qué? 日本語 😀";
            var f = new SessionFixture("S-2", tricky, "Cond,1");
            f.StartExperiment("Hanoi");
            f.StartTask("Hanoi", 0);
            f.PlayHanoi(3);
            f.EndTask("Hanoi", 0, "completed", 5);

            var csv = SessionCsvExporter.BuildSessionSummaryCsv(Aggregate(f));
            var table = Parse(csv);
            Assert.AreEqual(2, table.Count, "the embedded newline must stay inside its quoted cell");
            Assert.AreEqual(tricky, Cell(table, 1, "participant_id"));
            Assert.AreEqual("Cond,1", Cell(table, 1, "condition"));
        }

        [Test]
        public void Utf8_HasNoByteOrderMarkAndKeepsNonAsciiText()
        {
            var f = new SessionFixture("S-3", "Participante ñ ¿?", "Control");
            f.StartExperiment("Hanoi");
            f.StartTask("Hanoi", 0);
            f.PlayHanoi(3);
            f.EndTask("Hanoi", 0, "completed", 5);
            var csv = SessionCsvExporter.BuildSessionSummaryCsv(Aggregate(f));

            var bytes = new UTF8Encoding(false).GetBytes(csv);
            Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
            Assert.AreEqual(csv, Encoding.UTF8.GetString(bytes));
            StringAssert.Contains("Participante ñ ¿?", Encoding.UTF8.GetString(bytes));
        }

        [Test]
        public void LineEndingsAreCrlf_AndTheFileEndsWithOne()
        {
            var csv = SessionCsvExporter.BuildSessionSummaryCsv(Aggregate(SessionFixture.FullSession()));
            StringAssert.EndsWith("\r\n", csv);
            Assert.IsFalse(csv.Replace("\r\n", string.Empty).Contains("\n"), "no bare line feeds outside quoted cells");
        }

        [Test]
        public void SameSession_GivesTheSameBytesEveryTime()
        {
            var a = SessionCsvExporter.BuildSessionSummaryCsv(Aggregate(SessionFixture.FullSession()));
            var b = SessionCsvExporter.BuildSessionSummaryCsv(Aggregate(SessionFixture.FullSession()));
            Assert.AreEqual(a, b);

            var ea = SessionCsvExporter.BuildEventsCsv(SessionFixture.FullSession().Events());
            var eb = SessionCsvExporter.BuildEventsCsv(SessionFixture.FullSession().Events());
            Assert.AreEqual(ea, eb);
        }

        [Test]
        public void Numbers_UseTheInvariantCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("es-ES");
                var table = FullTable();
                var cubo = Enumerable.Range(1, 4).Single(i => Cell(table, i, "task_id") == "CuboRelaciones");
                Assert.AreEqual("19.5", Cell(table, cubo, "completion_time_s"));
                Assert.AreEqual(SessionCsvExporter.Format(1.25), "1.25");
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Test]
        public void Format_HandlesNullBoolListsAndNonFiniteNumbers()
        {
            Assert.AreEqual(string.Empty, SessionCsvExporter.Format(null));
            Assert.AreEqual("true", SessionCsvExporter.Format(true));
            Assert.AreEqual("false", SessionCsvExporter.Format(false));
            Assert.AreEqual(string.Empty, SessionCsvExporter.Format(double.NaN));
            Assert.AreEqual("[\"a\",\"b\"]", SessionCsvExporter.Format(new List<string> { "a", "b" }));
            Assert.AreEqual("7", SessionCsvExporter.Format(7L));
        }

        [Test]
        public void ATaskFieldNoAdapterDeclares_GetsAnExtraColumnWithoutBreakingTheOthers()
        {
            var summary = Aggregate(SessionFixture.FullSession());
            var declared = SessionCsvExporter.SummaryColumns(summary);

            summary.Records[0].TaskSpecific.Add(new KeyValuePair<string, object>("brand_new_metric", 42));
            var columns = SessionCsvExporter.SummaryColumns(summary);

            CollectionAssert.AreEqual(declared, columns.Take(declared.Count).ToList());
            Assert.AreEqual("Hanoi.brand_new_metric", columns.Last());
            var table = Parse(SessionCsvExporter.BuildSessionSummaryCsv(summary));
            Assert.AreEqual("42", Cell(table, 1, "Hanoi.brand_new_metric"));
            Assert.AreEqual(string.Empty, Cell(table, 2, "Hanoi.brand_new_metric"));
        }

        [Test]
        public void EventsCsv_HasOneRowPerEventAndKeepsTheEnvelope()
        {
            var f = SessionFixture.FullSession();
            var events = f.Events();
            var table = Parse(SessionCsvExporter.BuildEventsCsv(events));

            CollectionAssert.AreEqual(SessionCsvExporter.EventColumns, table[0]);
            Assert.AreEqual(events.Count + 1, table.Count);
            Assert.AreEqual("session_started", Cell(table, 1, "event"));
            Assert.AreEqual("S-1", Cell(table, 1, "session_id"));
            var summaryRow = Enumerable.Range(1, table.Count - 1).First(i => Cell(table, i, "event") == "trial_summary");
            StringAssert.StartsWith("{", Cell(table, summaryRow, "data"));
        }

        [Test]
        public void ExportingDoesNotChangeTheEvents_OrTheSummary()
        {
            var f = SessionFixture.FullSession();
            var before = f.Text.ToString();
            var summary = Aggregate(f);
            var rowsBefore = summary.Records.Count;

            SessionCsvExporter.BuildSessionSummaryCsv(summary);
            SessionCsvExporter.BuildEventsCsv(f.Events());

            Assert.AreEqual(before, f.Text.ToString());
            Assert.AreEqual(rowsBefore, summary.Records.Count);
        }
    }
}
