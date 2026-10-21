using System;
using System.Collections.Generic;
using System.Linq;
using HikariForge.M313.Core;
using UnityEngine;

namespace HikariForge.M313
{
    /// <summary>
    /// The whole game's presentation layer. Drives <see cref="GameSession"/> (shared with the
    /// browser playtest) and renders every screen with IMGUI on a virtual 1920x1080 canvas.
    /// </summary>
    public sealed class GameView : MonoBehaviour
    {
        private GameSession S;
        private AudioDirector _audio;
        private bool _intro = true;
        private float _introT;
        private bool _hasSave;
        private string _prepTab = "stock";
        private int _endingPage;
        private string _settingsReturn = "title";
        private float _shake;
        private readonly System.Random _rng = new System.Random();

        // Layout
        private const float HudH = 56, SceneW = 1440, MachineX = 1440;

        public void Init(AudioDirector audio)
        {
            _audio = audio;
            S = new GameSession(new UnityPlatform(audio), name => Resources.Load<TextAsset>("Content/" + name)?.text);
            if (S.Db.LoadErrors.Count > 0) S.Error = "Content failed to load: " + string.Join("; ", S.Db.LoadErrors);
            S.LoadPersistent();
            S.Settings.PlaytestTools = Debug.isDebugBuild;
            ApplyComfortSettings();
            _hasSave = S.HasSave();
            Ui.Sound = id => S.Sfx(id, 0.4f);
            S.Sfx("machine_hum_start", 0.4f);
        }

        private void ApplyComfortSettings()
        {
            _audio.StingerScale = S.Settings.ScareLevel == 2 ? 1f : S.Settings.ScareLevel == 1 ? 0.55f : 0.3f;
        }

        private void Update()
        {
            if (S == null) return;
            float dt = Mathf.Min(0.1f, Time.unscaledDeltaTime);
            SteamService.Instance.Tick();
            if (_intro)
            {
                _introT += dt;
                if (Math.Abs(_introT - 4.6f) < dt) S.Sfx("machine_hum_start", 0.5f);
                if (_introT > 7f) EndIntro();
                return;
            }
            if (!Application.isFocused && S.Screen == GameScreen.Shift) return;   // alt-tab pauses the shift
            try { S.Tick(dt); }
            catch (Exception e) { S.Error = "Game error: " + e.Message; Debug.LogException(e); }
            if (S.Screen == GameScreen.Failed && S.Endless) S.EndlessDeath();
            _shake = Mathf.Max(0, _shake - dt * 3);
        }

        private void EndIntro()
        {
            _intro = false;
            S.Music("last_stop");
        }

        private void OnApplicationQuit() => SteamService.Instance.Shutdown();

        // ================================================================== OnGUI

        private void OnGUI()
        {
            if (S == null) return;
            Ui.Begin();
            HandleKeys();
            if (_intro) { DrawIntro(); return; }
            switch (S.Screen)
            {
                case GameScreen.Title: DrawTitle(); break;
                case GameScreen.Prep: DrawPrep(); break;
                case GameScreen.Shift: DrawShift(); break;
                case GameScreen.Summary: DrawSummary(); break;
                case GameScreen.Ending: DrawEnding(); break;
                case GameScreen.Failed: DrawFailed(); break;
                case GameScreen.Achievements: DrawAchievements(); break;
                case GameScreen.Settings: DrawSettings(); break;
                case GameScreen.Credits: DrawCredits(); break;
                case GameScreen.EndlessOver: DrawEndlessOver(); break;
            }
            DrawToasts();
            if (S.Error != null)
            {
                Ui.Fill(0, 0, Ui.VW, Ui.VH, new Color(0, 0, 0, 0.75f));
                Ui.Box(560, 380, 800, 320, Ui.Panel, Ui.Edge);
                Ui.Text("SOMETHING WENT WRONG", 600, 410, 720, 40, Ui.F.Label, 30, Ui.Blood);
                Ui.Text(S.Error, 600, 460, 720, 160, Ui.F.Body, 22);
                if (Ui.Button("OK", 600, 630, 160, 46)) S.Error = null;
            }
        }

        private void HandleKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            if (_intro && (e.keyCode != KeyCode.None)) { EndIntro(); e.Use(); return; }
            if (S.Screen != GameScreen.Shift) return;
            switch (e.keyCode)
            {
                case KeyCode.Space:
                case KeyCode.Return:
                    if (S.Overlay == GameOverlay.None) S.AdvanceLine();
                    break;
                case KeyCode.H: Toggle(GameOverlay.Handbook); break;
                case KeyCode.M: Toggle(GameOverlay.Monitor); break;
                case KeyCode.E: Toggle(GameOverlay.Evidence); break;
                case KeyCode.Escape:
                    if (S.Overlay == GameOverlay.Panel) S.ClosePanel();
                    else if (S.Overlay != GameOverlay.Phone && S.Overlay != GameOverlay.Investigation) S.Overlay = GameOverlay.None;
                    break;
                default: return;
            }
            e.Use();
        }

        private void Toggle(GameOverlay o)
        {
            if (S.Overlay == GameOverlay.Phone || S.Overlay == GameOverlay.Investigation) return;
            if (S.Overlay == GameOverlay.Panel) S.ClosePanel();
            S.Overlay = S.Overlay == o ? GameOverlay.None : o;
            S.Sfx(o == GameOverlay.Handbook ? "paper_rustle" : o == GameOverlay.Monitor ? "monitor_on" : "ui_click", 0.5f);
        }

        // ================================================================== intro

        private void DrawIntro()
        {
            Ui.Fill(0, 0, Ui.VW, Ui.VH, Color.black);
            float t = _introT;
            if (t < 4.2f)
            {
                float a = Mathf.Clamp01(t / 0.8f) * Mathf.Clamp01((4.2f - t) / 0.6f);
                // Green and purple light sweeps behind the studio mark.
                float sweep = Mathf.Repeat(t * 0.35f, 1f);
                Ui.Fill(0, 520, Ui.VW * sweep, 2, new Color(Ui.Emerald.r, Ui.Emerald.g, Ui.Emerald.b, 0.5f * a));
                Ui.Fill(Ui.VW * (1 - sweep), 560, Ui.VW * sweep, 2, new Color(Ui.Violet.r, Ui.Violet.g, Ui.Violet.b, 0.5f * a));
                var emblem = Ui.Tex("Branding/hf_emblem");
                var wordmark = Ui.Tex("Branding/hf_wordmark");
                if (t < 2.3f && emblem != null) Ui.Image(emblem, 760, 340, 400, 400, ScaleMode.ScaleToFit, a);
                else if (wordmark != null) Ui.Image(wordmark, 460, 400, 1000, 280, ScaleMode.ScaleToFit, a);
                else
                {
                    // Approved logo files not supplied yet: plain studio credit, not a logo recreation.
                    Ui.Text("HIKARI FORGE STUDIOS", 0, 500, Ui.VW, 80, Ui.F.LabelMed, 54, new Color(Ui.Bone.r, Ui.Bone.g, Ui.Bone.b, a), TextAnchor.MiddleCenter);
                }
            }
            else if (t > 4.6f)
            {
                float a = Mathf.Clamp01((t - 4.6f) / 0.5f) * Mathf.Clamp01((7f - t) / 0.5f);
                bool blink = Mathf.Repeat(t, 1f) > 0.9f;
                Ui.Text("3:13 AM", 0, 440, Ui.VW, 200, Ui.F.Lcd, 160, new Color(Ui.LcdRed.r, Ui.LcdRed.g, Ui.LcdRed.b, blink ? a * 0.4f : a), TextAnchor.MiddleCenter);
            }
            Ui.Text("Click or press any key to skip", 0, 1030, Ui.VW - 30, 30, Ui.F.Body, 16, new Color(1, 1, 1, 0.25f), TextAnchor.MiddleRight);
            if (Event.current.type == EventType.MouseDown) { EndIntro(); Event.current.Use(); }
        }

        // ================================================================== title

        private void DrawTitle()
        {
            Ui.Fill(0, 0, Ui.VW, Ui.VH, Ui.Void);
            var bg = Ui.Tex("Art/bg/title");
            if (bg != null)
            {
                float breathe = 1f + Mathf.Sin(Time.unscaledTime * 0.45f) * 0.01f;
                Ui.Image(bg, 540 - 20 * breathe, -10 * breathe, 1400 * breathe, 1100 * breathe, ScaleMode.ScaleAndCrop);
            }
            for (int i = 0; i < 40; i++) Ui.Fill(i * 22, 0, 22, Ui.VH, new Color(0.027f, 0.031f, 0.043f, Mathf.Clamp01(1.4f - i / 28f)));
            float x = 130;
            Ui.Text("HIKARI FORGE STUDIOS PRESENTS", x, 90, 700, 30, Ui.F.Label, 18, Ui.Dim);
            Ui.Text("THE", x, 130, 400, 70, Ui.F.LabelMed, 56, Ui.Bone);
            bool flick = Mathf.Repeat(Time.unscaledTime, 6f) > 5.85f;
            Ui.Text("3:13", x - 6, 175, 700, 230, Ui.F.Lcd, 230, flick ? new Color(1, 0.2f, 0.2f, 0.4f) : Ui.LcdRed);
            Ui.Text("MACHINE", x, 380, 800, 140, Ui.F.LabelMed, 120, Ui.Bone);
            Ui.Text("<i>Some customers shouldn't exist. Unfortunately, they still expect service.</i>", x, 525, 640, 60, Ui.F.Italic, 22, Ui.Dim);
            float y = 600;
            if (_hasSave && Ui.Button("Continue", x, y, 420, 50, Ui.B.Primary)) { S.ContinueGame(); }
            if (_hasSave) y += 60;
            if (Ui.Button("New Game", x, y, 420, 50, _hasSave ? Ui.B.Normal : Ui.B.Primary)) { S.NewGame(); _hasSave = true; }
            y += 60;
            bool endless = S.Profile.EndlessUnlocked(S.Db);
            if (Ui.Button(endless ? "Endless Mode" : "Endless Mode  (locked)", x, y, 420, 50, Ui.B.Normal, endless)) S.StartEndless();
            y += 60;
            if (Ui.Button($"Endings & Achievements  {S.Profile.EndingCount(S.Db)}/15", x, y, 420, 50)) S.Screen = GameScreen.Achievements;
            y += 60;
            if (Ui.Button("Settings", x, y, 420, 50)) { _settingsReturn = "title"; S.Screen = GameScreen.Settings; }
            y += 60;
            if (Ui.Button("Credits", x, y, 200, 50)) S.Screen = GameScreen.Credits;
            if (Ui.Button("Quit", x + 220, y, 200, 50)) Application.Quit();
            if (S.Settings.PlaytestTools)
            {
                y += 80;
                Ui.Text("PLAYTEST TOOLS (development builds only)", x, y, 600, 24, Ui.F.Label, 16, Ui.Dim);
                for (int n = 2; n <= 7; n++)
                    if (Ui.Button("N" + n, x + (n - 2) * 70, y + 28, 62, 36, Ui.B.Ghost, true, 16)) S.SkipToNight(n);
                if (Ui.Button("Unlock endless", x + 430, y + 28, 180, 36, Ui.B.Ghost, true, 16)) S.UnlockAllEndings();
            }
            Ui.Text($"v{Application.version}", 20, 1040, 300, 30, Ui.F.Body, 16, Ui.Dim);
        }

        // ================================================================== prep

        private void DrawPrep()
        {
            var st = S.State;
            Ui.Fill(0, 0, Ui.VW, Ui.VH, Ui.Void);
            var night = S.Endless ? null : S.Db.Night(st.Night);
            Ui.Text(S.Endless ? $"ENDLESS MODE · ROUND {Endless.Round(st)}" : $"CYCLE {st.Cycle} · NIGHT {st.Night} OF 7", 60, 30, 800, 30, Ui.F.Label, 20, Ui.Sodium);
            Ui.Text(S.Endless ? "The minute never closes." : night?.Title, 60, 60, 1200, 60, Ui.F.Label, 48);
            Ui.Text(night?.Briefing, 60, 118, 1300, 30, Ui.F.Body, 20, Ui.Dim);
            Ui.Text("CASH ON HAND", 1560, 40, 300, 24, Ui.F.Label, 16, Ui.Dim, TextAnchor.UpperRight);
            Ui.Text(Ui.Money(st.Money), 1460, 62, 400, 60, Ui.F.Lcd, 56, Ui.Emerald, TextAnchor.UpperRight);
            Ui.Fill(60, 160, 1800, 1, Ui.Edge);
            string[] tabs = { "stock", "upgrades", "handbook", "evidence" };
            string[] names = { "Supplier & Stock", "Upgrades", "Handbook", $"Evidence ({st.Clues.Count})" };
            for (int i = 0; i < tabs.Length; i++)
                if (Ui.Button(names[i], 60 + i * 250, 176, 236, 44, _prepTab == tabs[i] ? Ui.B.On : Ui.B.Ghost)) _prepTab = tabs[i];

            if (_prepTab == "stock") DrawStock(st);
            else if (_prepTab == "upgrades") DrawUpgrades(st);
            else if (_prepTab == "handbook") DrawHandbook(60, 240, 1800, 740);
            else DrawEvidence(60, 240, 1800, 740);

            Ui.Fill(60, 996, 1800, 1, Ui.Edge);
            if (Ui.Button("Main menu", 60, 1012, 200, 50, Ui.B.Ghost)) S.Screen = GameScreen.Title;
            if (Ui.Button("Start shift · 11:00 PM", 1520, 1008, 340, 58, Ui.B.Primary, true, 24)) S.StartShift();
        }

        private void DrawStock(GameState st)
        {
            Ui.Text("SUPPLIER CATALOG", 60, 240, 600, 30, Ui.F.Label, 22);
            string[] cols = { "PRODUCT", "COST", "SELLS", "BACK", "MACHINE" };
            float[] cx = { 60, 470, 570, 670, 760 };
            for (int i = 0; i < cols.Length; i++) Ui.Text(cols[i], cx[i], 276, 200, 24, Ui.F.Label, 15, Ui.Dim);
            float y = 304;
            foreach (var p in Campaign.Catalog(st, S.Db))
            {
                Ui.Image(Ui.Tex("Art/Products/" + p.Icon), 60, y, 34, 34);
                Ui.Text(p.Name, 104, y + 4, 360, 30, Ui.F.Body, 20);
                Ui.Text(Ui.Money(p.Cost), cx[1], y + 4, 100, 30, Ui.F.Lcd, 24);
                Ui.Text(Ui.Money(p.Price), cx[2], y + 4, 100, 30, Ui.F.Lcd, 24, Ui.Dim);
                Ui.Text(st.StockroomCount(p.Id).ToString(), cx[3], y + 4, 80, 30, Ui.F.Lcd, 24);
                Ui.Text(st.MachineCount(p.Id).ToString(), cx[4], y + 4, 80, 30, Ui.F.Lcd, 24);
                if (Ui.Button("+1", 850, y, 56, 34, Ui.B.Normal, st.Money >= p.Cost, 16) && st.BuyStock(p, 1)) S.Sfx("purchase", 0.5f);
                if (Ui.Button("+4", 912, y, 56, 34, Ui.B.Normal, st.Money >= p.Cost * 4, 16) && st.BuyStock(p, 4)) S.Sfx("purchase", 0.5f);
                y += 44;
            }
            Ui.Text($"LOAD THE MACHINE  <size={Mathf.RoundToInt(16 * Ui.Scale)}><color=#8b919c>pick a slot, then a product · slots hold {st.SlotCapacity}</color></size>", 1040, 240, 820, 30, Ui.F.Label, 22);
            DrawSlots(1040, 280, 820, 560, true);
            if (S.SelectedSlot != null && S.SelectedSlot != "E1")
            {
                Ui.Text($"LOAD SLOT {S.SelectedSlot} WITH:", 1040, 850, 400, 24, Ui.F.Label, 16, Ui.Sodium);
                float bx = 1040, by = 878;
                foreach (var p in S.Db.ProductList.Where(x => !x.Unusual && st.StockroomCount(x.Id) > 0))
                {
                    if (bx > 1700) { bx = 1040; by += 42; }
                    if (Ui.Button($"{p.Name} ({st.StockroomCount(p.Id)})", bx, by, 200, 36, Ui.B.Normal, true, 14)) { st.LoadSlot(S.SelectedSlot, p.Id); S.Sfx("restock", 0.5f); }
                    bx += 206;
                }
                if (Ui.Button("Empty slot", bx, by, 140, 36, Ui.B.Ghost, true, 14)) st.UnloadSlot(S.SelectedSlot);
            }
            if (Ui.Button("Auto-stock from stockroom", 1500, 940, 360, 44)) { st.AutoStock(S.Db); S.Sfx("restock", 0.5f); }
        }

        private void DrawUpgrades(GameState st)
        {
            int i = 0;
            foreach (var u in S.Db.UpgradeList)
            {
                float x = 60 + (i % 3) * 610, y = 250 + (i / 3) * 330;
                bool owned = st.Upgrades.Contains(u.Id), locked = u.UnlockNight > st.Night;
                Ui.Box(x, y, 590, 310, Ui.Panel, owned ? Ui.Hex("1f5b41") : Ui.Edge);
                Ui.Text(u.Name, x + 20, y + 18, 550, 34, Ui.F.Label, 26);
                Ui.Text(u.Description, x + 20, y + 60, 550, 140, Ui.F.Body, 20, Ui.Hex("b4b9c2"));
                Ui.Text(u.Effect, x + 20, y + 200, 550, 50, Ui.F.Lcd, 24, Ui.Emerald);
                Ui.Text(owned ? "Installed" : locked ? $"Available night {u.UnlockNight}" : Ui.Money(u.Price), x + 20, y + 262, 300, 34, Ui.F.Lcd, 28, Ui.Sodium);
                if (!owned && !locked && Ui.Button("Buy", x + 450, y + 258, 120, 40, Ui.B.Normal, st.Money >= u.Price) && st.BuyUpgrade(u))
                {
                    S.Sfx("purchase");
                    S.Toast(u.Name + " installed.");
                }
                i++;
            }
        }

        private void DrawSlots(float x, float y, float w, float h, bool prep)
        {
            var st = S.State;
            float gap = 8, cw = (w - gap * 3) / 4, ch = (h - gap * 4) / 4.6f;
            foreach (var sl in st.Slots)
            {
                if (sl.Code == "E1" && sl.ProductId == null && !prep) continue;
                int idx = st.Slots.IndexOf(sl);
                float sx, sy, sw = cw, sh = ch;
                if (sl.Code == "E1") { sx = x; sy = y + 4 * (ch + gap); sw = w; sh = ch * 0.6f; }
                else { sx = x + (idx % 4) * (cw + gap); sy = y + (idx / 4) * (ch + gap); }
                var p = S.Db.Product(sl.ProductId);
                bool selected = prep ? S.SelectedSlot == sl.Code : S.Keypad == sl.Code;
                bool rowE = sl.Code == "E1";
                Ui.Box(sx, sy, sw, sh, rowE ? Ui.Hex("1a1428") : Ui.Hex("161a22"), selected ? Ui.Sodium : rowE ? Ui.Hex("4a3a73") : Ui.Edge);
                Ui.Text(sl.Code, sx + 6, sy + 2, 60, 26, Ui.F.Lcd, 22, rowE ? Ui.Violet : Ui.Emerald);
                if (p != null && sl.Count > 0)
                {
                    float icon = Mathf.Min(sh * 0.45f, 52);
                    Ui.Image(Ui.Tex("Art/Products/" + p.Icon), sx + (sw - icon) / 2, sy + 22, icon, icon);
                    Ui.Text(p.Name, sx + 4, sy + 24 + icon, sw - 8, 40, Ui.F.Label, 15, Ui.Fg, TextAnchor.UpperCenter);
                    Ui.Text($"×{sl.Count} {(p.Price > 0 ? Ui.Money(p.Price) : "")}", sx, sy + sh - 24, sw, 22, Ui.F.Lcd, 19, Ui.Dim, TextAnchor.UpperCenter);
                }
                else Ui.Text(rowE ? "ROW E" : "empty", sx, sy + sh / 2 - 10, sw, 24, Ui.F.Label, 15, Ui.Dim, TextAnchor.UpperCenter);
                if (GUI.Button(Ui.R(sx, sy, sw, sh), GUIContent.none, GUIStyle.none))
                {
                    if (prep) S.SelectedSlot = S.SelectedSlot == sl.Code ? null : sl.Code;
                    else { S.Keypad = sl.Code; S.Sfx("key_press", 0.4f); }
                }
            }
        }

        // ================================================================== shift

        private void DrawShift()
        {
            var ctx = S.Ctx;
            var st = S.State;
            var cur = ctx.Current;
            Ui.Fill(0, 0, Ui.VW, Ui.VH, Ui.Void);

            // ---------- scene
            float shakeX = S.Settings.ReducedShake ? 0 : (float)(_rng.NextDouble() - 0.5) * 18 * _shake;
            string weather = ctx.NightDef.Weather;
            var bg = Ui.Tex($"Art/bg/{weather}{(ctx.InMargin ? "_margin" : "")}");
            GUI.BeginGroup(Ui.R(0, HudH, SceneW, Ui.VH - HudH));
            GUI.EndGroup();
            Ui.Image(bg, shakeX, HudH, SceneW, Ui.VH - HudH, ScaleMode.ScaleAndCrop);
            DrawWeather(weather);
            if (cur != null)
            {
                var tex = Ui.Tex("Art/Customers/" + Portrait(cur));
                float age = Mathf.Clamp01((float)(S.Now % 1000));
                float leaving = cur.Stage == EncounterStage.Resolving ? 0.65f : 1f;
                Ui.Image(tex, 420 + shakeX, 150, 600, 800, ScaleMode.ScaleToFit, leaving);
                // Reflection in the machine glass (right edge of the scene).
                if (tex != null && !cur.Def.NoReflection)
                {
                    float lag = cur.Def.ReflectionLags ? Mathf.Sin(Time.unscaledTime * 0.9f) * 18f : 0f;
                    var old = GUI.color;
                    GUI.color = new Color(0.7f, 0.85f, 0.8f, 0.16f);
                    GUI.DrawTextureWithTexCoords(Ui.R(1170 + lag, 420, 240, 320), tex, new Rect(1, 0, -1, 1));
                    GUI.color = old;
                }
            }
            else if (ctx.Phase == ShiftPhase.Idle)
            {
                string note = ctx.KnockActive ? "Something is knocking inside the machine." : ctx.BlackoutActive ? "The lot is dark." : "The lot is empty. The machine hums.";
                Ui.Text($"<i>{note}</i>", 0, 480, SceneW, 40, Ui.F.Italic, 22, Ui.Dim, TextAnchor.MiddleCenter);
            }
            DrawAtmosphere();
            if (cur != null) DrawDialogue(cur);

            // ---------- HUD
            Ui.Fill(0, 0, Ui.VW, HudH, Ui.Hex("0b0d12"));
            Ui.Fill(0, HudH - 1, Ui.VW, 1, Ui.Edge);
            Ui.Text(S.Endless ? $"ROUND {Endless.Round(st)}" : $"NIGHT {st.Night}", 24, 10, 160, 40, Ui.F.Lcd, 32, Ui.Sodium);
            Ui.Text(ContentDatabase.FormatTime(ctx.Clock), 170, 10, 180, 40, Ui.F.Lcd, 32);
            Ui.Text(Ui.Money(st.Money), 330, 10, 200, 40, Ui.F.Lcd, 32, Ui.Emerald);
            Ui.Text("NERVES", 520, 19, 90, 24, Ui.F.Label, 15, Ui.Dim);
            Ui.Meter(600, 24, 180, 10, st.Instability / 100f, Ui.Emerald, Ui.Blood);
            float bx = 830;
            if (Ui.Button("Handbook [H]", bx, 8, 168, 40, S.Overlay == GameOverlay.Handbook ? Ui.B.On : Ui.B.Normal, true, 16)) Toggle(GameOverlay.Handbook); bx += 176;
            if (Ui.Button("Cameras [M]", bx, 8, 150, 40, S.Overlay == GameOverlay.Monitor ? Ui.B.On : Ui.B.Normal, true, 16)) Toggle(GameOverlay.Monitor); bx += 158;
            if (Ui.Button("Maint. panel", bx, 8, 150, 40, S.KnockActive ? Ui.B.Danger : Ui.B.Normal, true, 16)) S.OpenPanel(); bx += 158;
            if (Ui.Button("Evidence [E]", bx, 8, 150, 40, S.Overlay == GameOverlay.Evidence ? Ui.B.On : Ui.B.Normal, true, 16)) Toggle(GameOverlay.Evidence); bx += 158;
            if (Ui.Button("Restock", bx, 8, 110, 40, Ui.B.Normal, true, 16)) Toggle(GameOverlay.Restock); bx += 118;
            if (Ui.Button("Receipts", bx, 8, 120, 40, Ui.B.Normal, true, 16)) Toggle(GameOverlay.Records); bx += 128;
            if (Ui.Button("Leave", bx, 8, 90, 40, Ui.B.Ghost, true, 16)) S.Overlay = GameOverlay.ConfirmLeave;

            DrawMachine(ctx, st, cur);
            DrawShiftOverlay();
        }

        private string Portrait(ActiveEncounter cur) =>
            cur.Stage == EncounterStage.Resolving && !string.IsNullOrEmpty(cur.Outcome?.Portrait) ? cur.Outcome.Portrait : cur.Def.Portrait;

        private void DrawWeather(string weather)
        {
            float t = Time.unscaledTime;
            if (weather == "rain" || weather == "storm" || weather == "flood")
            {
                int drops = weather == "storm" ? 140 : 90;
                var c = new Color(0.7f, 0.78f, 0.9f, 0.18f);
                for (int i = 0; i < drops; i++)
                {
                    float seed = i * 37.17f;
                    float x = Mathf.Repeat(seed * 13.3f - t * 120f, SceneW);
                    float y = HudH + Mathf.Repeat(seed * 7.1f + t * (900f + i % 7 * 60), Ui.VH - HudH);
                    Ui.Fill(x, y, 1.5f, 26, c);
                }
                if (weather == "storm" && Mathf.Repeat(t, 11f) < 0.12f && !S.Settings.ReducedFlashing)
                    Ui.Fill(0, HudH, SceneW, Ui.VH, new Color(0.8f, 0.85f, 1f, 0.25f));
            }
            if (weather == "fog" || weather == "margin")
            {
                for (int i = 0; i < 5; i++)
                {
                    float x = Mathf.Repeat(i * 400 + t * 12f, SceneW + 600) - 600;
                    Ui.Fill(x, HudH + 380 + i * 60, 700, 140, new Color(0.6f, 0.62f, 0.66f, 0.06f));
                }
            }
            if (S.Ctx.InMargin) Ui.Fill(0, HudH, SceneW, Ui.VH, new Color(0.25f, 0.05f, 0.3f, 0.12f));
        }

        private void DrawAtmosphere()
        {
            float now = (float)S.Now;
            if (S.LightsOut) Ui.Fill(0, HudH, SceneW, Ui.VH, new Color(0, 0, 0, 0.86f));
            if (now < S.FlickerUntil && !S.Settings.ReducedFlashing && _rng.NextDouble() < 0.5)
                Ui.Fill(0, HudH, SceneW, Ui.VH, new Color(0, 0, 0, 0.6f));
            if (S.State.Instability >= 60)
                Ui.Fill(0, HudH, SceneW, Ui.VH, new Color(0.3f, 0, 0.1f, (S.State.Instability - 60) / 400f));
            if (S.HallucinationId != null && now < S.HallucinationUntil)
            {
                float a = Mathf.Sin(Mathf.Clamp01((float)(S.HallucinationUntil - now) / 2.5f) * Mathf.PI) * 0.7f;
                Ui.Fill(180, 520, 80, 280, new Color(0, 0, 0, a));
                if (S.HallucinationId == "hal_wrong_text") Ui.Text("HELLO NO. 0", 0, 200, SceneW, 80, Ui.F.Lcd, 60, new Color(Ui.Violet.r, Ui.Violet.g, Ui.Violet.b, a), TextAnchor.MiddleCenter);
            }
            if (S.ScareId != null && now < S.ScareUntil)
            {
                float left = (float)(S.ScareUntil - now);
                if (S.ScareFullscreen)
                {
                    float a = Mathf.Clamp01(left / 1.6f);
                    if (left > 1.5f) _shake = S.Settings.ReducedShake ? 0 : 1f;
                    Ui.Fill(0, 0, Ui.VW, Ui.VH, new Color(0, 0, 0, 0.6f * a));
                    float alpha = S.Settings.ScareLevel == 2 ? a : a * 0.55f;
                    Ui.Image(Ui.Tex("Art/Scares/" + S.ScareId), 0, 0, Ui.VW, Ui.VH, ScaleMode.ScaleAndCrop, alpha);
                }
                else
                {
                    float p = 1f - left / 2.4f;
                    float a = Mathf.Sin(p * Mathf.PI) * 0.85f;
                    switch (S.ScareId)
                    {
                        case "shadow_pass": Ui.Fill(-100 + p * 1700, 560, 70, 260, new Color(0, 0, 0, a)); break;
                        case "rush_figure": float s = 0.3f + p * 2.5f; Ui.Fill(720 - 35 * s, 820 - 260 * s, 70 * s, 260 * s, new Color(0, 0, 0, a)); break;
                        case "distant_figure": Ui.Fill(300, 600, 22, 90, new Color(0, 0, 0, a)); break;
                        case "figure_window": Ui.Fill(1150, 460, 90, 330, new Color(0, 0, 0, a)); break;
                        case "face_glass":
                        case "hands_glass":
                            Ui.Image(Ui.Tex("Art/Scares/" + S.ScareId), 470, 150, 500, 500, ScaleMode.ScaleToFit, a * 0.6f); break;
                        case "light_flicker":
                        case "lights_fail":
                            if (!S.Settings.ReducedFlashing && _rng.NextDouble() < 0.5) Ui.Fill(0, HudH, SceneW, Ui.VH, new Color(0, 0, 0, 0.7f)); break;
                        case "reality_warp":
                        case "margin_surge":
                            Ui.Fill(0, HudH, SceneW, Ui.VH, new Color(0.35f, 0.1f, 0.45f, a * 0.35f)); break;
                    }
                }
            }
        }

        private void DrawDialogue(ActiveEncounter cur)
        {
            float x = 20, w = SceneW - 40;
            var line = S.CurrentLine;
            bool awaiting = cur.Stage == EncounterStage.Awaiting && !S.LinesPending;
            var talk = awaiting ? S.Ctx.AvailableTalk() : new List<TalkDef>();
            var spots = awaiting ? S.Ctx.InspectSpots() : new List<string>();
            float h = 150 + (awaiting ? 70 + (talk.Count > 0 ? 54 * Mathf.Ceil(talk.Count / 2f) : 0) : 0)
                      + (awaiting && S.InspectText != null ? Ui.TextHeight(S.InspectText, w - 40, Ui.F.Body, 20) + 20 : 0);
            float y = Ui.VH - 20 - h;
            Ui.Box(x, y, w, h, new Color(0.03f, 0.035f, 0.05f, 0.92f), Ui.Edge);
            string name = line?.Speaker ?? cur.Def.Name;
            Ui.Text(name.ToUpperInvariant(), x + 20, y + 14, 600, 30, Ui.F.Label, 22, Ui.Sodium, TextAnchor.UpperLeft, false);
            string wants = !string.IsNullOrEmpty(cur.Def.RequestText) ? cur.Def.RequestText : S.Db.Product(cur.Def.Request)?.Name;
            if (!string.IsNullOrEmpty(wants)) Ui.Text("Wants: " + wants, x + 420, y + 14, 600, 30, Ui.F.Lcd, 24, Ui.Emerald, TextAnchor.UpperLeft, false);
            if (cur.Stage == EncounterStage.Awaiting && cur.Def.Patience < 5000)
                Ui.Meter(x + w - 260, y + 24, 230, 6, cur.PatienceLeft / cur.Def.Patience, Ui.Blood, Ui.Sodium);
            if (line != null)
            {
                string text = line.Narration ? $"<i>{line.Text}</i>" : line.Text;
                Ui.Text(text, x + 20, y + 52, w - 220, 90, line.Narration ? Ui.F.Italic : Ui.F.Body, 25, line.Narration ? Ui.Hex("b9bdc6") : Ui.Fg);
            }
            if (S.LinesPending || cur.Stage != EncounterStage.Awaiting)
            {
                string label = cur.Stage == EncounterStage.Resolving && !S.LinesPending ? "Continue ▸" : "Next ▸";
                if (Ui.Button(label + "  [Space]", x + w - 200, y + h - 56, 180, 42, Ui.B.Normal, true, 16)) S.AdvanceLine();
            }
            if (!awaiting) return;
            float cy = y + 150;
            if (S.InspectText != null)
            {
                float ih = Ui.TextHeight(S.InspectText, w - 40, Ui.F.Body, 20);
                Ui.Fill(x + 20, cy - 6, 3, ih + 8, Ui.Emerald);
                Ui.Text($"<b>{S.InspectLabel}:</b> {S.InspectText}", x + 32, cy - 4, w - 52, ih + 10, Ui.F.Body, 20);
                cy += ih + 20;
            }
            Ui.Text("INSPECT", x + 20, cy + 10, 100, 24, Ui.F.Label, 15, Ui.Dim);
            float bx = x + 110;
            foreach (var sp in spots)
            {
                var def = cur.Def.Inspect.FirstOrDefault(i => i.Spot == sp);
                string label = def?.Label ?? char.ToUpperInvariant(sp[0]) + sp.Substring(1);
                if (Ui.Button(label, bx, cy, 150, 40, Ui.B.Ghost, true, 16)) S.Inspect(sp);
                bx += 158;
            }
            cy += 54;
            for (int i = 0; i < talk.Count; i++)
            {
                float tx = x + 110 + (i % 2) * ((w - 130) / 2), ty = cy + (i / 2) * 54;
                if (i == 0) Ui.Text("SAY", x + 20, ty + 10, 100, 24, Ui.F.Label, 15, Ui.Dim);
                if (Ui.WrapButton(talk[i].Text, tx, ty, (w - 140) / 2, out _, talk[i].End ? Ui.B.Decision : Ui.B.Normal, 17)) S.Talk(talk[i]);
            }
        }

        private void DrawMachine(ShiftContext ctx, GameState st, ActiveEncounter cur)
        {
            float x = MachineX, w = Ui.VW - MachineX;
            Ui.Fill(x, HudH, w, Ui.VH - HudH, Ui.Hex("0e1117"));
            Ui.Fill(x, HudH, 1, Ui.VH - HudH, Ui.Edge);
            string display = S.Now < S.MachineDisplayUntil ? S.MachineDisplay : ctx.WrongTimeActive ? "3:13 AM" : ContentDatabase.FormatTime(ctx.Clock);
            Ui.Box(x + 20, HudH + 16, w - 40, 72, Ui.Hex("04120c"), Ui.Hex("1b3b2d"));
            Color lcd = S.LightsOut ? Ui.Hex("10251c") : ctx.WrongTimeActive ? Ui.Violet : Ui.Emerald;
            Ui.Text(display, x + 20, HudH + 16, w - 40, 72, Ui.F.Lcd, 46, lcd, TextAnchor.MiddleCenter);
            DrawSlots(x + 20, HudH + 104, w - 40, 470, false);
            float ky = HudH + 590;
            Ui.Box(x + 20, ky, w - 40, 44, Ui.Hex("0a0b0f"), Ui.Edge);
            Ui.Text(S.Keypad == "" ? "— —" : S.Keypad, x + 20, ky, w - 40, 44, Ui.F.Lcd, 36, Ui.Sodium, TextAnchor.MiddleCenter);
            string[] keys = { "A", "B", "C", "D", "E", "1", "2", "3", "4", "CLR" };
            float kw = (w - 40 - 32) / 5;
            for (int i = 0; i < keys.Length; i++)
                if (Ui.Button(keys[i], x + 20 + (i % 5) * (kw + 8), ky + 54 + (i / 5) * 52, kw, 44, Ui.B.Normal, true, 22)) S.PressKey(keys[i]);
            bool can = cur != null && cur.Stage != EncounterStage.Resolving;
            float by = ky + 166;
            if (Ui.Button("Dispense", x + 20, by, (w - 50) / 2, 52, Ui.B.Primary, can, 22)) S.Dispense();
            if (Ui.Button("Refuse service", x + 30 + (w - 50) / 2, by, (w - 50) / 2, 52, Ui.B.Danger, can, 20)) S.Refuse();
            var sel = st.Slot(S.Keypad);
            if (sel != null && ctx.CanConsume(sel.ProductId) && sel.Count > 0)
                if (Ui.Button($"Take {S.Db.Product(sel.ProductId).Name} yourself", x + 20, by + 60, w - 40, 36, Ui.B.Ghost, true, 15)) ctx.ConsumeForSelf(S.Keypad);
            float ty = by + 104;
            Ui.Box(x + 20, ty, w - 40, 64, Ui.Hex("07080b"), Ui.Edge);
            if (S.LastDispensed != null && S.Now - S.DispenseAt < 6)
            {
                float drop = Mathf.Clamp01((float)(S.Now - S.DispenseAt) / 0.4f);
                Ui.Image(Ui.Tex("Art/Products/" + S.Db.Product(S.LastDispensed)?.Icon), x + w / 2 - 26, ty + 8 - (1 - drop) * 40, 52, 52);
            }
            if (S.LastReceipt != null)
            {
                float rh = Mathf.Min(Ui.VH - (ty + 76) - 10, Ui.TextHeight(S.LastReceipt, w - 64, Ui.F.Type, 16) + 20);
                Ui.Box(x + 20, ty + 76, w - 40, rh, Ui.Paper, Ui.Hex("b8ae96"));
                Ui.Text(S.LastReceipt, x + 32, ty + 86, w - 64, rh - 10, Ui.F.Type, 16, Ui.Ink);
            }
        }

        // ================================================================== overlays

        private void Modal(float w, float h, out float x, out float y, bool dim = true)
        {
            if (dim) Ui.Fill(0, 0, Ui.VW, Ui.VH, new Color(0.01f, 0.015f, 0.02f, 0.8f));
            x = (Ui.VW - w) / 2;
            y = (Ui.VH - h) / 2;
            Ui.Box(x, y, w, h, Ui.Panel, Ui.Edge);
            // Swallow clicks behind the modal.
            if (Event.current.type == EventType.MouseDown && !Ui.Hover(x, y, w, h)) Event.current.Use();
        }

        private bool CloseButton(float x, float y)
        {
            return Ui.Button("Close", x, y, 140, 44);
        }

        private void DrawShiftOverlay()
        {
            float x, y;
            switch (S.Overlay)
            {
                case GameOverlay.Handbook:
                    Modal(1600, 900, out x, out y);
                    DrawHandbook(x + 30, y + 30, 1540, 780);
                    if (CloseButton(x + 30, y + 830)) S.Overlay = GameOverlay.None;
                    break;
                case GameOverlay.Monitor:
                    Modal(1300, 900, out x, out y);
                    DrawMonitor(x + 30, y + 30);
                    if (CloseButton(x + 30, y + 830)) S.Overlay = GameOverlay.None;
                    break;
                case GameOverlay.Evidence:
                    Modal(1600, 900, out x, out y);
                    DrawEvidence(x + 30, y + 30, 1540, 780);
                    if (CloseButton(x + 30, y + 830)) S.Overlay = GameOverlay.None;
                    break;
                case GameOverlay.Records:
                {
                    Modal(700, 900, out x, out y);
                    Ui.Text("TRANSACTION HISTORY — TONIGHT", x + 30, y + 24, 640, 34, Ui.F.Label, 24);
                    float ry = y + 70;
                    foreach (var t in S.State.Transactions.Where(t => t.Night == S.State.Night).Reverse())
                    {
                        float rh = Ui.TextHeight(t.Receipt, 600, Ui.F.Type, 15) + 16;
                        if (ry + rh > y + 820) break;
                        Ui.Box(x + 30, ry, 640, rh, Ui.Paper, Ui.Hex("b8ae96"));
                        Ui.Text(t.Receipt, x + 40, ry + 8, 620, rh, Ui.F.Type, 15, Ui.Ink);
                        ry += rh + 10;
                    }
                    if (CloseButton(x + 30, y + 836)) S.Overlay = GameOverlay.None;
                    break;
                }
                case GameOverlay.Restock:
                {
                    var ctx = S.Ctx;
                    Modal(820, 900, out x, out y);
                    Ui.Text("RESTOCK FROM THE STOCKROOM", x + 30, y + 24, 760, 34, Ui.F.Label, 24);
                    string note = ctx.CanRestockNow ? (S.State.Upgrades.Contains(UpgradeIds.FastRestock) ? "Rear hatch: 2 minutes per slot, even with a customer waiting." : "Takes 12 minutes per slot. Only between customers.") : "You can't restock while a customer is waiting. (Faster Restocking allows it.)";
                    Ui.Text(note, x + 30, y + 64, 760, 30, Ui.F.Body, 18, Ui.Dim);
                    float ry = y + 110;
                    foreach (var sl in S.State.Slots.Where(s => s.Code != "E1"))
                    {
                        var p = S.Db.Product(sl.ProductId);
                        Ui.Text(sl.Code, x + 30, ry + 6, 60, 30, Ui.F.Lcd, 26, Ui.Emerald);
                        Ui.Text($"{p?.Name ?? "empty"} ×{sl.Count}", x + 90, ry + 8, 380, 30, Ui.F.Body, 19);
                        if (p != null && S.State.StockroomCount(p.Id) > 0 && sl.Count < S.State.SlotCapacity)
                            if (Ui.Button($"Top up ({S.State.StockroomCount(p.Id)} in back)", x + 470, ry, 320, 38, Ui.B.Normal, ctx.CanRestockNow, 15)) ctx.Restock(sl.Code, sl.ProductId);
                        ry += 44;
                    }
                    if (CloseButton(x + 30, y + 836)) S.Overlay = GameOverlay.None;
                    break;
                }
                case GameOverlay.Panel:
                {
                    Modal(760, 800, out x, out y);
                    Ui.Text("MAINTENANCE PANEL — UNIT 313", x + 30, y + 24, 700, 34, Ui.F.Label, 24);
                    if (S.KnockActive) Ui.Text("The knocking is right behind the panel.", x + 30, y + 62, 700, 30, Ui.F.Semi, 20, Ui.Blood);
                    Ui.Text(S.PanelMessage, x + 30, y + 100, 700, 36, Ui.F.Lcd, 30, Ui.Emerald);
                    var st = S.State;
                    string diag = $"COIN BOX ........ {(st.Flag("marked") > 0 ? "0 0 0 0 0 0" : "NON-CURRENCY")}\nROW E ........... {st.Slot("E1").ProductId ?? "EMPTY"}\nUNIT AGE ........ 38 YRS (SALVAGE 1988)\nATTENDANT BOND .. {new string('■', Math.Min(8, st.Bond))}{new string('□', Math.Max(0, 8 - st.Bond))}";
                    Ui.Text(diag, x + 30, y + 146, 700, 130, Ui.F.Lcd, 24, Ui.Dim);
                    if (Ui.Button("Reset motor", x + 30, y + 286, 200, 42)) S.ResetMotor();
                    Ui.Text("OVERRIDE KEYPAD — DISTRICT USE ONLY", x + 30, y + 350, 700, 26, Ui.F.Label, 18, Ui.Violet);
                    Ui.Box(x + 30, y + 382, 300, 50, Ui.Hex("0a0b0f"), Ui.Edge);
                    Ui.Text(S.PanelCode.PadRight(4, '_'), x + 30, y + 382, 300, 50, Ui.F.Lcd, 40, Ui.Sodium, TextAnchor.MiddleCenter);
                    string[] k = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "CLR", "0", "ENT" };
                    for (int i = 0; i < k.Length; i++)
                        if (Ui.Button(k[i], x + 30 + (i % 3) * 104, y + 444 + (i / 3) * 58, 96, 50, Ui.B.Normal, true, 22)) S.PanelKey(k[i]);
                    if (Ui.Button("Close panel", x + 30, y + 730, 200, 46)) S.ClosePanel();
                    break;
                }
                case GameOverlay.Phone:
                {
                    var call = S.Ctx.PendingPhone;
                    var lines = call?.Lines ?? new List<string>();
                    float h = 200 + lines.Sum(l => Ui.TextHeight(l, 840, Ui.F.Body, 22) + 12);
                    Modal(900, h, out x, out y);
                    Ui.Text("INCOMING CALL", x + 30, y + 24, 600, 26, Ui.F.Label, 18, Ui.Sodium);
                    Ui.Text(call?.Text ?? S.Ctx.NightDef.PhoneSpeaker, x + 30, y + 52, 840, 40, Ui.F.Label, 30, Ui.Sodium);
                    float ly = y + 104;
                    foreach (var l in lines)
                    {
                        bool narr = l.StartsWith("[");
                        float lh = Ui.TextHeight(l, 840, Ui.F.Body, 22);
                        Ui.Text(narr ? $"<i>{l}</i>" : l, x + 30, ly, 840, lh + 4, narr ? Ui.F.Italic : Ui.F.Body, 22, narr ? Ui.Dim : Ui.Fg);
                        ly += lh + 12;
                    }
                    if (Ui.Button("Hang up", x + 30, y + h - 70, 200, 48, Ui.B.Primary))
                    {
                        S.Ctx.AcknowledgePhone();
                        S.Overlay = GameOverlay.None;
                        S.Sfx("phone_hangup");
                    }
                    break;
                }
                case GameOverlay.Investigation: DrawInvestigation(); break;
                case GameOverlay.ConfirmLeave:
                    Modal(700, 300, out x, out y);
                    Ui.Text("LEAVE THE BOOTH?", x + 30, y + 24, 640, 34, Ui.F.Label, 26);
                    Ui.Text("Rule A2: Do not leave the attendant booth between 11:00 PM and 6:00 AM for any reason.", x + 30, y + 74, 640, 80, Ui.F.Body, 21);
                    if (Ui.Button("Stay inside", x + 30, y + 210, 220, 50)) S.Overlay = GameOverlay.None;
                    if (Ui.Button("Open the door", x + 270, y + 210, 240, 50, Ui.B.Danger)) S.LeaveBooth();
                    break;
            }
        }

        private void DrawHandbook(float x, float y, float w, float h)
        {
            var pages = Campaign.VisiblePages(S.State, S.Db);
            var page = pages.FirstOrDefault(p => p.Id == S.HandbookPage) ?? pages.FirstOrDefault();
            Ui.Text("MERIDIAN ROADSIDE SERVICES", x, y, 340, 24, Ui.F.Label, 15, Ui.Sodium);
            Ui.Text("Night Attendant Handbook", x, y + 24, 340, 34, Ui.F.Label, 24);
            float ly = y + 70;
            foreach (var p in pages)
            {
                bool on = p == page;
                if (on) Ui.Fill(x, ly, 3, 34, Ui.Sodium);
                Ui.Text(p.Title, x + 12, ly + 4, 320, 34, Ui.F.Body, 17, p.Forged ? Ui.Hex("b9a0ff") : on ? Ui.Fg : Ui.Dim, TextAnchor.UpperLeft, false);
                if (GUI.Button(Ui.R(x, ly, 340, 34), GUIContent.none, GUIStyle.none)) { S.HandbookPage = p.Id; S.Sfx("paper_rustle", 0.4f); }
                ly += 38;
            }
            if (page == null) return;
            float px = x + 370, pw = w - 370;
            Ui.Box(px, y, pw, h, Ui.Paper, Ui.Hex("b8ae96"));
            Ui.Text(page.Title, px + 40, y + 34, pw - 260, 80, Ui.F.Type, 30, Ui.Ink);
            if (page.Stamped)
            {
                Ui.Frame(px + pw - 190, y + 24, 150, 90, Ui.Hex("1f7a52"), 3);
                Ui.Text("DISTRICT OFFICE\n✦ APPROVED ✦", px + pw - 190, y + 24, 150, 90, Ui.F.Label, 16, Ui.Hex("1f7a52"), TextAnchor.MiddleCenter);
            }
            if (page.Forged)
            {
                Ui.Frame(px + pw - 200, y + 30, 160, 56, Ui.Hex("6f3fd6"), 3);
                Ui.Text("REV. 3:13", px + pw - 200, y + 30, 160, 56, Ui.F.Label, 26, Ui.Hex("6f3fd6"), TextAnchor.MiddleCenter);
            }
            float cy = y + 120;
            if (!string.IsNullOrEmpty(page.Body))
            {
                float bh = Ui.TextHeight(page.Body, pw - 80, Ui.F.Type, 20);
                Ui.Text(page.Body, px + 40, cy, pw - 80, bh + 6, Ui.F.Type, 20, Ui.Ink);
                cy += bh + 20;
            }
            foreach (var r in page.Rules)
            {
                var parts = r.Split(new[] { '|' }, 2);
                string text = $"<b>{parts[0]}.</b> {(parts.Length > 1 ? parts[1] : "")}";
                float rh = Ui.TextHeight(text, pw - 80, Ui.F.Type, 21);
                Ui.Text(text, px + 40, cy, pw - 80, rh + 6, Ui.F.Type, 21, Ui.Ink);
                cy += rh + 14;
            }
            foreach (var a in Campaign.VisibleAnnotations(page, S.State, S.Db))
            {
                Color ink = a.Ink == "purple" ? Ui.Hex("5d32b8") : a.Ink == "red" ? Ui.Hex("9a1f22") : Ui.Hex("1c6e48");
                float ah = Ui.TextHeight(a.Text, pw - 120, Ui.F.Hand, 36);
                Ui.Text(a.Text, px + 70, cy, pw - 120, ah + 6, Ui.F.Hand, 36, ink);
                cy += ah + 8;
            }
        }

        private void DrawMonitor(float x, float y)
        {
            var ctx = S.Ctx;
            var cams = ShiftContext.Cameras.Where(c => (c != "cam4" || S.State.Upgrades.Contains(UpgradeIds.Monitor)) && (c != "cam0" || S.State.Flag("cam0") > 0)).ToList();
            string[] names = { "CAM 1 · LOT", "CAM 2 · UNIT", "CAM 3 · STOCKROOM", "CAM 4 · ROUTE 9", "CAM 0 · OBSERVATION" };
            if (!cams.Contains(S.MonitorCam)) S.MonitorCam = "cam1";
            float bx = x;
            foreach (var c in cams)
            {
                string label = names[Array.IndexOf(ShiftContext.Cameras, c)];
                if (Ui.Button(label, bx, y, 236, 40, S.MonitorCam == c ? Ui.B.On : Ui.B.Normal, true, 15)) { S.MonitorCam = c; S.Sfx("cam_switch", 0.4f); }
                bx += 244;
            }
            float fx = x, fy = y + 56, fw = 1240, fh = 640;
            var view = ctx.CameraView(S.MonitorCam);
            bool ok = ctx.CameraAvailable(S.MonitorCam);
            Ui.Fill(fx, fy, fw, fh, Ui.Hex("060908"));
            if (!ok)
            {
                for (int i = 0; i < 80; i++) Ui.Fill(fx, fy + i * 8, fw, 3, new Color(0.15f, 0.15f, 0.15f, (float)_rng.NextDouble() * 0.5f));
                Ui.Text("NO SIGNAL", fx, fy, fw, fh, Ui.F.Lcd, 80, Ui.Dim, TextAnchor.MiddleCenter);
            }
            else
            {
                bool enhanced = S.State.Upgrades.Contains(UpgradeIds.Monitor);
                Color tint = enhanced ? new Color(0.75f, 1f, 0.82f) : new Color(0.55f, 0.75f, 0.6f);
                Ui.Image(Ui.Tex("Art/cams/" + S.MonitorCam), fx, fy, fw, fh, ScaleMode.ScaleAndCrop, 1f, tint);
                string fig = view.State == "present" && ctx.Current != null ? ctx.Current.Def.Portrait
                    : view.State.StartsWith("other:") || view.State.StartsWith("extra:") ? view.State.Split(':')[1] : null;
                bool extra = view.State.StartsWith("extra:");
                if (extra && ctx.Current != null) Ui.Image(Ui.Tex("Art/Customers/" + ctx.Current.Def.Portrait), fx + fw / 2 - 225, fy + 60, 450, 600, ScaleMode.ScaleToFit, 0.9f, tint);
                if (fig != null)
                {
                    if (extra) Ui.Image(Ui.Tex("Art/Customers/" + fig), fx + 150, fy + 200, 200, 266, ScaleMode.ScaleToFit, 0.8f, tint);
                    else Ui.Image(Ui.Tex("Art/Customers/" + fig), fx + fw / 2 - 225, fy + 60, 450, 600, ScaleMode.ScaleToFit, 0.9f, tint);
                }
                float scan = Mathf.Repeat(Time.unscaledTime * 80f, fh);
                for (int i = 0; i < fh; i += 6) Ui.Fill(fx, fy + i, fw, 2, new Color(0, 0, 0, 0.25f));
                Ui.Fill(fx, fy + scan, fw, 30, new Color(1, 1, 1, 0.03f));
                Ui.Text($"{names[Array.IndexOf(ShiftContext.Cameras, S.MonitorCam)]} · {view.Timestamp}", fx + 16, fy + 10, 900, 40, Ui.F.Lcd, 32, Ui.Hex("e4ffe9"));
                if (view.State == "absent") Ui.Text("NO SUBJECT DETECTED", fx, fy + fh - 50, fw - 20, 40, Ui.F.Lcd, 28, Ui.Hex("ff9c9c"), TextAnchor.UpperRight);
            }
            string note = !ok ? "The camera has no power." : !string.IsNullOrEmpty(view.Note) ? view.Note
                : ctx.Current == null ? "Nothing moves." : view.State == "absent" ? "The customer does not appear on this camera." : "The customer is visible on this camera.";
            Ui.Text(note, fx, fy + fh + 14, fw, 60, Ui.F.Body, 22);
        }

        private void DrawEvidence(float x, float y, float w, float h)
        {
            var clues = S.State.Clues.Select(c => S.Db.Clues.TryGetValue(c, out var d) ? d : null).Where(c => c != null).ToList();
            var sel = clues.FirstOrDefault(c => c.Id == S.SelectedClue) ?? clues.FirstOrDefault();
            Ui.Text($"EVIDENCE · {S.State.KeyClueCount(S.Db)} KEY ITEMS", x, y, 400, 26, Ui.F.Label, 17, Ui.Sodium);
            float ly = y + 36;
            foreach (var c in clues)
            {
                bool on = c == sel;
                if (on) Ui.Fill(x, ly, 3, 30, Ui.Sodium);
                Ui.Text((c.Key ? "★ " : "") + c.Title, x + 12, ly + 3, 380, 30, Ui.F.Body, 17, on ? Ui.Fg : Ui.Dim, TextAnchor.UpperLeft, false);
                if (GUI.Button(Ui.R(x, ly, 400, 30), GUIContent.none, GUIStyle.none)) S.SelectedClue = c.Id;
                ly += 32;
            }
            if (clues.Count == 0) Ui.Text("Nothing yet. Inspect customers closely.", x, y + 40, 400, 30, Ui.F.Body, 18, Ui.Dim);
            if (sel == null) return;
            Ui.Box(x + 430, y, w - 430, h, Ui.Hex("151922"), Ui.Edge);
            Ui.Text(sel.Kind.ToUpperInvariant(), x + 460, y + 24, 600, 24, Ui.F.Label, 16, Ui.Sodium);
            Ui.Text(sel.Title, x + 460, y + 50, w - 490, 44, Ui.F.Label, 30);
            bool doc = sel.Kind == "drawing" || sel.Kind == "note" || sel.Kind == "record";
            Ui.Text(sel.Text, x + 460, y + 104, w - 490, h - 130, doc ? Ui.F.Type : Ui.F.Body, 22);
        }

        private void DrawInvestigation()
        {
            var ctx = S.Ctx;
            Modal(1700, 1000, out float x, out float y);
            Ui.Text("THE STOCKROOM", x + 30, y + 20, 600, 34, Ui.F.Label, 26);
            Ui.Text(ctx.NightDef.InvestigationIntro, x + 30, y + 58, 1640, 60, Ui.F.Body, 18, Ui.Dim);
            float ix = x + 30, iy = y + 120, iw = 1100, ih = 619;
            Ui.Image(Ui.Tex("Art/bg/stockroom"), ix, iy, iw, ih, ScaleMode.StretchToFill);
            foreach (var hs in ctx.AvailableHotspots())
            {
                float hx = ix + hs.X * iw, hy = iy + hs.Y * ih, hw = hs.W * iw, hh = hs.H * ih;
                bool done = ctx.SearchedHotspots.Contains(hs.Id);
                bool hover = Ui.Hover(hx, hy, hw, hh);
                Ui.Fill(hx, hy, hw, hh, new Color(0.91f, 0.69f, 0.3f, hover ? 0.18f : 0.06f));
                Ui.Frame(hx, hy, hw, hh, done ? Ui.Emerald : Ui.Sodium, 1.5f);
                Ui.Text(hs.Label, hx, hy + hh - 24, hw, 24, Ui.F.Label, 14, Ui.Fg, TextAnchor.MiddleCenter);
                if (GUI.Button(Ui.R(hx, hy, hw, hh), GUIContent.none, GUIStyle.none)) { S.LastHotspot = ctx.Search(hs.Id); S.Sfx("inspect", 0.5f); }
            }
            float tx = x + 1160, tw = 510;
            if (S.LastHotspot != null)
            {
                Ui.Text($"<b>{S.LastHotspot.Label}.</b> {S.LastHotspot.Text}", tx, iy, tw, 150, Ui.F.Body, 19);
                if (!string.IsNullOrEmpty(S.LastHotspot.Clue) && S.Db.Clues.TryGetValue(S.LastHotspot.Clue, out var c))
                {
                    float dh = Mathf.Min(560, Ui.TextHeight(c.Text, tw - 30, Ui.F.Type, 16) + 24);
                    Ui.Box(tx, iy + 150, tw, dh, Ui.Paper, Ui.Hex("b8ae96"));
                    Ui.Text(c.Text, tx + 15, iy + 162, tw - 30, dh - 20, Ui.F.Type, 16, Ui.Ink);
                }
            }
            else Ui.Text("Click anything that looks worth searching.", tx, iy, tw, 60, Ui.F.Italic, 20, Ui.Dim);
            string status = ctx.CanFinishInvestigation ? "You've found what you came for. Keep looking or go back out." : "You need at least her file and a tape before you go back out.";
            Ui.Text(status, x + 30, y + 760, 1200, 30, Ui.F.Body, 19, Ui.Dim);
            if (Ui.Button("Back to the counter", x + 1370, y + 920, 300, 54, Ui.B.Primary, ctx.CanFinishInvestigation))
            {
                ctx.FinishInvestigation();
                S.Overlay = GameOverlay.None;
            }
        }

        // ================================================================== results

        private void Card(float w, float h, Color bgTop, Color bgBottom, out float x, out float y)
        {
            for (int i = 0; i < 20; i++) Ui.Fill(0, i * 54, Ui.VW, 54, Color.Lerp(bgTop, bgBottom, i / 19f));
            x = (Ui.VW - w) / 2;
            y = (Ui.VH - h) / 2;
            Ui.Box(x, y, w, h, new Color(0.047f, 0.055f, 0.075f, 0.94f), Ui.Edge);
        }

        private void DrawSummary()
        {
            var sm = S.Summary;
            var night = S.Db.Night(sm.Night);
            Card(820, 700, Ui.Hex("1a1c2a"), Ui.Hex("6e4b33"), out float x, out float y);
            Ui.Text($"6:00 AM · {(S.Endless ? $"ROUND {sm.Night} SURVIVED" : $"NIGHT {sm.Night} COMPLETE")}", x + 40, y + 34, 740, 26, Ui.F.Label, 18, Ui.Sodium);
            Ui.Text("Sunrise", x + 40, y + 62, 740, 60, Ui.F.Label, 48);
            float cy = y + 130;
            if (!S.Endless && night != null && !string.IsNullOrEmpty(night.EndCard))
            {
                float eh = Ui.TextHeight(night.EndCard, 740, Ui.F.Italic, 20);
                Ui.Text($"<i>{night.EndCard}</i>", x + 40, cy, 740, eh + 6, Ui.F.Italic, 20, Ui.Hex("c7c2b6"));
                cy += eh + 20;
            }
            void Row(string label, string value, Color? c = null)
            {
                Ui.Text(label, x + 40, cy, 500, 34, Ui.F.Body, 21);
                Ui.Text(value, x + 440, cy, 340, 34, Ui.F.Lcd, 28, c ?? Ui.Emerald, TextAnchor.UpperRight);
                Ui.Fill(x + 40, cy + 36, 740, 1, Ui.Hex("1d212a"));
                cy += 42;
            }
            Row("Sales", Ui.Money(sm.Sales));
            Row("Wage", Ui.Money(sm.Wage));
            if (sm.Bonus > 0) Row("Bonus", Ui.Money(sm.Bonus));
            Row("Tonight", Ui.Money(sm.Total), Ui.Sodium);
            Row("Customers served / refused", $"{sm.Served} / {sm.Refused}");
            Row("Cash on hand", Ui.Money(S.State.Money));
            if (Ui.Button(S.Endless ? "Next round" : "Go home and sleep", x + 40, y + 610, 360, 58, Ui.B.Primary, true, 22)) S.AfterSummary();
        }

        private void DrawFailed()
        {
            Card(900, 640, Ui.Hex("2a0f12"), Ui.Hex("060406"), out float x, out float y);
            Ui.Text("SHIFT TERMINATED", x + 40, y + 34, 820, 26, Ui.F.Label, 18, Ui.Blood);
            Ui.Text(S.FailReason == "collapse" ? "You lost yourself" : "You made a mistake", x + 40, y + 62, 820, 60, Ui.F.Label, 46);
            float cy = y + 140;
            foreach (var l in S.FailLines)
            {
                string t = l.TrimStart('*');
                float lh = Ui.TextHeight(t, 820, Ui.F.Italic, 22);
                Ui.Text($"<i>{t}</i>", x + 40, cy, 820, lh + 6, Ui.F.Italic, 22, Ui.Hex("b9bdc6"));
                cy += lh + 14;
            }
            Ui.Text("Check the handbook and the cameras. Every death in this game is avoidable with information you have.", x + 40, y + 470, 820, 60, Ui.F.Body, 19, Ui.Dim);
            if (Ui.Button("Retry the night", x + 40, y + 560, 280, 54, Ui.B.Primary)) S.RetryNight();
            if (Ui.Button("Main menu", x + 340, y + 560, 220, 54, Ui.B.Ghost)) S.Screen = GameScreen.Title;
        }

        private void DrawEnding()
        {
            var e = S.Ending;
            Card(1100, 900, Ui.Hex("1b2a24"), Ui.Hex("07080b"), out float x, out float y);
            Ui.Text($"ENDING {S.Profile.EndingCount(S.Db)} OF 15 DISCOVERED", x + 50, y + 36, 1000, 26, Ui.F.Label, 18, Ui.Sodium);
            Ui.Text(e.Title, x + 50, y + 64, 1000, 60, Ui.F.Label, 50);
            // The epilogue plays in pages so long endings never overflow.
            const int perPage = 5;
            int pages = Mathf.CeilToInt(e.Lines.Count / (float)perPage);
            _endingPage = Mathf.Clamp(_endingPage, 0, Math.Max(0, pages - 1));
            float cy = y + 150;
            foreach (var l in e.Lines.Skip(_endingPage * perPage).Take(perPage))
            {
                bool narr = l.StartsWith("*");
                string t = l.TrimStart('*');
                float lh = Ui.TextHeight(t, 1000, narr ? Ui.F.Italic : Ui.F.Body, 24);
                Ui.Text(narr ? $"<i>{t}</i>" : t, x + 50, cy, 1000, lh + 6, narr ? Ui.F.Italic : Ui.F.Body, 24, narr ? Ui.Hex("c9ccd3") : Ui.Fg);
                cy += lh + 16;
            }
            float by = y + 820;
            if (_endingPage < pages - 1)
            {
                if (Ui.Button("Continue ▸", x + 50, by, 240, 54, Ui.B.Primary)) _endingPage++;
                return;
            }
            float ay = y + 680;
            foreach (var a in S.NewAchievements)
            {
                Ui.Box(x + 50, ay, 1000, 44, new Color(0.91f, 0.69f, 0.3f, 0.08f), Ui.Sodium);
                Ui.Text("Achievement unlocked: " + AchievementName(a), x + 66, ay, 980, 44, Ui.F.Semi, 20, Ui.Fg, TextAnchor.MiddleLeft);
                ay += 52;
            }
            if (Ui.Button($"Begin cycle {S.State.Cycle + 1}", x + 50, by, 300, 54, Ui.B.Primary)) { _endingPage = 0; S.NextCycle(); }
            if (Ui.Button("Endings", x + 370, by, 180, 54, Ui.B.Ghost)) { _endingPage = 0; S.Screen = GameScreen.Achievements; }
            if (Ui.Button("Credits", x + 570, by, 180, 54, Ui.B.Ghost)) { _endingPage = 0; S.Screen = GameScreen.Credits; }
        }

        private string AchievementName(string id)
        {
            if (id == Profile.EndlessAchievement) return "The Minute Never Closes — Endless Mode unlocked";
            var e = S.Db.Endings.FirstOrDefault(x => Profile.AchievementFor(x.Id) == id);
            return e?.Achievement ?? id;
        }

        private void DrawAchievements()
        {
            Card(1500, 960, Ui.Hex("0b0d12"), Ui.Hex("07080b"), out float x, out float y);
            Ui.Text($"{S.Profile.EndingCount(S.Db)} OF 15 ENDINGS · {S.Profile.CyclesCompleted} CYCLES PLAYED", x + 40, y + 30, 1400, 26, Ui.F.Label, 18, Ui.Sodium);
            Ui.Text("Endings & Achievements", x + 40, y + 56, 1400, 56, Ui.F.Label, 44);
            int i = 0;
            foreach (var e in S.Db.Endings)
            {
                bool got = S.Profile.Endings.Contains(e.Id);
                float cx = x + 40 + (i % 4) * 357, cy = y + 130 + (i / 4) * 168;
                Ui.Box(cx, cy, 345, 156, Ui.Hex("0d1015"), got ? Ui.Hex("3c6b4f") : Ui.Edge);
                Ui.Text(got ? e.Achievement : "???", cx + 14, cy + 10, 320, 28, Ui.F.Label, 20, got ? Ui.Sodium : Ui.Dim);
                Ui.Text(got ? e.Title : "Undiscovered ending", cx + 14, cy + 40, 320, 28, Ui.F.Lcd, 22, got ? Ui.Emerald : Ui.Dim);
                Ui.Text(got ? e.AchievementDesc : Hint(e.Id), cx + 14, cy + 72, 320, 80, Ui.F.Body, 16, Ui.Dim);
                i++;
            }
            bool endless = S.Profile.EndlessUnlocked(S.Db);
            float ex = x + 40 + (i % 4) * 357, ey = y + 130 + (i / 4) * 168;
            Ui.Box(ex, ey, 345, 156, Ui.Hex("130f1d"), Ui.Hex("4a3a73"));
            Ui.Text("The Minute Never Closes", ex + 14, ey + 10, 320, 28, Ui.F.Label, 20, Ui.Violet);
            Ui.Text("Endless Mode", ex + 14, ey + 40, 320, 28, Ui.F.Lcd, 22, endless ? Ui.Emerald : Ui.Dim);
            Ui.Text(endless ? $"Best: round {S.Profile.EndlessBestRound}, {Ui.Money(S.Profile.EndlessBestEarnings)}" : "Discover all 15 endings.", ex + 14, ey + 72, 320, 80, Ui.F.Body, 16, Ui.Dim);
            if (Ui.Button("Back", x + 40, y + 890, 180, 50)) S.Screen = GameScreen.Title;
        }

        private static string Hint(string id) => id switch
        {
            "off_the_clock" => "Some rules exist for a reason.",
            "wrong_minute" => "Not every page in the handbook is real.",
            "paid_in_full" => "Thirteen coins, from thirteen people.",
            "company_man" => "Hale is always looking for loyal staff.",
            "debt" => "Every deal is written down.",
            "truth" => "The date. Every name. Nobody left behind.",
            _ => "Your choices across the seven nights decide this one."
        };

        private void DrawSettings()
        {
            var st = S.Settings;
            Card(900, 900, Ui.Hex("0b0d12"), Ui.Hex("07080b"), out float x, out float y);
            Ui.Text("Settings", x + 40, y + 30, 800, 56, Ui.F.Label, 44);
            float cy = y + 110;
            bool changed = false;
            float Slider(string label, float v)
            {
                Ui.Text(label, x + 40, cy, 380, 40, Ui.F.Body, 22);
                float nv = GUI.HorizontalSlider(Ui.R(x + 420, cy + 12, 380, 24), v, 0f, 1f);
                Ui.Text($"{Mathf.RoundToInt(nv * 100)}%", x + 810, cy, 60, 40, Ui.F.Lcd, 26, Ui.Emerald);
                cy += 56;
                if (!Mathf.Approximately(nv, v)) changed = true;
                return nv;
            }
            bool Toggle(string label, bool v)
            {
                if (Ui.Button((v ? "■  " : "□  ") + label, x + 40, cy, 820, 44, v ? Ui.B.On : Ui.B.Ghost, true, 18)) { v = !v; changed = true; }
                cy += 54;
                return v;
            }
            st.Master = Slider("Master volume", st.Master);
            st.Music = Slider("Music volume", st.Music);
            st.Sfx = Slider("Sound effects", st.Sfx);
            Ui.Text("Scare intensity", x + 40, cy, 380, 40, Ui.F.Body, 22);
            string[] levels = { "Minimal", "Reduced", "Full" };
            for (int i = 0; i < 3; i++)
                if (Ui.Button(levels[i], x + 420 + i * 150, cy, 140, 42, st.ScareLevel == i ? Ui.B.On : Ui.B.Ghost, true, 17)) { st.ScareLevel = i; changed = true; }
            cy += 56;
            st.ReducedFlashing = Toggle("Reduce flashing", st.ReducedFlashing);
            st.ReducedShake = Toggle("Reduce camera shake", st.ReducedShake);
            st.Subtitles = Toggle("Subtitles for phone calls and voices", st.Subtitles);
            // Display (Unity-only settings, stored by Unity itself).
            Ui.Text("Display", x + 40, cy + 6, 380, 40, Ui.F.Body, 22);
            if (Ui.Button(Screen.fullScreen ? "Fullscreen" : "Windowed", x + 420, cy, 200, 42, Ui.B.Normal, true, 17))
                Screen.fullScreenMode = Screen.fullScreen ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
            var res = Screen.resolutions;
            if (res.Length > 0)
            {
                int idx = Array.FindIndex(res, r => r.width == Screen.width && r.height == Screen.height);
                if (Ui.Button($"{Screen.width}×{Screen.height}", x + 630, cy, 230, 42, Ui.B.Normal, true, 17))
                {
                    var next = res[(idx + 1 + res.Length) % res.Length];
                    Screen.SetResolution(next.width, next.height, Screen.fullScreenMode);
                }
            }
            cy += 56;
            if (changed) { S.SaveSettings(); ApplyComfortSettings(); }
            if (Ui.Button("Back", x + 40, y + 820, 180, 50)) S.Screen = _settingsReturn == "title" ? GameScreen.Title : GameScreen.Prep;
        }

        private void DrawCredits()
        {
            Card(1000, 760, Ui.Hex("0b0d12"), Ui.Hex("07080b"), out float x, out float y);
            Ui.Text("A HIKARI FORGE STUDIOS PRODUCTION", x + 40, y + 34, 920, 26, Ui.F.Label, 18, Ui.Sodium);
            Ui.Text("The 3:13 Machine", x + 40, y + 62, 920, 60, Ui.F.Label, 50);
            string credits =
                "Developed and published by Hikari Forge Studios.\n\n" +
                "Title key art and character designs: Hikari Forge Studios.\n\n" +
                "Game design, writing, code, placeholder art and music produced with AI assistance (Claude by Anthropic) under the direction of Hikari Forge Studios.\n\n" +
                "All music and sound effects are original procedural compositions made for this game.\n\n" +
                "Fonts: Barlow, Barlow Condensed, VT323, Reenie Beanie (SIL Open Font License 1.1); Special Elite (Apache License 2.0).\n\n" +
                "Thank you for working the night shift.";
            Ui.Text(credits, x + 40, y + 140, 920, 500, Ui.F.Body, 21);
            if (Ui.Button("Back", x + 40, y + 680, 180, 50)) S.Screen = GameScreen.Title;
        }

        private void DrawEndlessOver()
        {
            Card(820, 600, Ui.Hex("2a0f12"), Ui.Hex("060406"), out float x, out float y);
            Ui.Text("ENDLESS MODE · RUN OVER", x + 40, y + 34, 740, 26, Ui.F.Label, 18, Ui.Blood);
            Ui.Text($"Round {Endless.Round(S.State)}", x + 40, y + 62, 740, 60, Ui.F.Label, 46);
            Ui.Text($"You survived {Endless.Round(S.State) - 1} full rounds and earned {Ui.Money(S.EndlessEarnings)}.", x + 40, y + 150, 740, 60, Ui.F.Body, 22);
            if (S.NewEndlessBest) Ui.Text("New personal best!", x + 40, y + 220, 740, 40, Ui.F.Label, 26, Ui.Sodium);
            Ui.Text($"Best: round {S.Profile.EndlessBestRound} · {Ui.Money(S.Profile.EndlessBestEarnings)}", x + 40, y + 270, 740, 40, Ui.F.Body, 20, Ui.Dim);
            if (Ui.Button("New run", x + 40, y + 500, 220, 54, Ui.B.Primary)) S.StartEndless();
            if (Ui.Button("Main menu", x + 280, y + 500, 220, 54, Ui.B.Ghost)) S.Screen = GameScreen.Title;
        }

        private void DrawToasts()
        {
            float y = Ui.VH - 30;
            for (int i = S.Toasts.Count - 1; i >= 0; i--)
            {
                var t = S.Toasts[i];
                float h = Ui.TextHeight(t.Text, 440, Ui.F.Body, 18) + 22;
                y -= h + 8;
                Color edge = t.Kind == "clue" ? Ui.Emerald : t.Kind == "warn" ? Ui.Blood : t.Kind == "achievement" ? Ui.Sodium : Ui.Dim;
                Ui.Box(Ui.VW - 500, y, 470, h, Ui.Hex("11151c"), Ui.Edge);
                Ui.Fill(Ui.VW - 500, y, 4, h, edge);
                Ui.Text(t.Text, Ui.VW - 484, y + 11, 440, h, Ui.F.Body, 18);
            }
        }
    }
}
