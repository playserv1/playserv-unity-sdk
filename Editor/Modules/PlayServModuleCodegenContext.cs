using System;
using System.Collections.Generic;

namespace Playserv.Editor
{
    public sealed class PlayServModuleCodegenContext
    {
        private readonly PlayServRuntimeModuleState _runtimeState;
        private readonly HashSet<string> _compatibilityUsings = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string[]> _facadeMembers = new List<string[]>();
        private readonly List<PlayServGeneratedRuntimeRegistration> _runtimeRegistrations =
            new List<PlayServGeneratedRuntimeRegistration>();

        private string _activeModuleId;
        private string _localExecutionFactoryExpression;
        private int _localExecutionFactoryPriority = int.MinValue;

        internal PlayServModuleCodegenContext(PlayServRuntimeModuleState runtimeState)
        {
            _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
        }

        internal IReadOnlyCollection<string> CompatibilityUsings => _compatibilityUsings;

        internal IReadOnlyList<string[]> FacadeMembers => _facadeMembers;

        internal IReadOnlyList<PlayServGeneratedRuntimeRegistration> RuntimeRegistrations => _runtimeRegistrations;

        internal bool HasLocalExecutionFactory => !string.IsNullOrWhiteSpace(_localExecutionFactoryExpression);

        internal string LocalExecutionFactoryExpression => _localExecutionFactoryExpression ?? string.Empty;

        public bool IsModuleEnabled(string moduleId)
        {
            return _runtimeState.IsEnabled(moduleId);
        }

        public void RegisterRuntimeModule(string runtimeModuleTypeName)
        {
            EnsureActiveContributor();
            if (string.IsNullOrWhiteSpace(runtimeModuleTypeName))
                throw new ArgumentException("Runtime module type name is required.", nameof(runtimeModuleTypeName));

            for (var i = 0; i < _runtimeRegistrations.Count; i++)
            {
                if (string.Equals(_runtimeRegistrations[i].ModuleId, _activeModuleId, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Runtime module factory is already registered: {_activeModuleId}");
            }

            _runtimeRegistrations.Add(new PlayServGeneratedRuntimeRegistration(
                _activeModuleId,
                runtimeModuleTypeName.Trim()));
        }

        public void AddCompatibilityUsing(string namespaceName)
        {
            EnsureActiveContributor();
            if (string.IsNullOrWhiteSpace(namespaceName))
                throw new ArgumentException("Compatibility namespace is required.", nameof(namespaceName));

            _compatibilityUsings.Add(namespaceName.Trim());
        }

        public void AddFacadeMember(params string[] lines)
        {
            EnsureActiveContributor();
            if (lines == null || lines.Length == 0)
                return;

            _facadeMembers.Add((string[])lines.Clone());
        }

        public void SetLocalExecutionFactory(string factoryExpression, int priority = 0)
        {
            EnsureActiveContributor();
            if (string.IsNullOrWhiteSpace(factoryExpression))
                throw new ArgumentException("Local execution factory expression is required.", nameof(factoryExpression));

            var normalizedExpression = factoryExpression.Trim();
            if (priority < _localExecutionFactoryPriority)
                return;

            if (priority == _localExecutionFactoryPriority &&
                !string.IsNullOrEmpty(_localExecutionFactoryExpression) &&
                !string.Equals(_localExecutionFactoryExpression, normalizedExpression, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Multiple local execution factories use priority {priority}: " +
                    $"{_localExecutionFactoryExpression} and {normalizedExpression}");
            }

            _localExecutionFactoryExpression = normalizedExpression;
            _localExecutionFactoryPriority = priority;
        }

        internal void BeginContribution(string moduleId)
        {
            if (!string.IsNullOrEmpty(_activeModuleId))
                throw new InvalidOperationException($"Module codegen contribution is already active: {_activeModuleId}");

            _activeModuleId = moduleId;
        }

        internal void EndContribution()
        {
            _activeModuleId = null;
        }

        private void EnsureActiveContributor()
        {
            if (string.IsNullOrEmpty(_activeModuleId))
                throw new InvalidOperationException("Module codegen APIs can only be used from an active contributor.");
        }
    }

    internal sealed class PlayServGeneratedRuntimeRegistration
    {
        public PlayServGeneratedRuntimeRegistration(string moduleId, string runtimeModuleTypeName)
        {
            ModuleId = moduleId;
            RuntimeModuleTypeName = runtimeModuleTypeName;
        }

        public string ModuleId { get; }

        public string RuntimeModuleTypeName { get; }
    }
}
