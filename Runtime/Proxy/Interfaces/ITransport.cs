using System;
using System.Threading.Tasks;

namespace Playserv.Proxy.Interfaces
{
    public interface ITransport : IDisposable
    {
        Task<bool> Connect();
        Task Send<T>(T command, string moduleName = null);
        IObservable<T> OnReceive<T>();
        IObservable<object> OnReceive(string commandName);
    }
}
