using System;
using System.Collections.Generic;
using System.Linq;

namespace HikariForge.M313.Core
{
    /// <summary>
    /// Loads all game content from JSON. The text provider maps a content file name
    /// (e.g. "products") to its JSON text, so Unity (Resources) and tests (disk) share it.
    /// </summary>
    public sealed class ContentDatabase
    {
        public const int TotalNights = 7;
        public static readonly string[] ContentFiles =
        {
            "products", "upgrades", "handbook", "clues", "characters", "nights", "endings",
            "encounters_n1", "encounters_n2", "encounters_n3", "encounters_n4",
            "encounters_n5", "encounters_n6", "encounters_n7", "encounters_endless"
        };

        public readonly Dictionary<string, ProductDef> Products = new Dictionary<string, ProductDef>();
        public readonly List<ProductDef> ProductList = new List<ProductDef>();
        public readonly Dictionary<string, UpgradeDef> Upgrades = new Dictionary<string, UpgradeDef>();
        public readonly List<UpgradeDef> UpgradeList = new List<UpgradeDef>();
        public readonly List<HandbookPageDef> Pages = new List<HandbookPageDef>();
        public readonly Dictionary<string, ClueDef> Clues = new Dictionary<string, ClueDef>();
        public readonly Dictionary<string, CharacterDef> Characters = new Dictionary<string, CharacterDef>();
        public readonly Dictionary<int, NightDef> Nights = new Dictionary<int, NightDef>();
        public readonly List<EndingDef> Endings = new List<EndingDef>();
        public readonly Dictionary<string, EncounterDef> Encounters = new Dictionary<string, EncounterDef>();
        public readonly List<string> LoadErrors = new List<string>();

        public static ContentDatabase Load(Func<string, string> textProvider)
        {
            var db = new ContentDatabase();
            foreach (var file in ContentFiles)
            {
                string text;
                try { text = textProvider(file); }
                catch (Exception e) { db.LoadErrors.Add($"{file}: {e.Message}"); continue; }
                if (string.IsNullOrEmpty(text)) { db.LoadErrors.Add($"{file}: missing"); continue; }
                try { db.Parse(file, MiniJson.Parse(text)); }
                catch (Exception e) { db.LoadErrors.Add($"{file}: {e.Message}"); }
            }
            return db;
        }

        public IEnumerable<EncounterDef> EncountersForNight(int night) =>
            Encounters.Values.Where(e => e.Night == night).OrderBy(e => e.Time);

        public NightDef Night(int n) => Nights.TryGetValue(n, out var d) ? d : null;

        public ProductDef Product(string id) => id != null && Products.TryGetValue(id, out var p) ? p : null;

        private void Parse(string file, object root)
        {
            switch (file)
            {
                case "products":
                    foreach (var o in J.Arr(root)) AddProduct(J.Obj(o));
                    break;
                case "upgrades":
                    foreach (var o in J.Arr(root))
                    {
                        var d = J.Obj(o);
                        var u = new UpgradeDef
                        {
                            Id = J.Str(d, "id"), Name = J.Str(d, "name"),
                            UnlockNight = J.Int(d, "unlockNight", 1), Description = J.Str(d, "desc")
                        };
                        foreach (var lv in J.List(d, "levels"))
                        {
                            var ld = J.Obj(lv);
                            u.Levels.Add(new UpgradeLevelDef { Price = J.Int(ld, "price"), Effect = J.Str(ld, "effect") });
                        }
                        Upgrades[u.Id] = u;
                        UpgradeList.Add(u);
                    }
                    break;
                case "handbook":
                    foreach (var o in J.List(J.Obj(root), "pages")) Pages.Add(ParsePage(J.Obj(o)));
                    break;
                case "clues":
                    foreach (var o in J.Arr(root))
                    {
                        var d = J.Obj(o);
                        var c = new ClueDef
                        {
                            Id = J.Str(d, "id"), Title = J.Str(d, "title"), Kind = J.Str(d, "kind", "note"),
                            Text = J.Str(d, "text"), Key = J.Bool(d, "key"), Image = J.Str(d, "image")
                        };
                        Clues[c.Id] = c;
                    }
                    break;
                case "characters":
                    foreach (var o in J.Arr(root))
                    {
                        var d = J.Obj(o);
                        var c = new CharacterDef
                        {
                            Id = J.Str(d, "id"), Name = J.Str(d, "name"), Portrait = J.Str(d, "portrait"),
                            Motif = J.Str(d, "motif"), Bio = J.Str(d, "bio")
                        };
                        Characters[c.Id] = c;
                    }
                    break;
                case "nights":
                    foreach (var o in J.Arr(root))
                    {
                        var n = ParseNight(J.Obj(o));
                        Nights[n.Night] = n;
                    }
                    break;
                case "endings":
                    foreach (var o in J.Arr(root))
                    {
                        var d = J.Obj(o);
                        Endings.Add(new EndingDef
                        {
                            Id = J.Str(d, "id"), Title = J.Str(d, "title"), Condition = J.Str(d, "condition"),
                            Priority = J.Int(d, "priority"), Lines = J.Strings(d, "lines"),
                            Music = J.Str(d, "music"), Image = J.Str(d, "image"),
                            Achievement = J.Str(d, "achievement"), AchievementDesc = J.Str(d, "achievementDesc")
                        });
                    }
                    Endings.Sort((a, b) => b.Priority.CompareTo(a.Priority));
                    break;
                default:
                    if (file.StartsWith("encounters_"))
                    {
                        foreach (var o in J.Arr(root))
                        {
                            var e = ParseEncounter(J.Obj(o));
                            if (Encounters.ContainsKey(e.Id)) LoadErrors.Add($"{file}: duplicate encounter id {e.Id}");
                            Encounters[e.Id] = e;
                        }
                    }
                    break;
            }
        }

        private void AddProduct(Dictionary<string, object> d)
        {
            var p = new ProductDef
            {
                Id = J.Str(d, "id"), Name = J.Str(d, "name"), Price = J.Int(d, "price"), Cost = J.Int(d, "cost"),
                Category = J.Str(d, "category", "snack"), Purchasable = J.Bool(d, "purchasable", true),
                UnlockNight = J.Int(d, "unlockNight", 1), Description = J.Str(d, "desc"), Label = J.Str(d, "label"),
                Icon = J.Str(d, "icon"), Unusual = J.Bool(d, "unusual")
            };
            Products[p.Id] = p;
            ProductList.Add(p);
        }

        private static HandbookPageDef ParsePage(Dictionary<string, object> d)
        {
            var p = new HandbookPageDef
            {
                Id = J.Str(d, "id"), Section = J.Str(d, "section"), Title = J.Str(d, "title"),
                Night = J.Int(d, "night", 1), RemovedNight = J.Int(d, "removedNight"),
                Stamped = J.Bool(d, "stamped", true), Forged = J.Bool(d, "forged"), Unlocked = J.Bool(d, "unlocked"),
                Rules = J.Strings(d, "rules"), Body = J.Str(d, "body")
            };
            foreach (var a in J.List(d, "annotations"))
            {
                var ad = J.Obj(a);
                p.Annotations.Add(new AnnotationDef
                {
                    Ink = J.Str(ad, "ink", "green"), Text = J.Str(ad, "text"), Night = J.Int(ad, "night", 1),
                    Condition = J.Str(ad, "condition")
                });
            }
            return p;
        }

        private static NightDef ParseNight(Dictionary<string, object> d)
        {
            var n = new NightDef
            {
                Night = J.Int(d, "night"), Title = J.Str(d, "title"), Subtitle = J.Str(d, "subtitle"),
                Weather = J.Str(d, "weather", "clear"), Music = J.Str(d, "music"), MarginMusic = J.Str(d, "marginMusic"),
                Tier = J.Int(d, "tier", 1), Wage = J.Int(d, "wage", 1500), Briefing = J.Str(d, "briefing"),
                PhoneCall = J.Strings(d, "phone"), PhoneSpeaker = J.Str(d, "phoneSpeaker", "District Office"),
                InvestigationIntro = J.Str(d, "investigationIntro"), EndCard = J.Str(d, "endCard")
            };
            foreach (var e in J.List(d, "events"))
            {
                var ed = J.Obj(e);
                n.Events.Add(new EventDef
                {
                    Time = ParseTime(J.Str(ed, "time", "23:00")), Type = J.Str(ed, "type"), Condition = J.Str(ed, "condition"),
                    Id = J.Str(ed, "id"), Speaker = J.Str(ed, "speaker"), Lines = J.Strings(ed, "lines"),
                    Effects = J.Strings(ed, "effects"), Duration = J.Float(ed, "duration", 15f),
                    OnOpen = J.Strings(ed, "onOpen"), OpenResult = J.Str(ed, "openResult", "bad"),
                    OpenLines = J.Strings(ed, "openLines"), OnServe = J.Strings(ed, "onServe")
                });
            }
            n.Events.Sort((a, b) => a.Time.CompareTo(b.Time));
            foreach (var h in J.List(d, "investigation"))
            {
                var hd = J.Obj(h);
                n.Investigation.Add(new HotspotDef
                {
                    Id = J.Str(hd, "id"), Label = J.Str(hd, "label"), X = J.Float(hd, "x"), Y = J.Float(hd, "y"),
                    W = J.Float(hd, "w", 0.1f), H = J.Float(hd, "h", 0.1f), Text = J.Str(hd, "text"),
                    Clue = J.Str(hd, "clue"), Condition = J.Str(hd, "condition"), Effects = J.Strings(hd, "effects"),
                    Required = J.Bool(hd, "required")
                });
            }
            return n;
        }

        private static EncounterDef ParseEncounter(Dictionary<string, object> d)
        {
            var e = new EncounterDef
            {
                Id = J.Str(d, "id"), Night = J.Int(d, "night"), Time = ParseTime(J.Str(d, "time", "23:30")),
                Condition = J.Str(d, "condition"), Character = J.Str(d, "character"), Name = J.Str(d, "name"),
                Portrait = J.Str(d, "portrait"), Supernatural = J.Bool(d, "supernatural"), Lines = J.Strings(d, "lines"),
                Request = J.Str(d, "request"), RequestText = J.Str(d, "requestText"), Quantity = J.Int(d, "quantity"), Pays = J.Int(d, "pays"),
                PaymentDesc = J.Str(d, "payment"), Patience = J.Float(d, "patience", 75f), Music = J.Str(d, "music"),
                ArriveScare = J.Str(d, "arriveScare"), ArriveEffects = J.Strings(d, "arriveEffects"),
                Rules = J.Strings(d, "rules"), NoReflection = J.Bool(d, "noReflection"), ReflectionLags = J.Bool(d, "reflectionLags")
            };
            foreach (var i in J.List(d, "inspect"))
            {
                var id = J.Obj(i);
                e.Inspect.Add(new InspectDef
                {
                    Spot = J.Str(id, "spot"), Label = J.Str(id, "label"), Text = J.Str(id, "text"),
                    Upgrade = J.Str(id, "upgrade"), Clue = J.Str(id, "clue"), Effects = J.Strings(id, "effects"),
                    Condition = J.Str(id, "condition")
                });
            }
            foreach (var t in J.List(d, "talk"))
            {
                var td = J.Obj(t);
                e.Talk.Add(new TalkDef
                {
                    Id = J.Str(td, "id"), Text = J.Str(td, "text"), Reply = J.Strings(td, "reply"),
                    Effects = J.Strings(td, "effects"), Condition = J.Str(td, "condition"), End = J.Bool(td, "end")
                });
            }
            foreach (var c in J.List(d, "cameras"))
            {
                var cd = J.Obj(c);
                e.Cameras.Add(new CameraViewDef
                {
                    Cam = J.Str(cd, "cam"), State = J.Str(cd, "state", "present"), Note = J.Str(cd, "note"),
                    Timestamp = J.Str(cd, "timestamp"), Upgrade = J.Str(cd, "upgrade"), Condition = J.Str(cd, "condition")
                });
            }
            var outs = J.Obj(J.Get(d, "outcomes"));
            if (outs != null)
            {
                foreach (var kv in outs)
                {
                    var list = new List<OutcomeDef>();
                    // An outcome key maps to either one outcome object or a list of conditional variants.
                    if (kv.Value is List<object> variants)
                        foreach (var v in variants) list.Add(ParseOutcome(J.Obj(v)));
                    else
                        list.Add(ParseOutcome(J.Obj(kv.Value)));
                    e.Outcomes[kv.Key] = list;
                }
            }
            return e;
        }

        private static OutcomeDef ParseOutcome(Dictionary<string, object> d) => new OutcomeDef
        {
            Lines = J.Strings(d, "lines"), Effects = J.Strings(d, "effects"), Result = J.Str(d, "result", "neutral"),
            Scare = J.Str(d, "scare"), Receipt = J.Str(d, "receipt"), Portrait = J.Str(d, "portrait"),
            Paid = J.Bool(d, "paid", true), Condition = J.Str(d, "condition")
        };

        /// <summary>"HH:MM" on the 23:00-06:00 shift → minutes since 23:00.</summary>
        public static int ParseTime(string hhmm)
        {
            if (string.IsNullOrEmpty(hhmm)) return 0;
            var parts = hhmm.Split(':');
            int h = int.Parse(parts[0]);
            int m = parts.Length > 1 ? int.Parse(parts[1]) : 0;
            int fromStart = h >= 23 ? (h - 23) * 60 : (h + 1) * 60;
            return fromStart + m;
        }

        public static string FormatTime(float minutes)
        {
            int total = (int)minutes;
            int h = (23 + total / 60) % 24;
            int m = total % 60;
            int h12 = h % 12 == 0 ? 12 : h % 12;
            string ampm = h >= 12 ? "PM" : "AM";
            return $"{h12}:{m:00} {ampm}";
        }

        public const int MarginTime = 253;   // 3:13 AM
        public const int ShiftEnd = 420;     // 6:00 AM
    }
}
