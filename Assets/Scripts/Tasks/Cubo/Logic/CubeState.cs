using System;
using System.Collections.Generic;
using System.Text;

namespace PlayCT.Tasks.Cubo
{
    /// <summary>One of the 54 sticker positions: the cubie it sits on (each coordinate -1, 0 or 1) and the face it looks at.</summary>
    public readonly struct StickerSlot
    {
        public int Index { get; }
        public CubeFace Face { get; }
        public int Row { get; }
        public int Col { get; }
        public int X { get; }
        public int Y { get; }
        public int Z { get; }

        public StickerSlot(int index, CubeFace face, int row, int col, int x, int y, int z)
        {
            Index = index;
            Face = face;
            Row = row;
            Col = col;
            X = x;
            Y = y;
            Z = z;
        }

        public (int x, int y, int z) Normal => CubeFaces.Normal(Face);
    }

    /// <summary>
    /// Fixed geometry of the 3x3x3 cube shared by the logic and the visual cube. Stickers are numbered face by face
    /// (Up, Down, Front, Back, Left, Right), each face row by row as seen from outside.
    /// </summary>
    public static class CubeLayout
    {
        public const int StickerCount = 54;

        static readonly StickerSlot[] slots = BuildSlots();
        static readonly Dictionary<(int, int, int, CubeFace), int> indexByPosition = BuildIndex();
        static readonly int[][] permutations = BuildPermutations();

        public static IReadOnlyList<StickerSlot> Slots => slots;

        public static int IndexOf(CubeFace face, int row, int col) => (int)face * 9 + row * 3 + col;

        public static bool OnLayer(StickerSlot slot, CubeFace face)
        {
            switch (CubeFaces.Axis(face))
            {
                case 'x': return slot.X == CubeFaces.Sign(face);
                case 'y': return slot.Y == CubeFaces.Sign(face);
                default: return slot.Z == CubeFaces.Sign(face);
            }
        }

        /// <summary>For each sticker index, where that sticker ends up after the move.</summary>
        public static int[] Permutation(CubeMove move) => permutations[(int)move.Face * 2 + (int)move.Turn];

        static (int x, int y, int z) FacePosition(CubeFace face, int row, int col)
        {
            switch (face)
            {
                case CubeFace.Up: return (col - 1, 1, 1 - row);
                case CubeFace.Down: return (col - 1, -1, row - 1);
                case CubeFace.Front: return (col - 1, 1 - row, -1);
                case CubeFace.Back: return (1 - col, 1 - row, 1);
                case CubeFace.Left: return (-1, 1 - row, 1 - col);
                default: return (1, 1 - row, col - 1);
            }
        }

        static StickerSlot[] BuildSlots()
        {
            var result = new StickerSlot[StickerCount];
            foreach (var face in CubeFaces.All)
            {
                for (var row = 0; row < 3; row++)
                {
                    for (var col = 0; col < 3; col++)
                    {
                        var (x, y, z) = FacePosition(face, row, col);
                        var index = IndexOf(face, row, col);
                        result[index] = new StickerSlot(index, face, row, col, x, y, z);
                    }
                }
            }
            return result;
        }

        static Dictionary<(int, int, int, CubeFace), int> BuildIndex()
        {
            var map = new Dictionary<(int, int, int, CubeFace), int>();
            foreach (var slot in slots) map[(slot.X, slot.Y, slot.Z, slot.Face)] = slot.Index;
            return map;
        }

        // Quarter turn about +axis in Unity's left-handed axes: clockwise when looking at the origin from the +axis side.
        static (int x, int y, int z) RotateAbout(char axis, (int x, int y, int z) v)
        {
            switch (axis)
            {
                case 'x': return (v.x, -v.z, v.y);
                case 'y': return (v.z, v.y, -v.x);
                default: return (-v.y, v.x, v.z);
            }
        }

        static CubeFace FaceOf((int x, int y, int z) normal)
        {
            foreach (var face in CubeFaces.All)
                if (CubeFaces.Normal(face) == normal) return face;
            throw new InvalidOperationException("Not an axis-aligned normal.");
        }

        static int[][] BuildPermutations()
        {
            var result = new int[12][];
            foreach (var move in CubeMove.AllMoves())
            {
                var perm = new int[StickerCount];
                // Clockwise seen from outside is a +90 degree turn about the outward normal, so it is +90 about the
                // positive axis for Up/Right/Back and -90 about it for Down/Left/Front.
                var quarterTurns = (move.Turn == CubeTurn.Clockwise ? 1 : -1) * CubeFaces.Sign(move.Face);
                var steps = ((quarterTurns % 4) + 4) % 4;
                foreach (var slot in slots)
                {
                    if (!OnLayer(slot, move.Face))
                    {
                        perm[slot.Index] = slot.Index;
                        continue;
                    }
                    var position = (slot.X, slot.Y, slot.Z);
                    var normal = slot.Normal;
                    for (var i = 0; i < steps; i++)
                    {
                        position = RotateAbout(move.Axis, position);
                        normal = RotateAbout(move.Axis, normal);
                    }
                    perm[slot.Index] = indexByPosition[(position.Item1, position.Item2, position.Item3, FaceOf(normal))];
                }
                result[(int)move.Face * 2 + (int)move.Turn] = perm;
            }
            return result;
        }
    }

    /// <summary>
    /// Logical arrangement of the cube: the colour of each of the 54 stickers. A colour is named after the face it
    /// starts on (U D F B L R). States are immutable; applying a move returns a new state. Centre stickers never move.
    /// </summary>
    public sealed class CubeState : IEquatable<CubeState>
    {
        const string Letters = "UDFBLR";

        readonly byte[] colors;

        public static CubeState Solved { get; } = CreateSolved();

        CubeState(byte[] colors) => this.colors = colors;

        static CubeState CreateSolved()
        {
            var colors = new byte[CubeLayout.StickerCount];
            for (var i = 0; i < colors.Length; i++) colors[i] = (byte)(i / 9);
            return new CubeState(colors);
        }

        public static CubeState FromSnapshot(string snapshot)
        {
            if (snapshot == null || snapshot.Length != CubeLayout.StickerCount)
                throw new FormatException($"A cube state needs exactly {CubeLayout.StickerCount} sticker letters.");
            var colors = new byte[CubeLayout.StickerCount];
            var counts = new int[6];
            for (var i = 0; i < colors.Length; i++)
            {
                var index = Letters.IndexOf(char.ToUpperInvariant(snapshot[i]));
                if (index < 0) throw new FormatException($"'{snapshot[i]}' is not a sticker colour (use U D F B L R).");
                colors[i] = (byte)index;
                counts[index]++;
            }
            foreach (var count in counts)
                if (count != 9) throw new FormatException("Each colour must appear on exactly nine stickers.");
            return new CubeState(colors);
        }

        public static CubeState FromMoves(IEnumerable<CubeMove> moves) => Solved.Apply(moves);

        public CubeState Apply(CubeMove move)
        {
            var perm = CubeLayout.Permutation(move);
            var next = new byte[colors.Length];
            for (var i = 0; i < colors.Length; i++) next[perm[i]] = colors[i];
            return new CubeState(next);
        }

        public CubeState Apply(IEnumerable<CubeMove> moves)
        {
            var state = this;
            foreach (var move in moves) state = state.Apply(move);
            return state;
        }

        /// <summary>Colour (0-5, in U D F B L R order) of the sticker at this index.</summary>
        public int ColorAt(int stickerIndex) => colors[stickerIndex];

        public CubeFace ColorFace(int stickerIndex) => (CubeFace)colors[stickerIndex];

        public bool IsSolved => Equals(Solved);

        /// <summary>The centre sticker plus the four edge stickers of the face all show the centre's colour.</summary>
        public bool IsCrossOn(CubeFace face)
        {
            var centre = colors[CubeLayout.IndexOf(face, 1, 1)];
            return colors[CubeLayout.IndexOf(face, 0, 1)] == centre
                && colors[CubeLayout.IndexOf(face, 1, 0)] == centre
                && colors[CubeLayout.IndexOf(face, 1, 2)] == centre
                && colors[CubeLayout.IndexOf(face, 2, 1)] == centre;
        }

        /// <summary>True when all nine stickers of the face are the same in both states.</summary>
        public bool FaceEquals(CubeState other, CubeFace face)
        {
            for (var i = 0; i < 9; i++)
            {
                var index = (int)face * 9 + i;
                if (colors[index] != other.colors[index]) return false;
            }
            return true;
        }

        /// <summary>Nine letters of one face, row by row as seen from outside.</summary>
        public string FaceSnapshot(CubeFace face)
        {
            var sb = new StringBuilder(9);
            for (var i = 0; i < 9; i++) sb.Append(Letters[colors[(int)face * 9 + i]]);
            return sb.ToString();
        }

        /// <summary>All 54 stickers as letters, face by face (U D F B L R), row by row. Used in logs and configuration.</summary>
        public string Snapshot()
        {
            var sb = new StringBuilder(CubeLayout.StickerCount);
            foreach (var color in colors) sb.Append(Letters[color]);
            return sb.ToString();
        }

        public bool Equals(CubeState other)
        {
            if (other == null) return false;
            for (var i = 0; i < colors.Length; i++)
                if (colors[i] != other.colors[i]) return false;
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as CubeState);

        public override int GetHashCode()
        {
            var hash = 17;
            foreach (var color in colors) hash = hash * 31 + color;
            return hash;
        }

        public override string ToString() => Snapshot();
    }
}
