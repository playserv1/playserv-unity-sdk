mergeInto(LibraryManager.library, {
  RtcSig_Connect: function (goNamePtr, urlPtr) {
    var goName = UTF8ToString(goNamePtr);
    var url = UTF8ToString(urlPtr);

    if (!window.__unityWebRtcSignaling) {
      window.__unityWebRtcSignaling = {};
    }

    var s = window.__unityWebRtcSignaling;
    s.objName = goName;
    s.connectSeq = (s.connectSeq || 0) + 1;
    var seq = s.connectSeq;

    s._send = function (method, arg) {
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
        console.log('[PlayServ][WebGL][RtcSig] SendMessage failed', e);
      }

      console.log('[PlayServ][WebGL][RtcSig] No Unity SendMessage bridge found for', method);
    };

    if (s.ws) {
      try { s.ws.close(); } catch (e) {}
      s.ws = null;
    }

    var ws;
    try {
      ws = new WebSocket(url);
      s.ws = ws;
    } catch (e) {
      s._send('OnRtcSigError', 'create failed: ' + (e && e.message ? e.message : 'unknown'));
      return;
    }

    ws.onopen = function () {
      if (seq !== s.connectSeq || ws !== s.ws) {
        return;
      }
      s._send('OnRtcSigOpen', '');
    };

    ws.onerror = function (ev) {
      if (seq !== s.connectSeq || ws !== s.ws) {
        return;
      }
      var details = ev && ev.message ? ('' + ev.message) : '';
      s._send('OnRtcSigError', details);
    };

    ws.onclose = function (ev) {
      if (seq !== s.connectSeq || ws !== s.ws) {
        return;
      }
      var code = ev && ev.code ? ('' + ev.code) : '';
      var closeReason = ev && ev.reason ? ('' + ev.reason) : '';
      var reason = code + (closeReason ? (':' + closeReason) : '');
      s.ws = null;
      s._send('OnRtcSigClose', reason);
    };

    ws.onmessage = function (ev) {
      if (seq !== s.connectSeq || ws !== s.ws) {
        return;
      }
      var data = (typeof ev.data === 'string') ? ev.data : '';
      s._send('OnRtcSigMessage', data);
    };
  },

  RtcSig_Send: function (messagePtr) {
    var s = window.__unityWebRtcSignaling;
    if (!s || !s.ws || s.ws.readyState !== 1) {
      return 0;
    }

    var msg = UTF8ToString(messagePtr);
    try {
      s.ws.send(msg);
      return 1;
    } catch (e) {
      console.log('RtcSig send failed', e);
      return 0;
    }
  },

  RtcSig_Close: function () {
    var s = window.__unityWebRtcSignaling;
    if (!s || !s.ws) {
      return;
    }

    s.connectSeq = (s.connectSeq || 0) + 1;
    try { s.ws.close(); } catch (e) {}
    s.ws = null;
  },

  Rtc_Connect: function (goNamePtr, configJsonPtr, labelPtr) {
    var goName = UTF8ToString(goNamePtr);
    var configJson = UTF8ToString(configJsonPtr);
    var label = UTF8ToString(labelPtr);

    if (!window.__unityWebRtc) {
      window.__unityWebRtc = {};
    }

    var r = window.__unityWebRtc;
    r.objName = goName;
    r.connectSeq = (r.connectSeq || 0) + 1;
    var seq = r.connectSeq;
    r.pendingRemoteCandidates = [];
    r.remoteDescriptionSet = false;

    r._send = function (method, arg) {
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
        console.log('[PlayServ][WebGL][WebRTC] SendMessage failed', e);
      }

      console.log('[PlayServ][WebGL][WebRTC] No Unity SendMessage bridge found for', method);
    };

    var cleanup = function () {
      if (r.dc) {
        try { r.dc.onopen = null; } catch (e) {}
        try { r.dc.onmessage = null; } catch (e) {}
        try { r.dc.onerror = null; } catch (e) {}
        try { r.dc.onclose = null; } catch (e) {}
        try { r.dc.close(); } catch (e) {}
      }

      if (r.pc) {
        try { r.pc.onicecandidate = null; } catch (e) {}
        try { r.pc.ondatachannel = null; } catch (e) {}
        try { r.pc.onconnectionstatechange = null; } catch (e) {}
        try { r.pc.oniceconnectionstatechange = null; } catch (e) {}
        try { r.pc.close(); } catch (e) {}
      }

      r.dc = null;
      r.pc = null;
      r.pendingRemoteCandidates = [];
      r.remoteDescriptionSet = false;
    };

    cleanup();

    var parsedConfig = {};
    try {
      parsedConfig = configJson ? JSON.parse(configJson) : {};
    } catch (e) {
      r._send('OnRtcError', 'config parse failed: ' + (e && e.message ? e.message : 'unknown'));
      return;
    }

    var rtcConfig = {};
    if (parsedConfig && parsedConfig.iceServers && parsedConfig.iceServers.length > 0) {
      rtcConfig.iceServers = parsedConfig.iceServers.map(function (entry) {
        if (typeof entry === 'string') {
          return { urls: entry };
        }

        return entry;
      });
    }

    var attachDataChannel = function (dc) {
      if (!dc) {
        return;
      }

      r.dc = dc;
      try { dc.binaryType = 'arraybuffer'; } catch (e) {}

      dc.onopen = function () {
        if (seq !== r.connectSeq || dc !== r.dc) {
          return;
        }

        r._send('OnRtcOpen', '');
      };

      dc.onmessage = function (ev) {
        if (seq !== r.connectSeq || dc !== r.dc) {
          return;
        }

        var value = ev ? ev.data : '';
        if (typeof value === 'string') {
          r._send('OnRtcMessage', value);
          return;
        }

        if (typeof ArrayBuffer !== 'undefined' && value instanceof ArrayBuffer) {
          try {
            var decoded = new TextDecoder('utf-8').decode(new Uint8Array(value));
            r._send('OnRtcMessage', decoded);
          } catch (e) {
            r._send('OnRtcError', 'arraybuffer decode failed: ' + (e && e.message ? e.message : 'unknown'));
          }
          return;
        }

        if (typeof Blob !== 'undefined' && value instanceof Blob) {
          var reader = new FileReader();
          reader.onload = function () {
            try {
              var text = typeof reader.result === 'string'
                ? reader.result
                : new TextDecoder('utf-8').decode(new Uint8Array(reader.result));
              r._send('OnRtcMessage', text);
            } catch (e) {
              r._send('OnRtcError', 'blob decode failed: ' + (e && e.message ? e.message : 'unknown'));
            }
          };
          reader.onerror = function () {
            r._send('OnRtcError', 'blob read failed');
          };
          reader.readAsArrayBuffer(value);
          return;
        }

        r._send('OnRtcError', 'unsupported data channel payload type');
      };

      dc.onerror = function (ev) {
        if (seq !== r.connectSeq || dc !== r.dc) {
          return;
        }

        var details = ev && ev.message ? ('' + ev.message) : 'data channel error';
        r._send('OnRtcError', details);
      };

      dc.onclose = function () {
        if (seq !== r.connectSeq || dc !== r.dc) {
          return;
        }

        r.dc = null;
        r._send('OnRtcClose', 'datachannel closed');
      };
    };

    var flushPendingCandidates = function () {
      if (!r.pc || !r.remoteDescriptionSet || !r.pendingRemoteCandidates || r.pendingRemoteCandidates.length === 0) {
        return;
      }

      var pending = r.pendingRemoteCandidates.slice();
      r.pendingRemoteCandidates = [];

      pending.forEach(function (candidateInit) {
        r.pc.addIceCandidate(candidateInit).catch(function (e) {
          r._send('OnRtcError', 'addIceCandidate failed: ' + (e && e.message ? e.message : 'unknown'));
        });
      });
    };

    var emitLocalDescription = function (desc) {
      r._send('OnRtcSignal', JSON.stringify({
        messageType: desc.type,
        sdpType: desc.type,
        sdp: desc.sdp || ''
      }));
    };

    try {
      var pc = new RTCPeerConnection(rtcConfig);
      r.pc = pc;

      pc.onicecandidate = function (ev) {
        if (seq !== r.connectSeq || pc !== r.pc || !ev || !ev.candidate) {
          return;
        }

        r._send('OnRtcSignal', JSON.stringify({
          messageType: 'ice-candidate',
          candidate: ev.candidate.candidate || '',
          sdpMid: ev.candidate.sdpMid || '',
          sdpMLineIndex: typeof ev.candidate.sdpMLineIndex === 'number' ? ev.candidate.sdpMLineIndex : null
        }));
      };

      pc.ondatachannel = function (ev) {
        if (seq !== r.connectSeq || pc !== r.pc) {
          return;
        }

        attachDataChannel(ev.channel);
      };

      pc.onconnectionstatechange = function () {
        if (seq !== r.connectSeq || pc !== r.pc) {
          return;
        }

        if (pc.connectionState === 'failed') {
          r._send('OnRtcError', 'peer connection failed');
        } else if (pc.connectionState === 'closed') {
          r._send('OnRtcClose', 'peer connection closed');
        }
      };

      pc.oniceconnectionstatechange = function () {
        if (seq !== r.connectSeq || pc !== r.pc) {
          return;
        }

        if (pc.iceConnectionState === 'failed') {
          r._send('OnRtcError', 'ice connection failed');
        } else if (pc.iceConnectionState === 'closed') {
          r._send('OnRtcClose', 'ice connection ' + pc.iceConnectionState);
        }
      };

      attachDataChannel(pc.createDataChannel(label || 'playserv', { ordered: true }));

      pc.createOffer()
        .then(function (offer) { return pc.setLocalDescription(offer); })
        .then(function () {
          if (seq !== r.connectSeq || pc !== r.pc || !pc.localDescription) {
            return;
          }

          emitLocalDescription(pc.localDescription);
        })
        .catch(function (e) {
          r._send('OnRtcError', 'offer failed: ' + (e && e.message ? e.message : 'unknown'));
        });
    } catch (e) {
      cleanup();
      r._send('OnRtcError', 'peer connection create failed: ' + (e && e.message ? e.message : 'unknown'));
    }

    r._applyRemoteSignal = function (message) {
      if (seq !== r.connectSeq || !r.pc) {
        return;
      }

      if (!message || !message.messageType) {
        r._send('OnRtcError', 'invalid signaling message');
        return;
      }

      var normalizedType = (message.messageType || '').toLowerCase();

      if (normalizedType === 'answer' || normalizedType === 'offer') {
        var descType = message.sdpType || normalizedType;
        var remoteDesc = {
          type: descType,
          sdp: message.sdp || ''
        };

        r.pc.setRemoteDescription(remoteDesc)
          .then(function () {
            r.remoteDescriptionSet = true;
            flushPendingCandidates();

            if (descType !== 'offer') {
              return null;
            }

            return r.pc.createAnswer()
              .then(function (answer) { return r.pc.setLocalDescription(answer); })
              .then(function () {
                if (seq !== r.connectSeq || !r.pc || !r.pc.localDescription) {
                  return;
                }

                emitLocalDescription(r.pc.localDescription);
              });
          })
          .catch(function (e) {
            r._send('OnRtcError', 'setRemoteDescription failed: ' + (e && e.message ? e.message : 'unknown'));
          });

        return;
      }

      if (normalizedType === 'ice-candidate') {
        var candidateInit = {
          candidate: message.candidate || '',
          sdpMid: message.sdpMid || null,
          sdpMLineIndex: typeof message.sdpMLineIndex === 'number' ? message.sdpMLineIndex : null
        };

        if (!r.remoteDescriptionSet) {
          r.pendingRemoteCandidates.push(candidateInit);
          return;
        }

        r.pc.addIceCandidate(candidateInit).catch(function (e) {
          r._send('OnRtcError', 'addIceCandidate failed: ' + (e && e.message ? e.message : 'unknown'));
        });
        return;
      }

      if (normalizedType === 'ready' || normalizedType === 'hello' || normalizedType === 'hello-ack') {
        return;
      }

      if (normalizedType === 'error') {
        r._send('OnRtcError', message.reason || 'remote signaling error');
        return;
      }

      r._send('OnRtcError', 'unsupported signaling message type: ' + normalizedType);
    };
  },

  Rtc_ApplySignal: function (signalJsonPtr) {
    var r = window.__unityWebRtc;
    if (!r || !r.pc || !r._applyRemoteSignal) {
      return 0;
    }

    var signalJson = UTF8ToString(signalJsonPtr);
    var message = null;

    try {
      message = signalJson ? JSON.parse(signalJson) : null;
    } catch (e) {
      r._send('OnRtcError', 'signal parse failed: ' + (e && e.message ? e.message : 'unknown'));
      return 0;
    }

    try {
      r._applyRemoteSignal(message);
      return 1;
    } catch (e) {
      r._send('OnRtcError', 'apply signal failed: ' + (e && e.message ? e.message : 'unknown'));
      return 0;
    }
  },

  Rtc_Send: function (messagePtr) {
    var r = window.__unityWebRtc;
    if (!r || !r.dc || r.dc.readyState !== 'open') {
      return 0;
    }

    var msg = UTF8ToString(messagePtr);
    try {
      r.dc.send(msg);
      return 1;
    } catch (e) {
      console.log('WebRTC send failed', e);
      return 0;
    }
  },

  Rtc_Close: function () {
    var r = window.__unityWebRtc;
    if (!r) {
      return;
    }

    r.connectSeq = (r.connectSeq || 0) + 1;

    if (r.dc) {
      try { r.dc.close(); } catch (e) {}
      r.dc = null;
    }

    if (r.pc) {
      try { r.pc.close(); } catch (e) {}
      r.pc = null;
    }

    r.pendingRemoteCandidates = [];
    r.remoteDescriptionSet = false;
  }
});
