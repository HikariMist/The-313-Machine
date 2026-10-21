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
        public const float IdleRate = 3.0f;        // game minutes per real second between customers
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
            float rate = BlackoutActive && !BackupPowerActive ? IdleRate * 0.5f : IdleRate;
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
                    Emit(PresEventType.Scare, ev.Id);
                    Director.NotifyScripted(ev.Id);
                    break;
                case "page": break; // handled by effects (page:id)
                case "knock":
                    _knock = ev;
                    _knockRemaining = ev.Duration;
                    Emit(PresEventType.KnockStart, ev.Id, null, (int)ev.Duration);
                    break;
                case "blackout":
                    BackupPowerActive = State.Upgrades.Contains(UpgradeIds.PowerBackup);
                    _blackoutRemaining = BackupPowerActive ? ev.Duration * 0.35f : ev.Duration;
                    if (!BackupPowerActive) State.AddInstability(8);
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
            Current = new ActiveEncounter { Def = def, PatienceLeft = def.Patience };
            Phase = ShiftPhase.Encounter;
            EncountersSeen++;
            Effects.Apply(def.ArriveEffects, State, Db, this);
            Emit(PresEventType.Arrive, def.Id, def.Name);
            if (!string.IsNullOrEmpty(def.ArriveScare))
            {
                Emit(PresEventType.Scare, def.ArriveScare);
                Director.NotifyScripted(def.ArriveScare);
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
                if (!string.IsNullOrEmpty(d.Upgrade) && !State.Upgrades.Contains(d.Upgrade))
                {
                    if (defs.Count == 1) { result.Locked = true; result.RequiredUpgrade = d.Upgrade; }
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
                if (!State.Upgrades.Contains(UpgradeIds.IdTool))
                    return new InspectResult { Spot = spot, Label = "SCAN", Locked = true, RequiredUpgrade = UpgradeIds.IdTool, Text = "You would need better equipment to tell." };
                return new InspectResult { Spot = spot, Label = "SCAN", Text = d.Supernatural ? "PULSE: NONE.  RESIDUE: HIGH." : "PULSE: NORMAL.  RESIDUE: NONE." };
            }
            return new InspectResult { Spot = spot, Text = "Nothing unusual." };
        }

        public static readonly string[] Cameras = { "cam1", "cam2", "cam3", "cam4", "cam0" };

        public bool CameraAvailable(string cam)
        {
            if (BlackoutActive && !BackupPowerActive) return false;
            if (cam == "cam4") return State.Upgrades.Contains(UpgradeIds.Monitor);
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

        public bool CanRestockNow => Phase == ShiftPhase.Idle || (Phase == ShiftPhase.Encounter && State.Upgrades.Contains(UpgradeIds.FastRestock));

        public int Restock(string slotCode, string productId)
        {
            if (!CanRestockNow) return 0;
            int moved = State.LoadSlot(slotCode, productId);
            if (moved > 0)
            {
                int minutes = State.Upgrades.Contains(UpgradeIds.FastRestock) ? FastRestockMinutes : SlowRestockMinutes;
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

        private void Resolve(string key, string product)
        {
            var cur = Current;
            var def = cur.Def;
            OutcomeDef outcome = null;
            var list = FindOutcomes(key);
            if (list != null) outcome = list.FirstOrDefault(v => Conditions.Evaluate(v.Condition, State, Db, this));
            if (outcome == null) outcome = DefaultOutcome(key, def);

            // "retry" outcomes answer the player without ending the encounter (e.g. a wrong keypad code).
            if (outcome.Result == "retry")
            {
                Effects.Apply(outcome.Effects, State, Db, this);
                if (!string.IsNullOrEmpty(outcome.Scare)) { Emit(PresEventType.Scare, outcome.Scare); Director.NotifyScripted(outcome.Scare); }
                State.Decisions.Add(new DecisionRecord { Night = State.Night, Encounter = def.Id, Action = key, Result = "retry" });
                Emit(PresEventType.Reply, def.Id, null, 0, outcome.Lines);
                cur.Stage = EncounterStage.Awaiting;
                cur.PatienceLeft = Math.Max(cur.PatienceLeft, 30f);
                return;
            }

            cur.Outcome = outcome;
            cur.ActionKey = key;
            cur.ServedProduct = product;
            cur.Stage = EncounterStage.Resolving;

            if (product != null)
            {
                int amount = 0;
                if (outcome.Paid)
                {
                    var p = Db.Product(product);
                    amount = def.Pays > 0 ? def.Pays : (p?.Price ?? 0);
                    State.Money += amount;
                    State.TotalEarned += amount;
                    NightEarnings += amount;
                }
                CustomersServed++;
                State.TotalServed++;
                string receipt = !string.IsNullOrEmpty(outcome.Receipt)
                    ? outcome.Receipt
                    : DefaultReceipt(product, amount);
                State.Transactions.Add(new TransactionRecord
                {
                    Night = State.Night, Time = (int)Clock, Customer = def.Name, ProductId = product, Amount = amount, Receipt = receipt
                });
                Emit(PresEventType.Receipt, product, receipt, amount);
            }
            else if (key == "refuse" || key == "timeout")
            {
                CustomersRefused++;
                State.TotalRefused++;
            }

            Effects.Apply(outcome.Effects, State, Db, this);
            if (outcome.Result == "bad") State.AddInstability(5);
            if (!string.IsNullOrEmpty(outcome.Scare))
            {
                Emit(PresEventType.Scare, outcome.Scare);
                Director.NotifyScripted(outcome.Scare);
            }
            State.Decisions.Add(new DecisionRecord { Night = State.Night, Encounter = def.Id, Action = key, Result = outcome.Result });
            if (outcome.Result == "death") _pendingFail = true;
        }

        private string DefaultReceipt(string product, int amount)
        {
            var p = Db.Product(product);
            string name = p?.Name ?? product;
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
