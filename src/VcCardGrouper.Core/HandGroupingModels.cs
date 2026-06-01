using System.Collections.Generic;

namespace VcCardGrouper.Core;

public sealed class HandCardSnapshot
{
    public HandCardSnapshot(string stableId, int cost, bool isWild, bool canPlay)
    {
        StableId = stableId ?? string.Empty;
        Cost = cost;
        IsWild = isWild;
        CanPlay = canPlay;
    }

    public string StableId { get; }
    public int Cost { get; }
    public bool IsWild { get; }
    public bool CanPlay { get; }
}

public sealed class HandGroupingRequest
{
    public HandGroupingRequest(
        IReadOnlyList<int> stableCosts,
        IReadOnlyList<HandCardSnapshot> cards,
        int? desiredCost,
        int enableWhenHandCountAtLeast)
    {
        StableCosts = stableCosts;
        Cards = cards;
        DesiredCost = desiredCost;
        EnableWhenHandCountAtLeast = enableWhenHandCountAtLeast;
    }

    public IReadOnlyList<int> StableCosts { get; }
    public IReadOnlyList<HandCardSnapshot> Cards { get; }
    public int? DesiredCost { get; }
    public int EnableWhenHandCountAtLeast { get; }
}

public sealed class CostGroupState
{
    public CostGroupState(int cost, int count, bool isActive)
    {
        Cost = cost;
        Count = count;
        IsActive = isActive;
    }

    public int Cost { get; }
    public int Count { get; }
    public bool IsActive { get; }
}

public sealed class HandGroupingPlan
{
    public HandGroupingPlan(
        bool groupingEnabled,
        int? activeCost,
        int wildCount,
        bool comboBlocked,
        string statusText,
        IReadOnlyList<CostGroupState> groups,
        IReadOnlySet<string> visibleCardIds)
    {
        GroupingEnabled = groupingEnabled;
        ActiveCost = activeCost;
        WildCount = wildCount;
        ComboBlocked = comboBlocked;
        StatusText = statusText ?? string.Empty;
        Groups = groups;
        VisibleCardIds = visibleCardIds;
    }

    public bool GroupingEnabled { get; }
    public int? ActiveCost { get; }
    public int WildCount { get; }
    public bool ComboBlocked { get; }
    public string StatusText { get; }
    public IReadOnlyList<CostGroupState> Groups { get; }
    public IReadOnlySet<string> VisibleCardIds { get; }
}

