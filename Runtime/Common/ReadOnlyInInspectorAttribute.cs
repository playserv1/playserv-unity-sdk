#if UNITY_5_3_OR_NEWER
using UnityEngine;

namespace Playserv
{
    /// <summary>
    /// Marker attribute for read-only inspector rendering.
    /// </summary>
    public sealed class ReadOnlyInInspectorAttribute : PropertyAttribute
    {
    }
}
#endif
