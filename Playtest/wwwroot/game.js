// Browser glue for the playtest: storage, audio and small effects. All calls fail soft.
window.m313 = (function () {
  const store = {
    save(k, v) { try { localStorage.setItem(k, v); } catch (e) {} },
    load(k) { try { return localStorage.getItem(k); } catch (e) { return null; } }
  };
  const vol = { master: 0.8, music: 0.7, sfx: 0.8 };
  let musicEl = null, musicId = null, ambEl = null, ambId = null, unlocked = false;
  const missing = new Set();

  function make(src, loop) {
    const a = new Audio();
    a.src = src; a.loop = loop; a.preload = 'auto';
    a.addEventListener('error', () => missing.add(src));
    return a;
  }
  function fade(el, to, ms, done) {
    if (!el) { if (done) done(); return; }
    const from = el.volume, start = performance.now();
    function step(t) {
      const k = Math.min(1, (t - start) / ms);
      el.volume = Math.max(0, Math.min(1, from + (to - from) * k));
      if (k < 1) requestAnimationFrame(step); else if (done) done();
    }
    requestAnimationFrame(step);
  }
  const audio = {
    unlock() { unlocked = true; if (musicEl) musicEl.play().catch(() => {}); if (ambEl) ambEl.play().catch(() => {}); },
    volume(m, mu, s) {
      vol.master = m; vol.music = mu; vol.sfx = s;
      if (musicEl) musicEl.volume = m * mu;
      if (ambEl) ambEl.volume = m * s * 0.5;
    },
    music(id) {
      if (id === musicId) return;
      musicId = id;
      const old = musicEl;
      fade(old, 0, 1500, () => old && old.pause());
      const src = 'audio/music/' + id + '.ogg';
      if (missing.has(src)) { musicEl = null; return; }
      musicEl = make(src, true);
      musicEl.volume = 0;
      if (unlocked) musicEl.play().catch(() => {});
      fade(musicEl, vol.master * vol.music, 2000);
    },
    ambience(id) {
      if (id === ambId) return;
      ambId = id;
      const old = ambEl;
      fade(old, 0, 1200, () => old && old.pause());
      if (!id) { ambEl = null; return; }
      const src = 'audio/sfx/amb_' + id + '.ogg';
      if (missing.has(src)) { ambEl = null; return; }
      ambEl = make(src, true);
      ambEl.volume = 0;
      if (unlocked) ambEl.play().catch(() => {});
      fade(ambEl, vol.master * vol.sfx * 0.5, 2000);
    },
    sfx(id, v) {
      if (!unlocked) return;
      const src = 'audio/sfx/' + id + '.ogg';
      if (missing.has(src)) return;
      const a = make(src, false);
      a.volume = Math.max(0, Math.min(1, vol.master * vol.sfx * (v == null ? 1 : v)));
      a.play().catch(() => {});
    }
  };
  return {
    save: store.save, load: store.load, audio,
    achievement(id) { console.log('[achievement]', id); }
  };
})();
