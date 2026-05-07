using System;
using System.Reflection;

namespace Playserv.Editor
{
    internal sealed class PlayServOptionalEditorSection : IDisposable
    {
        private readonly string _typeName;
        private object _instance;
        private MethodInfo _drawMethod;
        private MethodInfo _disposeMethod;

        public PlayServOptionalEditorSection(string typeName)
        {
            _typeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        }

        public bool IsAvailable => ResolveType() != null;

        public bool Draw(PlayServWindowContext context)
        {
            var instance = ResolveInstance();
            if (instance == null || _drawMethod == null)
                return false;

            _drawMethod.Invoke(instance, new object[] { context });
            return true;
        }

        public void Dispose()
        {
            _disposeMethod?.Invoke(_instance, null);
            _instance = null;
            _drawMethod = null;
            _disposeMethod = null;
        }

        private object ResolveInstance()
        {
            if (_instance != null)
                return _instance;

            var type = ResolveType();
            if (type == null)
                return null;

            _instance = Activator.CreateInstance(type, nonPublic: true);
            _drawMethod = type.GetMethod(
                "Draw",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(PlayServWindowContext) },
                null);
            _disposeMethod = type.GetMethod(
                "Dispose",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            return _instance;
        }

        private Type ResolveType()
        {
            return Type.GetType(_typeName, throwOnError: false);
        }
    }
}
