using UnityEngine;

namespace PlayCT.Tasks.Hanoi
{
    /// <summary>
    /// A vertical peg. The transform sits at the centre of the peg's base, on the surface of the board.
    /// </summary>
    public class HanoiPeg : MonoBehaviour
    {
        [SerializeField] string pegName = HanoiPegs.Origen;
        [SerializeField] float height = 0.16f;

        public string PegName => pegName;
        public float Height => height;
        public Vector3 BasePoint => transform.position;
        public Vector3 TopPoint => transform.position + Vector3.up * height;

        public void Configure(string newName, float newHeight)
        {
            pegName = newName;
            height = newHeight;
        }

        /// <summary>World position of the centre of a disk resting in the given stack slot (0 = bottom).</summary>
        public Vector3 SlotPosition(int slot, float diskThickness)
        {
            return transform.position + Vector3.up * (diskThickness * (slot + 0.5f));
        }

        /// <summary>Horizontal distance from the peg axis to a world point.</summary>
        public float HorizontalDistance(Vector3 worldPoint)
        {
            var d = worldPoint - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(BasePoint, TopPoint);
        }
    }
}
