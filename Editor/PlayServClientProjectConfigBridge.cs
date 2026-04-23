#if UNITY_EDITOR
using System;
using System.Reflection;

namespace Playserv.Editor
{
    internal static class PlayServClientProjectConfigBridge
    {
        private const string BridgeTypeName = "Playserv.ClientEditor.PlayServProjectSettingsBridge";
        private const string DrawProjectConfigUiMethodName = "DrawProjectConfigUi";

        public static bool TryDraw(out bool changed)
        {
            changed = false;

            MethodInfo drawMethod;
            if (!TryGetDrawMethod(out drawMethod))
                return false;

            try
            {
                var args = new object[] { false };
                var result = drawMethod.Invoke(null, args);
                changed = args[0] is bool hasChanged && hasChanged;
                return result is bool drawn && drawn;
            }
            catch
            {
                changed = false;
                return false;
            }
        }

        private static bool TryGetDrawMethod(out MethodInfo drawMethod)
        {
            drawMethod = null;

            var bridgeType = FindBridgeType();
            if (bridgeType == null)
                return false;

            drawMethod = bridgeType.GetMethod(
                DrawProjectConfigUiMethodName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            return drawMethod != null;
        }

        private static Type FindBridgeType()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(BridgeTypeName, throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
        }
    }
}
#endif
