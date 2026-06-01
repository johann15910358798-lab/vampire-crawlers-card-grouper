using System.Collections.Generic;
using System.Linq;

namespace VcCardGrouper.Core;

public static class HandGroupingPlanner
{
    public static HandGroupingPlan CreatePlan(HandGroupingRequest request)
    {
        IReadOnlyList<HandCardSnapshot> cards = request.Cards ?? new List<HandCardSnapshot>();
        IReadOnlyList<int> stableCosts = NormalizeStableCosts(request.StableCosts, cards);
        bool groupingEnabled = cards.Count >= request.EnableWhenHandCountAtLeast;
        int? activeCost = request.DesiredCost ?? FindFirstPlayableNumericCost(cards) ?? FirstNumericCostWithCards(cards);

        Dictionary<int, int> countsByCost = CountNumericCards(cards);
        List<CostGroupState> groups = stableCosts
            .Select(cost => new CostGroupState(cost, countsByCost.TryGetValue(cost, out int count) ? count : 0, activeCost == cost))
            .ToList();

        int activeCount = activeCost.HasValue && countsByCost.TryGetValue(activeCost.Value, out int countForActive)
            ? countForActive
            : 0;
        int wildCount = cards.Count(card => card.IsWild);
        bool comboBlocked = groupingEnabled && activeCost.HasValue && activeCount == 0;

        HashSet<string> visibleCardIds = new();
        foreach (HandCardSnapshot card in cards)
        {
            if (!groupingEnabled || card.IsWild || (activeCost.HasValue && !card.IsWild && card.Cost == activeCost.Value))
            {
                visibleCardIds.Add(card.StableId);
            }
        }

        return new HandGroupingPlan(
            groupingEnabled,
            activeCost,
            wildCount,
            comboBlocked,
            comboBlocked ? "无法连击" : string.Empty,
            groups,
            visibleCardIds);
    }

    private static IReadOnlyList<int> NormalizeStableCosts(IReadOnlyList<int> stableCosts, IReadOnlyList<HandCardSnapshot> cards)
    {
        SortedSet<int> costs = new();
        if (stableCosts != null)
        {
            foreach (int cost in stableCosts)
            {
                if (cost >= 0)
                {
                    costs.Add(cost);
                }
            }
        }

        foreach (HandCardSnapshot card in cards)
        {
            if (!card.IsWild && card.Cost >= 0)
            {
                costs.Add(card.Cost);
            }
        }

        return costs.ToList();
    }

    private static Dictionary<int, int> CountNumericCards(IReadOnlyList<HandCardSnapshot> cards)
    {
        Dictionary<int, int> counts = new();
        foreach (HandCardSnapshot card in cards)
        {
            if (card.IsWild || card.Cost < 0)
            {
                continue;
            }

            counts[card.Cost] = counts.TryGetValue(card.Cost, out int count) ? count + 1 : 1;
        }

        return counts;
    }

    private static int? FindFirstPlayableNumericCost(IReadOnlyList<HandCardSnapshot> cards)
    {
        foreach (HandCardSnapshot card in cards)
        {
            if (!card.IsWild && card.CanPlay)
            {
                return card.Cost;
            }
        }

        return null;
    }

    private static int? FirstNumericCostWithCards(IReadOnlyList<HandCardSnapshot> cards)
    {
        int? firstCost = null;
        foreach (HandCardSnapshot card in cards)
        {
            if (card.IsWild)
            {
                continue;
            }

            if (!firstCost.HasValue || card.Cost < firstCost.Value)
            {
                firstCost = card.Cost;
            }
        }

        return firstCost;
    }
}

