using System;
using System.Reflection;
using Nosebleed.Pancake.Models;

namespace VcCardGrouper;

internal static class ComboTargetReader
{
    private static readonly string[] PreferredMemberNames =
    {
        "NextComboCost",
        "NextCardComboCost",
        "CurrentComboTargetCost",
        "CurrentComboTargetManaCost",
        "ExpectedCardCost",
        "ExpectedManaCost",
        "RequiredCardCost",
        "RequiredManaCost",
        "TargetComboCost",
        "ComboTargetCost",
        "_nextComboCost",
        "_nextCardComboCost",
        "_currentComboTargetCost",
        "_currentComboTargetManaCost",
        "_expectedCardCost",
        "_expectedManaCost",
        "_requiredCardCost",
        "_requiredManaCost",
        "_targetComboCost",
        "_comboTargetCost"
    };

    public static int? TryReadDesiredCost(PlayerModel player)
    {
        if (player == null)
        {
            return null;
        }

        Type type = player.GetType();
        foreach (string memberName in PreferredMemberNames)
        {
            if (TryReadIntMember(player, type, memberName, out int value))
            {
                return value;
            }
        }

        return TryScanLikelyComboCostMember(player, type);
    }

    private static int? TryScanLikelyComboCostMember(object instance, Type type)
    {
        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        foreach (PropertyInfo property in type.GetProperties(flags))
        {
            string name = property.Name;
            if (!LooksLikeComboCostTarget(name) || !property.CanRead)
            {
                continue;
            }

            try
            {
                object raw = property.GetValue(instance);
                if (TryCoerceCost(raw, out int value))
                {
                    return value;
                }
            }
            catch
            {
            }
        }

        foreach (FieldInfo field in type.GetFields(flags))
        {
            string name = field.Name;
            if (!LooksLikeComboCostTarget(name))
            {
                continue;
            }

            try
            {
                object raw = field.GetValue(instance);
                if (TryCoerceCost(raw, out int value))
                {
                    return value;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static bool TryReadIntMember(object instance, Type type, string memberName, out int value)
    {
        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        PropertyInfo property = type.GetProperty(memberName, flags);
        if (property != null && property.CanRead)
        {
            try
            {
                object raw = property.GetValue(instance);
                if (TryCoerceCost(raw, out value))
                {
                    return true;
                }
            }
            catch
            {
            }
        }

        FieldInfo field = type.GetField(memberName, flags);
        if (field != null)
        {
            try
            {
                object raw = field.GetValue(instance);
                if (TryCoerceCost(raw, out value))
                {
                    return true;
                }
            }
            catch
            {
            }
        }

        value = 0;
        return false;
    }

    private static bool LooksLikeComboCostTarget(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        string lower = name.ToLowerInvariant();
        return lower.Contains("combo")
            && lower.Contains("cost")
            && (lower.Contains("next") || lower.Contains("target") || lower.Contains("expected") || lower.Contains("required") || lower.Contains("current"));
    }

    private static bool TryCoerceCost(object raw, out int value)
    {
        value = 0;
        if (raw == null)
        {
            return false;
        }

        try
        {
            value = Convert.ToInt32(raw);
            return value >= 0 && value <= 99;
        }
        catch
        {
            return false;
        }
    }
}

