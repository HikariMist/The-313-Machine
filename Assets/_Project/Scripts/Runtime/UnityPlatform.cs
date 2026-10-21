using System;
using System.IO;
using HikariForge.M313.Core;
using UnityEngine;

namespace HikariForge.M313
{
    /// <summary>
    /// Unity implementation of the platform services: saves under Application.persistentDataPath
    /// (written atomically with a .bak copy), audio through <see cref="AudioDirector"/>, and
    /// achievements through the optional Steam integration.
    /// </summary>
    public sealed class UnityPlatform : IPlatform
    {
        private readonly AudioDirector _audio;
        private readonly string _dir;

        public UnityPlatform(AudioDirector audio)
        {
            _audio = audio;
            _dir = Path.Combine(Application.persistentDataPath, "Saves");
            try { Directory.CreateDirectory(_dir); }
            catch (Exception e) { Debug.LogError("[M313] Cannot create save folder: " + e.Message); }
        }

        private string PathFor(string key) => Path.Combine(_dir, key.Replace("m313.", "") + ".json");

        public string Load(string key)
        {
            string path = PathFor(key);
            foreach (var candidate in new[] { path, path + ".bak" })
            {
                try
                {
                    if (!File.Exists(candidate)) continue;
                    string text = File.ReadAllText(candidate);
                    if (key == "m313.save")
                    {
                        // Validate before trusting; fall back to the backup if the main file is damaged.
                        try { SaveSerializer.Deserialize(text); }
                        catch (SaveCorruptException e) { Debug.LogWarning($"[M313] {candidate} is damaged: {e.Message}"); continue; }
                    }
                    return text;
                }
                catch (Exception e) { Debug.LogWarning($"[M313] Could not read {candidate}: {e.Message}"); }
            }
            return null;
        }

        public void Save(string key, string value)
        {
            string path = PathFor(key);
            string tmp = path + ".tmp";
            try
            {
                File.WriteAllText(tmp, value);
                if (File.Exists(path)) File.Copy(path, path + ".bak", true);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                SteamService.Instance.CloudWrite(Path.GetFileName(path), value);
            }
            catch (Exception e) { Debug.LogError($"[M313] Save failed for {key}: {e.Message}"); }
        }

        public void Music(string id) => _audio.PlayMusic(id);
        public void Sfx(string id, float volume) => _audio.PlaySfx(id, volume);
        public void Ambience(string id) => _audio.PlayAmbience(id);
        public void Volumes(float master, float music, float sfx) => _audio.SetVolumes(master, music, sfx);
        public void Achievement(string id) => SteamService.Instance.Unlock(id);
    }
}
