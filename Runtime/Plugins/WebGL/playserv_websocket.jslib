mergeInto(LibraryManager.library, {
  Ws_Connect: function (goNamePtr, urlPtr) {
    var goName = UTF8ToString(goNamePtr);
    var url = UTF8ToString(urlPtr);

    if (!window.__unityWebSocket) {
      window.__unityWebSocket = {};
    }

    var socketState = window.__unityWebSocket;
    socketState.objName = goName;
    socketState.connectSeq = (socketState.connectSeq || 0) + 1;
    var seq = socketState.connectSeq;

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
        console.log('[PlayServ][WebGL] SendMessage failed', e);
      }

      console.log('[PlayServ][WebGL] No Unity SendMessage bridge found for', method);
    };

    if (socketState.ws) {
      try { socketState.ws.close(); } catch (e) {}
      socketState.ws = null;
    }

    var ws;
    try {
      ws = new WebSocket(url);
      socketState.ws = ws;
    } catch (e) {
      console.log('WebSocket create failed', e);
      socketState._send('OnWsError', 'create failed: ' + (e && e.message ? e.message : 'unknown'));
      return;
    }

    ws.onopen = function () {
      if (seq !== socketState.connectSeq || ws !== socketState.ws) {
        return;
      }
      socketState._send('OnWsOpen', '');
    };

    ws.onerror = function (ev) {
      if (seq !== socketState.connectSeq || ws !== socketState.ws) {
        return;
      }
      var details = '';
      if (ev && ev.message) {
        details = '' + ev.message;
      }
      socketState._send('OnWsError', details);
    };

    ws.onclose = function (ev) {
      if (seq !== socketState.connectSeq || ws !== socketState.ws) {
        return;
      }
      var code = ev && ev.code ? ('' + ev.code) : '';
      var closeReason = ev && ev.reason ? ('' + ev.reason) : '';
      var reason = code + (closeReason ? (':' + closeReason) : '');
      socketState.ws = null;
      socketState._send('OnWsClose', reason);
    };

    ws.onmessage = function (ev) {
      if (seq !== socketState.connectSeq || ws !== socketState.ws) {
        return;
      }
      var data = (typeof ev.data === 'string') ? ev.data : '';
      socketState._send('OnWsMessage', data);
    };
  },

  Ws_Send: function (messagePtr) {
    var socketState = window.__unityWebSocket;
    if (!socketState || !socketState.ws || socketState.ws.readyState !== 1) {
      return 0;
    }

    var message = UTF8ToString(messagePtr);
    try {
      socketState.ws.send(message);
      return 1;
    } catch (e) {
      console.log('WebSocket send failed', e);
      return 0;
    }
  },

  Ws_Close: function () {
    var socketState = window.__unityWebSocket;
    if (!socketState || !socketState.ws) {
      return;
    }

    socketState.connectSeq = (socketState.connectSeq || 0) + 1;
    try { socketState.ws.close(); } catch (e) {}
    socketState.ws = null;
  }
});
