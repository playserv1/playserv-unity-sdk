mergeInto(LibraryManager.library, {
  Ws_Connect: function (goNamePtr, urlPtr) {
    var goName = UTF8ToString(goNamePtr);
    var url = UTF8ToString(urlPtr);

    if (!window.__unityWebSocket) {
      window.__unityWebSocket = {};
    }

    var u = window.__unityWebSocket;
    u.objName = goName;
    u.connectSeq = (u.connectSeq || 0) + 1;
    var seq = u.connectSeq;

    u._send = function (method, arg) {
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

    if (u.ws) {
      try { u.ws.close(); } catch (e) {}
      u.ws = null;
    }

    var ws;
    try {
      ws = new WebSocket(url);
      u.ws = ws;
    } catch (e) {
      console.log('WebSocket create failed', e);
      u._send('OnWsError', 'create failed: ' + (e && e.message ? e.message : 'unknown'));
      return;
    }

    ws.onopen = function () {
      if (seq !== u.connectSeq || ws !== u.ws) {
        return;
      }
      u._send('OnWsOpen', '');
    };

    ws.onerror = function (ev) {
      if (seq !== u.connectSeq || ws !== u.ws) {
        return;
      }
      var details = '';
      if (ev && ev.message) {
        details = '' + ev.message;
      }
      u._send('OnWsError', details);
    };

    ws.onclose = function (ev) {
      if (seq !== u.connectSeq || ws !== u.ws) {
        return;
      }
      var code = ev && ev.code ? ('' + ev.code) : '';
      var closeReason = ev && ev.reason ? ('' + ev.reason) : '';
      var reason = code + (closeReason ? (':' + closeReason) : '');
      u.ws = null;
      u._send('OnWsClose', reason);
    };

    ws.onmessage = function (ev) {
      if (seq !== u.connectSeq || ws !== u.ws) {
        return;
      }
      var data = (typeof ev.data === 'string') ? ev.data : '';
      u._send('OnWsMessage', data);
    };
  },

  Ws_Send: function (messagePtr) {
    var u = window.__unityWebSocket;
    if (!u || !u.ws || u.ws.readyState !== 1) {
      return 0;
    }

    var msg = UTF8ToString(messagePtr);
    try {
      u.ws.send(msg);
      return 1;
    } catch (e) {
      console.log('WebSocket send failed', e);
      return 0;
    }
  },

  Ws_Close: function () {
    var u = window.__unityWebSocket;
    if (!u || !u.ws) {
      return;
    }

    u.connectSeq = (u.connectSeq || 0) + 1;
    try { u.ws.close(); } catch (e) {}
    u.ws = null;
  }
});
