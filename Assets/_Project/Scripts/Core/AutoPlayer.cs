using System;
using System.Collections.Generic;
using System.Linq;

namespace HikariForge.M313.Core
{
    /// <summary>
    /// A scripted player. Plays a whole night through the same API the Unity layer uses,
    /// choosing actions from a policy keyed by encounter id.
    /// Policy steps: "serve" (requested item), "serve:PRODUCT", "refuse", "wait", "choice:id", "talk:id", "code:1013".
    /// </summary>
    public sealed class AutoPlayer
    {
        public readonly Dictionary<string, string[]> Policy = new Dictionary<string, string[]>();
        public bool OpenPanelOnKnock;
        public readonly HashSet<string> SkipHotspots = new HashSet<string>();
        public readonly List<string> Log = new List<string>();
        /// <summary>Chance to refuse an ordinary request the policy doesn't cover (simulates player mistakes).</summary>
        public double MistakeRate;
        /// <summary>none, spread (cheapest next level), or focus:&lt;upgradeId&gt;.</summary>
        public string UpgradeStrategy = "none";
        public int UpgradeReserve = 1500;
        public readonly List<string> Purchases = new List<string>();
        /// <summary>Human pacing for length tests: seconds per dialogue line, and thinking time before each decision.</summary>
        public float ReadSeconds, DecideSeconds, InvestigateSeconds;
        private float _wait;
        private object _waitingFor;
        private readonly Random _rng = new Random(313);

        public static readonly string[] Staples =
            { "WATER", "COFFEE", "GUM", "SALT", "COLA", "ASPIRIN", "MINTS", "BATTERY", "ENERGY", "CHOCO", "PEANUTS", "CHIPS", "NOODLES", "MATCHES" };

        public void Prep(GameState s, ContentDatabase db)
        {
            foreach (var id in Staples)
            {
                var p = db.Product(id);
                if (p == null || !p.Purchasable || p.UnlockNight > s.Night) continue;
                int want = Math.Max(0, 4 - s.StockroomCount(id) - s.MachineCount(id));
                if (want > 0) s.BuyStock(p, Math.Min(want, Math.Max(0, s.Money / Math.Max(1, p.Cost))));
            }
            // Make sure every staple has a slot: unload duplicates first.
            foreach (var id in Staples)
            {
                if (s.MachineCount(id) > 0 || s.StockroomCount(id) == 0) continue;
                var empty = s.Slots.FirstOrDefault(sl => sl.Code != "E1" && (sl.ProductId == null || sl.Count == 0));
                if (empty != null) s.LoadSlot(empty.Code, id);
            }
            s.AutoStock(db);
            BuyUpgrades(s, db);
        }

        private void BuyUpgrades(GameState s, ContentDatabase db)
        {
            if (UpgradeStrategy == "none") return;
            while (true)
            {
                var options = Campaign.AvailableUpgrades(s, db).Where(u => s.NextUpgradePrice(u) >= 0).ToList();
                if (UpgradeStrategy.StartsWith("focus:"))
                {
                    var target = options.FirstOrDefault(u => u.Id == UpgradeStrategy.Substring(6));
                    options = target != null ? new List<UpgradeDef> { target } : options;
                }
                var pick = options.OrderBy(u => s.NextUpgradePrice(u)).FirstOrDefault();
                if (pick == null || s.Money - s.NextUpgradePrice(pick) < UpgradeReserve) return;
                s.BuyUpgrade(pick);
                Purchases.Add($"N{s.Night} {pick.Id} L{s.Level(pick.Id)}");
            }
        }

        public ShiftContext PlayNight(GameState s, ContentDatabase db, int maxTicks = 40000)
        {
            Prep(s, db);
            return PlayShift(new ShiftContext(s, db), maxTicks);
        }

        public ShiftContext PlayShift(ShiftContext ctx, int maxTicks = 40000)
        {
            var s = ctx.State;
            var steps = new Dictionary<string, Queue<string>>();
            for (int i = 0; i < maxTicks && !ctx.IsOver; i++)
            {
                ctx.Tick(0.5f);
                if (ReadSeconds > 0 && HumanWait(ctx)) continue;
                while (ctx.Events.Count > 0)
                {
                    var e = ctx.Events.Dequeue();
                    if (e.Type == PresEventType.KnockStart && OpenPanelOnKnock) { ctx.OpenPanel(); ctx.ClosePanel(); }
                }
                switch (ctx.Phase)
                {
                    case ShiftPhase.Phone: ctx.AcknowledgePhone(); break;
                    case ShiftPhase.Investigation:
                        for (int pass = 0; pass < 2; pass++) // second pass: spots revealed by earlier finds
                            foreach (var h in ctx.AvailableHotspots())
                                if (!SkipHotspots.Contains(h.Id)) ctx.Search(h.Id);
                        ctx.FinishInvestigation();
                        break;
                    case ShiftPhase.Encounter:
                        var cur = ctx.Current;
                        if (cur.Stage == EncounterStage.Talking) { ctx.BeginAwaiting(); break; }
                        if (cur.Stage == EncounterStage.Resolving)
                        {
                            Log.Add($"N{s.Night} {cur.Def.Id} -> {cur.ActionKey} ({cur.Outcome.Result})");
                            ctx.FinishEncounter();
                            break;
                        }
                        if (!steps.TryGetValue(cur.Def.Id, out var q))
                        {
                            q = new Queue<string>(PolicyFor(cur.Def.Id));
                            steps[cur.Def.Id] = q;
                        }
                        Act(ctx, q);
                        break;
                }
            }
            return ctx;
        }

        /// <summary>Simulates reading and thinking time. Returns true while the "player" is still busy.</summary>
        private bool HumanWait(ShiftContext ctx)
        {
            object key = null;
            float need = 0;
            switch (ctx.Phase)
            {
                case ShiftPhase.Phone: key = ctx.PendingPhone; need = (ctx.PendingPhone?.Lines?.Count ?? 1) * ReadSeconds; break;
                case ShiftPhase.Investigation: key = ctx.PendingInvestigation; need = InvestigateSeconds; break;
                case ShiftPhase.Encounter:
                    var cur = ctx.Current;
                    if (cur.Stage == EncounterStage.Talking) { key = cur; need = cur.Def.Lines.Count * ReadSeconds; }
                    else if (cur.Stage == EncounterStage.Awaiting) { key = cur.Def.Id + ":decide"; need = DecideSeconds; }
                    else { key = cur.Def.Id + ":outcome"; need = (cur.Outcome?.Lines.Count ?? 1) * ReadSeconds; }
                    break;
            }
            if (key == null) return false;
            if (!Equals(key, _waitingFor)) { _waitingFor = key; _wait = need; }
            if (_wait <= 0) return false;
            _wait -= 0.5f;
            return true;
        }

        private string[] PolicyFor(string id)
        {
            if (Policy.TryGetValue(id, out var p)) return p;
            // Endless Mode clones templates as "<template>_r<round>_<i>".
            foreach (var kv in Policy) if (id.StartsWith(kv.Key + "_r")) return kv.Value;
            return new[] { "serve" };
        }

        private void Act(ShiftContext ctx, Queue<string> q)
        {
            var def = ctx.Current.Def;
            string step = q.Count > 0 ? q.Peek() : "wait";
            if (step == "wait") return;
            if (step.StartsWith("talk:"))
            {
                ctx.Talk(step.Substring(5));
                q.Dequeue();
                return;
            }
            if (step.StartsWith("choice:")) { q.Dequeue(); ctx.Talk(step.Substring(7)); return; }
            if (step.StartsWith("code:")) { q.Dequeue(); ctx.OpenPanel(); ctx.EnterCode(step.Substring(5)); ctx.ClosePanel(); return; }
            if (step == "refuse") { q.Dequeue(); ctx.Refuse(); return; }
            if (step == "leave") { q.Dequeue(); ctx.LeaveBooth(); return; }
            if (step == "serve" || step.StartsWith("serve:"))
            {
                string product = step == "serve" ? def.Request : step.Substring(6);
                if (step == "serve" && !Policy.Keys.Any(k => def.Id == k || def.Id.StartsWith(k + "_r")) && _rng.NextDouble() < MistakeRate)
                {
                    q.Dequeue();
                    ctx.Refuse();
                    return;
                }
                if (string.IsNullOrEmpty(product) || product == "UNLISTED" || ctx.State.MachineCount(product) == 0)
                {
                    q.Dequeue();
                    ctx.Refuse();
                    return;
                }
                var slot = ctx.State.SlotWith(product);
                var err = ctx.Serve(slot.Code);
                if (err == ShiftContext.ServeError.Jammed) { ctx.OpenPanel(); ctx.ResetMotor(); ctx.ClosePanel(); return; }
                if (err == ShiftContext.ServeError.NoPower) return; // wait for the lights
                if (err == ShiftContext.ServeError.None && ctx.Current != null && ctx.Current.Stage != EncounterStage.Resolving) return; // more items in the order
                q.Dequeue();
            }
        }

        /// <summary>Alias used by the playtest's "skip to night" tool.</summary>
        public static AutoPlayer Careful() => TruthSeeker();

        /// <summary>A careful player who follows Mara and the sealed rules.</summary>
        public static AutoPlayer TruthSeeker()
        {
            var b = new AutoPlayer();
            var p = b.Policy;
            p["n2_collector"] = new[] { "refuse" };
            p["n2_crane_return"] = new[] { "choice:tell_truth" };
            p["n2_smile"] = new[] { "talk:deny", "serve" };
            p["n3_crane"] = new[] { "talk:ticket", "serve" };
            p["n3_collector"] = new[] { "refuse" };
            p["n3_mimic"] = new[] { "refuse" };
            p["n3_walter"] = new[] { "talk:machine", "serve" };
            p["n3_smile"] = new[] { "talk:deny", "refuse" };
            p["n4_collector"] = new[] { "choice:decline" };
            p["n4_smile"] = new[] { "refuse" };
            p["n4_double"] = new[] { "wait" };
            p["n5_hale"] = new[] { "talk:lie", "serve" };
            p["n5_collector"] = new[] { "refuse" };
            p["n5_mimic"] = new[] { "refuse" };
            p["n6_collector"] = new[] { "refuse" };
            p["n6_lily"] = new[] { "choice:hide" };
            p["n6_hollow"] = new[] { "refuse" };
            p["n6_motorist"] = new[] { "refuse" };
            p["n6_mara"] = new[] { "talk:how", "serve" };
            p["n7_mimic"] = new[] { "refuse" };
            p["n7_lily"] = new[] { "choice:thanks" };
            p["n7_hollow"] = new[] { "wait" };
            p["n7_mara"] = new[] { "refuse" };
            p["n7_collector"] = new[] { "talk:names", "code:1013" };
            return b;
        }

        /// <summary>A cautious player who survives but never pursues the truth.</summary>
        public static AutoPlayer Survivor()
        {
            var b = TruthSeeker();
            b.Policy["n2_crane_return"] = new[] { "choice:lie" };
            b.Policy["n6_lily"] = new[] { "choice:run" };
            b.Policy["n7_collector"] = new[] { "choice:refuse" };
            return b;
        }

        /// <summary>Endless Mode policy: follows every rule.</summary>
        public static AutoPlayer EndlessExpert()
        {
            var b = new AutoPlayer();
            b.Policy["e_mimic"] = new[] { "refuse" };
            b.Policy["e_collector"] = new[] { "refuse" };
            b.Policy["e_hollow"] = new[] { "refuse" };
            b.Policy["e_twoplaces"] = new[] { "wait" };
            b.Policy["e_double"] = new[] { "wait" };
            b.Policy["e_motorist"] = new[] { "refuse" };
            return b;
        }

        /// <summary>A player who takes every deal the Collector offers.</summary>
        public static AutoPlayer Dealmaker()
        {
            var b = Survivor();
            b.Policy["n2_collector"] = new[] { "serve:UNLABELED" };
            b.Policy["n4_collector"] = new[] { "choice:sell" };
            b.Policy["n5_collector"] = new[] { "choice:sell_tapes" };
            b.Policy["n7_collector"] = new[] { "choice:refuse" };
            return b;
        }
    }
}
