using System;
using System.Collections.Generic;
using System.Text;

namespace PlayCT.Tasks.Hanoi
{
    public static class HanoiPegs
    {
        public const string Origen = "Origen";
        public const string Apoyo = "Apoyo";
        public const string Destino = "Destino";

        public static readonly string[] All = { Origen, Apoyo, Destino };

        public static int IndexOf(string pegName)
        {
            for (var i = 0; i < All.Length; i++)
                if (All[i] == pegName) return i;
            return -1;
        }
    }

    public enum Placement
    {
        Legal,
        SamePeg,
        LargerOnSmaller,
        DiskNotAccessible,
        UnknownDisk,
        UnknownPeg,
    }

    /// <summary>
    /// Pure rules of the Tower of Hanoi. Disks are identified by their size: 1 is the smallest.
    /// Each peg is a stack listed from the bottom to the top.
    /// </summary>
    public sealed class HanoiState
    {
        public const int MinDisks = 3;
        public const int MaxDisks = 5;

        readonly List<int>[] pegs =
        {
            new List<int>(), new List<int>(), new List<int>()
        };

        public int DiskCount { get; private set; }

        public HanoiState(int diskCount)
        {
            Reset(diskCount);
        }

        public static int OptimalMoves(int diskCount) => (1 << diskCount) - 1;

        public void Reset(int diskCount)
        {
            if (diskCount < MinDisks || diskCount > MaxDisks)
                throw new ArgumentOutOfRangeException(nameof(diskCount), $"Disk count must be between {MinDisks} and {MaxDisks}.");

            DiskCount = diskCount;
            foreach (var peg in pegs) peg.Clear();
            for (var size = diskCount; size >= 1; size--)
                pegs[0].Add(size);
        }

        public IReadOnlyList<int> Stack(string pegName)
        {
            var index = HanoiPegs.IndexOf(pegName);
            if (index < 0) throw new ArgumentException($"Unknown peg '{pegName}'.", nameof(pegName));
            return pegs[index];
        }

        public string PegOf(int disk)
        {
            for (var i = 0; i < pegs.Length; i++)
                if (pegs[i].Contains(disk)) return HanoiPegs.All[i];
            return null;
        }

        public int SlotIndexOf(int disk)
        {
            for (var i = 0; i < pegs.Length; i++)
            {
                var slot = pegs[i].IndexOf(disk);
                if (slot >= 0) return slot;
            }
            return -1;
        }

        public int TopDisk(string pegName)
        {
            var stack = Stack(pegName);
            return stack.Count == 0 ? 0 : stack[stack.Count - 1];
        }

        public bool IsTop(int disk)
        {
            var peg = PegOf(disk);
            return peg != null && TopDisk(peg) == disk;
        }

        public Placement Evaluate(int disk, string targetPeg)
        {
            var source = PegOf(disk);
            if (source == null) return Placement.UnknownDisk;
            if (HanoiPegs.IndexOf(targetPeg) < 0) return Placement.UnknownPeg;
            if (TopDisk(source) != disk) return Placement.DiskNotAccessible;
            if (source == targetPeg) return Placement.SamePeg;
            var targetTop = TopDisk(targetPeg);
            if (targetTop != 0 && targetTop < disk) return Placement.LargerOnSmaller;
            return Placement.Legal;
        }

        public void Move(int disk, string targetPeg)
        {
            var result = Evaluate(disk, targetPeg);
            if (result != Placement.Legal)
                throw new InvalidOperationException($"Disk {disk} cannot move to {targetPeg}: {result}.");

            pegs[HanoiPegs.IndexOf(PegOf(disk))].Remove(disk);
            pegs[HanoiPegs.IndexOf(targetPeg)].Add(disk);
        }

        public bool IsSolved => pegs[2].Count == DiskCount;

        public string Snapshot()
        {
            var sb = new StringBuilder(64);
            for (var i = 0; i < pegs.Length; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(HanoiPegs.All[i]).Append(":[");
                sb.Append(string.Join(",", pegs[i]));
                sb.Append(']');
            }
            return sb.ToString();
        }
    }
}
