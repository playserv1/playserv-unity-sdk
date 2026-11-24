using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Commerce;
using Playserv.DataSubscription;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.Samples
{
    /// <summary>Read the current player's purchases and observe full-list updates without polling.</summary>
    public sealed class PlayServPurchasedItemsSample : MonoBehaviour
    {
        private CancellationTokenSource _lifetime;
        private ISharedCollection<PlayServPurchasedItem> _subscription;
        private PlayServPurchasedItem[] _items = Array.Empty<PlayServPurchasedItem>();
        private string _status = "Connect the SDK with the same player account used by the web shop.";
        private bool _busy;
        private int _generation;
        private Vector2 _scroll;

        private void OnEnable() => _lifetime = new CancellationTokenSource();
        private void OnDisable()
        {
            ++_generation;
            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _lifetime = null;
            StopSubscription();
            _items = Array.Empty<PlayServPurchasedItem>();
            _busy = false;
        }

        private void StopSubscription()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        private async Task RunAsync(bool subscribe, bool connect = false)
        {
            if (_busy || _lifetime == null) return;
            _busy = true;
            var generation = ++_generation;
            var ct = _lifetime.Token;
            StopSubscription();
            try
            {
                if (connect)
                {
                    var connected = await PlayServ.Connect();
                    if (ct.IsCancellationRequested || generation != _generation) return;
                    _status = connected ? "Connected. Read once or subscribe to purchased items." : "Connection failed.";
                    return;
                }
                if (subscribe)
                {
                    var handle = await PlayServData.SubscribePurchasedItemsAsync(ct);
                    if (ct.IsCancellationRequested || generation != _generation) { handle.Dispose(); return; }
                    _subscription = handle;
                    handle.Changed += items =>
                    {
                        if (generation != _generation || ct.IsCancellationRequested) return;
                        Show(items);
                    };
                    handle.Failure += error =>
                    {
                        if (generation == _generation && !ct.IsCancellationRequested) _status = error.Message;
                    };
                    handle.Terminated += () =>
                    {
                        if (generation != _generation || ct.IsCancellationRequested) return;
                        _items = Array.Empty<PlayServPurchasedItem>();
                        _status = "Subscription ended. Check the signed-in player and subscribe again.";
                    };
                    Show(handle.Items);
                }
                else
                {
                    var result = await PlayServCatalog.GetPurchasedItemsAsync(ct);
                    if (ct.IsCancellationRequested || generation != _generation) return;
                    Show(result.Items);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!ct.IsCancellationRequested && generation == _generation) _status = error.Message;
            }
            finally { if (generation == _generation) _busy = false; }
        }

        private void Show(IReadOnlyList<PlayServPurchasedItem> items)
        {
            _items = items.ToArray();
            _status = $"Received {_items.Length} purchased items at {DateTime.Now:T}.";
        }

        private void OnGUI()
        {
            var width = Mathf.Max(200, Screen.width - 32);
            var items = _items;
            GUI.Box(new Rect(8, 8, Screen.width - 16, Screen.height - 16), "Purchased items");
            GUI.Label(new Rect(16, 40, width, 40), _status);
            GUI.Label(new Rect(16, 80, width, 25), $"SDK: {PlayServ.State}    Subscription: {_subscription?.State.ToString() ?? "None"}");
            GUI.enabled = !_busy;
            var buttonWidth = (width - 12) / 3;
            if (GUI.Button(new Rect(16, 110, buttonWidth, 40), "Connect SDK")) _ = RunAsync(false, true);
            if (GUI.Button(new Rect(22 + buttonWidth, 110, buttonWidth, 40), "Read once")) _ = RunAsync(false);
            if (GUI.Button(new Rect(28 + buttonWidth * 2, 110, buttonWidth, 40), "Subscribe")) _ = RunAsync(true);
            GUI.enabled = true;
            if (GUI.Button(new Rect(16, 160, width, 32), "Stop / cancel"))
            {
                OnDisable();
                OnEnable();
                _status = "Stopped. No further updates will be displayed.";
            }
            var viewport = new Rect(16, 208, width, Mathf.Max(40, Screen.height - 224));
            using var scroll = new GUI.ScrollViewScope(viewport, _scroll,
                new Rect(0, 0, width - 20, Mathf.Max(viewport.height, items.Length * 52 + 25)));
            _scroll = scroll.scrollPosition;
            if (items.Length == 0) GUI.Label(new Rect(4, 0, width - 24, 25), "No purchased items in this snapshot.");
            for (var i = 0; i < items.Length; i++)
                GUI.Label(new Rect(4, i * 52, width - 24, 50),
                    $"{items[i].Name} ({items[i].Sku}) × {items[i].Count}\n{items[i].ProductType} · {items[i].Status}");
        }
    }
}
