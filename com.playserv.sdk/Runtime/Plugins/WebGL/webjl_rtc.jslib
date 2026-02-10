mergeInto(LibraryManager.library, {
  Ws_Connect: function (goNamePtr, urlPtr) {
    var goName = UTF8ToString(goNamePtr);
    var url = UTF8ToString(urlPtr);

    if (!window.__unityWebSocket) {
      window.__unityWebSocket = {};
    }

    var u = window.__unityWebSocket;
    u.objName = goName;

    u._send = function (method, arg) {
      if (typeof unityInstance !== 'undefined' && unityInstance) {
        unityInstance.SendMessage(goName, method, arg);
      } else if (typeof Module !== 'undefined' && Module['SendMessage']) {
        Module['SendMessage'](goName, method, arg);
      }
    };

    if (u.ws) {
      try { u.ws.close(); } catch (e) {}
      u.ws = null;
    }

    try {
      u.ws = new WebSocket(url);
    } catch (e) {
      console.log('WebSocket create failed', e);
      u._send('OnWsError', 'create failed');
      return;
    }

    u.ws.onopen = function () {
      u._send('OnWsOpen', '');
    };

    u.ws.onerror = function () {
      u._send('OnWsError', '');
    };

    u.ws.onclose = function (ev) {
      var reason = ev && ev.code ? ('' + ev.code) : '';
      u._send('OnWsClose', reason);
    };

    u.ws.onmessage = function (ev) {
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

    try { u.ws.close(); } catch (e) {}
    u.ws = null;
  }
});