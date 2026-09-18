mergeInto(LibraryManager.library, {
  Ws_Connect: function (goNamePtr, urlPtr) {
    var goName = UTF8ToString(goNamePtr);
    var url = UTF8ToString(urlPtr);

    if (!window.__playServWebSockets) {
      window.__playServWebSockets = Object.create(null);
    }

    var sockets = window.__playServWebSockets;
    if (sockets[goName]) {
      sockets[goName].retire(true);
    }
    var socketState = { ws: null };
    sockets[goName] = socketState;

    socketState.retire = function (close) {
      if (sockets[goName] === socketState) {
        delete sockets[goName];
      }
      var oldSocket = socketState.ws;
      socketState.ws = null;
      if (oldSocket) {
        oldSocket.onopen = oldSocket.onmessage = oldSocket.onerror = oldSocket.onclose = null;
        if (close) {
          try { oldSocket.close(); } catch (e) {}
        }
      }
    };

    socketState._send = function (method, arg) {
      try {
        if (typeof unityInstance !== 'undefined' && unityInstance && unityInstance.SendMessage) {
          unityInstance.SendMessage(goName, method, arg);
          return;
        }

        if (typeof window !== 'undefined' && window.unityInstance && window.unityInstance.SendMessage) {
          window.unityInstance.SendMessage(goName, method, arg);
          return;
        }

        if (typeof window !== 'undefined' && window.gameInstance && window.gameInstance.SendMessage) {
          window.gameInstance.SendMessage(goName, method, arg);
          return;
        }

        if (typeof SendMessage === 'function') {
          SendMessage(goName, method, arg);
          return;
        }

        if (typeof Module !== 'undefined' && Module && Module.SendMessage) {
          Module.SendMessage(goName, method, arg);
          return;
        }
      } catch (e) {
        console.log('[PlayServ][WebGL] SendMessage failed.');
      }

      console.log('[PlayServ][WebGL] No Unity SendMessage bridge found for', method);
    };

    var ws;
    try {
      ws = new WebSocket(url);
      socketState.ws = ws;
    } catch (e) {
      socketState.retire(false);
      socketState._send('OnWsError', 'WebSocket creation failed.');
      return;
    }

    ws.onopen = function () {
      if (sockets[goName] !== socketState || ws !== socketState.ws) {
        return;
      }
      socketState._send('OnWsOpen', '');
    };

    ws.onerror = function (ev) {
      if (sockets[goName] !== socketState || ws !== socketState.ws) {
        return;
      }
      socketState.retire(true);
      socketState._send('OnWsError', 'WebSocket connection failed.');
    };

    ws.onclose = function (ev) {
      if (sockets[goName] !== socketState || ws !== socketState.ws) {
        return;
      }
      var code = ev && ev.code ? ('' + ev.code) : '';
      var closeReason = ev && ev.reason ? ('' + ev.reason) : '';
      var reason = code + (closeReason ? (':' + closeReason) : '');
      socketState.retire(false);
      socketState._send('OnWsClose', reason);
    };

    ws.onmessage = function (ev) {
      if (sockets[goName] !== socketState || ws !== socketState.ws) {
        return;
      }
      var data = (typeof ev.data === 'string') ? ev.data : '';
      var bytes = new TextEncoder().encode(data);
      var chunks = [];
      for (var offset = 0; offset < bytes.length; offset += 32768) {
        chunks.push(String.fromCharCode.apply(null, bytes.subarray(offset, offset + 32768)));
      }
      socketState._send('OnWsMessage', btoa(chunks.join('')));
    };
  },

  Ws_Send: function (goNamePtr, messagePtr, messageByteLength) {
    var sockets = window.__playServWebSockets;
    var socketState = sockets && sockets[UTF8ToString(goNamePtr)];
    if (!socketState || !socketState.ws || socketState.ws.readyState !== 1) {
      return 0;
    }

    try {
      var message = new TextDecoder('utf-8', { ignoreBOM: true }).decode(
        HEAPU8.subarray(messagePtr, messagePtr + messageByteLength));
      socketState.ws.send(message);
      return 1;
    } catch (e) {
      return 0;
    }
  },

  Ws_Close: function (goNamePtr) {
    var sockets = window.__playServWebSockets;
    var socketState = sockets && sockets[UTF8ToString(goNamePtr)];
    if (!socketState) {
      return;
    }

    socketState.retire(true);
  }
});
