using System;

namespace PlayCT.Tasks.Cubo
{
    /// <summary>What the task needs from the visual cube. The view only shows states; it holds no research logic.</summary>
    public interface ICuboView
    {
        bool IsAnimating { get; }
        void SetVisible(bool visible);

        /// <summary>Shows the state immediately, cancelling any turn animation in progress.</summary>
        void Show(CubeState state);

        /// <summary>Animates one exact quarter turn, then shows <paramref name="result"/> and calls <paramref name="done"/>.</summary>
        void AnimateTurn(CubeMove move, CubeState result, Action done);
    }
}
