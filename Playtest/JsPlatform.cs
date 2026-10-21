using System.Reflection;
using HikariForge.M313.Core;
using Microsoft.JSInterop;

namespace HikariForge.M313.Playtest
{
    /// <summary>Browser implementation of the platform services (localStorage + HTML audio).</summary>
    public sealed class JsPlatform : IPlatform
    {
        private readonly IJSInProcessRuntime _js;

        public JsPlatform(IJSRuntime js) { _js = (IJSInProcessRuntime)js; }

        public static string Content(string name)
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Content.{name}.json");
            if (s == null) return null;
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }

        public string Load(string key) => _js.Invoke<string>("m313.load", key);
        public void Save(string key, string value) => _js.InvokeVoid("m313.save", key, value);
        public void Music(string id) => _js.InvokeVoid("m313.audio.music", id);
        public void Sfx(string id, float volume) => _js.InvokeVoid("m313.audio.sfx", id, volume);
        public void Ambience(string id) => _js.InvokeVoid("m313.audio.ambience", id);
        public void Volumes(float master, float music, float sfx) => _js.InvokeVoid("m313.audio.volume", master, music, sfx);
        public void Achievement(string id) => _js.InvokeVoid("m313.achievement", id);
    }
}
