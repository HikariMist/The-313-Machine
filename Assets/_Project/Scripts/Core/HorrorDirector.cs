using System;
using System.Collections.Generic;
using System.Linq;

namespace HikariForge.M313.Core
{
    public sealed class ScareLogEntry
    {
        public float Time;       // real seconds into the shift
        public string Clock;     // in-game time
        public string Id;
        public string Kind;      // atmosphere, sudden, major, softened, death
        public string Reason;
        public override string ToString() => $"[{Time,6:0.0}s {Clock}] {Kind,-10} {Id} — {Reason}";
    }

    /// <summary>
    /// Central horror pacing. Three classes of event:
    /// - atmosphere: frequent, quiet anomalies (flicker, footsteps, a shadow) — no stingers;
    /// - sudden: noticeable minor scares with a stinger, rationed per night;
    /// - major: scripted full-screen scares from the story, also rationed.
    /// Random sudden scares only happen while no customer is at the glass, so they never interrupt
    /// a conversation or a decision. A scripted scare that would land too soon after another one,
    /// or past the night's budget, is softened into an atmospheric version (deaths always play).
    /// It never touches clue data, so required information is never hidden by randomness.
    /// </summary>
    public sealed class HorrorDirector
    {
        // Quiet atmospheric anomalies by tier (no loud audio).
        public static readonly string[][] AtmospherePools =
        {
            new[] { "shadow_pass", "reflection_linger", "distant_figure", "machine_hum", "item_shift", "far_headlights" },
            new[] { "light_flicker", "shadow_pass", "cam_static", "knock_once", "distant_figure", "item_shift" },
            new[] { "light_flicker", "whisper_burst", "shadow_pass", "cam_static", "knock_once", "far_headlights" },
            new[] { "light_flicker", "whisper_burst", "lights_fail", "cam_static", "shadow_pass", "knock_once" }
        };

        // Minor sudden scares (short stinger) by tier.
        public static readonly string[][] SuddenPools =
        {
            new[] { "figure_window" },
            new[] { "figure_window", "object_fall", "stinger_soft" },
            new[] { "face_glass", "rush_figure", "hands_glass", "object_fall" },
            new[] { "face_glass", "rush_figure", "hands_glass", "reality_warp", "margin_surge" }
        };

        /// <summary>Kept for compatibility with older references (all ambient ids).</summary>
        public static readonly string[][] AmbientPools = AtmospherePools.Select((p, i) => p.Concat(SuddenPools[i]).ToArray()).ToArray();

        public static readonly string[] HallucinationPool =
        {
            "hal_extra_customer", "hal_wrong_text", "hal_whisper", "hal_shadow_behind", "hal_blood_glass"
        };

        public static readonly string[] ScriptedScares =
        {
            "js_drowned", "js_smile", "js_collector", "js_mara", "js_child_behind", "js_hollow", "js_final",
            "js_mimic", "js_crowd"
        };

        /// <summary>Upper limit of noticeable scares (sudden + major) per night, index = night.</summary>
        public static readonly int[] SuddenBudget = { 2, 1, 1, 2, 2, 3, 3, 4 };

        // Real-time spacing. Target design is 8-12 min between majors on a full-length night; a shift
        // lasts roughly 15-25 real minutes, so the minimums are scaled to keep 1-4 scares per night apart.
        public const float MajorGap = 240f;
        public const float SuddenGap = 150f;
        public const float AtmosphereMin = 40f, AtmosphereMax = 85f;
        public const float QuietAfterScare = 20f;

        private readonly Random _rng;
        private readonly Queue<string> _recent = new Queue<string>();
        private float _time, _nextAtmosphere, _nextSuddenCheck, _nextHallucination;
        private float _lastMajor = -9999f, _lastSudden = -9999f, _lastAny = -9999f;
        private readonly HashSet<string> _oneShotsPlayed = new HashSet<string>();

        /// <summary>0.25 (minimal) .. 1 (full). Minimal disables random sudden scares and softens story scares.</summary>
        public float Intensity = 1f;
        public bool LogEnabled = true;
        public readonly List<ScareLogEntry> Log = new List<ScareLogEntry>();
        public float Threat { get; private set; }
        public int ScaresFired { get; private set; }
        public int NoticeableScares { get; private set; }
        public float LastScareAge => _time - _lastAny;
        public string LastScare { get; private set; }

        public HorrorDirector(int seed)
        {
            _rng = new Random(seed);
            _nextAtmosphere = 30f;
            _nextSuddenCheck = 90f;
            _nextHallucination = 40f;
        }

        public int EffectiveTier(int nightTier, bool inMargin) => Math.Max(1, Math.Min(4, nightTier + (inMargin ? 1 : 0)));

        public int Budget(int night)
        {
            int b = SuddenBudget[Math.Max(0, Math.Min(SuddenBudget.Length - 1, night))];
            if (Intensity < 0.5f) return 0;
            if (Intensity < 0.9f) return Math.Max(1, b - 1);
            return b;
        }

        private void Record(string id, string kind, string reason, ShiftContext ctx)
        {
            ScaresFired++;
            LastScare = id;
            _lastAny = _time;
            if (LogEnabled)
                Log.Add(new ScareLogEntry { Time = _time, Clock = ContentDatabase.FormatTime(ctx.Clock), Id = id, Kind = kind, Reason = reason });
        }

        public void Tick(float dt, ShiftContext ctx)
        {
            _time += dt;
            int night = ctx.State.Night;
            int tier = EffectiveTier(ctx.NightDef?.Tier ?? 1, ctx.InMargin);
            Threat = Math.Min(1f, tier * 0.18f + ctx.State.Instability / 200f + (ctx.Current != null && ctx.Current.Def.Supernatural ? 0.15f : 0f));

            // Never during phone calls, the stockroom search or a customer's reply.
            bool calm = ctx.Phase == ShiftPhase.Idle ||
                        (ctx.Phase == ShiftPhase.Encounter && ctx.Current != null && ctx.Current.Stage == EncounterStage.Awaiting);
            if (!calm || _time - _lastAny < QuietAfterScare) return;

            // Atmosphere: quiet, frequent, tier-appropriate.
            if (_time >= _nextAtmosphere)
            {
                var pool = AtmospherePools[tier - 1];
                var options = pool.Where(p => !_recent.Contains(p)).ToArray();
                string id = (options.Length > 0 ? options : pool)[_rng.Next((options.Length > 0 ? options : pool).Length)];
                Remember(id);
                ctx.Emit(PresEventType.Ambient, id, null, 0);
                Record(id, "atmosphere", "ambient timer", ctx);
                _nextAtmosphere = _time + AtmosphereMin + (float)_rng.NextDouble() * (AtmosphereMax - AtmosphereMin) / Math.Max(0.5f, Intensity);
            }

            // Sudden: only with nobody at the glass, within budget and spacing; early nights only after 3:13.
            if (_time >= _nextSuddenCheck && ctx.Phase == ShiftPhase.Idle)
            {
                _nextSuddenCheck = _time + 20f + (float)_rng.NextDouble() * 25f;
                bool allowed = NoticeableScares < Budget(night) && _time - _lastSudden >= SuddenGap && _time - _lastMajor >= SuddenGap
                               && (night >= 3 || ctx.InMargin);
                // Leave room for the night's story scares: random ones use at most half the budget.
                if (allowed && NoticeableScares * 2 < Budget(night) + 1 && _rng.NextDouble() < 0.35)
                {
                    var pool = SuddenPools[tier - 1].Where(p => !_recent.Contains(p)).ToArray();
                    if (pool.Length == 0) pool = SuddenPools[tier - 1];
                    string id = pool[_rng.Next(pool.Length)];
                    Remember(id);
                    _lastSudden = _time;
                    NoticeableScares++;
                    ctx.Emit(PresEventType.Ambient, id, null, tier);
                    Record(id, "sudden", $"random (budget {NoticeableScares}/{Budget(night)})", ctx);
                    if (tier >= 2) ctx.State.AddInstability(tier - 1);
                }
            }

            if (ctx.State.Instability >= 60 && _time >= _nextHallucination)
            {
                string id = HallucinationPool[_rng.Next(HallucinationPool.Length)];
                ctx.Emit(PresEventType.Hallucination, id, null, ctx.State.Instability);
                Record(id, "atmosphere", "hallucination (instability " + ctx.State.Instability + ")", ctx);
                _nextHallucination = _time + 30f + (float)_rng.NextDouble() * 25f - (ctx.State.Instability - 60) * 0.3f;
            }
        }

        private void Remember(string id)
        {
            _recent.Enqueue(id);
            while (_recent.Count > 3) _recent.Dequeue();
        }

        /// <summary>
        /// A story scare asked to play. Deaths always play in full. Otherwise it plays in full only when
        /// spacing and the night's budget allow; if not, it is softened to a quiet atmospheric version.
        /// </summary>
        public void RequestScripted(string id, bool lethal, string reason, ShiftContext ctx)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!lethal && AtmospherePools.Any(pool => pool.Contains(id)))
            {
                // Quiet story beats are atmosphere: they never use the scare budget.
                ctx.Emit(PresEventType.Ambient, id, null, 0);
                Record(id, "atmosphere", reason, ctx);
                return;
            }
            bool major = id.StartsWith("js_") || id == "margin_surge";
            int night = ctx.State.Night;
            if (lethal)
            {
                _lastMajor = _time;
                ctx.Emit(PresEventType.Scare, id);
                Record(id, "death", reason, ctx);
                return;
            }
            float gap = major ? MajorGap : SuddenGap;
            bool spaced = _time - _lastMajor >= gap && _time - _lastSudden >= Math.Min(gap, SuddenGap) * 0.5f;
            bool budget = NoticeableScares < Budget(night);
            if (spaced && budget)
            {
                NoticeableScares++;
                if (major) _lastMajor = _time; else _lastSudden = _time;
                ctx.Emit(PresEventType.Scare, id);
                Record(id, major ? "major" : "sudden", reason, ctx);
            }
            else
            {
                // Softened: the story beat still registers, quietly.
                ctx.Emit(PresEventType.Ambient, major ? "shadow_pass" : id, null, 0);
                Record(id, "softened", reason + (spaced ? " (night budget reached)" : " (too soon after previous scare)"), ctx);
            }
            _nextAtmosphere = Math.Max(_nextAtmosphere, _time + QuietAfterScare + 15f);
        }

        /// <summary>Back-compat: treat as a scripted request without a context (no-op).</summary>
        public void NotifyScripted(string id) { _lastMajor = _time; _lastAny = _time; }
    }
}
