using System;
using System.Collections.Generic;

namespace PlayCT.Tasks.Cubo
{
    /// <summary>The six faces of the cube. The Front face is the one towards the participant (outward normal -z).</summary>
    public enum CubeFace
    {
        Up,
        Down,
        Front,
        Back,
        Left,
        Right,
    }

    /// <summary>Turn direction as seen looking at the face from outside the cube.</summary>
    public enum CubeTurn
    {
        Clockwise,
        CounterClockwise,
    }

    public static class CubeFaces
    {
        public static readonly CubeFace[] All = { CubeFace.Up, CubeFace.Down, CubeFace.Front, CubeFace.Back, CubeFace.Left, CubeFace.Right };

        public static char Letter(CubeFace face)
        {
            switch (face)
            {
                case CubeFace.Up: return 'U';
                case CubeFace.Down: return 'D';
                case CubeFace.Front: return 'F';
                case CubeFace.Back: return 'B';
                case CubeFace.Left: return 'L';
                default: return 'R';
            }
        }

        public static bool TryFromLetter(char letter, out CubeFace face)
        {
            switch (char.ToUpperInvariant(letter))
            {
                case 'U': face = CubeFace.Up; return true;
                case 'D': face = CubeFace.Down; return true;
                case 'F': face = CubeFace.Front; return true;
                case 'B': face = CubeFace.Back; return true;
                case 'L': face = CubeFace.Left; return true;
                case 'R': face = CubeFace.Right; return true;
                default: face = CubeFace.Up; return false;
            }
        }

        /// <summary>Outward normal as integers. x is right, y is up, z points away from the participant.</summary>
        public static (int x, int y, int z) Normal(CubeFace face)
        {
            switch (face)
            {
                case CubeFace.Up: return (0, 1, 0);
                case CubeFace.Down: return (0, -1, 0);
                case CubeFace.Front: return (0, 0, -1);
                case CubeFace.Back: return (0, 0, 1);
                case CubeFace.Left: return (-1, 0, 0);
                default: return (1, 0, 0);
            }
        }

        public static char Axis(CubeFace face)
        {
            var (x, y, _) = Normal(face);
            return x != 0 ? 'x' : y != 0 ? 'y' : 'z';
        }

        public static int Sign(CubeFace face)
        {
            var (x, y, z) = Normal(face);
            return x + y + z;
        }

        public static CubeFace Opposite(CubeFace face)
        {
            switch (face)
            {
                case CubeFace.Up: return CubeFace.Down;
                case CubeFace.Down: return CubeFace.Up;
                case CubeFace.Front: return CubeFace.Back;
                case CubeFace.Back: return CubeFace.Front;
                case CubeFace.Left: return CubeFace.Right;
                default: return CubeFace.Left;
            }
        }
    }

    /// <summary>
    /// One quarter turn (exactly 90 degrees) of the outer layer of a face. Only face layers can be turned,
    /// so the axis is always the axis of the selected face; there is no free rotation.
    /// </summary>
    public readonly struct CubeMove : IEquatable<CubeMove>
    {
        public const int AmountDegrees = 90;

        public CubeFace Face { get; }
        public CubeTurn Turn { get; }

        public CubeMove(CubeFace face, CubeTurn turn)
        {
            Face = face;
            Turn = turn;
        }

        public char Axis => CubeFaces.Axis(Face);
        public string DirectionLabel => Turn == CubeTurn.Clockwise ? "clockwise" : "counterclockwise";
        public string Notation => CubeFaces.Letter(Face) + (Turn == CubeTurn.CounterClockwise ? "'" : "");
        public CubeMove Inverse => new CubeMove(Face, Turn == CubeTurn.Clockwise ? CubeTurn.CounterClockwise : CubeTurn.Clockwise);

        public bool Equals(CubeMove other) => Face == other.Face && Turn == other.Turn;
        public override bool Equals(object obj) => obj is CubeMove other && Equals(other);
        public override int GetHashCode() => ((int)Face * 2) + (int)Turn;
        public override string ToString() => Notation;

        public static CubeMove[] AllMoves()
        {
            var moves = new List<CubeMove>();
            foreach (var face in CubeFaces.All)
            {
                moves.Add(new CubeMove(face, CubeTurn.Clockwise));
                moves.Add(new CubeMove(face, CubeTurn.CounterClockwise));
            }
            return moves.ToArray();
        }
    }

    /// <summary>Move lists as text: "R U' F" (letter = clockwise, apostrophe = counterclockwise, no half turns).</summary>
    public static class CubeNotation
    {
        public static List<CubeMove> Parse(string text)
        {
            var moves = new List<CubeMove>();
            if (string.IsNullOrWhiteSpace(text)) return moves;

            foreach (var token in text.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length < 1 || token.Length > 2 || !CubeFaces.TryFromLetter(token[0], out var face))
                    throw new FormatException($"'{token}' is not a quarter-turn move (use U D F B L R, with ' for counterclockwise).");
                var turn = CubeTurn.Clockwise;
                if (token.Length == 2)
                {
                    if (token[1] != '\'') throw new FormatException($"'{token}' is not a quarter-turn move (half turns are not supported).");
                    turn = CubeTurn.CounterClockwise;
                }
                moves.Add(new CubeMove(face, turn));
            }
            return moves;
        }

        public static string Format(IEnumerable<CubeMove> moves)
        {
            var parts = new List<string>();
            foreach (var move in moves) parts.Add(move.Notation);
            return string.Join(" ", parts);
        }
    }
}
