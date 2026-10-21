using System.Reflection;
using HikariForge.M313.Core;
using Microsoft.JSInterop;

namespace HikariForge.M313.Playtest
{
    public enum Screen { Title, Prep, Shift, Summary, Ending, Failed, Achievements, Settings, Credits, EndlessOver }
    public enum Overlay { None, Handbook, Monitor, Panel, Evidence, Records, Restock, Phone, Investigation, ConfirmLeave }

    public sealed class Line
    {
        public string Speaker;
        public string Text;
        public bool Narration;
    }

    public sealed class Toast
    {
        public string Text;
        public string Kind;   // clue, achievement, notice, warn
        public double Until;
    }

    public sealed class Settings
    {
        public float Master = 0.8f, Music = 0.7f, Sfx = 0.8f;
        public bool ReducedFlashing;
        public bool ReducedShake;
        public int ScareLevel = 2;   // 0 minimal, 1 reduced, 2 full
        public bool Subtitles = true;
        public float Speed = 1f;     // playtest time multiplier
        public bool PlaytestTools = true;

        public string Serialize() => MiniJson.Serialize(new Dictionary<string, object>
        {
            ["master"] = (double)Master, ["music"] = (double)Music, ["sfx"] = (double)Sfx,
            ["flash"] = ReducedFlashing, ["shake"] = ReducedShake, ["scare"] = ScareLevel, ["subs"] = Subtitles,
            ["speed"] = (double)Speed, ["tools"] = PlaytestTools
        });

        public static Settings Parse(string json)
        {
            var s = new Settings();
            try
            {
                var d = J.Obj(MiniJson.Parse(json));
                s.Master = J.Float(d, "master", s.Master); s.Music = J.Float(d, "music", s.Music); s.Sfx = J.Float(d, "sfx", s.Sfx);
                s.ReducedFlashing = J.Bool(d, "flash"); s.ReducedShake = J.Bool(d, "shake"); s.ScareLevel = J.Int(d, "scare", 2);
                s.Subtitles = J.Bool(d, "subs", true); s.Speed = J.Float(d, "speed", 1f); s.PlaytestTools = J.Bool(d, "tools", true);
            }
            catch { }
            return s;
        }
    }

    /// <summary>
    /// Front-end controller for the browser playtest. Owns the campaign state, the running shift and
    /// all UI-side state (dialogue, overlays, toasts, scares). Rendering lives in GameRoot.razor.
    /// </summary>
    public sealed class GameSession
    {
        public readonly ContentDatabase Db;
        public GameState State;
        public Profile Profile = new Profile();
        public Settings Settings = new Settings();
        public ShiftContext Ctx;
        public Screen Screen = Screen.Title;
        public Overlay Overlay = Overlay.None;
        public bool Endless;

        // Dialogue
        public readonly List<Line> Lines = new List<Line>();
        public int LineIndex;
        public bool ShowingOutcome;
        public string InspectText;
        public string InspectLabel;
        public string MachineDisplay = "";
        public double MachineDisplayUntil;
        public readonly List<string> Feed = new List<string>();
        public readonly List<Toast> Toasts = new List<Toast>();
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

        private readonly IJSRuntime _js;

        public GameSession(IJSRuntime js)
        {
            _js = js;
            var asm = Assembly.GetExecutingAssembly();
            Db = ContentDatabase.Load(name =>
            {
                using var s = asm.GetManifestResourceStream($"Content.{name}.json");
                if (s == null) return null;
                using var r = new StreamReader(s);
                return r.ReadToEnd();
            });
        }

        // ------------------------------------------------------------ persistence

        public async Task LoadPersistentAsync()
        {
            try { Profile = Profile.Deserialize(await Get("m313.profile")); } catch { Profile = new Profile(); }
            Settings = Settings.Parse(await Get("m313.settings") ?? "{}");
            await ApplyAudioSettings();
        }

        public async Task<bool> HasSaveAsync() => !string.IsNullOrEmpty(await Get("m313.save"));

        private async Task<string> Get(string key)
        {
            try { return await _js.InvokeAsync<string>("m313.load", key); } catch { return null; }
        }

        private async Task Set(string key, string value)
        {
            try { await _js.InvokeVoidAsync("m313.save", key, value); } catch { }
        }

        public Task SaveProfile() => Set("m313.profile", Profile.Serialize());
        public async Task SaveSettings() { await Set("m313.settings", Settings.Serialize()); await ApplyAudioSettings(); }
        private Task SaveGame() => Endless ? Task.CompletedTask : Set("m313.save", SaveSerializer.Serialize(State));

        public async Task ApplyAudioSettings()
        {
            try { await _js.InvokeVoidAsync("m313.audio.volume", Settings.Master, Settings.Music, Settings.Sfx); } catch { }
        }

        public void Music(string id) { if (!string.IsNullOrEmpty(id)) _ = _js.InvokeVoidAsync("m313.audio.music", id); }
        public void Sfx(string id, float vol = 1f) { if (!string.IsNullOrEmpty(id)) _ = _js.InvokeVoidAsync("m313.audio.sfx", id, vol); }
        public void Ambience(string id) => _ = _js.InvokeVoidAsync("m313.audio.ambience", id ?? "");

        // ------------------------------------------------------------ flow

        public async Task NewGame()
        {
            Endless = false;
            State = GameState.NewGame();
            await SaveGame();
            GoPrep();
        }

        public async Task<bool> ContinueGame()
        {
            Endless = false;
            var json = await Get("m313.save");
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
            Screen = Screen.Prep;
            Overlay = Overlay.None;
            Music("last_stop");
            Ambience("wind");
        }

        public void StartShift()
        {
            State.CheckpointPhase = "shift";
            Checkpoint = SaveSerializer.Serialize(State);
            Ctx = Endless ? Endless_Build() : new ShiftContext(State, Db, 313 + State.Cycle * 31);
            Ctx.Director.Intensity = Settings.ScareLevel == 0 ? 0.25f : Settings.ScareLevel == 1 ? 0.6f : 1f;
            Screen = Screen.Shift;
            Overlay = Overlay.None;
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

        public async Task AfterSummary()
        {
            await SaveGame();
            GoPrep();
        }

        public async Task NextCycle()
        {
            State = Campaign.StartNextCycle(State);
            await SaveGame();
            GoPrep();
        }

        // ------------------------------------------------------------ tick

        public void Tick(double dt)
        {
            Now += dt;
            Toasts.RemoveAll(t => t.Until < Now);
            if (Screen != Screen.Shift || Ctx == null) return;
            bool paused = Overlay == Overlay.Phone || Overlay == Overlay.Investigation || Overlay == Overlay.ConfirmLeave;
            if (!paused) Ctx.Tick((float)(dt * Settings.Speed));
            DrainEvents();

            if (Ctx.Phase == ShiftPhase.Phone && Overlay != Overlay.Phone) { Overlay = Overlay.Phone; Sfx("phone_ring"); }
            if (Ctx.Phase == ShiftPhase.Investigation && Overlay != Overlay.Investigation) Overlay = Overlay.Investigation;
        }

        private void Say(IEnumerable<string> lines, string speaker)
        {
            foreach (var raw in lines ?? Enumerable.Empty<string>())
            {
                var l = new Line { Speaker = speaker, Text = raw };
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
        public Line CurrentLine => Lines.Count == 0 ? null : Lines[Math.Min(LineIndex, Lines.Count - 1)];

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
            Toasts.Add(new Toast { Text = text, Kind = kind, Until = Now + seconds });
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
                        Screen = Screen.Failed;
                        Overlay = Overlay.None;
                        Music("no_more_change");
                        Ambience("");
                        break;
                    case PresEventType.Sunrise: _ = OnSunrise(); break;
                    case PresEventType.Ending: _ = OnEnding(e.Id); break;
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

        private async Task OnSunrise()
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
                await SaveGame();
            }
            Screen = Screen.Summary;
            Overlay = Overlay.None;
        }

        private async Task OnEnding(string requested)
        {
            Ending = Campaign.ResolveEnding(State, Db, requested);
            State.EndingReached = Ending.Id;
            NewAchievements = Profile.RecordEnding(Ending.Id, Db);
            await SaveProfile();
            await SaveGame();
            foreach (var a in NewAchievements)
                _ = _js.InvokeVoidAsync("m313.achievement", a);
            Screen = Screen.Ending;
            Overlay = Overlay.None;
            Music(Ending.Music);
            Ambience("");
        }

        public async Task EndlessDeath()
        {
            NewEndlessBest = Profile.RecordEndlessRun(HikariForge.M313.Core.Endless.Round(State), EndlessEarnings + Ctx.NightEarnings);
            await SaveProfile();
            Screen = Screen.EndlessOver;
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
            Overlay = Overlay.Panel;
            PanelCode = "";
            PanelMessage = Ctx != null && Ctx.Jammed ? "ERROR E-13: MOTOR JAM" : "DIAGNOSTICS NOMINAL";
            Sfx("panel_open");
        }

        public void ClosePanel()
        {
            Ctx?.ClosePanel();
            Overlay = Overlay.None;
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
                if (used) { Overlay = Overlay.None; Ctx.ClosePanel(); }
                return;
            }
            if (PanelCode.Length < 4) PanelCode += k;
        }

        public void ResetMotor()
        {
            if (Ctx != null && Ctx.ResetMotor()) Toast("Motor reset. The machine hums back to life.");
            else PanelMessage = "NO FAULT DETECTED";
        }

        public void LeaveBooth()
        {
            Overlay = Overlay.None;
            Ctx?.LeaveBooth();
        }

        // ------------------------------------------------------------ playtest tools

        /// <summary>Plays earlier nights automatically with a careful policy so testers can jump ahead.</summary>
        public async Task SkipToNight(int night)
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
            await SaveGame();
            GoPrep();
            Toast($"Playtest: auto-played to Night {s.Night} on the careful route.", "notice", 6);
        }

        public async Task UnlockAllEndings()
        {
            foreach (var e in Db.Endings) Profile.RecordEnding(e.Id, Db);
            await SaveProfile();
            Toast("Playtest: all endings marked as seen. Endless Mode unlocked.", "achievement", 6);
        }
    }
}
