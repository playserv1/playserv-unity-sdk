using System;
using System.Threading;
using Playserv.Identity;

namespace Playserv.SteamAuth
{
    /// <summary>A short-lived Steam Web API ticket. Dispose it after backend verification.</summary>
    public sealed class PlayServSteamCredential : IDisposable
    {
        private Action _release;

        internal PlayServSteamCredential(string ticket, Action release)
        {
            if (string.IsNullOrWhiteSpace(ticket))
                throw new ArgumentException("Steam ticket is required.", nameof(ticket));

            Ticket = ticket;
            _release = release;
        }

        public string Ticket { get; }

        public bool IsReleased => _release == null;

        public bool TryCreateBackendProof(out PlayServExternalIdentityProof proof)
        {
            if (IsReleased || string.IsNullOrWhiteSpace(Ticket))
            {
                proof = null;
                return false;
            }

            proof = PlayServExternalIdentityProof.FromSteamTicket(Ticket);
            return true;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _release, null)?.Invoke();
        }
    }
}
