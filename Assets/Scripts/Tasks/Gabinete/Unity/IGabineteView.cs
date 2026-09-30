using System;

namespace PlayCT.Tasks.Gabinete
{
    /// <summary>What the task needs from the visual cabinet. The view only shows and moves pieces; it holds no research logic.</summary>
    public interface IGabineteView
    {
        void SetVisible(bool visible);

        /// <summary>Builds the board and the tray for a trial and puts every piece on its tray slot, cancelling anything in progress.</summary>
        void Show(GabineteTrialConfig config);

        /// <summary>Lowers an accepted piece into its opening, locks it there, then calls <paramref name="done"/>.</summary>
        void SeatPiece(string pieceId, int openingIndex, Action done);

        /// <summary>Puts a rejected piece back on its tray slot in its starting orientation.</summary>
        void ReturnPiece(string pieceId);

        /// <summary>Stops all pieces from being picked up (the trial is over).</summary>
        void LockPieces();
    }
}
