using System.Collections.Generic;
using UnityEngine;

namespace HikariForge.M313
{
    /// <summary>
    /// Dynamic music and sound: two music sources that crossfade, one looping ambience bed,
    /// and a small pool of one-shot effect sources. Clips load from Resources/Audio.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        private AudioSource _musicA, _musicB, _ambience;
        private readonly List<AudioSource> _pool = new List<AudioSource>();
        private readonly Dictionary<string, AudioClip> _cache = new Dictionary<string, AudioClip>();
        private readonly HashSet<string> _missing = new HashSet<string>();
        private string _musicId, _ambienceId;
        private float _master = 0.8f, _music = 0.7f, _sfx = 0.8f;
        private float _fade = 1f;            // 0 → B audible, 1 → A audible (A is the active one)
        private bool _aActive = true;
        private float _ambienceLevel;

        /// <summary>Extra attenuation for sudden loud sounds (Reduced sudden-audio setting).</summary>
        public float StingerScale = 1f;

        private void Awake()
        {
            _musicA = MakeSource("MusicA", true);
            _musicB = MakeSource("MusicB", true);
            _ambience = MakeSource("Ambience", true);
            for (int i = 0; i < 10; i++) _pool.Add(MakeSource("Sfx" + i, false));
        }

        private AudioSource MakeSource(string n, bool loop)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.loop = loop;
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            return s;
        }

        private AudioClip Clip(string path)
        {
            if (_missing.Contains(path)) return null;
            if (_cache.TryGetValue(path, out var c)) return c;
            c = Resources.Load<AudioClip>(path);
            if (c == null) { _missing.Add(path); return null; }
            _cache[path] = c;
            return c;
        }

        public void SetVolumes(float master, float music, float sfx)
        {
            _master = master; _music = music; _sfx = sfx;
            AudioListener.volume = 1f;
        }

        public void PlayMusic(string id)
        {
            if (string.IsNullOrEmpty(id) || id == _musicId) return;
            var clip = Clip("Audio/Music/" + id);
            if (clip == null) return;
            _musicId = id;
            _aActive = !_aActive;
            var src = _aActive ? _musicA : _musicB;
            src.clip = clip;
            src.volume = 0f;
            src.Play();
            _fade = 0f;
        }

        public void PlayAmbience(string id)
        {
            if (id == _ambienceId) return;
            _ambienceId = id;
            var clip = string.IsNullOrEmpty(id) ? null : Clip("Audio/Sfx/amb_" + id);
            _ambience.clip = clip;
            _ambienceLevel = 0f;
            if (clip != null) _ambience.Play(); else _ambience.Stop();
        }

        public void PlaySfx(string id, float volume)
        {
            var clip = Clip("Audio/Sfx/" + id);
            if (clip == null) return;
            AudioSource free = null;
            foreach (var s in _pool) if (!s.isPlaying) { free = s; break; }
            if (free == null) free = _pool[0];
            bool sudden = id.StartsWith("stinger") || id.StartsWith("scare_");
            free.volume = Mathf.Clamp01(_master * _sfx * volume * (sudden ? StingerScale : 1f));
            free.PlayOneShot(clip);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _fade = Mathf.MoveTowards(_fade, 1f, dt / 2f);
            var on = _aActive ? _musicA : _musicB;
            var off = _aActive ? _musicB : _musicA;
            float target = _master * _music;
            on.volume = target * _fade;
            off.volume = target * (1f - _fade);
            if (_fade >= 1f && off.isPlaying) off.Stop();
            _ambienceLevel = Mathf.MoveTowards(_ambienceLevel, 1f, dt / 2f);
            _ambience.volume = _master * _sfx * 0.5f * _ambienceLevel;
        }
    }
}
