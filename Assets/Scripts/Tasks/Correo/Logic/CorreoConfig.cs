using System;
using System.Collections.Generic;

namespace PlayCT.Tasks.Correo
{
    /// <summary>
    /// Everything that defines one trial. The network and the packages are fixed by the experiment and are not part of the
    /// configuration; only the duration can be set.
    /// </summary>
    public sealed class CorreoTrialConfig
    {
        public CorreoNetwork Network { get; set; } = CorreoNetwork.Standard;

        /// <summary>Maximum trial duration in seconds; 0 means no limit. The participant is never shown a timer.</summary>
        public double DurationSeconds { get; set; }

        public void Validate()
        {
            if (Network == null) throw new InvalidOperationException("A trial needs a network.");
            if (DurationSeconds < 0) throw new InvalidOperationException("Duration cannot be negative.");
        }
    }

    /// <summary>Text form of a trial for the inspector or a config file.</summary>
    [Serializable]
    public class CorreoTrialSpec
    {
        public float durationSeconds;

        public CorreoTrialConfig ToConfig()
        {
            var config = new CorreoTrialConfig { DurationSeconds = durationSeconds };
            config.Validate();
            return config;
        }
    }

    public static class CorreoProtocol
    {
        /// <summary>One trial of the near-transfer task with the standard network.</summary>
        public static IReadOnlyList<CorreoTrialSpec> DefaultSpecs() => new[] { new CorreoTrialSpec { durationSeconds = 600 } };

        public static List<CorreoTrialConfig> Default()
        {
            var configs = new List<CorreoTrialConfig>();
            foreach (var spec in DefaultSpecs()) configs.Add(spec.ToConfig());
            return configs;
        }
    }
}
