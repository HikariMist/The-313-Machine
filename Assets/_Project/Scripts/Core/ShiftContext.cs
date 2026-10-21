using System;
using System.Collections.Generic;
using System.Linq;

namespace HikariForge.M313.Core
{
    public enum ShiftPhase { Idle, Encounter, Phone, Investigation, Ended, Failed }

    public enum EncounterStage { Talking, Awaiting, Resolving }

    public sealed class ActiveEncounter
    {
        public EncounterDef Def;
        public EncounterStage Stage = EncounterStage.Talking;
        public float PatienceLeft;
        public readonly HashSet<string> UsedTalk = new HashSet<string>();
        public readonly HashSet<string> Inspected = new HashSet<string>();
        public OutcomeDef Outcome;
        public string ActionKey;
        public string ServedProduct;
        public bool Acknowledged;   // player spoke to, served or refused this customer
        public int Wanted = 1;      // items in this order
        public int Given;           // items already dispensed toward the order
        public int PaidSoFar;       // cents taken for those items
        public int Remaining => Wanted - Given;
    }

    public sealed class InspectResult
    {
        public string Spot;
        public string Label;
        public string Text;
        public bool Locked;
        public string RequiredUpgrade;
    }

    /// <summary>
    /// Pure simulation of one night shift. The Unity layer calls Tick() every frame and the
    /// action methods on player input, then drains <see cref="Events"/> to render the results.
    /// </summary>
    public sealed class ShiftContext
    {
        public const float IdleRate = 3.0f;        // reference rate; between customers the clock is paced adaptively
        public const float MinIdleRate = 0.7f, MaxIdleRate = 6f;
        /// <summary>Target real length of a night shift (seconds). Design goal: 10 minutes.</summary>
        public static float TargetNightSeconds = 600f;
        /// <summary>Expected real time a player spends with one customer, used to plan the idle pace.</summary>
        public const float ExpectedEncounterSeconds = 60f;
        public const float EncounterRate = 0.25f;  // game minutes per real second during a customer
        public const int SlowRestockMinutes = 12;
        public const int FastRestockMinutes = 2;

        public readonly GameState State;
        public readonly ContentDatabase Db;
        public readonly NightDef NightDef;
        public readonly HorrorDirector Director;
        public readonly Queue<PresEvent> Events = new Queue<PresEvent>();

        public float Clock { get; private set; }
        public ShiftPhase Phase { get; private set; } = ShiftPhase.Idle;
        public ActiveEncounter Current { get; private set; }
        public bool InMargin { get; private set; }
        public bool WrongTimeActive => _wrongTimeUntil > Clock;
        public bool KnockActive => _knockRemaining > 0;
        public bool BlackoutActive => _blackoutRemaining > 0;
        public bool BackupPowerActive { get; private set; }
        public bool Jammed { get; private set; }
        public bool PanelOpen { get; private set; }
        public int NightEarnings { get; private set; }
        public int CustomersServed { get; private set; }
        /// <summary>Items dispensed to customers tonight (a three-item order counts three).</summary>
        public int ItemsSold { get; private set; }
        /// <summary>Orders a customer left before receiving every item (refused, timed out or wrong item).</summary>
        public int OrdersCutShort { get; private set; }
        public int CustomersRefused { get; private set; }
        public string FailReason { get; private set; }
        public List<string> FailLines { get; private set; } = new List<string>();
        public string RequestedEnding { get; private set; }
        public PresEvent PendingPhone { get; private set; }
        public EventDef PendingInvestigation { get; private set; }
        public readonly HashSet<string> SearchedHotspots = new HashSet<string>();
        public int EncountersSeen { get; private set; }
        public float RealTime { get; private set; }

        private readonly List<EncounterDef> _pendingEncounters;
        private readonly List<EventDef> _pendingEvents;
        private readonly Queue<EventDef> _blockingQueue = new Queue<EventDef>();
        private EventDef _knock;
        private float _knockRemaining;
        private float _blackoutRemaining;
        private float _wrongTimeUntil;
        private EventDef _wrongTime;
        private bool _pendingFail;
        private bool _marginFired;
        private readonly int _plannedEnd;

        public ShiftContext(GameState state, ContentDatabase db, int seed = 313)
            : this(state, db, db.Night(state.Night) ?? new NightDef { Night = state.Night, Title = "Night " + state.Night },
                   db.EncountersForNight(state.Night).ToList(), seed) { }

        /// <summary>Custom night (used by Endless Mode).</summary>
        public ShiftContext(GameState state, ContentDatabase db, NightDef night, List<EncounterDef> encounters, int seed = 313)
        {
            State = state;
            Db = db;
            NightDef = night;
            Director = new HorrorDirector(seed + state.Night * 97);
            _pendingEncounters = encounters.OrderBy(e => e.Time).ToList();
            _pendingEvents = NightDef.Events.OrderBy(e => e.Time).ToList();
            // Nights that end in a finale (an ending) stop at the finale, not at 6:00 AM: pace to that instead.
            var finale = _pendingEncounters.LastOrDefault(e => e.Outcomes.Values.Any(l => l.Any(o => o.Effects.Any(x => x.StartsWith("ending:")))));
            _plannedEnd = finale != null ? Math.Min(ContentDatabase.ShiftEnd, finale.Time + 15) : ContentDatabase.ShiftEnd;
            // Every shift opens with the night's phone call from the District Office.
            if (NightDef.PhoneCall.Count > 0 && !_pendingEvents.Any(e => e.Type == "phone" && e.Time <= 1))
                _pendingEvents.Insert(0, new EventDef { Time = 1, Type = "phone", Id = "call_open", Speaker = NightDef.PhoneSpeaker, Lines = NightDef.PhoneCall });
            State.Transactions.RemoveAll(t => t.Night == state.Night);
            InMargin = false;
        }

        public void Emit(PresEventType type, string id = null, string text = null, int value = 0, List<string> lines = null) =>
            Events.Enqueue(new PresEvent { Type = type, Id = id, Text = text, Value = value, Lines = lines });

        public bool IsOver => Phase == ShiftPhase.Ended || Phase == ShiftPhase.Failed;

        public int RemainingEncounters => _pendingEncounters.Count + (Current != null ? 1 : 0);

        // ------------------------------------------------------------------ tick

        public void Tick(float dt)
        {
            if (IsOver || dt <= 0) return;
            RealTime += dt;

            if (_knockRemaining > 0)
            {
                _knockRemaining -= dt;
                if (_knockRemaining <= 0) { _knock = null; Emit(PresEventType.KnockEnd); }
            }
            if (_blackoutRemaining > 0)
            {
                _blackoutRemaining -= dt;
                if (_blackoutRemaining <= 0) { BackupPowerActive = false; Emit(PresEventType.BlackoutEnd); }
                else if (!BackupPowerActive && (int)(_blackoutRemaining + dt) / 4 != (int)_blackoutRemaining / 4)
                    State.AddInstability(2);
            }

            Director.Tick(dt, this);

            switch (Phase)
            {
                case ShiftPhase.Idle: TickIdle(dt); break;
                case ShiftPhase.Encounter: TickEncounter(dt); break;
            }

            CheckCollapse();
        }

        private float NextScheduledTime()
        {
            float t = ContentDatabase.ShiftEnd;
            if (_pendingEncounters.Count > 0) t = Math.Min(t, _pendingEncounters[0].Time);
            if (_pendingEvents.Count > 0) t = Math.Min(t, _pendingEvents[0].Time);
            if (!_marginFired) t = Math.Min(t, ContentDatabase.MarginTime);
            return t;
        }

        private void TickIdle(float dt)
        {
            if (_blockingQueue.Count > 0) { StartBlocking(_blockingQueue.Dequeue()); return; }

            float target = NextScheduledTime();
            float rate = PacedIdleRate();
            if (BlackoutActive && !BackupPowerActive) rate *= 0.5f;
            AdvanceClock(Math.Min(target, Clock + dt * rate));
            FireDueEvents();
            if (Phase != ShiftPhase.Idle) return;
            if (_blockingQueue.Count > 0) { StartBlocking(_blockingQueue.Dequeue()); return; }

            // Start the next due encounter whose condition holds; drop ones whose condition fails.
            while (_pendingEncounters.Count > 0 && _pendingEncounters[0].Time <= Clock)
            {
                var next = _pendingEncounters[0];
                _pendingEncounters.RemoveAt(0);
                if (Conditions.Evaluate(next.Condition, State, Db, this)) { StartEncounter(next); return; }
            }

            if (Clock >= ContentDatabase.ShiftEnd && _pendingEncounters.Count == 0 && _pendingEvents.Count == 0)
                EndShift();
        }

        /// <summary>
        /// Paces the empty stretches so the whole night lands near <see cref="TargetNightSeconds"/>:
        /// the remaining clock (minus what upcoming customers will use) is spread over the remaining real time.
        /// </summary>
        private float PacedIdleRate()
        {
            int pending = _pendingEncounters.Count;
            float gameLeft = _plannedEnd - Clock - pending * ExpectedEncounterSeconds * EncounterRate;
            float realLeft = TargetNightSeconds - RealTime - pending * ExpectedEncounterSeconds;
            if (gameLeft <= 0) return MaxIdleRate;
            if (realLeft <= 20f) return MaxIdleRate;
            return Math.Max(MinIdleRate, Math.Min(MaxIdleRate, gameLeft / realLeft));
        }

        private void TickEncounter(float dt)
        {
            float limit = Math.Min(ContentDatabase.ShiftEnd - 1, NextScheduledTime());
            if (Clock < limit) AdvanceClock(Math.Min(limit, Clock + dt * EncounterRate));
            FireDueEvents(nonBlockingOnly: true);

            var cur = Current;
            if (cur != null && cur.Stage == EncounterStage.Awaiting && !(BlackoutActive && !BackupPowerActive))
            {
                cur.PatienceLeft -= dt;
                if (cur.PatienceLeft <= 0) Resolve("timeout", null);
            }
        }

        private void AdvanceClock(float to)
        {
            if (to <= Clock) return;
            Clock = to;
            if (!_marginFired && Clock >= ContentDatabase.MarginTime)
            {
                _marginFired = true;
                InMargin = true;
                Emit(PresEventType.Margin, NightDef.MarginMusic);
            }
            if (_wrongTime != null && Clock >= _wrongTimeUntil)
            {
                _wrongTime = null;
                Emit(PresEventType.WrongTimeEnd);
            }
        }

        private void FireDueEvents(bool nonBlockingOnly = false)
        {
            for (int i = 0; i < _pendingEvents.Count; i++)
            {
                var ev = _pendingEvents[i];
                if (ev.Time > Clock) break;
                bool blocking = ev.Type == "phone" || ev.Type == "investigate";
                if (blocking && nonBlockingOnly) continue;
                _pendingEvents.RemoveAt(i);
                i--;
                if (!Conditions.Evaluate(ev.Condition, State, Db, this)) continue;
                if (blocking) _blockingQueue.Enqueue(ev);
                else FireEvent(ev);
            }
        }

        private void FireEvent(EventDef ev)
        {
            Effects.Apply(ev.Effects, State, Db, this);
            switch (ev.Type)
            {
                case "machine": Emit(PresEventType.MachineMessage, ev.Id, string.Join("\n", ev.Lines)); break;
                case "scare":
                    TriggerScare(ev.Id, false, "event " + ev.Id);
                    break;
                case "page": break; // handled by effects (page:id)
                case "knock":
                    _knock = ev;
                    _knockRemaining = ev.Duration;
                    Emit(PresEventType.KnockStart, ev.Id, null, (int)ev.Duration);
                    break;
                case "blackout":
                {
                    int power = State.Level(UpgradeIds.PowerBackup);
                    BackupPowerActive = power > 0;
                    _blackoutRemaining = ev.Duration * BlackoutFactor[Math.Min(3, power)];
                    if (!BackupPowerActive && State.Level(UpgradeIds.Lighting) < 3) State.AddInstability(8);
                }
                    Emit(PresEventType.BlackoutStart, ev.Id, null, BackupPowerActive ? 1 : 0);
                    break;
                case "wrongtime":
                    _wrongTime = ev;
                    _wrongTimeUntil = Clock + ev.Duration;
                    Emit(PresEventType.WrongTimeStart, ev.Id, null, (int)ev.Duration);
                    break;
                case "jam":
                    Jammed = true;
                    Emit(PresEventType.Jam, ev.Id);
                    break;
                case "music": Emit(PresEventType.Music, ev.Id); break;
                case "notice": Emit(PresEventType.Notice, ev.Id, string.Join("\n", ev.Lines)); break;
                case "effects":
                case "clue":
                    break;
                default: Emit(PresEventType.Notice, ev.Id, string.Join("\n", ev.Lines)); break;
            }
        }

        private void StartBlocking(EventDef ev)
        {
            Effects.Apply(ev.Effects, State, Db, this);
            if (ev.Type == "phone")
            {
                Phase = ShiftPhase.Phone;
                PendingPhone = new PresEvent { Type = PresEventType.Phone, Id = ev.Id, Text = ev.Speaker, Lines = ev.Lines };
                Emit(PresEventType.Phone, ev.Id, ev.Speaker, 0, ev.Lines);
            }
            else
            {
                Phase = ShiftPhase.Investigation;
                PendingInvestigation = ev;
                Emit(PresEventType.Investigation, ev.Id, NightDef.InvestigationIntro);
            }
        }

        // ------------------------------------------------------------------ encounters

        private void StartEncounter(EncounterDef def)
        {
            Current = new ActiveEncounter { Def = def, PatienceLeft = def.Patience, Wanted = OrderSize(def, State.Night, State.Cycle) };
            Phase = ShiftPhase.Encounter;
            EncountersSeen++;
            Effects.Apply(def.ArriveEffects, State, Db, this);
            Emit(PresEventType.Arrive, def.Id, def.Name);
            if (!string.IsNullOrEmpty(def.ArriveScare))
            {
                TriggerScare(def.ArriveScare, false, "arrival " + def.Id);
            }
            if (!string.IsNullOrEmpty(def.Music)) Emit(PresEventType.Music, def.Music);
        }

        /// <summary>The UI calls this once the customer's opening lines have been shown.</summary>
        public void BeginAwaiting()
        {
            if (Current != null && Current.Stage == EncounterStage.Talking) Current.Stage = EncounterStage.Awaiting;
        }

        public List<InspectDef> AvailableInspections()
        {
            if (Current == null) return new List<InspectDef>();
            return Current.Def.Inspect.Where(i => Conditions.Evaluate(i.Condition, State, Db, this)).ToList();
        }

        public InspectResult Inspect(string spot)
        {
            if (Current == null || Current.Stage == EncounterStage.Resolving) return null;
            var defs = AvailableInspections().Where(i => i.Spot == spot).ToList();
            if (defs.Count == 0) return DefaultInspection(spot);
            var parts = new List<string>();
            InspectResult result = new InspectResult { Spot = spot, Label = defs[0].Label };
            foreach (var d in defs)
            {
                // The Customer ID Tool shows authored scan readings from level 2; level 1 gives a basic reading.
                int needed = d.Upgrade == UpgradeIds.IdTool ? 2 : 1;
                if (!string.IsNullOrEmpty(d.Upgrade) && State.Level(d.Upgrade) < needed)
                {
                    if (defs.Count == 1)
                    {
                        // ID Tool level 1 still gives the basic reading.
                        if (spot == "scan" && State.Level(UpgradeIds.IdTool) == 1) return DefaultInspection(spot);
                        result.Locked = true; result.RequiredUpgrade = d.Upgrade;
                    }
                    continue;
                }
                parts.Add(d.Text);
                if (Current.Inspected.Add(spot + "|" + d.Text))
                {
                    if (!string.IsNullOrEmpty(d.Clue)) Effects.Apply("clue:" + d.Clue, State, Db, this);
                    Effects.Apply(d.Effects, State, Db, this);
                }
            }
            result.Text = parts.Count > 0 ? string.Join("\n\n", parts) : "You would need better equipment to tell.";
            if (spot == "scan" && parts.Count > 0) result.Text += RuleHint();
            return result;
        }

        public static readonly string[] StandardSpots = { "face", "hands", "clothes", "reflection", "payment", "scan" };

        /// <summary>Inspection buttons for the current customer: authored spots plus reflection and scan.</summary>
        public List<string> InspectSpots()
        {
            var spots = new List<string>();
            if (Current == null) return spots;
            foreach (var i in AvailableInspections()) if (!spots.Contains(i.Spot)) spots.Add(i.Spot);
            if (!spots.Contains("reflection")) spots.Add("reflection");
            if (!spots.Contains("scan")) spots.Add("scan");
            return spots;
        }

        private InspectResult DefaultInspection(string spot)
        {
            var d = Current.Def;
            if (spot == "reflection")
            {
                string text = d.NoReflection ? "There is no reflection in the glass."
                    : d.ReflectionLags ? "The reflection does not move when they move."
                    : "Their reflection matches them.";
                return new InspectResult { Spot = spot, Label = "Reflection", Text = text };
            }
            if (spot == "scan")
            {
                if (State.Level(UpgradeIds.IdTool) == 0)
                    return new InspectResult { Spot = spot, Label = "SCAN", Locked = true, RequiredUpgrade = UpgradeIds.IdTool, Text = "You would need better equipment to tell." };
                return new InspectResult { Spot = spot, Label = "SCAN", Text = (d.Supernatural ? "PULSE: NONE.  RESIDUE: HIGH." : "PULSE: NORMAL.  RESIDUE: NONE.") + RuleHint() };
            }
            return new InspectResult { Spot = spot, Text = "Nothing unusual." };
        }

        /// <summary>Customer ID Tool level 3: names the handbook sections that apply, never the answer.</summary>
        private string RuleHint()
        {
            if (State.Level(UpgradeIds.IdTool) < 3 || Current == null || Current.Def.Rules.Count == 0) return "";
            var sections = Current.Def.Rules.Select(r => r.Substring(0, 1)).Distinct().OrderBy(x => x);
            return "\nCROSS-REFERENCE: handbook section " + string.Join(", ", sections) + ".";
        }

        public static readonly string[] Cameras = { "cam1", "cam2", "cam3", "cam4", "cam0" };

        public bool CameraAvailable(string cam)
        {
            if (BlackoutActive && !BackupPowerActive && State.Level(UpgradeIds.Monitor) < 3) return false;
            if (cam == "cam4") return State.Level(UpgradeIds.Monitor) >= 1;
            if (cam == "cam0") return State.Flag("cam0") > 0;
            return true;
        }

        /// <summary>What a camera shows right now. Defaults: the living appear on CAM 2, the dead do not.</summary>
        public CameraViewDef CameraView(string cam)
        {
            var view = new CameraViewDef { Cam = cam, State = "empty", Note = "", Timestamp = ContentDatabase.FormatTime(Clock) };
            if (InMargin && (cam == "cam1" || cam == "cam3") && NightDef.Tier >= 2)
                view.Timestamp = (int)RealTime % 7 == 0 ? "3:13 AM" : view.Timestamp;
            if (Current == null) return view;
            var d = Current.Def;
            if (cam == "cam1") view.State = "present";
            else if (cam == "cam2") view.State = d.Supernatural ? "absent" : "present";
            else if (cam == "cam0") view.State = "present";
            foreach (var c in d.Cameras)
            {
                if (c.Cam != cam) continue;
                if (!string.IsNullOrEmpty(c.Upgrade) && !State.Upgrades.Contains(c.Upgrade)) continue;
                if (!Conditions.Evaluate(c.Condition, State, Db, this)) continue;
                view.State = c.State;
                view.Note = c.Note;
                if (!string.IsNullOrEmpty(c.Timestamp)) view.Timestamp = c.Timestamp;
            }
            // Enhanced Monitor level 2: anomaly alert when the customer's reflection is wrong.
            if (cam == "cam2" && State.Level(UpgradeIds.Monitor) >= 2 && (d.NoReflection || d.ReflectionLags))
                view.Note = (string.IsNullOrEmpty(view.Note) ? "" : view.Note + " ") + "[ANOMALY ALERT: reflection does not match subject.]";
            return view;
        }

        public List<TalkDef> AvailableTalk()
        {
            if (Current == null || Current.Stage == EncounterStage.Resolving) return new List<TalkDef>();
            return Current.Def.Talk
                .Where(t => (t.End || !Current.UsedTalk.Contains(t.Id)) && Conditions.Evaluate(t.Condition, State, Db, this))
                .ToList();
        }

        /// <summary>Non-terminal talk returns the reply lines; terminal talk resolves the encounter.</summary>
        public List<string> Talk(string id)
        {
            if (Current == null || Current.Stage == EncounterStage.Resolving) return null;
            var t = AvailableTalk().FirstOrDefault(x => x.Id == id);
            if (t == null) return null;
            Current.Acknowledged = true;
            if (t.End)
            {
                Resolve("choice:" + id, null);
                return Current?.Outcome?.Lines;
            }
            Current.UsedTalk.Add(id);
            Effects.Apply(t.Effects, State, Db, this);
            return t.Reply;
        }

        public enum ServeError { None, NoCustomer, EmptySlot, NoPower, Jammed }

        public ServeError Serve(string slotCode)
        {
            if (Current == null || Current.Stage == EncounterStage.Resolving) return ServeError.NoCustomer;
            if (BlackoutActive && !BackupPowerActive) { Emit(PresEventType.DispenseFailed, "power"); return ServeError.NoPower; }
            if (Jammed) { Emit(PresEventType.DispenseFailed, "jam"); return ServeError.Jammed; }
            var slot = State.Slot(slotCode);
            if (slot == null || slot.ProductId == null || slot.Count <= 0) { Emit(PresEventType.DispenseFailed, "empty", slotCode); return ServeError.EmptySlot; }
            State.TakeFromSlot(slotCode, out var product);
            Emit(PresEventType.Dispense, product, slotCode);
            if (WrongTimeActive && _wrongTime != null) Effects.Apply(_wrongTime.OnServe, State, Db, this);
            Current.Acknowledged = true;
            if (product == Current.Def.Request && Current.Remaining > 1 && IsPlainSale(PeekOutcome("serve:" + product)))
            {
                // Part of a larger order: take payment for this item and wait for the rest.
                // Anything other than a plain good sale (a rule trap, a scare) resolves on the first item instead.
                int unit = UnitPrice(Current.Def, product);
                Current.Given++;
                ItemsSold++;
                Current.PaidSoFar += unit;
                State.Money += unit;
                State.TotalEarned += unit;
                NightEarnings += unit;
                Current.PatienceLeft = Math.Min(Current.Def.Patience, Current.PatienceLeft + 20f);
                Emit(PresEventType.OrderProgress, product, null, Current.Remaining);
                return ServeError.None;
            }
            Resolve("serve:" + product, product);
            return ServeError.None;
        }

        public void Refuse()
        {
            if (Current == null || Current.Stage == EncounterStage.Resolving) return;
            Current.Acknowledged = true;
            Resolve("refuse", null);
        }

        /// <summary>Maintenance keypad code entered while a customer is present (or idle).</summary>
        public bool EnterCode(string code)
        {
            if (Current == null || Current.Stage == EncounterStage.Resolving) return false;
            string key = "code:" + code;
            if (!Current.Def.Outcomes.ContainsKey(key) && !Current.Def.Outcomes.ContainsKey("code:*")) return false;
            Resolve(key, null);
            return true;
        }

        public void OpenPanel()
        {
            if (PanelOpen || IsOver) return;
            PanelOpen = true;
            if (KnockActive && _knock != null)
            {
                var knock = _knock;
                _knock = null;
                _knockRemaining = 0;
                Emit(PresEventType.KnockEnd, "opened");
                Effects.Apply(knock.OnOpen, State, Db, this);
                State.SetFlag("opened_on_knock", State.Flag("opened_on_knock") + 1);
                if (knock.OpenResult == "death") Fail("panel", knock.OpenLines);
                else if (knock.OpenLines.Count > 0) Emit(PresEventType.Notice, "panel", string.Join("\n", knock.OpenLines));
            }
        }

        public void ClosePanel() => PanelOpen = false;

        public bool ResetMotor()
        {
            if (!PanelOpen || !Jammed) return false;
            Jammed = false;
            Emit(PresEventType.JamCleared);
            return true;
        }

        private static readonly Dictionary<string, int> SelfUse = new Dictionary<string, int>
        {
            ["COFFEE"] = -15, ["ENERGY"] = -10, ["ASPIRIN"] = -12, ["CHOCO"] = -6, ["MINTS"] = -4, ["NOODLES"] = -8
        };

        public bool CanConsume(string productId) => productId != null && SelfUse.ContainsKey(productId);

        /// <summary>Attendant takes an item for themselves to steady their nerves.</summary>
        public bool ConsumeForSelf(string slotCode)
        {
            if (IsOver || (BlackoutActive && !BackupPowerActive)) return false;
            var slot = State.Slot(slotCode);
            if (slot == null || !CanConsume(slot.ProductId) || slot.Count <= 0) return false;
            State.TakeFromSlot(slotCode, out var product);
            State.AddInstability(SelfUse[product]);
            Emit(PresEventType.Consumed, product, null, SelfUse[product]);
            return true;
        }

        public static readonly int[] RestockMinutes = { 12, 8, 4, 2 };
        public static readonly float[] BlackoutFactor = { 1f, 0.6f, 0.35f, 0.2f };

        public bool CanRestockNow => Phase == ShiftPhase.Idle || (Phase == ShiftPhase.Encounter && State.Level(UpgradeIds.FastRestock) >= 2);

        public int Restock(string slotCode, string productId)
        {
            if (!CanRestockNow) return 0;
            int moved = State.LoadSlot(slotCode, productId);
            if (moved > 0)
            {
                int minutes = RestockMinutes[Math.Min(3, State.Level(UpgradeIds.FastRestock))];
                if (Phase == ShiftPhase.Idle) AdvanceClock(Math.Min(Clock + minutes, NextScheduledTime()));
                Emit(PresEventType.Restocked, productId, slotCode, moved);
            }
            return moved;
        }

        private List<OutcomeDef> FindOutcomes(string key)
        {
            var o = Current.Def.Outcomes;
            if (o.TryGetValue(key, out var list)) return list;
            if (key.StartsWith("serve:") && o.TryGetValue("serve:*", out list)) return list;
            if (key.StartsWith("code:") && o.TryGetValue("code:*", out list)) return list;
            if (key == "timeout" && o.TryGetValue("refuse", out list)) return list;
            return null;
        }

        private OutcomeDef PeekOutcome(string key)
        {
            var list = FindOutcomes(key);
            return list?.FirstOrDefault(v => Conditions.Evaluate(v.Condition, State, Db, this)) ?? DefaultOutcome(key, Current.Def);
        }

        private static bool IsPlainSale(OutcomeDef o) =>
            o.Result == "good" && o.Paid && string.IsNullOrEmpty(o.Scare) && string.IsNullOrEmpty(o.Receipt);

        private void Resolve(string key, string product)
        {
            var cur = Current;
            var def = cur.Def;
            var outcome = PeekOutcome(key);

            // "retry" outcomes answer the player without ending the encounter (e.g. a wrong keypad code).
            if (outcome.Result == "retry")
            {
                Effects.Apply(outcome.Effects, State, Db, this);
                if (!string.IsNullOrEmpty(outcome.Scare)) TriggerScare(outcome.Scare, false, "reply " + def.Id);
                State.Decisions.Add(new DecisionRecord { Night = State.Night, Encounter = def.Id, Action = key, Result = "retry" });
                Emit(PresEventType.Reply, def.Id, null, 0, outcome.Lines);
                cur.Stage = EncounterStage.Awaiting;
                cur.PatienceLeft = Math.Max(cur.PatienceLeft, 30f);
                return;
            }

            if (cur.Given > 0 && !(product == def.Request && cur.Remaining == 1)) OrdersCutShort++;
            cur.Outcome = outcome;
            cur.ActionKey = key;
            cur.ServedProduct = product;
            cur.Stage = EncounterStage.Resolving;

            if (product != null)
            {
                int amount = 0;
                if (outcome.Paid)
                {
                    amount = UnitPrice(def, product);
                    State.Money += amount;
                    State.TotalEarned += amount;
                    NightEarnings += amount;
                }
                CustomersServed++;
                ItemsSold++;
                State.TotalServed++;
                int quantity = cur.Given + 1, total = amount + cur.PaidSoFar;
                string receipt = !string.IsNullOrEmpty(outcome.Receipt)
                    ? outcome.Receipt
                    : DefaultReceipt(product, total, quantity);
                State.Transactions.Add(new TransactionRecord
                {
                    Night = State.Night, Time = (int)Clock, Customer = def.Name, ProductId = product, Amount = total, Receipt = receipt
                });
                Emit(PresEventType.Receipt, product, receipt, total);
            }
            else if (key == "refuse" || key == "timeout")
            {
                CustomersRefused++;
                State.TotalRefused++;
            }

            Effects.Apply(outcome.Effects, State, Db, this);
            if (outcome.Result == "bad") State.AddInstability(5);
            if (!string.IsNullOrEmpty(outcome.Scare)) TriggerScare(outcome.Scare, outcome.Result == "death", "outcome " + def.Id + " " + key);
            State.Decisions.Add(new DecisionRecord { Night = State.Night, Encounter = def.Id, Action = key, Result = outcome.Result });
            if (outcome.Result == "death") _pendingFail = true;
        }

        private int UnitPrice(EncounterDef def, string product) =>
            def.Pays > 0 ? def.Pays : (Db.Product(product)?.Price ?? 0);

        /// <summary>
        /// Items an ordinary customer wants: 1 on Night 1, rising every two nights and with each cycle (max 5).
        /// Supernatural visitors and unlisted or story requests always take a single item.
        /// </summary>
        public static int OrderSize(EncounterDef def, int night, int cycle)
        {
            if (def.Quantity > 0) return def.Quantity;
            if (def.Supernatural || string.IsNullOrEmpty(def.Request) || def.Request == "UNLISTED" || def.Request == "TOKEN") return 1;
            int max = Math.Min(MaxOrder, 1 + (Math.Max(1, night) - 1) / 2 + Math.Max(0, cycle - 1));
            if (max <= 1) return 1;
            // Stable per customer and night so a replayed night asks for the same amount.
            uint h = 2166136261;
            foreach (char c in def.Id + ":" + night + ":" + cycle) h = (h ^ c) * 16777619;
            return h % 3 == 0 ? max - 1 : max;
        }

        public const int MaxOrder = 5;

        private string DefaultReceipt(string product, int amount, int quantity = 1)
        {
            var p = Db.Product(product);
            string name = (p?.Name ?? product) + (quantity > 1 ? " x" + quantity : "");
            string time = ContentDatabase.FormatTime(Clock);
            return $"MERIDIAN ROADSIDE SERVICES\nUNIT 313 — KESSLER FLATS\n{time}\n{name.ToUpperInvariant()}  ${amount / 100}.{amount % 100:00}\nTHANK YOU. COME AGAIN.";
        }

        private static OutcomeDef DefaultOutcome(string key, EncounterDef def)
        {
            if (key.StartsWith("serve:"))
            {
                bool right = string.IsNullOrEmpty(def.Request) || key == "serve:" + def.Request;
                return new OutcomeDef
                {
                    Lines = new List<string> { right ? "Thanks." : "...That's not what I asked for. Whatever." },
                    Result = right ? "good" : "neutral"
                };
            }
            if (key.StartsWith("code:"))
                return new OutcomeDef { Lines = new List<string> { "*The keypad beeps twice. Nothing happens." }, Result = "neutral", Paid = false };
            if (key == "timeout")
                return new OutcomeDef { Lines = new List<string> { "*They wait a while longer, then walk back into the dark." }, Result = "neutral", Paid = false };
            return new OutcomeDef { Lines = new List<string> { "*They leave without a word." }, Result = "neutral", Paid = false };
        }

        /// <summary>The UI calls this after the outcome lines finish; the customer leaves.</summary>
        public void FinishEncounter()
        {
            if (Current == null || Current.Stage != EncounterStage.Resolving) return;
            var def = Current.Def;
            var outcome = Current.Outcome;
            Current = null;
            Emit(PresEventType.Leave, def.Id);
            if (_pendingFail) { Fail(def.Id, outcome.Lines); return; }
            if (RequestedEnding != null) { EndShift(); return; }
            Phase = ShiftPhase.Idle;
        }

        public void AcknowledgePhone()
        {
            if (Phase != ShiftPhase.Phone) return;
            PendingPhone = null;
            Phase = ShiftPhase.Idle;
        }

        public List<HotspotDef> AvailableHotspots() =>
            NightDef.Investigation.Where(h => Conditions.Evaluate(h.Condition, State, Db, this)).ToList();

        public HotspotDef Search(string hotspotId)
        {
            if (Phase != ShiftPhase.Investigation) return null;
            var h = AvailableHotspots().FirstOrDefault(x => x.Id == hotspotId);
            if (h == null) return null;
            if (SearchedHotspots.Add(h.Id))
            {
                if (!string.IsNullOrEmpty(h.Clue)) Effects.Apply("clue:" + h.Clue, State, Db, this);
                Effects.Apply(h.Effects, State, Db, this);
            }
            return h;
        }

        public bool CanFinishInvestigation =>
            Phase == ShiftPhase.Investigation && NightDef.Investigation.Where(h => h.Required).All(h => SearchedHotspots.Contains(h.Id));

        public bool FinishInvestigation()
        {
            if (!CanFinishInvestigation) return false;
            PendingInvestigation = null;
            Phase = ShiftPhase.Idle;
            return true;
        }

        /// <summary>Rule A2: the attendant walks out of the booth. Always ends the campaign.</summary>
        public void LeaveBooth()
        {
            if (IsOver) return;
            Current = null;
            State.Decisions.Add(new DecisionRecord { Night = State.Night, Encounter = "booth", Action = "leave", Result = "ending" });
            RequestEnding("off_the_clock");
            EndShift();
        }

        /// <summary>All scripted scares go through the director, which may soften them to protect pacing.</summary>
        public void TriggerScare(string id, bool lethal, string reason) => Director.RequestScripted(id, lethal, reason, this);

        public void RequestEnding(string id)
        {
            RequestedEnding = string.IsNullOrEmpty(id) ? "auto" : id;
        }

        private void CheckCollapse()
        {
            if (!IsOver && State.Instability >= 100)
            {
                Fail("collapse", new List<string>
                {
                    "*The lot tilts. The hum of the machine becomes the only sound in the world.",
                    "*When you come back to yourself, you are standing in the reservoir up to your knees, and it is already morning."
                });
            }
        }

        private void Fail(string reason, List<string> lines)
        {
            Phase = ShiftPhase.Failed;
            FailReason = reason;
            FailLines = lines ?? new List<string>();
            Current = null;
            Emit(PresEventType.Failed, reason, null, 0, FailLines);
        }

        private void EndShift()
        {
            Phase = ShiftPhase.Ended;
            if (RequestedEnding != null) Emit(PresEventType.Ending, RequestedEnding);
            else Emit(PresEventType.Sunrise);
        }

        /// <summary>Debug/test helper: skips the clock forward while idle.</summary>
        public void DebugAdvance(float minutes)
        {
            if (Phase == ShiftPhase.Idle) AdvanceClock(Math.Min(Clock + minutes, NextScheduledTime()));
        }
    }
}
