using UnityEngine;

namespace Shogo0x2e.HokuyoUam05lpForUnity
{
    [ExecuteAlways]
    public sealed class UamRoiGizmo : MonoBehaviour
    {
        public Color OutlineColor = new(0f, 0.75f, 1f, 0.9f);

        private void OnDrawGizmos()
        {
            Gizmos.color = OutlineColor;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        }
    }
}
