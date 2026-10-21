// Static content definitions loaded from JSON in Resources/Content.
using System.Collections.Generic;

namespace HikariForge.M313.Core
{
    public sealed class ProductDef
    {
        public string Id;
        public string Name;
        public int Price;          // cents
        public int Cost;           // cents (supplier cost)
        public string Category;    // drink, snack, supply, unusual
        public bool Purchasable;   // can be ordered from the supplier
        public int UnlockNight = 1;
        public string Description;
        public string Label;       // printed label text, may hide messages
        public string Icon;
        public bool Unusual;
    }

    public sealed class UpgradeDef
    {
        public string Id;
        public string Name;
        public int Price;
        public int UnlockNight = 1;
        public string Description;
        public string Effect;
    }

    public sealed class AnnotationDef
    {
        public string Ink;   // green (Mara), purple (forger), red
        public string Text;
        public int Night = 1;
        public string Condition;
    }

    public sealed class HandbookPageDef
    {
        public string Id;
        public string Section;
        public string Title;
        public int Night = 1;          // appears from this night (at shift start)
        public int RemovedNight;       // 0 = never removed
        public bool Stamped = true;    // District Office seal present
        public bool Forged;            // purple "REV. 3:13" stamp
        public bool Unlocked;          // only appears when unlocked by an effect
        public List<string> Rules = new List<string>();   // "B1|Never serve water ..."
        public List<AnnotationDef> Annotations = new List<AnnotationDef>();
        public string Body;
    }

    public sealed class ClueDef
    {
        public string Id;
        public string Title;
        public string Kind;    // record, audio, receipt, footage, code, drawing, note, item
        public string Text;
        public bool Key;       // counts toward the hidden ending
        public string Image;
    }

    public sealed class CharacterDef
    {
        public string Id;
        public string Name;
        public string Portrait;
        public string Motif;   // sfx id played on arrival
        public string Bio;
    }

    public sealed class InspectDef
    {
        public string Spot;       // face, hands, reflection, clothes, payment, id
        public string Label;
        public string Text;
        public string Upgrade;    // if set, text only visible with that upgrade
        public string Clue;
        public List<string> Effects = new List<string>();
        public string Condition;
    }

    public sealed class TalkDef
    {
        public string Id;
        public string Text;
        public List<string> Reply = new List<string>();
        public List<string> Effects = new List<string>();
        public string Condition;
        public bool End;          // terminal decision -> outcome "choice:<id>"
    }

    public sealed class OutcomeDef
    {
        public List<string> Lines = new List<string>();
        public List<string> Effects = new List<string>();
        public string Result = "neutral";  // good, neutral, bad, death
        public string Scare;
        public string Receipt;
        public string Portrait;            // portrait variant while outcome plays
        public bool Paid = true;           // serving collects payment
        public string Condition;           // alternative outcomes: first passing wins
    }

    public sealed class CameraViewDef
    {
        public string Cam;      // cam1 (lot), cam2 (machine), cam3 (stockroom), cam4 (road), cam0 (booth)
        public string State;    // present, absent, other:<portrait>, extra:<portrait>, empty
        public string Note;     // shown as caption / observation
        public string Timestamp;
        public string Upgrade;  // only visible with this upgrade (bonus clue)
        public string Condition;
    }

    public sealed class EncounterDef
    {
        public string Id;
        public int Night;
        public int Time;          // minutes after 23:00
        public string Condition;
        public string Character;
        public string Name;
        public string Portrait;
        public bool Supernatural;
        public List<string> Lines = new List<string>();
        public string Request;    // product id, or "UNLISTED"
        public string RequestText;
        public int Pays;
        public string PaymentDesc;
        public float Patience = 75f;
        public string Music;
        public string ArriveScare;
        public List<string> ArriveEffects = new List<string>();
        public List<string> Rules = new List<string>();
        public List<InspectDef> Inspect = new List<InspectDef>();
        public List<TalkDef> Talk = new List<TalkDef>();
        public List<CameraViewDef> Cameras = new List<CameraViewDef>();
        public Dictionary<string, List<OutcomeDef>> Outcomes = new Dictionary<string, List<OutcomeDef>>();
        public bool NoReflection;
        public bool ReflectionLags;
    }

    public sealed class EventDef
    {
        public int Time;
        public string Type;      // phone, machine, scare, page, knock, blackout, investigate, clue, music, wrongtime, effects
        public string Condition;
        public string Id;
        public string Speaker;
        public List<string> Lines = new List<string>();
        public List<string> Effects = new List<string>();
        public float Duration;
        public List<string> OnOpen = new List<string>();     // knock: effects if panel opened
        public string OpenResult;                            // knock: result if panel opened
        public List<string> OpenLines = new List<string>();
        public List<string> OnServe = new List<string>();    // wrongtime: effects if dispensing
    }

    public sealed class HotspotDef
    {
        public string Id;
        public string Label;
        public float X, Y, W, H;    // normalized rect in the stockroom image
        public string Text;
        public string Clue;
        public string Condition;
        public List<string> Effects = new List<string>();
        public bool Required;
    }

    public sealed class NightDef
    {
        public int Night;
        public string Title;
        public string Subtitle;
        public string Weather;      // clear, fog, rain, storm, margin
        public string Music;
        public string MarginMusic;
        public int Tier = 1;        // horror tier 1..4
        public int Wage = 1500;
        public string Briefing;
        public List<string> PhoneCall = new List<string>();
        public string PhoneSpeaker = "District Office";
        public List<EventDef> Events = new List<EventDef>();
        public List<HotspotDef> Investigation = new List<HotspotDef>();
        public string InvestigationIntro;
        public string EndCard;
    }

    public sealed class EndingDef
    {
        public string Id;
        public string Title;
        public string Condition;
        public int Priority;
        public List<string> Lines = new List<string>();
        public string Music;
        public string Image;
    }
}
