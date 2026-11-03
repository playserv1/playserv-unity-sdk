using System;
using Playserv.Wrapper;
namespace Playserv.Editor
{
    internal sealed class DeploymentTarget : IEquatable<DeploymentTarget>
    {
        internal readonly string Config, Environment, Api, Key;
        internal DeploymentTarget(string config, string environment, string api, string key)
        { Config = config; Environment = environment; Api = api; Key = key; }
        internal static DeploymentTarget Read(PlayServConfig config)
        {
            PlayServDeploymentSettings.MigrateDashboard(config);
            PlayServDeploymentSettings.MigrateLegacyKey(config);
            var environment = PlayServEnvironmentClientTokens.ActiveEnvironment;
            return new DeploymentTarget(PlayServDeploymentSettings.ConfigId(config), environment,
                config == null ? "" : (config.DashboardAddress ?? "").Trim().TrimEnd('/'),
                PlatformFunctionEditorStore.ResolveKey(PlayServDeploymentSettings.GetLocal(config, environment)));
        }
        internal bool CanConnect => Config.Length > 0 && Api.Length > 0 && Key.Length > 0;
        internal PlatformFunctionClient CreateClient(System.Net.Http.HttpClient http = null) =>
            new PlatformFunctionClient(Api, Key, http, expectedEnvironment: Environment);
        public bool Equals(DeploymentTarget other) => other != null && Config == other.Config && Environment == other.Environment && Api == other.Api && Key == other.Key;
    }
}
