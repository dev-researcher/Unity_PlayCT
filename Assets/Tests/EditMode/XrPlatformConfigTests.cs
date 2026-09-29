using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace PlayCT.Tests
{
    /// <summary>
    /// Checks the text of the XR asset files: Quest 3 (Android) is still configured as before and a Standalone (PC / Quest Link)
    /// target has been added next to it. These are file checks only; they do not load the assets in Unity.
    /// </summary>
    public class XrPlatformConfigTests
    {
        const int AndroidGroup = 7;
        const int StandaloneGroup = 1;
        const string OpenXrLoaderGuid = "17afc41331b61634396db4295dfabe3b";

        static string ProjectRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Packages", "manifest.json"))) dir = dir.Parent;
            if (dir == null) dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            return dir.FullName;
        }

        static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { ProjectRoot() }.Concat(parts).ToArray()));

        static string GeneralSettings() => Read("Assets", "XR", "XRGeneralSettingsPerBuildTarget.asset");

        static string OpenXrSettings() => Read("Assets", "XR", "Settings", "OpenXRPackageSettings.asset");

        static int[] Keys(string yaml)
        {
            var hex = Regex.Match(yaml, @"^  Keys: ([0-9a-f]*)$", RegexOptions.Multiline).Groups[1].Value;
            return Enumerable.Range(0, hex.Length / 8)
                .Select(i => BitConverter.ToInt32(Enumerable.Range(0, 4).Select(b => Convert.ToByte(hex.Substring(i * 8 + b * 2, 2), 16)).ToArray(), 0))
                .ToArray();
        }

        static int ValueCount(string yaml)
        {
            var section = Regex.Match(yaml, @"^  Values:\n((?:  - \{fileID: -?\d+\}\n)+)", RegexOptions.Multiline).Groups[1].Value;
            return section.Split('\n').Count(l => l.StartsWith("  - "));
        }

        static string Document(string yaml, string name) =>
            Regex.Split(yaml, @"(?=^--- !u!)", RegexOptions.Multiline).Single(d => Regex.IsMatch(d, @"^  m_Name: " + Regex.Escape(name) + @"\s*$", RegexOptions.Multiline));

        [Test]
        public void GeneralSettings_KeepAndroidAndAddStandalone()
        {
            var keys = Keys(GeneralSettings());
            CollectionAssert.AreEquivalent(new[] { AndroidGroup, StandaloneGroup }, keys);
            Assert.AreEqual(keys.Length, ValueCount(GeneralSettings()));
        }

        [Test]
        public void BothTargets_InitializeXrOnStartWithTheSameOpenXrLoader()
        {
            var yaml = GeneralSettings();
            foreach (var target in new[] { "Android", "Standalone" })
            {
                StringAssert.Contains("m_InitManagerOnStart: 1", Document(yaml, target + " Settings"));
                StringAssert.Contains(OpenXrLoaderGuid, Document(yaml, target + " Providers"));
            }
        }

        [Test]
        public void AndroidProviders_AreTheOriginalOnes()
        {
            var providers = Document(GeneralSettings(), "Android Providers");
            StringAssert.Contains("m_AutomaticLoading: 0", providers);
            Assert.AreEqual(1, Regex.Matches(providers, @"guid: " + OpenXrLoaderGuid).Count);
        }

        [Test]
        public void OpenXrSettings_KeepAndroidAndAddStandalone()
        {
            var yaml = OpenXrSettings();
            var keys = Keys(yaml);
            CollectionAssert.AreEquivalent(new[] { AndroidGroup, StandaloneGroup }, keys);
            Assert.AreEqual(keys.Length, ValueCount(yaml));
        }

        [Test]
        public void QuestFeatures_StayEnabledForAndroid()
        {
            var yaml = OpenXrSettings();
            StringAssert.Contains("m_enabled: 1", Document(yaml, "MetaQuestFeature Android"));
            StringAssert.Contains("m_enabled: 1", Document(yaml, "OculusTouchControllerProfile Android"));
            StringAssert.Contains("m_enabled: 1", Document(yaml, "MetaQuestTouchPlusControllerProfile Android"));
            Assert.AreEqual(18, Regex.Matches(Document(yaml, "Android"), @"^  - \{fileID: -?\d+\}$", RegexOptions.Multiline).Count, "the Android feature list is unchanged");
        }

        [Test]
        public void Standalone_HasTheTouchControllerProfilesEnabled_AndNoQuestOnlyFeature()
        {
            var yaml = OpenXrSettings();
            StringAssert.Contains("m_enabled: 1", Document(yaml, "OculusTouchControllerProfile Standalone"));
            StringAssert.Contains("m_enabled: 1", Document(yaml, "MetaQuestTouchPlusControllerProfile Standalone"));
            Assert.IsFalse(Regex.IsMatch(yaml, @"m_Name: MetaQuestFeature Standalone"), "MetaQuestFeature is Android only");
        }

        [Test]
        public void EveryLocalReference_PointsToADocumentInTheSameFile()
        {
            foreach (var yaml in new[] { GeneralSettings(), OpenXrSettings() })
            {
                var ids = Regex.Matches(yaml, @"^--- !u!\d+ &(-?\d+)$", RegexOptions.Multiline).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
                CollectionAssert.AllItemsAreUnique(ids);
                var refs = Regex.Matches(yaml, @"\{fileID: (-?\d+)\}").Cast<Match>().Select(m => m.Groups[1].Value).Where(v => v != "0");
                foreach (var id in refs) CollectionAssert.Contains(ids, id);
            }
        }

        [Test]
        public void ProjectStillTargetsQuest3ThroughOpenXr()
        {
            var manifest = Read("Packages", "manifest.json");
            StringAssert.Contains("\"com.unity.xr.openxr\"", manifest);
            StringAssert.Contains("\"com.unity.xr.management\"", manifest);
            StringAssert.Contains("\"com.unity.xr.interaction.toolkit\"", manifest);
            StringAssert.Contains("\"com.unity.inputsystem\"", manifest);

            var settings = Read("ProjectSettings", "ProjectSettings.asset");
            StringAssert.Contains("- m_BuildTarget: AndroidPlayer\n    m_APIs: 15000000", settings, "Vulkan only on Android");
            StringAssert.Contains("activeInputHandler: 1", settings);

            var build = Read("ProjectSettings", "EditorBuildSettings.asset");
            StringAssert.Contains("com.unity.xr.management.loader_settings", build);
            StringAssert.Contains("com.unity.xr.openxr.settings4", build);
        }
    }
}
