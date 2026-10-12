// Conquer.Runtime — Trade state machine and session.
// Original: MapGroupKernel/SquareDeal.h, SquareDeal.cpp CSquareDeal

namespace Conquer.Runtime;

/// <summary>
/// A 2-phase secure trading session between two players.
/// <para><b>Original:</b> MapGroupKernel/SquareDeal.cpp CSquareDeal</para>
/// </summary>
public sealed class TradeSession
{
    public const int MaxTradeItems = 10;
    public const int MaxTradeDistance = 18; // CELLS_PER_VIEW

    public Player Player1 { get; }
    public Player Player2 { get; }

    public List<Item> StagedItems1 { get; } = new(MaxTradeItems);
    public List<Item> StagedItems2 { get; } = new(MaxTradeItems);

    public uint StagedMoney1 { get; private set; }
    public uint StagedMoney2 { get; private set; }

    public bool Confirmed1 { get; private set; }
    public bool Confirmed2 { get; private set; }

    public bool IsCompleted { get; private set; }
    public bool IsAborted { get; private set; }

    public TradeSession(Player player1, Player player2)
    {
        Player1 = player1;
        Player2 = player2;
    }

    public bool ContainsPlayer(uint playerId) => Player1.Id == playerId || Player2.Id == playerId;

    public Player GetOtherPlayer(uint playerId) => Player1.Id == playerId ? Player2 : Player1;

    public bool TryAddItem(Player player, Item item)
    {
        if (IsCompleted || IsAborted) return false;

        bool isP1 = player.Id == Player1.Id;
        var list = isP1 ? StagedItems1 : StagedItems2;

        if (list.Count >= MaxTradeItems) return false;
        if (list.Any(i => i.Id == item.Id)) return false;

        // Verify player actually has this item in package
        if (!player.Package.Contains(item.Id)) return false;

        list.Add(item);
        ResetConfirmations();
        return true;
    }

    public bool TryAddMoney(Player player, uint amount)
    {
        if (IsCompleted || IsAborted) return false;
        if (amount > player.Money) return false;

        if (player.Id == Player1.Id)
        {
            StagedMoney1 = amount;
        }
        else if (player.Id == Player2.Id)
        {
            StagedMoney2 = amount;
        }
        else
        {
            return false;
        }

        ResetConfirmations();
        return true;
    }

    public void Confirm(Player player)
    {
        if (IsCompleted || IsAborted) return;

        if (player.Id == Player1.Id)
        {
            Confirmed1 = true;
        }
        else if (player.Id == Player2.Id)
        {
            Confirmed2 = true;
        }
    }

    private void ResetConfirmations()
    {
        Confirmed1 = false;
        Confirmed2 = false;
    }

    public bool BothConfirmed => Confirmed1 && Confirmed2;

    public IReadOnlyList<Item> GetStagedItems(uint playerId) =>
        playerId == Player1.Id ? StagedItems1 : StagedItems2;

    public uint GetStagedMoney(uint playerId) =>
        playerId == Player1.Id ? StagedMoney1 : StagedMoney2;

    /// <summary>
    /// Executes atomic swap of staged items and money between Player 1 and Player 2.
    /// Returns true if successful, false if preconditions failed.
    /// </summary>
    public bool TryCommit()
    {
        if (IsCompleted || IsAborted) return false;
        if (!BothConfirmed) return false;

        // Pre-condition 1: Both alive and on same map within range
        if (!Player1.IsAlive || !Player2.IsAlive || Player1.MapId != Player2.MapId)
        {
            Abort();
            return false;
        }

        int dx = Math.Abs(Player1.PosX - Player2.PosX);
        int dy = Math.Abs(Player1.PosY - Player2.PosY);
        if (dx > MaxTradeDistance || dy > MaxTradeDistance)
        {
            Abort();
            return false;
        }

        // Pre-condition 2: Items still exist in backpack
        foreach (var item in StagedItems1)
        {
            if (!Player1.Package.Contains(item.Id))
            {
                Abort();
                return false;
            }
        }
        foreach (var item in StagedItems2)
        {
            if (!Player2.Package.Contains(item.Id))
            {
                Abort();
                return false;
            }
        }

        // Pre-condition 3: Money still available
        if (Player1.Money < StagedMoney1 || Player2.Money < StagedMoney2)
        {
            Abort();
            return false;
        }

        // Pre-condition 4: Backpack space
        int p1RemainingSlots = InventoryPackage.MaxCapacity - (Player1.Package.Count - StagedItems1.Count);
        if (p1RemainingSlots < StagedItems2.Count)
        {
            Abort();
            return false;
        }

        int p2RemainingSlots = InventoryPackage.MaxCapacity - (Player2.Package.Count - StagedItems2.Count);
        if (p2RemainingSlots < StagedItems1.Count)
        {
            Abort();
            return false;
        }

        // --- ATOMIC EXECUTION ---
        // 1. Remove staged items from owners
        foreach (var item in StagedItems1)
        {
            Player1.Package.Remove(item.Id);
        }
        foreach (var item in StagedItems2)
        {
            Player2.Package.Remove(item.Id);
        }

        // 2. Add staged items to recipients
        foreach (var item in StagedItems1)
        {
            Player2.Package.TryAdd(item);
        }
        foreach (var item in StagedItems2)
        {
            Player1.Package.TryAdd(item);
        }

        // 3. Swap money
        Player1.Money -= StagedMoney1;
        Player2.Money += StagedMoney1;

        Player2.Money -= StagedMoney2;
        Player1.Money += StagedMoney2;

        IsCompleted = true;
        return true;
    }

    public void Abort()
    {
        if (IsCompleted) return;
        IsAborted = true;
    }
}

/// <summary>
/// Active trade sessions registry.
/// </summary>
public sealed class TradeManager
{
    private readonly Dictionary<uint, TradeSession> _activeTrades = new();

    public TradeSession? GetSession(uint playerId) => _activeTrades.GetValueOrDefault(playerId);

    public TradeSession? StartTrade(Player p1, Player p2)
    {
        // Neither player can already be trading
        if (_activeTrades.ContainsKey(p1.Id) || _activeTrades.ContainsKey(p2.Id))
        {
            return null;
        }

        var session = new TradeSession(p1, p2);
        _activeTrades[p1.Id] = session;
        _activeTrades[p2.Id] = session;
        return session;
    }

    public void EndTrade(TradeSession session)
    {
        _activeTrades.Remove(session.Player1.Id);
        _activeTrades.Remove(session.Player2.Id);
    }
}
