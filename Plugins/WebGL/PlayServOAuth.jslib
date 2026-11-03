mergeInto(LibraryManager.library, {
  $PlayServOAuth: { next: 1, windows: {} },
  PlayServOAuth_Open__deps: ['$PlayServOAuth'],
  PlayServOAuth_Open: function () {
    var popup = window.open('about:blank', '_blank', 'popup,width=520,height=720');
    if (!popup) return 0;
    popup.opener = null;
    var id = PlayServOAuth.next++;
    PlayServOAuth.windows[id] = popup;
    return id;
  },
  PlayServOAuth_Navigate__deps: ['$PlayServOAuth'],
  PlayServOAuth_Navigate: function (id, url) {
    var popup = PlayServOAuth.windows[id];
    if (!popup || popup.closed) return 0;
    try { popup.location.replace(UTF8ToString(url)); return 1; } catch (_) { return 0; }
  },
  PlayServOAuth_Close__deps: ['$PlayServOAuth'],
  PlayServOAuth_Close: function (id) {
    var popup = PlayServOAuth.windows[id];
    delete PlayServOAuth.windows[id];
    if (popup) { try { popup.close(); } catch (_) {} }
  },
  PlayServOAuth_Random: function (ptr, length) {
    if (!window.crypto || !window.crypto.getRandomValues) return 0;
    try { HEAPU8.set(window.crypto.getRandomValues(new Uint8Array(length)), ptr); return 1; }
    catch (_) { return 0; }
  }
});
