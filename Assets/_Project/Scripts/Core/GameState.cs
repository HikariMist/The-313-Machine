using System;
using System.Collections.Generic;
using System.Linq;

namespace HikariForge.M313.Core
{
    public sealed class MachineSlot
    {
        public string Code;       // A1..D4, E1 (machine-only row)
        public string ProductId;  // null = empty
        public int Count;
    }

    public sealed class TransactionRecord
    {
        public int Night;
        public int Time;
        public string Customer;
        public string ProductId;
        public int Amount;
        public string Receipt;
    }

    public sealed class DecisionRecord
    {
        public int Night;
        public string Encounter;
        public string Action;   // serve:WATER, refuse, choice:x, timeout, code:1013
        public string Result;
    }

    /// <summary>Complete persistent campaign state. Everything here is saved.</summary>
    public sealed class GameState
    {
        public const int SaveVersion = 2;   // 2: upgrade levels (v1 saves migrate)
        public const int Rows = 4, Cols = 4;
        public const int BaseCapacity = 4, ExpandedCapacity = 8;
        public const int StartingMoney = 2500;

        public int Cycle = 1;       // the campaign loops: each ending starts a new cycle
        public readonly HashSet<string> EndingsSeen = new HashSet<string>();
        public int Night = 1;
        public int NightsCompleted;
        public int Money = StartingMoney;
        public int Instability;     // 0..100, per night
        public int Exposure;        // accumulated supernatural exposure across the campaign
        public int Bond;            // how tightly the machine holds the attendant
        public string EndingReached;
        public string CheckpointPhase = "prep";  // prep or shift

        public readonly Dictionary<string, int> Stockroom = new Dictionary<string, int>();
        public readonly List<MachineSlot> Slots = new List<MachineSlot>();
        /// <summary>Ids of upgrades owned at level 1 or higher (kept in sync with UpgradeLevels).</summary>
        public readonly HashSet<string> Upgrades = new HashSet<string>();
        public readonly Dictionary<string, int> UpgradeLevels = new Dictionary<string, int>();
        public readonly Dictionary<string, int> Flags = new Dictionary<string, int>();
        public readonly HashSet<string> Clues = new HashSet<string>();
        public readonly HashSet<string> UnlockedPages = new HashSet<string>();
        public readonly List<DecisionRecord> Decisions = new List<DecisionRecord>();
        public readonly List<TransactionRecord> Transactions = new List<TransactionRecord>();

        public int TotalServed, TotalRefused, TotalEarned;

        public GameState()
        {
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    Slots.Add(new MachineSlot { Code = $"{(char)('A' + r)}{c + 1}" });
            Slots.Add(new MachineSlot { Code = "E1" });  // the row the company says does not exist
        }

        public static GameState NewGame()
        {
            var s = new GameState();
            // The previous attendant left the machine partly stocked.
            s.Load("A1", "COLA", 3);
            s.Load("A2", "WATER", 3);
            s.Load("A3", "COFFEE", 3);
            s.Load("B1", "CHIPS", 2);
            s.Load("B2", "CHOCO", 2);
            s.Stockroom["COFFEE"] = 4;
            s.Stockroom["WATER"] = 2;
            s.Stockroom["ENERGY"] = 3;
            s.Stockroom["GUM"] = 3;
            s.Stockroom["BATTERY"] = 2;
            s.Stockroom["PEANUTS"] = 2;
            return s;
        }

        private void Load(string code, string product, int count)
        {
            var slot = Slot(code);
            slot.ProductId = product;
            slot.Count = count;
        }

        public int Level(string upgradeId) =>
            UpgradeLevels.TryGetValue(upgradeId, out var l) ? l : (Upgrades.Contains(upgradeId) ? 1 : 0);

        public void SetLevel(string upgradeId, int level)
        {
            UpgradeLevels[upgradeId] = level;
            if (level > 0) Upgrades.Add(upgradeId); else Upgrades.Remove(upgradeId);
        }

        // Upgrade effect tables, indexed by level 0..3. Balance lives here and in upgrades.json (prices).
        public static readonly int[] StorageCapacity = { 4, 5, 6, 8 };
        public static readonly float[] LightingInstability = { 1f, 0.85f, 0.7f, 0.6f };

        public int SlotCapacity => StorageCapacity[Math.Min(3, Level(UpgradeIds.ExpandedStorage))];

        public MachineSlot Slot(string code) => Slots.FirstOrDefault(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));

        public MachineSlot SlotWith(string productId) => Slots.FirstOrDefault(s => s.ProductId == productId && s.Count > 0);

        public int MachineCount(string productId) => Slots.Where(s => s.ProductId == productId).Sum(s => s.Count);

        public int StockroomCount(string productId) => Stockroom.TryGetValue(productId, out var n) ? n : 0;

        public int Flag(string name) => Flags.TryGetValue(name, out var v) ? v : 0;

        public void SetFlag(string name, int value) => Flags[name] = value;

        public void AddFlag(string name, int delta) => Flags[name] = Flag(name) + delta;

        public int KeyClueCount(ContentDatabase db) => Clues.Count(c => db.Clues.TryGetValue(c, out var d) && d.Key);

        public DecisionRecord LastDecision(string encounterId)
        {
            for (int i = Decisions.Count - 1; i >= 0; i--)
                if (Decisions[i].Encounter == encounterId) return Decisions[i];
            return null;
        }

        public void AddInstability(int amount)
        {
            if (amount > 0) amount = (int)Math.Ceiling(amount * LightingInstability[Math.Min(3, Level(UpgradeIds.Lighting))]);
            Instability = Math.Max(0, Math.Min(100, Instability + amount));
            if (amount > 0) Exposure += amount;
        }

        // ---------------- Inventory ----------------

        /// <summary>Buys stock from the supplier into the stockroom.</summary>
        public bool BuyStock(ProductDef p, int quantity)
        {
            if (p == null || !p.Purchasable || quantity <= 0) return false;
            int cost = p.Cost * quantity;
            if (Money < cost) return false;
            Money -= cost;
            Stockroom[p.Id] = StockroomCount(p.Id) + quantity;
            return true;
        }

        /// <summary>Loads a product from the stockroom into a slot, filling to capacity. Returns units moved.</summary>
        public int LoadSlot(string code, string productId)
        {
            var slot = Slot(code);
            if (slot == null || slot.Code == "E1") return 0;
            if (slot.ProductId != null && slot.ProductId != productId && slot.Count > 0)
            {
                // Swap out: return current units to the stockroom.
                Stockroom[slot.ProductId] = StockroomCount(slot.ProductId) + slot.Count;
                slot.Count = 0;
            }
            slot.ProductId = productId;
            int space = SlotCapacity - slot.Count;
            int available = StockroomCount(productId);
            int moved = Math.Max(0, Math.Min(space, available));
            slot.Count += moved;
            Stockroom[productId] = available - moved;
            if (slot.Count == 0) slot.ProductId = null;
            return moved;
        }

        public void UnloadSlot(string code)
        {
            var slot = Slot(code);
            if (slot == null || slot.ProductId == null || slot.Code == "E1") return;
            Stockroom[slot.ProductId] = StockroomCount(slot.ProductId) + slot.Count;
            slot.ProductId = null;
            slot.Count = 0;
        }

        /// <summary>Tops up every loaded slot, then fills empty slots with stockroom products.</summary>
        public int AutoStock(ContentDatabase db)
        {
            int moved = 0;
            foreach (var s in Slots.Where(s => s.Code != "E1" && s.ProductId != null))
                moved += LoadSlot(s.Code, s.ProductId);
            foreach (var s in Slots.Where(s => s.Code != "E1" && s.ProductId == null))
            {
                var candidate = db.ProductList
                    .Where(p => !p.Unusual && StockroomCount(p.Id) > 0 && MachineCount(p.Id) == 0)
                    .Select(p => p.Id).FirstOrDefault();
                if (candidate == null) break;
                moved += LoadSlot(s.Code, candidate);
            }
            return moved;
        }

        /// <summary>The machine places an item in the hidden row on its own.</summary>
        public void MachineProduces(string productId, int count = 1)
        {
            var e = Slot("E1");
            if (e.ProductId != productId) { e.ProductId = productId; e.Count = 0; }
            e.Count = Math.Min(9, e.Count + count);
        }

        public bool TakeFromSlot(string code, out string productId)
        {
            productId = null;
            var slot = Slot(code);
            if (slot == null || slot.ProductId == null || slot.Count <= 0) return false;
            productId = slot.ProductId;
            slot.Count--;
            if (slot.Count == 0 && slot.Code == "E1") slot.ProductId = null;
            return true;
        }

        /// <summary>Price of the next level, or -1 when the upgrade is maxed.</summary>
        public int NextUpgradePrice(UpgradeDef u)
        {
            int lv = Level(u.Id);
            return lv < u.MaxLevel ? u.Levels[lv].Price : -1;
        }

        /// <summary>Buys the next level. Never overspends, never exceeds the max level.</summary>
        public bool BuyUpgrade(UpgradeDef u)
        {
            if (u == null) return false;
            int price = NextUpgradePrice(u);
            if (price < 0 || Money < price) return false;
            Money -= price;
            SetLevel(u.Id, Level(u.Id) + 1);
            return true;
        }

        public GameState Clone() => SaveSerializer.Deserialize(SaveSerializer.Serialize(this));
    }

    public static class UpgradeIds
    {
        public const string ExpandedStorage = "storage";
        public const string FastRestock = "restock";
        public const string Monitor = "monitor";
        public const string Lighting = "lighting";
        public const string IdTool = "idtool";
        public const string PowerBackup = "power";
    }
}
