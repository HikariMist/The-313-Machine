using System;
using System.Collections.Generic;
using System.Linq;

namespace HikariForge.M313.Core
{
    /// <summary>
    /// Endless Mode (unlocked after all 15 endings): an unending series of procedurally assembled
    /// shifts built from rule-test encounter templates. Each round is harder. One death ends the run.
    /// </summary>
    public static class Endless
    {
        public const string Flag = "endless_round";

        public static GameState NewRun(Profile profile)
        {
            var s = GameState.NewGame();
            s.Night = ContentDatabase.TotalNights;   // every handbook section is available
            s.Money = 4000;
            s.SetFlag(Flag, 1);
            s.SetFlag("cam0", 1);
            s.SetFlag("smile_met", 1);
            return s;
        }

        public static int Round(GameState s) => Math.Max(1, s.Flag(Flag));

        public static List<EncounterDef> Templates(ContentDatabase db) =>
            db.Encounters.Values.Where(e => e.Night == 0).OrderBy(e => e.Id).ToList();

        public static ShiftContext BuildShift(GameState s, ContentDatabase db, int seed)
        {
            int round = Round(s);
            var rng = new Random(seed * 7919 + round * 104729);
            var templates = Templates(db);
            int tier = Math.Min(4, 1 + (round - 1) / 2);
            int count = Math.Min(12, 6 + round / 2);

            var night = new NightDef
            {
                Night = s.Night,
                Title = $"Endless Shift — Round {round}",
                Subtitle = "The minute never closes.",
                Weather = new[] { "clear", "fog", "rain", "storm", "margin", "flood" }[Math.Min(5, (round - 1) % 6)],
                Music = round % 2 == 0 ? "three_thirteen" : "read_the_rules",
                MarginMusic = "no_more_change",
                Tier = tier,
                Wage = 1500 + round * 200,
                PhoneSpeaker = "Unit 313",
                PhoneCall = new List<string> { $"ROUND {round}.", "THE RULES ARE THE SAME. THE CUSTOMERS ARE NOT." }
            };
            night.Events.Add(new EventDef { Time = 2, Type = "phone", Id = "endless_call", Speaker = "Unit 313", Lines = night.PhoneCall });
            if (round >= 2) night.Events.Add(Knock(rng.Next(260, 380), 12 + round));
            if (round >= 3) night.Events.Add(new EventDef { Time = rng.Next(60, 240), Type = "blackout", Id = "endless_blackout", Duration = 14 + round * 2 });
            if (round >= 4) night.Events.Add(new EventDef { Time = rng.Next(270, 330), Type = "wrongtime", Id = "endless_wrongtime", Duration = 25, OnServe = new List<string> { "instability+20", "scare:reality_warp" } });
            if (rng.NextDouble() < 0.5) night.Events.Add(new EventDef { Time = rng.Next(30, 200), Type = "jam", Id = "endless_jam" });

            // Ordinary customers before 3:13, rule tests after. Harder rounds mix rule tests in earlier.
            var ordinary = templates.Where(t => !t.Supernatural).ToList();
            var tests = templates.Where(t => t.Supernatural).ToList();
            var picks = new List<EncounterDef>();
            int start = 15, end = 400;
            int gap = (end - start) / count;
            for (int i = 0; i < count; i++)
            {
                int time = start + i * gap + rng.Next(0, Math.Max(1, gap / 3));
                bool afterMargin = time >= ContentDatabase.MarginTime;
                bool useTest = afterMargin || rng.NextDouble() < 0.12 * round;
                var pool = useTest ? tests : ordinary;
                var t = pool[rng.Next(pool.Count)];
                picks.Add(Clone(t, $"{t.Id}_r{round}_{i}", time, Math.Max(25f, t.Patience - round * 3)));
            }
            return new ShiftContext(s, db, night, picks, seed + round);
        }

        private static EventDef Knock(int time, float duration) => new EventDef
        {
            Time = time, Type = "knock", Id = "endless_knock", Duration = duration, OpenResult = "death",
            OpenLines = new List<string> { "*Something inside the unit was waiting for exactly that." }
        };

        private static EncounterDef Clone(EncounterDef t, string id, int time, float patience) => new EncounterDef
        {
            Id = id, Night = 0, Time = time, Condition = "", Character = t.Character, Name = t.Name, Portrait = t.Portrait,
            Supernatural = t.Supernatural, Lines = t.Lines, Request = t.Request, RequestText = t.RequestText, Pays = t.Pays,
            PaymentDesc = t.PaymentDesc, Patience = patience, Music = t.Music, ArriveScare = t.ArriveScare,
            ArriveEffects = t.ArriveEffects, Rules = t.Rules, Inspect = t.Inspect, Talk = t.Talk, Cameras = t.Cameras,
            Outcomes = t.Outcomes, NoReflection = t.NoReflection, ReflectionLags = t.ReflectionLags
        };

        /// <summary>After surviving a round: pay, advance, recover nerves.</summary>
        public static void CompleteRound(GameState s, ShiftContext ctx)
        {
            int pay = ctx.NightDef.Wage;
            s.Money += pay;
            s.TotalEarned += pay + ctx.NightEarnings;
            s.SetFlag(Flag, Round(s) + 1);
            s.Instability = Math.Min(40, s.Instability / 2);
        }
    }
}
