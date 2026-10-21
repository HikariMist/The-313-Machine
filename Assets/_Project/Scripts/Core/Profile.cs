using System;
using System.Collections.Generic;
using System.Linq;

namespace HikariForge.M313.Core
{
    /// <summary>
    /// Persistent player profile, separate from campaign saves: endings and achievements unlocked,
    /// cycles played and Endless Mode records. Survives New Game.
    /// </summary>
    public sealed class Profile
    {
        public readonly HashSet<string> Endings = new HashSet<string>();
        public readonly HashSet<string> Achievements = new HashSet<string>();
        public int CyclesCompleted;
        public int EndlessBestRound;
        public int EndlessBestEarnings;
        public int EndlessRuns;

        public const string EndlessAchievement = "ENDLESS_UNLOCKED";

        public static string AchievementFor(string endingId) => "ENDING_" + endingId.ToUpperInvariant();

        public bool EndlessUnlocked(ContentDatabase db) => db.Endings.Count > 0 && db.Endings.All(e => Endings.Contains(e.Id));

        public int EndingCount(ContentDatabase db) => db.Endings.Count(e => Endings.Contains(e.Id));

        /// <summary>Records an ending. Returns the achievement ids newly unlocked (may be empty).</summary>
        public List<string> RecordEnding(string endingId, ContentDatabase db)
        {
            var unlocked = new List<string>();
            if (string.IsNullOrEmpty(endingId)) return unlocked;
            CyclesCompleted++;
            Endings.Add(endingId);
            if (Achievements.Add(AchievementFor(endingId))) unlocked.Add(AchievementFor(endingId));
            if (EndlessUnlocked(db) && Achievements.Add(EndlessAchievement)) unlocked.Add(EndlessAchievement);
            return unlocked;
        }

        public bool RecordEndlessRun(int round, int earnings)
        {
            EndlessRuns++;
            bool best = round > EndlessBestRound || (round == EndlessBestRound && earnings > EndlessBestEarnings);
            if (best) { EndlessBestRound = round; EndlessBestEarnings = earnings; }
            return best;
        }

        public string Serialize()
        {
            var d = new Dictionary<string, object>
            {
                ["endings"] = new List<object>(Endings),
                ["achievements"] = new List<object>(Achievements),
                ["cycles"] = CyclesCompleted,
                ["endlessBest"] = EndlessBestRound,
                ["endlessBestEarnings"] = EndlessBestEarnings,
                ["endlessRuns"] = EndlessRuns
            };
            string body = MiniJson.Serialize(d);
            return MiniJson.Serialize(new Dictionary<string, object> { ["checksum"] = SaveSerializer.Checksum(body), ["profile"] = d }, true);
        }

        public static Profile Deserialize(string json)
        {
            var p = new Profile();
            if (string.IsNullOrEmpty(json)) return p;
            Dictionary<string, object> env;
            try { env = J.Obj(MiniJson.Parse(json)); }
            catch (JsonException) { throw new SaveCorruptException("Profile is not valid JSON"); }
            var d = J.Obj(J.Get(env, "profile"));
            if (d == null || J.Str(env, "checksum") != SaveSerializer.Checksum(MiniJson.Serialize(d)))
                throw new SaveCorruptException("Profile checksum mismatch");
            foreach (var e in J.Strings(d, "endings")) p.Endings.Add(e);
            foreach (var a in J.Strings(d, "achievements")) p.Achievements.Add(a);
            p.CyclesCompleted = J.Int(d, "cycles");
            p.EndlessBestRound = J.Int(d, "endlessBest");
            p.EndlessBestEarnings = J.Int(d, "endlessBestEarnings");
            p.EndlessRuns = J.Int(d, "endlessRuns");
            return p;
        }
    }
}
