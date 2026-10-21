using System.Collections.Generic;
using System.IO;
using System.Linq;
using HikariForge.M313.Core;
using NUnit.Framework;

namespace HikariForge.M313.Tests
{
    public class ContentTests
    {
        private static ContentDatabase Db => TestContent.Db;

        [Test]
        public void AllContentFilesLoadWithoutErrors()
        {
            Assert.IsEmpty(Db.LoadErrors, string.Join("\n", Db.LoadErrors));
        }

        [Test]
        public void CampaignHasSevenNightsWithEncounters()
        {
            for (int n = 1; n <= ContentDatabase.TotalNights; n++)
            {
                Assert.IsNotNull(Db.Night(n), $"Night {n} missing");
                Assert.GreaterOrEqual(Db.EncountersForNight(n).Count(), 6, $"Night {n} needs at least 6 encounters");
            }
            Assert.GreaterOrEqual(Db.Encounters.Values.Count(e => e.Night > 0), 45, "Campaign should have 45+ encounters");
            Assert.GreaterOrEqual(Endless.Templates(Db).Count, 10, "Endless Mode needs templates");
        }

        [Test]
        public void ProductAndUpgradeCountsMatchDesign()
        {
            Assert.That(Db.ProductList.Count, Is.InRange(15, 20));
            Assert.AreEqual(6, Db.UpgradeList.Count);
            Assert.AreEqual(15, Db.Endings.Count);
        }

        [Test]
        public void EveryReferenceInContentIsValid()
        {
            var errors = new List<string>();
            void Cond(string where, string expr)
            {
                var err = Conditions.Validate(expr);
                if (err != null) errors.Add($"{where}: condition '{expr}': {err}");
            }
            void Eff(string where, IEnumerable<string> effects)
            {
                foreach (var e in effects)
                {
                    var err = Effects.Validate(e, Db);
                    if (err != null) errors.Add($"{where}: effect '{e}': {err}");
                }
            }

            foreach (var e in Db.Encounters.Values)
            {
                string w = e.Id;
                if (e.Night < 0 || e.Night > 7) errors.Add($"{w}: bad night");
                string prefix = e.Night == 0 ? "e_" : "n" + e.Night + "_";
                if (!e.Id.StartsWith(prefix)) errors.Add($"{w}: id should start with {prefix}");
                if (!string.IsNullOrEmpty(e.Request) && e.Request != "UNLISTED" && Db.Product(e.Request) == null)
                    errors.Add($"{w}: unknown request product {e.Request}");
                if (!string.IsNullOrEmpty(e.Character) && !Db.Characters.ContainsKey(e.Character))
                    errors.Add($"{w}: unknown character {e.Character}");
                if (e.Time >= ContentDatabase.ShiftEnd) errors.Add($"{w}: arrives after 6:00 AM");
                if (e.Lines.Count == 0) errors.Add($"{w}: no lines");
                if (e.Outcomes.Count == 0) errors.Add($"{w}: no outcomes");
                Cond(w, e.Condition);
                Eff(w + ".arrive", e.ArriveEffects);
                foreach (var i in e.Inspect)
                {
                    Cond(w + ".inspect", i.Condition);
                    Eff(w + ".inspect", i.Effects);
                    if (!string.IsNullOrEmpty(i.Clue) && !Db.Clues.ContainsKey(i.Clue)) errors.Add($"{w}: unknown clue {i.Clue}");
                    if (!string.IsNullOrEmpty(i.Upgrade) && !Db.Upgrades.ContainsKey(i.Upgrade)) errors.Add($"{w}: unknown upgrade {i.Upgrade}");
                    if (!ShiftContext.StandardSpots.Contains(i.Spot)) errors.Add($"{w}: unknown inspect spot {i.Spot}");
                }
                foreach (var t in e.Talk)
                {
                    Cond(w + ".talk." + t.Id, t.Condition);
                    Eff(w + ".talk." + t.Id, t.Effects);
                    if (t.End && !e.Outcomes.ContainsKey("choice:" + t.Id)) errors.Add($"{w}: terminal talk {t.Id} has no choice outcome");
                }
                foreach (var c in e.Cameras)
                {
                    if (!ShiftContext.Cameras.Contains(c.Cam)) errors.Add($"{w}: unknown camera {c.Cam}");
                    Cond(w + ".cam", c.Condition);
                }
                foreach (var kv in e.Outcomes)
                {
                    var key = kv.Key;
                    if (key.StartsWith("serve:") && key != "serve:*" && Db.Product(key.Substring(6)) == null)
                        errors.Add($"{w}: outcome for unknown product {key}");
                    if (key.StartsWith("choice:") && !e.Talk.Any(t => t.End && "choice:" + t.Id == key))
                        errors.Add($"{w}: outcome {key} has no terminal talk option");
                    if (!(key.StartsWith("serve:") || key.StartsWith("choice:") || key.StartsWith("code:") || key == "refuse" || key == "timeout"))
                        errors.Add($"{w}: unknown outcome key {key}");
                    foreach (var o in kv.Value)
                    {
                        Cond(w + "." + key, o.Condition);
                        Eff(w + "." + key, o.Effects);
                        if (!new[] { "good", "neutral", "bad", "death", "retry" }.Contains(o.Result)) errors.Add($"{w}.{key}: bad result {o.Result}");
                        if (o.Lines.Count == 0) errors.Add($"{w}.{key}: outcome has no lines");
                    }
                    // Unconditional variant must be last, otherwise later variants are unreachable.
                    for (int i = 0; i < kv.Value.Count - 1; i++)
                        if (string.IsNullOrEmpty(kv.Value[i].Condition)) errors.Add($"{w}.{key}: unconditional variant {i} hides later variants");
                }
            }
            foreach (var n in Db.Nights.Values)
            {
                foreach (var ev in n.Events)
                {
                    Cond($"night{n.Night}.{ev.Id}", ev.Condition);
                    Eff($"night{n.Night}.{ev.Id}", ev.Effects);
                    Eff($"night{n.Night}.{ev.Id}.open", ev.OnOpen);
                    Eff($"night{n.Night}.{ev.Id}.serve", ev.OnServe);
                }
                foreach (var h in n.Investigation)
                {
                    Cond($"night{n.Night}.{h.Id}", h.Condition);
                    Eff($"night{n.Night}.{h.Id}", h.Effects);
                    if (!string.IsNullOrEmpty(h.Clue) && !Db.Clues.ContainsKey(h.Clue)) errors.Add($"night{n.Night}.{h.Id}: unknown clue");
                }
            }
            foreach (var p in Db.Pages)
                foreach (var a in p.Annotations) Cond("page." + p.Id, a.Condition);
            foreach (var end in Db.Endings) Cond("ending." + end.Id, end.Condition);

            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void ArtReferencedByContentExists()
        {
            string art = Path.Combine(TestContent.ProjectRoot(), "Assets", "_Project", "Resources", "Art");
            if (!Directory.Exists(Path.Combine(art, "Customers"))) Assert.Ignore("Art not generated yet");
            var missing = new List<string>();
            foreach (var e in Db.Encounters.Values)
            {
                if (!string.IsNullOrEmpty(e.Portrait) && !File.Exists(Path.Combine(art, "Customers", e.Portrait + ".png"))) missing.Add(e.Portrait);
                foreach (var c in e.Cameras)
                {
                    var parts = c.State.Split(':');
                    if (parts.Length == 2 && !File.Exists(Path.Combine(art, "Customers", parts[1] + ".png"))) missing.Add(parts[1]);
                }
            }
            foreach (var p in Db.ProductList)
                if (!File.Exists(Path.Combine(art, "Products", p.Icon + ".png"))) missing.Add(p.Icon);
            Assert.IsEmpty(missing.Distinct().ToList(), "Missing art: " + string.Join(", ", missing.Distinct()));
        }

        [Test]
        public void EveryNightHasOrdinaryAndSupernaturalCustomers()
        {
            for (int n = 1; n <= 7; n++)
            {
                var encs = Db.EncountersForNight(n).ToList();
                Assert.IsTrue(encs.Any(e => e.Supernatural), $"Night {n} has no supernatural customer");
            }
            Assert.IsTrue(Db.EncountersForNight(1).Count(e => !e.Supernatural) >= 4, "Night 1 should be mostly ordinary");
        }
    }

    public class SaveTests
    {
        [Test]
        public void RoundTripPreservesState()
        {
            var s = GameState.NewGame();
            s.Night = 4; s.Money = 1234; s.Bond = 2; s.Cycle = 3;
            s.SetFlag("lily_trust", 2);
            s.Clues.Add("ticket_stub");
            s.Upgrades.Add(UpgradeIds.Monitor);
            s.EndingsSeen.Add("escape");
            s.Decisions.Add(new DecisionRecord { Night = 2, Encounter = "n2_crane", Action = "serve:COFFEE", Result = "good" });
            var json = SaveSerializer.Serialize(s);
            var r = SaveSerializer.Deserialize(json);
            Assert.AreEqual(4, r.Night);
            Assert.AreEqual(1234, r.Money);
            Assert.AreEqual(2, r.Bond);
            Assert.AreEqual(3, r.Cycle);
            Assert.AreEqual(2, r.Flag("lily_trust"));
            Assert.IsTrue(r.Clues.Contains("ticket_stub"));
            Assert.IsTrue(r.Upgrades.Contains(UpgradeIds.Monitor));
            Assert.IsTrue(r.EndingsSeen.Contains("escape"));
            Assert.AreEqual("serve:COFFEE", r.LastDecision("n2_crane").Action);
            Assert.AreEqual(s.MachineCount("COLA"), r.MachineCount("COLA"));
            Assert.AreEqual(json, SaveSerializer.Serialize(r), "Serialization should be stable");
        }

        [Test]
        public void TamperedSaveIsRejected()
        {
            var json = SaveSerializer.Serialize(GameState.NewGame());
            var tampered = json.Replace("\"money\": 2500", "\"money\": 999999");
            Assert.AreNotEqual(json, tampered);
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(tampered));
        }

        [Test]
        public void GarbageSaveIsRejected()
        {
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize("{not json"));
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize("[]"));
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize("{\"version\": 99, \"data\": {}}"));
        }
    }

    public class InventoryTests
    {
        [Test]
        public void BuyingAndLoadingRespectsMoneyAndCapacity()
        {
            var db = TestContent.Db;
            var s = GameState.NewGame();
            int money = s.Money;
            Assert.IsTrue(s.BuyStock(db.Product("GUM"), 10));
            Assert.AreEqual(money - db.Product("GUM").Cost * 10, s.Money);
            int moved = s.LoadSlot("D4", "GUM");
            Assert.AreEqual(GameState.BaseCapacity, moved);
            s.Upgrades.Add(UpgradeIds.ExpandedStorage);
            moved = s.LoadSlot("D4", "GUM");
            Assert.AreEqual(GameState.ExpandedCapacity - GameState.BaseCapacity, moved);
            Assert.IsFalse(s.BuyStock(db.Product("BATTERY"), 1000), "cannot overspend");
            Assert.IsFalse(s.BuyStock(db.Product("TOKEN"), 1), "machine-only items cannot be bought");
            Assert.AreEqual(0, s.LoadSlot("E1", "GUM"), "Row E cannot be loaded by hand");
        }

        [Test]
        public void UpgradesCostMoneyAndApplyOnce()
        {
            var db = TestContent.Db;
            var s = GameState.NewGame();
            s.Money = 100000;
            Assert.IsTrue(s.BuyUpgrade(db.Upgrades[UpgradeIds.Lighting]));
            Assert.IsFalse(s.BuyUpgrade(db.Upgrades[UpgradeIds.Lighting]));
            s.Instability = 0;
            s.AddInstability(10);
            Assert.AreEqual(7, s.Instability, "Improved lighting reduces instability gain by 30%");
        }

        [Test]
        public void ConditionsAndEffectsWork()
        {
            var db = TestContent.Db;
            var s = GameState.NewGame();
            Effects.Apply(new[] { "flag:x=2", "flag:x+1", "bond+2", "clue:ticket_stub", "money-500" }, s, db, null);
            Assert.AreEqual(3, s.Flag("x"));
            Assert.IsTrue(Conditions.Evaluate("flag:x>=3 & bond==2 & clue:ticket_stub", s, db));
            Assert.IsFalse(Conditions.Evaluate("!flag:x", s, db));
            Assert.IsTrue(Conditions.Evaluate("flag:nope | keyclues>=1", s, db));
            Assert.AreEqual(2000, s.Money);
        }
    }

    public class ShiftTests
    {
        private static GameState StateAtNight(int night, AutoPlayer bot, out List<ShiftContext> contexts)
        {
            var db = TestContent.Db;
            var s = GameState.NewGame();
            contexts = new List<ShiftContext>();
            while (s.Night < night)
            {
                var ctx = bot.PlayNight(s, db);
                contexts.Add(ctx);
                Assert.AreEqual(ShiftPhase.Ended, ctx.Phase, $"Night {s.Night} did not end: {ctx.FailReason}\n" + string.Join("\n", bot.Log));
                Campaign.CompleteNight(s, db, ctx);
            }
            return s;
        }

        [Test]
        public void NightOneCanBeCompletedAndPays()
        {
            var db = TestContent.Db;
            var s = GameState.NewGame();
            var bot = AutoPlayer.TruthSeeker();
            var ctx = bot.PlayNight(s, db);
            Assert.AreEqual(ShiftPhase.Ended, ctx.Phase, ctx.FailReason);
            Assert.GreaterOrEqual(ctx.CustomersServed, 5);
            Assert.Greater(ctx.NightEarnings, 0);
            Assert.AreEqual(1, s.Flag("walter_rest"));
            Assert.AreEqual("UNLABELED", s.Slot("E1").ProductId, "machine should produce the unlabeled can");
            var summary = Campaign.CompleteNight(s, db, ctx);
            Assert.AreEqual(2, s.Night);
            Assert.Greater(summary.Total, 0);
        }

        [Test]
        public void ServingWaterToTheMimicKills()
        {
            var bot = AutoPlayer.TruthSeeker();
            var s = StateAtNight(3, bot, out _);
            bot.Policy["n3_mimic"] = new[] { "serve:WATER" };
            var ctx = bot.PlayNight(s, TestContent.Db);
            Assert.AreEqual(ShiftPhase.Failed, ctx.Phase);
            Assert.AreEqual("n3_mimic", ctx.FailReason);
        }

        [Test]
        public void OpeningPanelDuringKnockIncreasesBond()
        {
            var bot = AutoPlayer.TruthSeeker();
            var s = StateAtNight(3, bot, out _);
            int bond = s.Bond;
            bot.OpenPanelOnKnock = true;
            var ctx = bot.PlayNight(s, TestContent.Db);
            Assert.AreEqual(ShiftPhase.Ended, ctx.Phase);
            Assert.AreEqual(bond + 2, s.Bond);
            Assert.AreEqual(1, s.Flag("marked"));
        }

        [Test]
        public void MarginStartsAt313()
        {
            var db = TestContent.Db;
            var s = GameState.NewGame();
            var ctx = new ShiftContext(s, db);
            bool margin = false;
            for (int i = 0; i < 20000 && !margin; i++)
            {
                ctx.Tick(0.5f);
                if (ctx.Phase == ShiftPhase.Phone) ctx.AcknowledgePhone();
                if (ctx.Phase == ShiftPhase.Encounter)
                {
                    if (ctx.Current.Stage == EncounterStage.Talking) ctx.BeginAwaiting();
                    else if (ctx.Current.Stage == EncounterStage.Awaiting) ctx.Refuse();
                    else ctx.FinishEncounter();
                }
                while (ctx.Events.Count > 0)
                    if (ctx.Events.Dequeue().Type == PresEventType.Margin)
                    {
                        margin = true;
                        Assert.That(ctx.Clock, Is.InRange(253f, 256f));
                    }
            }
            Assert.IsTrue(margin);
        }

        private static (EndingDef ending, GameState state, AutoPlayer bot) PlayCampaign(AutoPlayer bot)
        {
            var db = TestContent.Db;
            var s = GameState.NewGame();
            for (int n = 1; n <= 7; n++)
            {
                var ctx = bot.PlayNight(s, db);
                Assert.AreEqual(ShiftPhase.Ended, ctx.Phase, $"Night {n} failed: {ctx.FailReason}\n" + string.Join("\n", bot.Log));
                if (n == 7)
                {
                    Assert.IsNotNull(ctx.RequestedEnding, "Night 7 must end with an ending\n" + string.Join("\n", bot.Log));
                    var end = Campaign.ResolveEnding(s, db, ctx.RequestedEnding);
                    s.EndingReached = end.Id;
                    return (end, s, bot);
                }
                Campaign.CompleteNight(s, db, ctx);
            }
            return (null, s, bot);
        }

        public static IEnumerable<TestCaseData> EndingRoutes()
        {
            AutoPlayer B(System.Func<AutoPlayer> make, System.Action<AutoPlayer> edit) { var b = make(); edit(b); return b; }
            yield return new TestCaseData("truth", B(AutoPlayer.TruthSeeker, b => { })).SetName("Ending_truth");
            yield return new TestCaseData("escape", B(AutoPlayer.Survivor, b => { })).SetName("Ending_escape");
            yield return new TestCaseData("replacement", B(AutoPlayer.Survivor, b =>
            {
                b.Policy["n5_mara"] = new[] { "refuse" }; b.Policy["n6_mara"] = new[] { "refuse" };
                b.Policy["n7_collector"] = new[] { "choice:sign" };
            })).SetName("Ending_replacement");
            yield return new TestCaseData("debt", B(AutoPlayer.Dealmaker, b => b.Policy["n3_collector"] = new[] { "choice:roll" })).SetName("Ending_debt");
            yield return new TestCaseData("empty_seat", B(AutoPlayer.TruthSeeker, b => b.Policy["n6_lily"] = new[] { "choice:run" })).SetName("Ending_empty_seat");
            yield return new TestCaseData("last_passenger", B(AutoPlayer.TruthSeeker, b => b.Policy["n2_crane_return"] = new[] { "choice:lie" })).SetName("Ending_last_passenger");
            yield return new TestCaseData("driver", B(AutoPlayer.TruthSeeker, b =>
            {
                b.Policy["n1_walter"] = new[] { "refuse" }; b.Policy["n3_walter"] = new[] { "talk:machine", "refuse" };
                b.Policy["n6_mara"] = new[] { "talk:how", "talk:token", "serve" };
            })).SetName("Ending_driver");
            yield return new TestCaseData("nameless", B(AutoPlayer.TruthSeeker, b => { b.SkipHotspots.Add("locker"); b.SkipHotspots.Add("panel"); })).SetName("Ending_nameless");
            yield return new TestCaseData("wrong_minute", B(AutoPlayer.TruthSeeker, b => b.Policy["n7_collector"] = new[] { "code:0313" })).SetName("Ending_wrong_minute");
            yield return new TestCaseData("paid_in_full", B(AutoPlayer.TruthSeeker, b => b.Policy["n7_collector"] = new[] { "serve:EXACT" })).SetName("Ending_paid_in_full");
            yield return new TestCaseData("maras_shift", B(AutoPlayer.TruthSeeker, b => b.Policy["n7_collector"] = new[] { "choice:sign" })).SetName("Ending_maras_shift");
            yield return new TestCaseData("company_man", B(AutoPlayer.Survivor, b =>
            {
                b.Policy["n5_hale"] = new[] { "choice:hand_over" }; b.Policy["n7_collector"] = new[] { "choice:sign" };
            })).SetName("Ending_company_man");
            yield return new TestCaseData("together", B(AutoPlayer.TruthSeeker, b =>
            {
                b.Policy["n2_crane_return"] = new[] { "choice:lie" }; b.Policy["n7_collector"] = new[] { "choice:refuse" };
            })).SetName("Ending_together");
            yield return new TestCaseData("ones_who_left", B(AutoPlayer.TruthSeeker, b => b.Policy["n7_collector"] = new[] { "choice:refuse" })).SetName("Ending_ones_who_left");
            yield return new TestCaseData("off_the_clock", B(AutoPlayer.TruthSeeker, b => b.Policy["n1_del"] = new[] { "leave" })).SetName("Ending_off_the_clock");
        }

        [TestCaseSource(nameof(EndingRoutes))]
        public void EndingIsReachable(string expected, AutoPlayer bot)
        {
            var db = TestContent.Db;
            var s = GameState.NewGame();
            string reached = null;
            for (int n = 1; n <= 7 && reached == null; n++)
            {
                var ctx = bot.PlayNight(s, db);
                Assert.AreEqual(ShiftPhase.Ended, ctx.Phase, $"Night {n} failed: {ctx.FailReason}\n" + string.Join("\n", bot.Log));
                if (ctx.RequestedEnding != null) reached = Campaign.ResolveEnding(s, db, ctx.RequestedEnding).Id;
                else Campaign.CompleteNight(s, db, ctx);
            }
            Assert.AreEqual(expected, reached,
                $"bond={s.Bond} flags: {string.Join(",", s.Flags.Select(kv => kv.Key + "=" + kv.Value))}\n" + string.Join("\n", bot.Log));
        }

        [Test]
        public void AllFifteenEndingsHaveDistinctAchievements()
        {
            var db = TestContent.Db;
            var ids = db.Endings.Select(e => Profile.AchievementFor(e.Id)).ToList();
            Assert.AreEqual(15, ids.Distinct().Count());
            Assert.AreEqual(15, EndingRoutes().Select(t => (string)t.Arguments[0]).Distinct().Count(), "every ending needs a tested route");
        }

        [Test]
        public void EndlessModeUnlocksAfterAllEndings()
        {
            var db = TestContent.Db;
            var p = new Profile();
            foreach (var e in db.Endings.Take(14)) p.RecordEnding(e.Id, db);
            Assert.IsFalse(p.EndlessUnlocked(db));
            var unlocked = p.RecordEnding(db.Endings.Last().Id, db);
            Assert.IsTrue(p.EndlessUnlocked(db));
            CollectionAssert.Contains(unlocked, Profile.EndlessAchievement);
            Assert.IsEmpty(p.RecordEnding(db.Endings[0].Id, db), "repeat endings unlock nothing new");
            var round = Profile.Deserialize(p.Serialize());
            Assert.IsTrue(round.EndlessUnlocked(db));
            Assert.AreEqual(16, round.Achievements.Count);
        }

        [Test]
        public void EndlessRoundsCanBeSurvivedAndEscalate()
        {
            var db = TestContent.Db;
            var s = Endless.NewRun(new Profile());
            var bot = AutoPlayer.EndlessExpert();
            int lastCount = 0;
            for (int r = 1; r <= 5; r++)
            {
                bot.Prep(s, db);
                var ctx = Endless.BuildShift(s, db, 42);
                Assert.GreaterOrEqual(ctx.RemainingEncounters, lastCount);
                lastCount = ctx.RemainingEncounters;
                bot.PlayShift(ctx);
                Assert.AreEqual(ShiftPhase.Ended, ctx.Phase, $"round {r}: {ctx.FailReason}\n" + string.Join("\n", bot.Log));
                Endless.CompleteRound(s, ctx);
            }
            Assert.AreEqual(6, Endless.Round(s));
        }

        [Test]
        public void EndlessMistakeEndsTheRun()
        {
            var db = TestContent.Db;
            var s = Endless.NewRun(new Profile());
            var bot = AutoPlayer.EndlessExpert();
            bot.Policy["e_mimic"] = new[] { "serve:WATER" };
            bot.Policy["e_twoplaces"] = new[] { "refuse" };
            bool died = false;
            for (int r = 1; r <= 8 && !died; r++)
            {
                bot.Prep(s, db);
                var ctx = bot.PlayShift(Endless.BuildShift(s, db, 7));
                if (ctx.Phase == ShiftPhase.Failed) died = true;
                else Endless.CompleteRound(s, ctx);
            }
            Assert.IsTrue(died);
        }

        [Test]
        public void CampaignLoopsIntoNextCycle()
        {
            var db0 = TestContent.Db;
            var s = GameState.NewGame();
            var bot0 = AutoPlayer.Survivor();
            ShiftContext last = null;
            for (int n = 1; n <= 7; n++)
            {
                last = bot0.PlayNight(s, db0);
                if (n < 7) Campaign.CompleteNight(s, db0, last);
            }
            var end = Campaign.ResolveEnding(s, db0, last.RequestedEnding);
            s.EndingReached = end.Id;
            var next = Campaign.StartNextCycle(s);
            Assert.AreEqual(2, next.Cycle);
            Assert.AreEqual(1, next.Night);
            Assert.IsTrue(next.EndingsSeen.Contains(end.Id));
            var db = TestContent.Db;
            var page = db.Pages.First(p => p.Id == "welcome");
            Assert.IsTrue(Campaign.VisibleAnnotations(page, next, db).Any(a => a.Condition == "cycle>=2"), "the machine remembers the previous cycle");
            var ctx = AutoPlayer.Survivor().PlayNight(next, db);
            Assert.AreEqual(ShiftPhase.Ended, ctx.Phase);
        }
    }
}
