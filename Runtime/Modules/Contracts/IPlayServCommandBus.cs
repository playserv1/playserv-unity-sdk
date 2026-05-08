using System;
using System.Threading.Tasks;
using Playserv.Proxy.Common;

namespace Playserv.Modules
{
    /// <summary>
    /// Narrow command transport surface exposed by core runtime to optional modules.
    /// </summary>
    public interface IPlayServCommandBus
    {
        PlayServState State { get; }

        IDisposable On<T>(Action<T> onNext);

        IDisposable OnCommand(string commandName, Action<object> onNext);

        Task SendAsync<T>(T command, string moduleName = null);
    }
}
