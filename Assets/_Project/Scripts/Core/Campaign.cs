using System;
using System.Collections.Generic;
using System.Linq;

namespace HikariForge.M313.Core
{
    public sealed class NightSummary
    {
        public int Night;
        public int Sales;
        public int Wage;
        public int Bonus;
        public int Served;
        public int Refused;
        public List<string> Notes = new List<string>();
        public int Total => Sales + Wage + Bonus;
    }

    /// <summary>Campaign-level rules: handbook visibility, night transitions and endings.</summary>
    public static class Campaign
    {
        public static List<HandbookPageDef> VisiblePages(GameState s, ContentDatabase db, ShiftContext ctx = null) =>
            db.Pages.Where(p =>
                    (p.Unlocked ? s.UnlockedPages.Contains(p.Id) : (p.Night <= s.Night || s.UnlockedPages.Contains(p.Id))) &&
                    (p.RemovedNight == 0 || s.Night < p.RemovedNight))
                .ToList();

        public static List<AnnotationDef> VisibleAnnotations(HandbookPageDef page, GameState s, ContentDatabase db) =>
            page.Annotations.Where(a => a.Night <= s.Night && Conditions.Evaluate(a.Condition, s, db)).ToList();

        /// <summary>Applies end-of-night pay and advances the campaign. Returns the summary to show.</summary>
        public static NightSummary CompleteNight(GameState s, ContentDatabase db, ShiftContext ctx)
        {
            var night = db.Night(s.Night);
            var summary = new NightSummary
            {
                Night = s.Night,
                Sales = ctx.NightEarnings,
                Wage = night?.Wage ?? 1500,
                Served = ctx.CustomersServed,
                Refused = ctx.CustomersRefused
            };
            int empty = s.Slots.Count(sl => sl.Code != "E1" && (sl.ProductId == null || sl.Count == 0));
            if (empty <= 4)
            {
                summary.Bonus = 500;
                summary.Notes.Add("District Office: well-stocked unit bonus +$5.00");
            }
            s.Money += summary.Wage + summary.Bonus;
            s.TotalEarned += summary.Wage + summary.Bonus;
            s.NightsCompleted = Math.Max(s.NightsCompleted, s.Night);
            if (s.Night < ContentDatabase.TotalNights) s.Night++;
            // Nerves recover by day, but exposure leaves a residue.
            s.Instability = Math.Min(30, s.Exposure / 25);
            s.CheckpointPhase = "prep";
            return summary;
        }

        public static EndingDef ResolveEnding(GameState s, ContentDatabase db, string requested)
        {
            if (!string.IsNullOrEmpty(requested) && requested != "auto")
            {
                var exact = db.Endings.FirstOrDefault(e => e.Id == requested);
                if (exact != null) return exact;
            }
            return db.Endings.FirstOrDefault(e => Conditions.Evaluate(e.Condition, s, db)) ?? db.Endings.LastOrDefault();
        }

        /// <summary>
        /// The campaign is a loop. After any ending, a new cycle starts at Night 1 with a fresh shift
        /// but the machine remembers: endings seen, the cycle count and the upgrades carry over.
        /// </summary>
        public static GameState StartNextCycle(GameState finished)
        {
            var next = GameState.NewGame();
            next.Cycle = finished.Cycle + 1;
            foreach (var e in finished.EndingsSeen) next.EndingsSeen.Add(e);
            if (!string.IsNullOrEmpty(finished.EndingReached)) next.EndingsSeen.Add(finished.EndingReached);
            foreach (var u in finished.Upgrades) next.SetLevel(u, finished.Level(u));
            next.SetFlag("cycle_prev_" + (finished.EndingReached ?? "none"), 1);
            return next;
        }

        /// <summary>Products the supplier sells tonight.</summary>
        public static List<ProductDef> Catalog(GameState s, ContentDatabase db) =>
            db.ProductList.Where(p => p.Purchasable && p.UnlockNight <= s.Night).ToList();

        public static List<UpgradeDef> AvailableUpgrades(GameState s, ContentDatabase db) =>
            db.UpgradeList.Where(u => u.UnlockNight <= s.Night).ToList();
    }
}
