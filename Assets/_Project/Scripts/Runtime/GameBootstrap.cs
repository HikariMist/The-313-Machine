using UnityEngine;

namespace HikariForge.M313
{
    /// <summary>Creates the game root at startup, independent of scene contents.</summary>
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Object.FindAnyObjectByType<GameView>() != null) return;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 1;
            var root = new GameObject("M313");
            Object.DontDestroyOnLoad(root);
            var cam = root.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;
            root.AddComponent<AudioListener>();
            SteamService.Instance.Init();
            var audio = root.AddComponent<AudioDirector>();
            root.AddComponent<GameView>().Init(audio);
        }
    }
}
