using System;
using System.Collections.Generic;
using System.Linq;

namespace HikariForge.M313.Core
{
    /// <summary>What a front end (Unity, browser) must provide: storage, audio and platform services.</summary>
    public interface IPlatform
    {
        string Load(string key);
        void Save(string key, string value);
        void Music(string id);
        void Sfx(string id, float volume);
        void Ambience(string id);
        void Volumes(float master, float music, float sfx);
        void Achievement(string id);
    }

    public enum GameScreen { Title, Prep, Shift, Summary, Ending, Failed, Achievements, Settings, Credits, EndlessOver, Feedback }
    public enum GameOverlay { None, Handbook, Monitor, Panel, Evidence, Records, Restock, Phone, Investigation, ConfirmLeave, Pause }

    /// <summary>Build identity and beta configuration.</summary>
    public static class BuildInfo
    {
        public const string Version = "0.1.0";
        public const string Label = "BETA v" + Version;
        /// <summary>HTTPS link to the tester feedback form (e.g. a Google Form). Empty until the studio sets one up.</summary>
        public static string FeedbackUrl = "";
        public const string LogLocation = @"%USERPROFILE%\AppData\LocalLow\Hikari Forge Studios\The 313 Machine\Player.log";
        public const string SaveLocation = @"%USERPROFILE%\AppData\LocalLow\Hikari Forge Studios\The 313 Machine\Saves";

        public static readonly string[] FeedbackQuestions =
        {
            "Did the game start successfully?",
            "How long did you play?",
            "Which night did you reach?",
            "Were the controls understandable?",
            "Did the upgrade prices feel fair?",
            "Were the jumpscares too frequent, too rare, or balanced?",
            "Were the scares effective?",
            "Did anything break? (what were you doing, which night, what time on the clock)",
            "What did you enjoy most?",
            "What should be improved?"
        };
    }

    public sealed class DialogueLine
    {
        public string Speaker;
        public string Text;
        public bool Narration;
    }

    public sealed class ToastMessage
    {
        public string Text;
        public string Kind;   // clue, achievement, notice, warn
        public double Until;
    }

    public sealed class GameSettings
    {
        public float Master = 0.8f, Music = 0.7f, Sfx = 0.8f;
        public bool ReducedFlashing;
        public bool ReducedShake;
        public int ScareLevel = 2;   // 0 minimal, 1 reduced, 2 full
        public bool Subtitles = true;
        public float Speed = 1f;     // playtest time multiplier
        public bool PlaytestTools = true;
        public bool Hints = true;    // first-week guidance

        public string Serialize() => MiniJson.Serialize(new Dictionary<string, object>
        {
            ["master"] = (double)Master, ["music"] = (double)Music, ["sfx"] = (double)Sfx,
            ["flash"] = ReducedFlashing, ["shake"] = ReducedShake, ["scare"] = ScareLevel, ["subs"] = Subtitles,
            ["speed"] = (double)Speed, ["tools"] = PlaytestTools, ["hints"] = Hints
        });

        public static GameSettings Parse(string json)
        {
            var s = new GameSettings();
            try
            {
                var d = J.Obj(MiniJson.Parse(json));
                s.Master = J.Float(d, "master", s.Master); s.Music = J.Float(d, "music", s.Music); s.Sfx = J.Float(d, "sfx", s.Sfx);
                s.ReducedFlashing = J.Bool(d, "flash"); s.ReducedShake = J.Bool(d, "shake"); s.ScareLevel = J.Int(d, "scare", 2);
                s.Subtitles = J.Bool(d, "subs", true); s.Speed = J.Float(d, "speed", 1f); s.PlaytestTools = J.Bool(d, "tools", true); s.Hints = J.Bool(d, "hints", true);
            }
            catch { }
            return s;
        }
    }

    /// <summary>
    /// Front-end-independent game flow. Owns the campaign state, the running shift and all
    /// presentation state (screens, dialogue, overlays, toasts, scares). Unity (OnGUI) and the
    /// browser playtest (Razor) only render this object and forward input to it.
    /// </summary>
    public sealed class GameSession
    {
        public readonly ContentDatabase Db;
        public GameState State;
        public Profile Profile = new Profile();
        public GameSettings Settings = new GameSettings();
        public ShiftContext Ctx;
        public GameScreen Screen = GameScreen.Title;
        public GameOverlay Overlay = GameOverlay.None;
        public bool Endless;

        // Dialogue
        public readonly List<DialogueLine> Lines = new List<DialogueLine>();
        public int LineIndex;
        public bool ShowingOutcome;
        public string InspectText;
        public string InspectLabel;
        public string MachineDisplay = "";
        public double MachineDisplayUntil;
        public readonly List<string> Feed = new List<string>();
        public readonly List<ToastMessage> Toasts = new List<ToastMessage>();
        public string LastReceipt;
        public string LastDispensed;
        public double DispenseAt = -10;
        public string Keypad = "";
        public string PanelCode = "";
        public string PanelMessage = "";
        public string MonitorCam = "cam1";
        public string HandbookPage;
        public string SelectedSlot;
        public string SelectedClue;
        public HotspotDef LastHotspot;

        // Atmosphere
        public string ScareId;
        public double ScareUntil;
        public bool ScareFullscreen;
        public string HallucinationId;
        public double HallucinationUntil;
        public double FlickerUntil;
        public bool LightsOut;
        public bool KnockActive;
        public double Now;     // seconds since session start

        // Results
        public NightSummary Summary;
        public EndingDef Ending;
        public List<string> NewAchievements = new List<string>();
        public List<string> FailLines = new List<string>();
        public string FailReason;
        public string Checkpoint;   // save json at the start of the current night
        public int EndlessEarnings;
        public bool NewEndlessBest;
        public string Error;

        private readonly IPlatform _p;

        public GameSession(IPlatform platform, Func<string, string> contentProvider)
        {
            _p = platform;
            Db = ContentDatabase.Load(contentProvider);
        }

        // ------------------------------------------------------------ persistence

        public void LoadPersistent()
        {
            try { Profile = Profile.Deserialize(Get("m313.profile")); } catch { Profile = new Profile(); }
            Settings = GameSettings.Parse(Get("m313.settings") ?? "{}");
            ApplyAudioSettings();
        }

        public bool HasSave() => !string.IsNullOrEmpty(Get("m313.save"));

        private string Get(string key)
        {
            try { return _p.Load(key); } catch { return null; }
        }

        private void Set(string key, string value)
        {
            try { _p.Save(key, value); } catch { }
        }

        public void SaveProfile() => Set("m313.profile", Profile.Serialize());
        public void SaveSettings() { Set("m313.settings", Settings.Serialize()); ApplyAudioSettings(); }
        private void SaveGame() { if (!Endless) Set("m313.save", SaveSerializer.Serialize(State)); }

        public void ApplyAudioSettings() => _p.Volumes(Settings.Master, Settings.Music, Settings.Sfx);

        public void Music(string id) { if (!string.IsNullOrEmpty(id)) _p.Music(id); }
        public void Sfx(string id, float vol = 1f) { if (!string.IsNullOrEmpty(id)) _p.Sfx(id, vol); }
        public void Ambience(string id) => _p.Ambience(id ?? "");

        // ------------------------------------------------------------ flow

        public void NewGame()
        {
            Endless = false;
            State = GameState.NewGame();
            SaveGame();
            GoPrep();
        }

        public bool ContinueGame()
        {
            Endless = false;
            var json = Get("m313.save");
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                State = SaveSerializer.Deserialize(json);
            }
            catch (SaveCorruptException e)
            {
                Error = "Your save file was damaged (" + e.Message + "). A backup could not be restored, so a new game was started.";
                State = GameState.NewGame();
            }
            GoPrep();
            return true;
        }

        public void StartEndless()
        {
            Endless = true;
            State = HikariForge.M313.Core.Endless.NewRun(Profile);
            EndlessEarnings = 0;
            GoPrep();
        }

        public void GoPrep()
        {
            Screen = GameScreen.Prep;
            Overlay = GameOverlay.None;
            Music("last_stop");
            Ambience("wind");
        }

        public void StartShift()
        {
            State.CheckpointPhase = "shift";
            Checkpoint = SaveSerializer.Serialize(State);
            Ctx = Endless ? Endless_Build() : new ShiftContext(State, Db, 313 + State.Cycle * 31);
            Ctx.Director.Intensity = Settings.ScareLevel == 0 ? 0.25f : Settings.ScareLevel == 1 ? 0.6f : 1f;
            Screen = GameScreen.Shift;
            Overlay = GameOverlay.None;
            Lines.Clear();
            Feed.Clear();
            InspectText = null;
            Keypad = "";
            LightsOut = false;
            KnockActive = false;
            MachineDisplay = "";
            Music(Ctx.NightDef.Music);
            Ambience(Ctx.NightDef.Weather);
            Sfx("machine_hum_start", 0.5f);
        }

        private ShiftContext Endless_Build() => HikariForge.M313.Core.Endless.BuildShift(State, Db, Profile.EndlessRuns + 1);

        public void RetryNight()
        {
            if (Checkpoint == null) { GoPrep(); return; }
            State = SaveSerializer.Deserialize(Checkpoint);
            State.CheckpointPhase = "prep";
            GoPrep();
        }

        public void AfterSummary()
        {
            SaveGame();
            GoPrep();
        }

        public void NextCycle()
        {
            State = Campaign.StartNextCycle(State);
            SaveGame();
            GoPrep();
        }

        // ------------------------------------------------------------ tick

        public void Tick(double dt)
        {
            Now += dt;
            Toasts.RemoveAll(t => t.Until < Now);
            if (Screen != GameScreen.Shift || Ctx == null) return;
            bool paused = Overlay == GameOverlay.Phone || Overlay == GameOverlay.Investigation || Overlay == GameOverlay.ConfirmLeave || Overlay == GameOverlay.Pause;
            if (!paused) Ctx.Tick((float)(dt * Settings.Speed));
            DrainEvents();

            if (Ctx.Phase == ShiftPhase.Phone && Overlay != GameOverlay.Phone) { Overlay = GameOverlay.Phone; Sfx("phone_ring"); }
            if (Ctx.Phase == ShiftPhase.Investigation && Overlay != GameOverlay.Investigation) Overlay = GameOverlay.Investigation;
        }

        private void Say(IEnumerable<string> lines, string speaker)
        {
            foreach (var raw in lines ?? Enumerable.Empty<string>())
            {
                var l = new DialogueLine { Speaker = speaker, Text = raw };
                if (raw.StartsWith("*")) { l.Narration = true; l.Text = raw.Substring(1); l.Speaker = null; }
                else
                {
                    int colon = raw.IndexOf(':');
                    if (colon > 0 && colon < 24 && raw.Substring(0, colon).ToUpperInvariant() == raw.Substring(0, colon))
                    {
                        l.Speaker = raw.Substring(0, colon);
                        l.Text = raw.Substring(colon + 1).Trim();
                    }
                }
                Lines.Add(l);
            }
        }

        public bool LinesPending => LineIndex < Lines.Count - 1;
        public DialogueLine CurrentLine => Lines.Count == 0 ? null : Lines[Math.Min(LineIndex, Lines.Count - 1)];

        public void AdvanceLine()
        {
            if (LinesPending) { LineIndex++; Sfx("ui_tick", 0.3f); return; }
            var cur = Ctx?.Current;
            if (cur == null) return;
            if (cur.Stage == EncounterStage.Talking) Ctx.BeginAwaiting();
            else if (cur.Stage == EncounterStage.Resolving) Ctx.FinishEncounter();
        }

        private void ResetDialogue(IEnumerable<string> lines, string speaker)
        {
            Lines.Clear();
            LineIndex = 0;
            Say(lines, speaker);
        }

        public void Toast(string text, string kind = "notice", double seconds = 4)
        {
            Toasts.Add(new ToastMessage { Text = text, Kind = kind, Until = Now + seconds });
        }

        private void DrainEvents()
        {
            while (Ctx.Events.Count > 0)
            {
                var e = Ctx.Events.Dequeue();
                switch (e.Type)
                {
                    case PresEventType.Arrive:
                    {
                        var def = Ctx.Current.Def;
                        ResetDialogue(def.Lines, def.Name);
                        ShowingOutcome = false;
                        InspectText = null;
                        Keypad = "";
                        Sfx("footsteps", 0.6f);
                        if (Db.Characters.TryGetValue(def.Character ?? "", out var ch)) Sfx(ch.Motif, 0.5f);
                        break;
                    }
                    case PresEventType.Leave:
                        Lines.Clear();
                        InspectText = null;
                        break;
                    case PresEventType.Dispense:
                        LastDispensed = e.Id;
                        DispenseAt = Now;
                        Sfx("dispense");
                        break;
                    case PresEventType.DispenseFailed:
                        Sfx("error_beep");
                        Flash(e.Id == "power" ? "NO POWER" : e.Id == "jam" ? "ERROR E-13 — MOTOR JAM" : "SLOT EMPTY");
                        break;
                    case PresEventType.Receipt:
                        LastReceipt = e.Text;
                        Sfx("receipt");
                        break;
                    case PresEventType.Scare:
                        StartScare(e.Id, e.Id.StartsWith("js_"));
                        break;
                    case PresEventType.Ambient:
                        StartScare(e.Id, false);
                        break;
                    case PresEventType.Hallucination:
                        HallucinationId = e.Id;
                        HallucinationUntil = Now + 2.5;
                        Sfx("whisper", 0.4f);
                        break;
                    case PresEventType.Music: Music(e.Id); break;
                    case PresEventType.Sfx: Sfx(e.Id); break;
                    case PresEventType.MachineMessage: Flash(e.Text, 6); Sfx("machine_beep", 0.6f); break;
                    case PresEventType.Notice:
                        if (!string.IsNullOrEmpty(e.Text)) { Feed.Insert(0, e.Text.TrimStart('*')); Toast(e.Text.TrimStart('*'), "notice", 6); }
                        break;
                    case PresEventType.Clue: Toast("Evidence found: " + e.Text, "clue", 5); Sfx("clue", 0.6f); break;
                    case PresEventType.ClueLost: Toast("You can't remember: " + e.Text, "warn", 5); break;
                    case PresEventType.HandbookChanged: Toast("The handbook has changed.", "warn", 5); Sfx("paper_rustle"); break;
                    case PresEventType.Produced: Toast("Something dropped into Row E.", "notice", 4); Sfx("dispense", 0.7f); break;
                    case PresEventType.Margin:
                        Flash("3:13", 8);
                        Music(Ctx.NightDef.MarginMusic);
                        Sfx("margin_drop");
                        FlickerUntil = Now + 2;
                        Feed.Insert(0, "It is 3:13 AM. The lot goes very quiet.");
                        break;
                    case PresEventType.KnockStart: KnockActive = true; Sfx("knock_inside"); Toast("Knocking... from inside the machine.", "warn", 5); break;
                    case PresEventType.KnockEnd: KnockActive = false; break;
                    case PresEventType.BlackoutStart:
                        LightsOut = e.Value == 0;
                        Sfx("power_down");
                        Toast(e.Value == 1 ? "Power failure. Emergency backup engaged." : "Power failure. The machine is dead.", "warn", 5);
                        break;
                    case PresEventType.BlackoutEnd: LightsOut = false; Sfx("power_up"); break;
                    case PresEventType.WrongTimeStart: Toast("The machine's display reads 3:13.", "warn", 4); Sfx("glitch"); break;
                    case PresEventType.WrongTimeEnd: break;
                    case PresEventType.Jam: Sfx("motor_jam"); break;
                    case PresEventType.JamCleared: Sfx("motor_reset"); PanelMessage = "MOTOR RESET OK"; break;
                    case PresEventType.Consumed: Toast($"You feel steadier. ({e.Value})", "notice", 3); Sfx("can_open"); break;
                    case PresEventType.Restocked: Sfx("restock", 0.6f); break;
                    case PresEventType.Reply:
                        ResetDialogue(e.Lines, Ctx.Current?.Def.Name);
                        break;
                    case PresEventType.Phone: break;
                    case PresEventType.Investigation: LastHotspot = null; break;
                    case PresEventType.Failed:
                        FailLines = e.Lines ?? new List<string>();
                        FailReason = e.Id;
                        Screen = GameScreen.Failed;
                        Overlay = GameOverlay.None;
                        Music("no_more_change");
                        Ambience("");
                        break;
                    case PresEventType.Sunrise: OnSunrise(); break;
                    case PresEventType.Ending: OnEnding(e.Id); break;
                }

                // Outcome lines arrive once the encounter is resolved.
                if (Ctx.Current != null && Ctx.Current.Stage == EncounterStage.Resolving && !ShowingOutcome)
                {
                    ShowingOutcome = true;
                    ResetDialogue(Ctx.Current.Outcome.Lines, Ctx.Current.Def.Name);
                    InspectText = null;
                }
            }
        }

        private void Flash(string text, double seconds = 3)
        {
            MachineDisplay = text;
            MachineDisplayUntil = Now + seconds;
        }

        private void StartScare(string id, bool full)
        {
            if (Settings.ScareLevel == 0 && full) full = false;
            ScareId = id;
            ScareFullscreen = full;
            ScareUntil = Now + (full ? 1.6 : 2.4);
            Sfx(full ? "stinger_" + (Settings.ScareLevel == 2 ? "hard" : "soft") : "scare_" + id, full ? 0.9f : 0.5f);
            if (!Settings.ReducedFlashing && (id.Contains("light") || id == "margin_surge")) FlickerUntil = Now + 1.5;
        }

        private void OnSunrise()
        {
            Music("sunrise");
            Ambience("birds");
            if (Endless)
            {
                EndlessEarnings += Ctx.NightEarnings;
                HikariForge.M313.Core.Endless.CompleteRound(State, Ctx);
                Summary = new NightSummary { Night = HikariForge.M313.Core.Endless.Round(State) - 1, Sales = Ctx.NightEarnings, Wage = Ctx.NightDef.Wage, Served = Ctx.CustomersServed, Refused = Ctx.CustomersRefused };
            }
            else
            {
                Summary = Campaign.CompleteNight(State, Db, Ctx);
                SaveGame();
            }
            Screen = GameScreen.Summary;
            Overlay = GameOverlay.None;
        }

        private void OnEnding(string requested)
        {
            Ending = Campaign.ResolveEnding(State, Db, requested);
            State.EndingReached = Ending.Id;
            NewAchievements = Profile.RecordEnding(Ending.Id, Db);
            SaveProfile();
            SaveGame();
            foreach (var a in NewAchievements)
                _p.Achievement(a);
            Screen = GameScreen.Ending;
            Overlay = GameOverlay.None;
            Music(Ending.Music);
            Ambience("");
        }

        public void EndlessDeath()
        {
            NewEndlessBest = Profile.RecordEndlessRun(HikariForge.M313.Core.Endless.Round(State), EndlessEarnings + Ctx.NightEarnings);
            SaveProfile();
            Screen = GameScreen.EndlessOver;
        }

        // ------------------------------------------------------------ actions

        public void PressKey(string k)
        {
            Sfx("key_press", 0.5f);
            if (k == "CLR") { Keypad = ""; return; }
            if (Keypad.Length >= 2) Keypad = "";
            if (Keypad.Length == 0 && "ABCDE".Contains(k)) Keypad = k;
            else if (Keypad.Length == 1 && "1234".Contains(k)) Keypad += k;
        }

        public void Dispense()
        {
            if (Ctx?.Current == null) { Flash("NO CUSTOMER"); Sfx("error_beep"); return; }
            if (Keypad.Length != 2) { Flash("ENTER A CODE"); Sfx("error_beep"); return; }
            var err = Ctx.Serve(Keypad);
            if (err == ShiftContext.ServeError.None) Keypad = "";
        }

        public void Refuse()
        {
            Sfx("ui_click");
            Ctx?.Refuse();
        }

        public void Inspect(string spot)
        {
            var r = Ctx?.Inspect(spot);
            if (r == null) return;
            Sfx("inspect", 0.4f);
            InspectLabel = r.Label ?? spot;
            InspectText = r.Locked
                ? $"[{Db.Upgrades[r.RequiredUpgrade].Name} required] {r.Text}"
                : r.Text;
        }

        public void Talk(TalkDef t)
        {
            Sfx("ui_click");
            var reply = Ctx.Talk(t.Id);
            if (!t.End && reply != null)
            {
                var name = Ctx.Current?.Def.Name;
                ResetDialogue(new[] { "› " + t.Text }.Concat(reply), name);
                Lines[0].Speaker = "YOU";
                Lines[0].Text = t.Text;
                Lines[0].Narration = false;
            }
        }

        public void OpenPanel()
        {
            Ctx?.OpenPanel();
            Overlay = GameOverlay.Panel;
            PanelCode = "";
            PanelMessage = Ctx != null && Ctx.Jammed ? "ERROR E-13: MOTOR JAM" : "DIAGNOSTICS NOMINAL";
            Sfx("panel_open");
        }

        public void ClosePanel()
        {
            Ctx?.ClosePanel();
            Overlay = GameOverlay.None;
            Sfx("panel_close");
        }

        public void PanelKey(string k)
        {
            Sfx("key_press", 0.5f);
            if (k == "CLR") { PanelCode = ""; return; }
            if (k == "ENT")
            {
                if (PanelCode.Length != 4) { PanelMessage = "ENTER 4 DIGITS"; return; }
                bool used = Ctx != null && Ctx.EnterCode(PanelCode);
                PanelMessage = used ? "CODE ACCEPTED" : "ACCESS DENIED — DISTRICT USE ONLY";
                PanelCode = "";
                if (used) { Overlay = GameOverlay.None; Ctx.ClosePanel(); }
                return;
            }
            if (PanelCode.Length < 4) PanelCode += k;
        }

        public void ResetMotor()
        {
            if (Ctx != null && Ctx.ResetMotor()) Toast("Motor reset. The machine hums back to life.");
            else PanelMessage = "NO FAULT DETECTED";
        }

        // ------------------------------------------------------------ guidance

        /// <summary>
        /// A one-line contextual tip for new players (first cycle, nights 1-3). Null when nothing needs saying.
        /// Tips explain controls and point at the right tool; they never reveal the answer to a customer.
        /// </summary>
        public string Hint
        {
            get
            {
                if (!Settings.Hints || State == null || Endless || State.Cycle > 1 || State.Night > 3) return null;
                if (Screen == GameScreen.Prep)
                {
                    bool empty = State.Slots.Any(sl => sl.Code != "E1" && (sl.ProductId == null || sl.Count == 0));
                    bool stock = State.Stockroom.Values.Any(v => v > 0);
                    if (State.Night == 1 && empty && stock)
                        return "Stock the machine: pick an empty slot, then a product, or press Auto-stock. Customers can only buy what is loaded.";
                    if (State.Night == 2) return "Your wages are in. The Upgrades tab improves the machine, and the handbook has a new section.";
                    if (State.Night == 3) return "Handbook pages can change during a shift. Only pages with the green District seal are official.";
                    return State.Night == 1 ? "Ready? Press Start shift." : null;
                }
                if (Screen != GameScreen.Shift || Ctx == null || Overlay != GameOverlay.None) return null;
                if (Ctx.KnockActive) return "Knocking from inside the machine. Handbook rule D1 is about this.";
                var cur = Ctx.Current;
                if (cur == null)
                {
                    if (Ctx.InMargin && Ctx.Clock < ContentDatabase.MarginTime + 40)
                        return "It's 3:13. Different rules apply until sunrise: read Section B in the handbook (H).";
                    if (State.Night == 1 && State.TotalServed >= 1 && !Ctx.InMargin)
                        return "Quiet moment. Read the handbook (H), check the cameras (M), or restock between customers.";
                    return null;
                }
                if (cur.Stage == EncounterStage.Talking) return State.Night == 1 ? "Click the text or press Space to continue." : null;
                if (cur.Stage != EncounterStage.Awaiting || LinesPending) return null;
                var product = Db.Product(cur.Def.Request);
                if (State.Night == 1 && State.TotalServed == 0 && product != null)
                    return $"Click the slot holding {product.Name} (or type its code on the keypad), then press DISPENSE.";
                if (cur.Def.Request == "UNLISTED")
                    return "They want something that isn't on the menu. The handbook says what to check first.";
                if (cur.Def.Supernatural && State.Night == 1)
                    return "Something is wrong here. Inspect the REFLECTION and check the cameras (M) before you decide.";
                if (product != null && State.MachineCount(product.Id) == 0)
                    return $"You have no {product.Name} loaded. Refuse, offer something else, or restock between customers.";
                return null;
            }
        }

        // ------------------------------------------------------------ pause

        public bool ReturnToShiftFromSettings;

        public void TogglePause()
        {
            if (Screen != GameScreen.Shift) return;
            if (Overlay == GameOverlay.Pause) { Overlay = GameOverlay.None; return; }
            if (Overlay == GameOverlay.Phone || Overlay == GameOverlay.Investigation) return;
            if (Overlay == GameOverlay.Panel) ClosePanel();
            Overlay = GameOverlay.Pause;
        }

        public void OpenSettingsFromPause()
        {
            ReturnToShiftFromSettings = true;
            Screen = GameScreen.Settings;
        }

        public void CloseSettings()
        {
            if (ReturnToShiftFromSettings && Ctx != null && !Ctx.IsOver) { Screen = GameScreen.Shift; Overlay = GameOverlay.Pause; }
            else Screen = GameScreen.Title;
            ReturnToShiftFromSettings = false;
        }

        /// <summary>Leaves the shift. Progress is kept from the start of tonight (the checkpoint).</summary>
        public void QuitToMenu()
        {
            if (Checkpoint != null && !Endless)
            {
                try { State = SaveSerializer.Deserialize(Checkpoint); State.CheckpointPhase = "prep"; SaveGame(); } catch (SaveCorruptException) { }
            }
            Ctx = null;
            Overlay = GameOverlay.None;
            Screen = GameScreen.Title;
            Music("last_stop");
            Ambience("wind");
        }

        public void LeaveBooth()
        {
            Overlay = GameOverlay.None;
            Ctx?.LeaveBooth();
        }

        // ------------------------------------------------------------ playtest tools

        /// <summary>Plays earlier nights automatically with a careful policy so testers can jump ahead.</summary>
        public void SkipToNight(int night)
        {
            Endless = false;
            var s = GameState.NewGame();
            var bot = AutoPlayer.Careful();
            while (s.Night < night)
            {
                var ctx = bot.PlayNight(s, Db);
                if (ctx.Phase != ShiftPhase.Ended) break;
                Campaign.CompleteNight(s, Db, ctx);
            }
            State = s;
            SaveGame();
            GoPrep();
            Toast($"Playtest: auto-played to Night {s.Night} on the careful route.", "notice", 6);
        }

        public void UnlockAllEndings()
        {
            foreach (var e in Db.Endings) Profile.RecordEnding(e.Id, Db);
            SaveProfile();
            Toast("Playtest: all endings marked as seen. Endless Mode unlocked.", "achievement", 6);
        }
    }
}
