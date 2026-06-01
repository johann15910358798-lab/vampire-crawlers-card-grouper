using System;
using System.Collections.Generic;
using Nosebleed.Pancake.GameConfig;
using UnityEngine;

namespace VcCardGrouper;

internal static class CardCostCatalog
{
    private static readonly List<int> CachedCosts = new();
    private static bool _hasScanned;

    public static IReadOnlyList<int> GetStableNumericCosts(int fallbackMaxCost)
    {
        if (!_hasScanned)
        {
            ScanGameCardConfigs();
        }

        if (CachedCosts.Count > 0)
        {
            return CachedCosts;
        }

        List<int> fallback = new();
        for (int cost = 0; cost <= Math.Max(0, fallbackMaxCost); cost++)
        {
            fallback.Add(cost);
        }

        return fallback;
    }

    private static void ScanGameCardConfigs()
    {
        _hasScanned = true;
        SortedSet<int> costs = new();

        try
        {
            CardConfig[] configs = Resources.FindObjectsOfTypeAll<CardConfig>();
            foreach (CardConfig config in configs)
            {
                if (config == null || IsWildConfig(config))
                {
                    continue;
                }

                int cost = config.manaCost;
                if (cost >= 0)
                {
                    costs.Add(cost);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogWarning($"Unable to scan card config costs: {ex.Message}");
        }

        CachedCosts.Clear();
        CachedCosts.AddRange(costs);
        Plugin.Logger?.LogInfo($"Scanned {CachedCosts.Count} numeric card cost groups.");
    }

    private static bool IsWildConfig(CardConfig config)
    {
        string name = config?.name;
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        string[] parts = name.Split('_');
        return parts.Length == 3 || (parts.Length >= 2 && string.Equals(parts[1], "E", StringComparison.OrdinalIgnoreCase));
    }
}

