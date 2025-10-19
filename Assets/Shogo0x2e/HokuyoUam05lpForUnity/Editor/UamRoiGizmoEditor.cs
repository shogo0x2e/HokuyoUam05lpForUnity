#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Shogo0x2e.HokuyoUam05lpForUnity.Editor
{
    [CustomEditor(typeof(UamRoiGizmo))]
    public sealed class UamRoiGizmoEditor : UnityEditor.Editor
    {
        private void OnSceneGUI()
        {
            // TODO: implement ROI handles
        }
    }
}
#endif
