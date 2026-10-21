using System.Collections.Generic;

namespace HikariForge.M313.Core
{
    public enum PresEventType
    {
        Arrive, Leave, Dispense, DispenseFailed, Receipt, Scare, Ambient, Hallucination, Music, Sfx,
        MachineMessage, Notice, Clue, ClueLost, HandbookChanged, Produced, Stat, Margin, Phone,
        KnockStart, KnockEnd, BlackoutStart, BlackoutEnd, WrongTimeStart, WrongTimeEnd, Jam, JamCleared,
        Investigation, Sunrise, Failed, Ending, Consumed, Restocked, Reply, OrderProgress
    }

    /// <summary>A presentation event emitted by the simulation for the Unity layer to render.</summary>
    public sealed class PresEvent
    {
        public PresEventType Type;
        public string Id;
        public string Text;
        public int Value;
        public List<string> Lines;

        public override string ToString() => $"{Type}:{Id}:{Text}:{Value}";
    }
}
