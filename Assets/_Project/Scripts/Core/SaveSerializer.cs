using System;
using System.Collections.Generic;
using System.Globalization;

namespace HikariForge.M313.Core
{
    public sealed class SaveCorruptException : Exception
    {
        public SaveCorruptException(string message) : base(message) { }
    }

    /// <summary>GameState ↔ JSON, wrapped in an envelope with a version and checksum.</summary>
    public static class SaveSerializer
    {
        public static string Serialize(GameState s)
        {
            var data = ToDict(s);
            string body = MiniJson.Serialize(data);
            var envelope = new Dictionary<string, object>
            {
                ["game"] = "THE 3:13 MACHINE",
                ["version"] = GameState.SaveVersion,
                ["checksum"] = Checksum(body),
                ["data"] = data
            };
            return MiniJson.Serialize(envelope, true);
        }

        public static GameState Deserialize(string json)
        {
            object root;
            try { root = MiniJson.Parse(json); }
            catch (JsonException e) { throw new SaveCorruptException("Save is not valid JSON: " + e.Message); }
            var env = J.Obj(root);
            if (env == null) throw new SaveCorruptException("Save root is not an object");
            int version = J.Int(env, "version", -1);
            if (version < 1 || version > GameState.SaveVersion) throw new SaveCorruptException($"Unsupported save version {version}");
            var data = J.Obj(J.Get(env, "data"));
            if (data == null) throw new SaveCorruptException("Save has no data");
            string expected = J.Str(env, "checksum");
            if (expected != Checksum(MiniJson.Serialize(data))) throw new SaveCorruptException("Save checksum mismatch");
            return FromDict(data);
        }

        public static string Checksum(string text)
        {
            uint hash = 2166136261;
            foreach (char c in text) { hash ^= c; hash *= 16777619; }
            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }

        private static Dictionary<string, object> ToDict(GameState s)
        {
            var slots = new List<object>();
            foreach (var sl in s.Slots)
                slots.Add(new Dictionary<string, object> { ["code"] = sl.Code, ["product"] = sl.ProductId, ["count"] = sl.Count });
            var decisions = new List<object>();
            foreach (var d in s.Decisions)
                decisions.Add(new Dictionary<string, object> { ["n"] = d.Night, ["e"] = d.Encounter, ["a"] = d.Action, ["r"] = d.Result });
            var tx = new List<object>();
            foreach (var t in s.Transactions)
                tx.Add(new Dictionary<string, object>
                {
                    ["n"] = t.Night, ["t"] = t.Time, ["c"] = t.Customer, ["p"] = t.ProductId, ["a"] = t.Amount, ["r"] = t.Receipt
                });
            var stock = new Dictionary<string, object>();
            foreach (var kv in s.Stockroom) stock[kv.Key] = kv.Value;
            var flags = new Dictionary<string, object>();
            foreach (var kv in s.Flags) flags[kv.Key] = kv.Value;

            return new Dictionary<string, object>
            {
                ["cycle"] = s.Cycle,
                ["endingsSeen"] = new List<object>(s.EndingsSeen),
                ["night"] = s.Night,
                ["nightsCompleted"] = s.NightsCompleted,
                ["money"] = s.Money,
                ["instability"] = s.Instability,
                ["exposure"] = s.Exposure,
                ["bond"] = s.Bond,
                ["ending"] = s.EndingReached,
                ["phase"] = s.CheckpointPhase,
                ["served"] = s.TotalServed,
                ["refused"] = s.TotalRefused,
                ["earned"] = s.TotalEarned,
                ["stockroom"] = stock,
                ["slots"] = slots,
                ["upgrades"] = new List<object>(s.Upgrades),
                ["upgradeLevels"] = LevelsDict(s),
                ["flags"] = flags,
                ["clues"] = new List<object>(s.Clues),
                ["pages"] = new List<object>(s.UnlockedPages),
                ["decisions"] = decisions,
                ["transactions"] = tx
            };
        }

        private static GameState FromDict(Dictionary<string, object> d)
        {
            var s = new GameState
            {
                Night = Clamp(J.Int(d, "night", 1), 1, ContentDatabase.TotalNights),
                NightsCompleted = Clamp(J.Int(d, "nightsCompleted"), 0, ContentDatabase.TotalNights),
                Money = Math.Max(0, J.Int(d, "money", GameState.StartingMoney)),
                Instability = Clamp(J.Int(d, "instability"), 0, 100),
                Exposure = Math.Max(0, J.Int(d, "exposure")),
                Bond = Math.Max(0, J.Int(d, "bond")),
                EndingReached = J.Get(d, "ending") as string,
                CheckpointPhase = J.Str(d, "phase", "prep"),
                TotalServed = J.Int(d, "served"),
                TotalRefused = J.Int(d, "refused"),
                TotalEarned = J.Int(d, "earned")
            };
            s.Cycle = Math.Max(1, J.Int(d, "cycle", 1));
            foreach (var e in J.Strings(d, "endingsSeen")) s.EndingsSeen.Add(e);
            var stock = J.Obj(J.Get(d, "stockroom"));
            if (stock != null)
                foreach (var kv in stock)
                    if (kv.Value is double v) s.Stockroom[kv.Key] = Math.Max(0, (int)v);
            foreach (var o in J.List(d, "slots"))
            {
                var sd = J.Obj(o);
                var slot = s.Slot(J.Str(sd, "code"));
                if (slot == null) continue;
                slot.ProductId = J.Get(sd, "product") as string;
                slot.Count = Math.Max(0, J.Int(sd, "count"));
                if (slot.ProductId == null) slot.Count = 0;
            }
            // Version 1 saves only stored owned upgrade ids: they migrate to level 1.
            foreach (var u in J.Strings(d, "upgrades")) s.SetLevel(u, 1);
            var levels = J.Obj(J.Get(d, "upgradeLevels"));
            if (levels != null)
                foreach (var kv in levels)
                    if (kv.Value is double lv) s.SetLevel(kv.Key, Math.Max(0, Math.Min(3, (int)lv)));
            var flags = J.Obj(J.Get(d, "flags"));
            if (flags != null)
                foreach (var kv in flags)
                    if (kv.Value is double v) s.Flags[kv.Key] = (int)v;
            foreach (var c in J.Strings(d, "clues")) s.Clues.Add(c);
            foreach (var p in J.Strings(d, "pages")) s.UnlockedPages.Add(p);
            foreach (var o in J.List(d, "decisions"))
            {
                var dd = J.Obj(o);
                s.Decisions.Add(new DecisionRecord
                {
                    Night = J.Int(dd, "n"), Encounter = J.Str(dd, "e"), Action = J.Str(dd, "a"), Result = J.Str(dd, "r")
                });
            }
            foreach (var o in J.List(d, "transactions"))
            {
                var td = J.Obj(o);
                s.Transactions.Add(new TransactionRecord
                {
                    Night = J.Int(td, "n"), Time = J.Int(td, "t"), Customer = J.Str(td, "c"), ProductId = J.Str(td, "p"),
                    Amount = J.Int(td, "a"), Receipt = J.Str(td, "r")
                });
            }
            return s;
        }

        private static Dictionary<string, object> LevelsDict(GameState s)
        {
            var d = new Dictionary<string, object>();
            foreach (var u in s.Upgrades) d[u] = s.Level(u);
            return d;
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
