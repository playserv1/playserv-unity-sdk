using System;
using System.Threading.Tasks;

namespace Playserv.Proxy.Interfaces
{
    public interface ITransportImplementation : IDisposable
    {
        Task<bool> Connect();
        Task Send(byte[] data);
        IObservable<byte[]> OnReceive();
    }
}
