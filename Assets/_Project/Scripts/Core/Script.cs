using System;
using System.Collections.Generic;
using System.Globalization;

namespace HikariForge.M313.Core
{
    /// <summary>
    /// Tiny condition language used by content.
    ///   expr  := and ('|' and)*
    ///   and   := term ('&amp;' term)*
    ///   term  := ['!'] subject [op number]
    /// Subjects: flag:x, clue:x, upgrade:x, page:x, did:ENC[=prefix], night, bond, instability,
    /// money, keyclues, exposure, stock:PRODUCT, margin, true, false.
    /// Without an operator a subject is true when its value is greater than zero.
    /// </summary>
    public static class Conditions
    {
        public static bool Evaluate(string expr, GameState s, ContentDatabase db, ShiftContext ctx = null)
        {
            if (string.IsNullOrWhiteSpace(expr)) return true;
            foreach (var orPart in expr.Split('|'))
            {
                bool all = true;
                foreach (var andPart in orPart.Split('&'))
                {
                    if (!EvalTerm(andPart.Trim(), s, db, ctx)) { all = false; break; }
                }
                if (all) return true;
            }
            return false;
        }

        private static readonly string[] Ops = { ">=", "<=", "==", "!=", ">", "<", "=" };

        private static bool EvalTerm(string term, GameState s, ContentDatabase db, ShiftContext ctx)
        {
            if (term.Length == 0) return true;
            bool negate = false;
            while (term.StartsWith("!")) { negate = !negate; term = term.Substring(1).Trim(); }

            // did:ENC=prefix compares decision actions as text.
            if (term.StartsWith("did:"))
            {
                string body = term.Substring(4);
                string enc = body, prefix = null;
                int eq = body.IndexOf('=');
                if (eq >= 0) { enc = body.Substring(0, eq); prefix = body.Substring(eq + 1); }
                var rec = s.LastDecision(enc);
                bool r = rec != null && (prefix == null || (rec.Action ?? "").StartsWith(prefix, StringComparison.Ordinal));
                return negate ? !r : r;
            }

            string subject = term, op = null;
            int rhs = 0;
            foreach (var candidate in Ops)
            {
                int idx = term.IndexOf(candidate, StringComparison.Ordinal);
                if (idx > 0)
                {
                    subject = term.Substring(0, idx).Trim();
                    op = candidate;
                    rhs = int.Parse(term.Substring(idx + candidate.Length).Trim(), CultureInfo.InvariantCulture);
                    break;
                }
            }
            int lhs = Value(subject, s, db, ctx);
            bool result;
            switch (op)
            {
                case null: result = lhs > 0; break;
                case ">=": result = lhs >= rhs; break;
                case "<=": result = lhs <= rhs; break;
                case ">": result = lhs > rhs; break;
                case "<": result = lhs < rhs; break;
                case "!=": result = lhs != rhs; break;
                default: result = lhs == rhs; break;
            }
            return negate ? !result : result;
        }

        public static int Value(string subject, GameState s, ContentDatabase db, ShiftContext ctx)
        {
            int colon = subject.IndexOf(':');
            string kind = colon >= 0 ? subject.Substring(0, colon) : subject;
            string arg = colon >= 0 ? subject.Substring(colon + 1) : null;
            switch (kind)
            {
                case "true": return 1;
                case "false": return 0;
                case "flag": return s.Flag(arg);
                case "clue": return s.Clues.Contains(arg) ? 1 : 0;
                case "upgrade": return s.Upgrades.Contains(arg) ? 1 : 0;
                case "page": return s.UnlockedPages.Contains(arg) ? 1 : 0;
                case "night": return s.Night;
                case "cycle": return s.Cycle;
                case "seen": return s.EndingsSeen.Contains(arg) ? 1 : 0;
                case "endings": return s.EndingsSeen.Count;
                case "bond": return s.Bond;
                case "instability": return s.Instability;
                case "exposure": return s.Exposure;
                case "money": return s.Money;
                case "keyclues": return db != null ? s.KeyClueCount(db) : 0;
                case "stock": return s.MachineCount(arg);
                case "margin": return ctx != null && ctx.InMargin ? 1 : 0;
                case "wrongtime": return ctx != null && ctx.WrongTimeActive ? 1 : 0;
                default: throw new FormatException($"Unknown condition subject '{subject}'");
            }
        }

        /// <summary>Validates syntax without a live state; returns null when valid.</summary>
        public static string Validate(string expr)
        {
            try
            {
                Evaluate(expr, new GameState(), null);
                return null;
            }
            catch (Exception e) { return e.Message; }
        }
    }

    /// <summary>
    /// Effects language. Each effect is one string:
    ///   money+250  money-100  bond+1  instability+10  exposure+5
    ///   flag:x=1   flag:x+1   flag:x-1   clue:id   unclue:id   page:id
    ///   produce:PRODUCT   upgrade:id   scare:id   music:id   sfx:id   machine:TEXT   receipt:TEXT   note:TEXT
    /// Presentation-only effects are forwarded to the shift's event queue.
    /// </summary>
    public static class Effects
    {
        public static void Apply(IEnumerable<string> effects, GameState s, ContentDatabase db, ShiftContext ctx)
        {
            if (effects == null) return;
            foreach (var e in effects) Apply(e, s, db, ctx);
        }

        public static void Apply(string effect, GameState s, ContentDatabase db, ShiftContext ctx)
        {
            if (string.IsNullOrWhiteSpace(effect)) return;
            effect = effect.Trim();
            int colon = effect.IndexOf(':');
            string head = colon >= 0 ? effect.Substring(0, colon) : effect;

            // Numeric stat deltas: money+250, bond+1 ...
            if (colon < 0)
            {
                int sign = effect.IndexOfAny(new[] { '+', '-' });
                if (sign <= 0) throw new FormatException($"Bad effect '{effect}'");
                string stat = effect.Substring(0, sign);
                int amount = int.Parse(effect.Substring(sign), CultureInfo.InvariantCulture);
                switch (stat)
                {
                    case "money": s.Money = Math.Max(0, s.Money + amount); if (amount > 0) s.TotalEarned += amount; break;
                    case "bond": s.Bond = Math.Max(0, s.Bond + amount); break;
                    case "instability": s.AddInstability(amount); break;
                    case "exposure": s.Exposure = Math.Max(0, s.Exposure + amount); break;
                    default: throw new FormatException($"Unknown stat '{stat}' in '{effect}'");
                }
                ctx?.Emit(PresEventType.Stat, stat, null, amount);
                return;
            }

            string arg = effect.Substring(colon + 1);
            switch (head)
            {
                case "flag":
                {
                    int op = arg.IndexOfAny(new[] { '=', '+', '-' });
                    if (op <= 0) { s.SetFlag(arg, 1); break; }
                    string name = arg.Substring(0, op);
                    int value = int.Parse(arg.Substring(op + 1), CultureInfo.InvariantCulture);
                    if (arg[op] == '=') s.SetFlag(name, value);
                    else s.AddFlag(name, arg[op] == '+' ? value : -value);
                    break;
                }
                case "clue":
                    if (s.Clues.Add(arg)) ctx?.Emit(PresEventType.Clue, arg, db?.Clues.TryGetValue(arg, out var c) == true ? c.Title : arg);
                    break;
                case "unclue":
                    if (s.Clues.Remove(arg)) ctx?.Emit(PresEventType.ClueLost, arg, db?.Clues.TryGetValue(arg, out var lc) == true ? lc.Title : arg);
                    break;
                case "page":
                    if (s.UnlockedPages.Add(arg)) ctx?.Emit(PresEventType.HandbookChanged, arg);
                    break;
                case "produce":
                    s.MachineProduces(arg);
                    ctx?.Emit(PresEventType.Produced, arg);
                    break;
                case "upgrade":
                    s.Upgrades.Add(arg);
                    break;
                case "scare": ctx?.Emit(PresEventType.Scare, arg); break;
                case "music": ctx?.Emit(PresEventType.Music, arg); break;
                case "sfx": ctx?.Emit(PresEventType.Sfx, arg); break;
                case "machine": ctx?.Emit(PresEventType.MachineMessage, null, arg); break;
                case "receipt": ctx?.Emit(PresEventType.Receipt, null, arg); break;
                case "note": ctx?.Emit(PresEventType.Notice, null, arg); break;
                case "ending": ctx?.RequestEnding(arg); break;
                default: throw new FormatException($"Unknown effect '{effect}'");
            }
        }

        public static string Validate(string effect, ContentDatabase db)
        {
            try
            {
                var s = new GameState();
                Apply(effect, s, db, null);
                int colon = effect.IndexOf(':');
                if (colon > 0 && db != null)
                {
                    string head = effect.Substring(0, colon), arg = effect.Substring(colon + 1);
                    if ((head == "clue" || head == "unclue") && !db.Clues.ContainsKey(arg)) return $"unknown clue '{arg}'";
                    if (head == "produce" && !db.Products.ContainsKey(arg)) return $"unknown product '{arg}'";
                    if (head == "upgrade" && !db.Upgrades.ContainsKey(arg)) return $"unknown upgrade '{arg}'";
                    if (head == "page" && !db.Pages.Exists(p => p.Id == arg)) return $"unknown page '{arg}'";
                }
                return null;
            }
            catch (Exception e) { return e.Message; }
        }
    }
}
