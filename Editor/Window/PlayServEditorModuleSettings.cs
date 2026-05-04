#if UNITY_EDITOR
using System;
using UnityEditor;

namespace Playserv.Editor
{
    internal sealed class PlayServEditorModuleSettings
    {
        private const bool DefaultOptionalModuleState = true;

        public bool Deployment { get; private set; } = DefaultOptionalModuleState;
        public bool ModelSync { get; private set; } = DefaultOptionalModuleState;
        public bool Events { get; private set; } = DefaultOptionalModuleState;
        public bool Codegen { get; private set; } = DefaultOptionalModuleState;

        public void Load()
        {
            Deployment = EditorPrefs.GetBool(Const.PrefModuleDeployment, DefaultOptionalModuleState);
            ModelSync = EditorPrefs.GetBool(Const.PrefModuleModelSync, DefaultOptionalModuleState);
            Events = EditorPrefs.GetBool(Const.PrefModuleEvents, DefaultOptionalModuleState);
            Codegen = EditorPrefs.GetBool(Const.PrefModuleCodegen, DefaultOptionalModuleState);
        }

        public bool SetDeployment(bool enabled) => Set(Const.PrefModuleDeployment, Deployment, enabled, value => Deployment = value);

        public bool SetModelSync(bool enabled) => Set(Const.PrefModuleModelSync, ModelSync, enabled, value => ModelSync = value);

        public bool SetEvents(bool enabled) => Set(Const.PrefModuleEvents, Events, enabled, value => Events = value);

        public bool SetCodegen(bool enabled) => Set(Const.PrefModuleCodegen, Codegen, enabled, value => Codegen = value);

        public void ResetToDefaults()
        {
            SetDeployment(DefaultOptionalModuleState);
            SetModelSync(DefaultOptionalModuleState);
            SetEvents(DefaultOptionalModuleState);
            SetCodegen(DefaultOptionalModuleState);
        }

        private static bool Set(string key, bool current, bool enabled, Action<bool> assign)
        {
            if (current == enabled)
                return false;

            assign(enabled);
            EditorPrefs.SetBool(key, enabled);
            return true;
        }
    }
}
#endif
