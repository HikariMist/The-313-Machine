using System;
using System.Collections.Generic;
using System.Linq;

namespace HikariForge.M313.Core
{
    /// <summary>
    /// Central horror pacing. Decides when ambient scares and hallucinations fire, keeps
    /// cooldowns so scares never overlap, and exposes a threat level the audio/visual layers read.
    /// It never touches clue data, so required information is never hidden by randomness.
    /// </summary>
    public sealed class HorrorDirector
    {
        // Ambient scare pools by tier (1 = nights 1-2 ... 4 = night 7). Ids are rendered by the Unity ScareRenderer.
        public static readonly string[][] AmbientPools =
        {
            new[] { "shadow_pass", "reflection_linger", "distant_figure", "machine_hum", "item_shift", "far_headlights" },
            new[] { "light_flicker", "figure_window", "cam_static", "object_fall", "stinger_soft", "shadow_pass", "knock_once" },
            new[] { "lights_fail", "face_glass", "rush_figure", "reality_warp", "whisper_burst", "light_flicker", "hands_glass" },
            new[] { "margin_surge", "hands_glass", "rush_figure", "face_glass", "reality_warp", "lights_fail", "whisper_burst" }
        };

        public static readonly string[] HallucinationPool =
        {
            "hal_extra_customer", "hal_wrong_text", "hal_whisper", "hal_shadow_behind", "hal_blood_glass"
        };

        // Scripted scares that are always allowed (they respect cooldown by delaying ambient ones).
        public static readonly string[] ScriptedScares =
        {
            "js_drowned", "js_smile", "js_collector", "js_mara", "js_child_behind", "js_hollow", "js_final",
            "js_mimic", "js_crowd"
        };

        private readonly Random _rng;
        private float _nextAmbient;
        private float _cooldown;
        private float _nextHallucination;
        private readonly Queue<string> _recent = new Queue<string>();

        /// <summary>0.25 (minimal) .. 1 (full). Scales frequency of ambient scares.</summary>
        public float Intensity = 1f;
        public float Threat { get; private set; }
        public int ScaresFired { get; private set; }
        public float LastScareAge { get; private set; } = 999f;
        public string LastScare { get; private set; }

        public HorrorDirector(int seed)
        {
            _rng = new Random(seed);
            _nextAmbient = 25f;
            _nextHallucination = 40f;
        }

        public int EffectiveTier(int nightTier, bool inMargin) => Math.Max(1, Math.Min(4, nightTier + (inMargin ? 1 : 0)));

        public void Tick(float dt, ShiftContext ctx)
        {
            LastScareAge += dt;
            int tier = EffectiveTier(ctx.NightDef?.Tier ?? 1, ctx.InMargin);
            Threat = Math.Min(1f, tier * 0.18f + ctx.State.Instability / 200f + (ctx.Current != null && ctx.Current.Def.Supernatural ? 0.15f : 0f));

            if (ctx.Phase != ShiftPhase.Idle && ctx.Phase != ShiftPhase.Encounter) return;
            if (_cooldown > 0) { _cooldown -= dt; return; }

            _nextAmbient -= dt * Math.Max(0.25f, Intensity);
            if (_nextAmbient <= 0)
            {
                var pool = AmbientPools[tier - 1];
                var options = pool.Where(p => !_recent.Contains(p)).ToArray();
                if (options.Length == 0) options = pool;
                string id = options[_rng.Next(options.Length)];
                Fire(id, ctx, PresEventType.Ambient, tier);
                // Before 3:13 the lot stays quiet; afterwards scares come faster.
                float baseInterval = ctx.InMargin ? 32f : 55f;
                _nextAmbient = baseInterval + (float)_rng.NextDouble() * 30f - tier * 4f;
            }

            if (ctx.State.Instability >= 60)
            {
                _nextHallucination -= dt;
                if (_nextHallucination <= 0)
                {
                    string id = HallucinationPool[_rng.Next(HallucinationPool.Length)];
                    ctx.Emit(PresEventType.Hallucination, id, null, ctx.State.Instability);
                    _nextHallucination = 30f + (float)_rng.NextDouble() * 25f - (ctx.State.Instability - 60) * 0.3f;
                }
            }
        }

        private void Fire(string id, ShiftContext ctx, PresEventType type, int tier)
        {
            ctx.Emit(type, id, null, tier);
            _recent.Enqueue(id);
            while (_recent.Count > 3) _recent.Dequeue();
            _cooldown = 12f;
            ScaresFired++;
            LastScareAge = 0f;
            LastScare = id;
            if (tier >= 2) ctx.State.AddInstability(tier - 1);
        }

        /// <summary>Called when a scripted scare plays; pushes ambient scares back so they never stack.</summary>
        public void NotifyScripted(string id)
        {
            _cooldown = Math.Max(_cooldown, 18f);
            _nextAmbient = Math.Max(_nextAmbient, 20f);
            ScaresFired++;
            LastScareAge = 0f;
            LastScare = id;
        }
    }
}
