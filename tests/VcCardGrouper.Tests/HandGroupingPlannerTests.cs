using System.Collections.Generic;
using System.Linq;
using VcCardGrouper.Core;
using Xunit;

namespace VcCardGrouper.Tests;

public sealed class HandGroupingPlannerTests
{
    [Fact]
    public void KeepsStableCostGroupsEvenWhenAGroupHasNoCards()
    {
        HandGroupingPlan plan = HandGroupingPlanner.CreatePlan(new HandGroupingRequest(
            new[] { 0, 1, 2, 3, 4, 5 },
            new[]
            {
                new HandCardSnapshot("c0", 0, false, false),
                new HandCardSnapshot("c2", 2, false, false),
                new HandCardSnapshot("w0", 0, true, true),
            },
            desiredCost: 4,
            enableWhenHandCountAtLeast: 1));

        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, plan.Groups.Select(group => group.Cost));
        Assert.Contains(plan.Groups, group => group.Cost == 4 && group.Count == 0 && group.IsActive);
    }

    [Fact]
    public void ShowsWildCardsWhenDesiredCostGroupIsEmpty()
    {
        HandGroupingPlan plan = HandGroupingPlanner.CreatePlan(new HandGroupingRequest(
            new[] { 0, 1, 2, 3, 4 },
            new[]
            {
                new HandCardSnapshot("c2", 2, false, false),
                new HandCardSnapshot("w0", 0, true, true),
                new HandCardSnapshot("w1", 0, true, true),
            },
            desiredCost: 4,
            enableWhenHandCountAtLeast: 1));

        Assert.True(plan.ComboBlocked);
        Assert.Equal("无法连击", plan.StatusText);
        Assert.Equal(new[] { "w0", "w1" }, plan.VisibleCardIds.OrderBy(id => id));
    }

    [Fact]
    public void ExpandsDesiredCostAndWildCardsOnlyWhenGroupingIsEnabled()
    {
        HandGroupingPlan plan = HandGroupingPlanner.CreatePlan(new HandGroupingRequest(
            new[] { 0, 1, 2, 3 },
            new[]
            {
                new HandCardSnapshot("c1", 1, false, true),
                new HandCardSnapshot("c2", 2, false, false),
                new HandCardSnapshot("c3", 3, false, false),
                new HandCardSnapshot("w0", 0, true, true),
            },
            desiredCost: 2,
            enableWhenHandCountAtLeast: 3));

        Assert.True(plan.GroupingEnabled);
        Assert.Equal(2, plan.ActiveCost);
        Assert.Equal(new[] { "c2", "w0" }, plan.VisibleCardIds.OrderBy(id => id));
    }

    [Fact]
    public void ShowsAllCardsBelowThreshold()
    {
        HandGroupingPlan plan = HandGroupingPlanner.CreatePlan(new HandGroupingRequest(
            new[] { 0, 1, 2, 3 },
            new[]
            {
                new HandCardSnapshot("c1", 1, false, true),
                new HandCardSnapshot("c2", 2, false, false),
                new HandCardSnapshot("w0", 0, true, true),
            },
            desiredCost: 2,
            enableWhenHandCountAtLeast: 20));

        Assert.False(plan.GroupingEnabled);
        Assert.Equal(new[] { "c1", "c2", "w0" }, plan.VisibleCardIds.OrderBy(id => id));
    }
}

