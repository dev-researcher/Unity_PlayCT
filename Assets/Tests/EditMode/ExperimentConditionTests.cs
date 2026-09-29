using NUnit.Framework;
using PlayCT.Research;

namespace PlayCT.Tests
{
    public class ExperimentConditionTests
    {
        [TestCase("Static", ExperimentCondition.Static)]
        [TestCase("static", ExperimentCondition.Static)]
        [TestCase("PreAdapted", ExperimentCondition.PreAdapted)]
        [TestCase("pre-adapted", ExperimentCondition.PreAdapted)]
        [TestCase("Pre_Adapted", ExperimentCondition.PreAdapted)]
        [TestCase(" pre adapted ", ExperimentCondition.PreAdapted)]
        public void TryParse_AcceptsSupportedConditions(string text, ExperimentCondition expected)
        {
            Assert.IsTrue(ExperimentConditions.TryParse(text, out var condition));
            Assert.AreEqual(expected, condition);
        }

        [Test]
        public void TryParse_TreatsLegacyBaselineAsStatic()
        {
            Assert.IsTrue(ExperimentConditions.TryParse("baseline", out var condition));
            Assert.AreEqual(ExperimentCondition.Static, condition);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        [TestCase("condition-B")]
        [TestCase("adaptive")]
        public void TryParse_RejectsAnythingElse(string text)
        {
            Assert.IsFalse(ExperimentConditions.TryParse(text, out _));
        }

        [Test]
        public void Label_RoundTripsThroughTryParse()
        {
            foreach (ExperimentCondition condition in System.Enum.GetValues(typeof(ExperimentCondition)))
            {
                Assert.IsTrue(ExperimentConditions.TryParse(ExperimentConditions.ToLabel(condition), out var parsed));
                Assert.AreEqual(condition, parsed);
            }
        }
    }
}
