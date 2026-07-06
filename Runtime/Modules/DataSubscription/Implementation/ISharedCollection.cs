using System;
using System.Collections.Generic;
using Playserv.DataSubscription.Exceptions;

namespace Playserv.DataSubscription
{
    /// <summary>
    /// A live, push-based view over a whole (optionally filtered) table — the client side of a
    /// collection subscription (conventions §21.2). Subscribe ONCE via
    /// <c>PlayServData.SelectCollection</c>; <see cref="Items"/> is replaced and
    /// <see cref="Changed"/> raised on every server push (initial snapshot + on any row change).
    /// There is no polling and no client-side write — the collection is server-maintained.
    /// </summary>
    /// <typeparam name="TItem">Row model the collection rows deserialize to.</typeparam>
    public interface ISharedCollection<TItem>
    {
        /// <summary>Current rows. Replaced wholesale on each server push (Overwrite semantics).</summary>
        IReadOnlyList<TItem> Items { get; }

        /// <summary>Raised after <see cref="Items"/> is replaced by a fresh server push.</summary>
        event Action<IReadOnlyList<TItem>> Changed;

        /// <summary>Raised on a subscription error frame from the server.</summary>
        event Action<DataSubscriptionException> Error;

        /// <summary>Raised when the subscription is terminated by the server.</summary>
        event Action Terminated;
    }
}
