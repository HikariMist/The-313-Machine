using UnityEngine;

namespace HikariForge.M313
{
    /// <summary>
    /// Optional Steam integration. The game never requires Steam: without the STEAMWORKS_NET
    /// define (Steamworks.NET package + a real App ID) every call is a no-op and achievements
    /// are tracked only in the local profile. See STEAM_RELEASE.md for setup.
    /// </summary>
    public sealed class SteamService
    {
        public static readonly SteamService Instance = new SteamService();
        public bool Available { get; private set; }

        public void Init()
        {
#if STEAMWORKS_NET && !DISABLESTEAMWORKS
            try { Available = Steamworks.SteamAPI.Init(); }
            catch (System.Exception e) { Debug.LogWarning("[M313] Steam unavailable: " + e.Message); Available = false; }
#endif
        }

        public void Tick()
        {
#if STEAMWORKS_NET && !DISABLESTEAMWORKS
            if (Available) Steamworks.SteamAPI.RunCallbacks();
#endif
        }

        /// <summary>Achievement ids match Profile ids: ENDING_TRUTH ... ENDING_OFF_THE_CLOCK, ENDLESS_UNLOCKED.</summary>
        public void Unlock(string achievementId)
        {
            Debug.Log("[M313] Achievement: " + achievementId);
#if STEAMWORKS_NET && !DISABLESTEAMWORKS
            if (!Available) return;
            Steamworks.SteamUserStats.SetAchievement(achievementId);
            Steamworks.SteamUserStats.StoreStats();
#endif
        }

        public void SetStat(string name, int value)
        {
#if STEAMWORKS_NET && !DISABLESTEAMWORKS
            if (!Available) return;
            Steamworks.SteamUserStats.SetStat(name, value);
            Steamworks.SteamUserStats.StoreStats();
#endif
        }

        /// <summary>Steam Cloud: mirrors save files when enabled (Auto-Cloud can be used instead).</summary>
        public void CloudWrite(string fileName, string contents)
        {
#if STEAMWORKS_NET && !DISABLESTEAMWORKS
            if (!Available) return;
            var bytes = System.Text.Encoding.UTF8.GetBytes(contents);
            Steamworks.SteamRemoteStorage.FileWrite(fileName, bytes, bytes.Length);
#endif
        }

        public void Shutdown()
        {
#if STEAMWORKS_NET && !DISABLESTEAMWORKS
            if (Available) Steamworks.SteamAPI.Shutdown();
#endif
        }
    }
}
