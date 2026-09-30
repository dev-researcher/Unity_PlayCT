using System;
using System.Collections.Generic;

namespace PlayCT.Tasks.Correo
{
    /// <summary>What the task needs from the visual postal network. The view only shows state and animates moves; it holds no research logic.</summary>
    public interface ICorreoView
    {
        bool IsAnimating { get; }

        void SetVisible(bool visible);

        /// <summary>Shows the network with every package at the location given by the state, cancelling any animation in progress.</summary>
        void Show(CorreoTrialConfig config, CorreoState state);

        /// <summary>Shows the participant's pending choice: nothing more is displayed about it.</summary>
        void ShowSelection(Settlement? source, Settlement? destination, IReadOnlyList<CorreoPackage> packages);

        /// <summary>Carries the shipment's packages together to the destination, then shows <paramref name="result"/> and calls <paramref name="done"/>.</summary>
        void AnimateShipment(CorreoShipment shipment, CorreoState result, Action done);

        /// <summary>A refused shipment: the packages stay where they are; only a brief neutral movement shows that the action was received.</summary>
        void ShowNotShipped(CorreoShipment shipment);
    }
}
