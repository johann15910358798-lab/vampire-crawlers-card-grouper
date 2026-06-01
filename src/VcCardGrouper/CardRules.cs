using System;
using Nosebleed.Pancake.GameConfig;
using Nosebleed.Pancake.GameLogic;
using Nosebleed.Pancake.Models;

namespace VcCardGrouper;

internal static class CardRules
{
    public static bool IsWildCard(CardModel card)
    {
        try
        {
            CardCostType costType = card?.CardCostType;
            if (costType != null && (costType is WildCostType || costType.TryCast<WildCostType>() != null))
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogWarning($"Unable to read card cost type: {ex.Message}");
        }

        string configName = GetCardConfigName(card);
        if (string.IsNullOrEmpty(configName))
        {
            return false;
        }

        string[] parts = configName.Split('_');
        return parts.Length == 3 || (parts.Length >= 2 && string.Equals(parts[1], "E", StringComparison.OrdinalIgnoreCase));
    }

    public static int GetSortCost(CardModel card)
    {
        try
        {
            if (card != null && card.IsCardFreeToPlay())
            {
                return card.GetCardComboCost();
            }

            return card?.GetCardCostTypeManaCost() ?? int.MaxValue;
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogWarning($"Unable to read card mana cost: {ex.Message}");
            return int.MaxValue;
        }
    }

    public static bool CanPlay(PlayerModel player, CardModel card)
    {
        if (player == null || card == null || card.IsBroken)
        {
            return false;
        }

        try
        {
            return player.CanAffordCard(card);
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogWarning($"Unable to read playable state: {ex.Message}");
            return false;
        }
    }

    public static string GetStableCardId(CardModel card, int fallbackIndex)
    {
        if (card == null)
        {
            return $"slot-{fallbackIndex}";
        }

        try
        {
            string guid = card.Guid.ToGuid().ToString();
            if (!string.IsNullOrEmpty(guid) && guid != "00000000-0000-0000-0000-000000000000")
            {
                return guid;
            }
        }
        catch
        {
        }

        return $"{GetCardConfigName(card) ?? "card"}-{fallbackIndex}";
    }

    public static string GetCardConfigName(CardModel card)
    {
        string name = null;
        try
        {
            name = card?.CardView?._appliedCardConfig?.name;
        }
        catch
        {
        }

        if (string.IsNullOrEmpty(name))
        {
            try
            {
                name = card?.CardConfig?.name;
            }
            catch
            {
                return null;
            }
        }

        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        int cloneIndex = name.IndexOf("(Clone)", StringComparison.Ordinal);
        return cloneIndex > 0 ? name.Substring(0, cloneIndex).TrimEnd() : name;
    }

    public static string GetDisplayName(CardModel card)
    {
        try
        {
            if (!string.IsNullOrEmpty(card?.Name))
            {
                return card.Name;
            }
        }
        catch
        {
        }

        try
        {
            CardConfig config = card?.CardConfig;
            return config?.Name ?? config?.name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
