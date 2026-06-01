using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using Il2CppInterop.Runtime.Attributes;
using Nosebleed.Pancake.Models;
using Nosebleed.Pancake.View;
using UnityEngine;
using UnityEngine.UI;
using UObject = UnityEngine.Object;

namespace VcCardGrouper;

internal static class CardFaceReplacement
{
    private static readonly Dictionary<string, string> ReplacementFiles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Sprite> LoadedSprites = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ExportedOriginals = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] PreferredImageNames =
    {
        "_cardImage",
        "_image",
        "CardImage",
        "CardArt",
        "Artwork",
        "Art",
        "Illustration",
        "Portrait"
    };

    private static ConfigEntry<bool> _enableReplacement;
    private static ConfigEntry<bool> _exportOriginalFaces;
    private static ConfigEntry<string> _replacementDirectoryName;
    private static ConfigEntry<string> _exportDirectoryName;
    private static ConfigEntry<float> _scanIntervalSeconds;
    private static float _scanTimer;
    private static string _replacementDirectory;
    private static string _exportDirectory;
    private static float _lastReplacementFileScanTime = float.NegativeInfinity;

    public static void Configure(ConfigFile config)
    {
        _enableReplacement = config.Bind(
            "CardFaces",
            "EnableReplacement",
            true,
            "Replace card face sprites from local PNG files.");

        _exportOriginalFaces = config.Bind(
            "CardFaces",
            "ExportOriginalFaces",
            true,
            "Export original visible card face sprites as PNG files.");

        _replacementDirectoryName = config.Bind(
            "CardFaces",
            "ReplacementDirectory",
            "VcCardGrouper/card-faces",
            "Directory under BepInEx/plugins containing replacement card face PNG files.");

        _exportDirectoryName = config.Bind(
            "CardFaces",
            "ExportDirectory",
            "VcCardGrouper/exported-card-faces",
            "Directory under BepInEx/plugins where original card face PNG files are exported.");

        _scanIntervalSeconds = config.Bind(
            "CardFaces",
            "ScanIntervalSeconds",
            0.35f,
            "How often visible cards are scanned for face export/replacement.");

        _replacementDirectory = Path.Combine(Paths.PluginPath, _replacementDirectoryName.Value);
        _exportDirectory = Path.Combine(Paths.PluginPath, _exportDirectoryName.Value);
        Directory.CreateDirectory(_replacementDirectory);
        Directory.CreateDirectory(_exportDirectory);
    }

    [HideFromIl2Cpp]
    public static void ScanVisibleCards(PlayerModel player)
    {
        _scanTimer -= Time.unscaledDeltaTime;
        if (_scanTimer > 0f)
        {
            return;
        }

        _scanTimer = Mathf.Clamp(_scanIntervalSeconds?.Value ?? 0.35f, 0.1f, 5f);

        if (player == null || player.HandPile?.CardPile == null)
        {
            return;
        }

        RefreshReplacementFileMap();

        CardPileModel cardPile = player.HandPile.CardPile;
        for (int i = 0; i < cardPile.Count; i++)
        {
            if (!cardPile.TryPeekIndex(i, out CardModel card) || card == null)
            {
                continue;
            }

            CardView cardView = null;
            try
            {
                cardView = card.CardView;
            }
            catch
            {
            }

            if (cardView != null)
            {
                ProcessCardView(cardView, card);
            }
        }
    }

    [HideFromIl2Cpp]
    private static void ProcessCardView(CardView cardView, CardModel card)
    {
        Image artImage = FindCardArtImage(cardView);
        if (artImage == null || artImage.sprite == null)
        {
            return;
        }

        string configName = CardRules.GetCardConfigName(card);
        string displayName = CardRules.GetDisplayName(card);
        if (string.IsNullOrEmpty(configName) && string.IsNullOrEmpty(displayName))
        {
            return;
        }

        ExportOriginalFaceIfNeeded(artImage.sprite, configName, displayName);

        if (_enableReplacement?.Value != true)
        {
            return;
        }

        Sprite replacement = TryGetReplacementSprite(configName, displayName, artImage.sprite);
        if (replacement == null || artImage.sprite == replacement)
        {
            return;
        }

        artImage.sprite = replacement;
        artImage.preserveAspect = true;
    }

    [HideFromIl2Cpp]
    private static Image FindCardArtImage(CardView cardView)
    {
        Transform root = cardView?.gameObject?.transform;
        if (root == null)
        {
            return null;
        }

        foreach (string name in PreferredImageNames)
        {
            Transform child = FindNamedChild(root, name, 12);
            Image image = child?.GetComponent<Image>();
            if (IsLikelyCardArtImage(image))
            {
                return image;
            }
        }

        Image[] images = root.GetComponentsInChildren<Image>(true);
        Image best = null;
        float bestArea = 0f;
        foreach (Image image in images)
        {
            if (!IsLikelyCardArtImage(image))
            {
                continue;
            }

            float area = GetImageArea(image);
            if (area > bestArea)
            {
                best = image;
                bestArea = area;
            }
        }

        return best;
    }

    [HideFromIl2Cpp]
    private static bool IsLikelyCardArtImage(Image image)
    {
        if (image == null || image.sprite == null || image.type == Image.Type.Sliced)
        {
            return false;
        }

        string name = image.gameObject.name?.ToLowerInvariant() ?? string.Empty;
        if (name.Contains("cost") || name.Contains("mana") || name.Contains("combo") || name.Contains("frame")
            || name.Contains("border") || name.Contains("lock") || name.Contains("badge") || name.Contains("text")
            || name.Contains("backgroundelement") || name.Contains("element"))
        {
            return false;
        }

        return GetImageArea(image) > 1024f;
    }

    [HideFromIl2Cpp]
    private static float GetImageArea(Image image)
    {
        RectTransform rectTransform = image.transform as RectTransform;
        if (rectTransform != null)
        {
            Rect rect = rectTransform.rect;
            float area = Mathf.Abs(rect.width * rect.height);
            if (area > 0f)
            {
                return area;
            }
        }

        Rect spriteRect = image.sprite.rect;
        return Mathf.Abs(spriteRect.width * spriteRect.height);
    }

    [HideFromIl2Cpp]
    private static void RefreshReplacementFileMap()
    {
        if (Time.unscaledTime - _lastReplacementFileScanTime < 2f)
        {
            return;
        }

        _lastReplacementFileScanTime = Time.unscaledTime;
        ReplacementFiles.Clear();

        if (!Directory.Exists(_replacementDirectory))
        {
            Directory.CreateDirectory(_replacementDirectory);
            return;
        }

        foreach (string file in Directory.GetFiles(_replacementDirectory, "*.png", SearchOption.TopDirectoryOnly))
        {
            string key = NormalizeKey(Path.GetFileNameWithoutExtension(file));
            if (!string.IsNullOrEmpty(key))
            {
                ReplacementFiles[key] = file;
            }
        }
    }

    [HideFromIl2Cpp]
    private static Sprite TryGetReplacementSprite(string configName, string displayName, Sprite originalSprite)
    {
        string file = FindReplacementFile(configName, displayName);
        if (string.IsNullOrEmpty(file))
        {
            return null;
        }

        if (LoadedSprites.TryGetValue(file, out Sprite sprite) && sprite != null)
        {
            return sprite;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(file);
            Texture2D texture = new(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes))
            {
                UObject.Destroy(texture);
                return null;
            }

            texture.name = Path.GetFileNameWithoutExtension(file);
            float pixelsPerUnit = originalSprite != null ? originalSprite.pixelsPerUnit : 100f;
            Vector2 pivot = originalSprite != null
                ? new Vector2(originalSprite.pivot.x / originalSprite.rect.width, originalSprite.pivot.y / originalSprite.rect.height)
                : new Vector2(0.5f, 0.5f);
            Sprite replacement = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                pivot,
                pixelsPerUnit,
                0,
                SpriteMeshType.FullRect,
                originalSprite != null ? originalSprite.border : Vector4.zero);
            replacement.name = texture.name;
            LoadedSprites[file] = replacement;
            Plugin.Logger?.LogInfo($"Loaded card face replacement: {Path.GetFileName(file)}");
            return replacement;
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogWarning($"Unable to load card face replacement '{file}': {ex.Message}");
            return null;
        }
    }

    [HideFromIl2Cpp]
    private static string FindReplacementFile(string configName, string displayName)
    {
        foreach (string key in BuildCandidateKeys(configName, displayName))
        {
            if (ReplacementFiles.TryGetValue(key, out string file))
            {
                return file;
            }
        }

        return null;
    }

    [HideFromIl2Cpp]
    private static IEnumerable<string> BuildCandidateKeys(string configName, string displayName)
    {
        yield return NormalizeKey(configName);
        yield return NormalizeKey(SafeFileName(configName));
        yield return NormalizeKey(displayName);
        yield return NormalizeKey(SafeFileName(displayName));
    }

    [HideFromIl2Cpp]
    private static void ExportOriginalFaceIfNeeded(Sprite sprite, string configName, string displayName)
    {
        if (_exportOriginalFaces?.Value != true || sprite == null)
        {
            return;
        }

        string fileNameBase = !string.IsNullOrEmpty(configName) ? configName : displayName;
        string safeName = SafeFileName(fileNameBase);
        if (string.IsNullOrEmpty(safeName) || ExportedOriginals.Contains(safeName))
        {
            return;
        }

        string path = Path.Combine(_exportDirectory, safeName + ".png");
        if (File.Exists(path))
        {
            ExportedOriginals.Add(safeName);
            return;
        }

        try
        {
            Texture2D copy = CopySpriteTexture(sprite);
            if (copy == null)
            {
                return;
            }

            File.WriteAllBytes(path, ImageConversion.EncodeToPNG(copy));
            UObject.Destroy(copy);
            ExportedOriginals.Add(safeName);
            Plugin.Logger?.LogInfo($"Exported original card face: {safeName}.png");
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogWarning($"Unable to export original card face '{safeName}': {ex.Message}");
        }
    }

    [HideFromIl2Cpp]
    private static Texture2D CopySpriteTexture(Sprite sprite)
    {
        Texture2D source = sprite.texture;
        Rect textureRect = sprite.textureRect;
        int x = Mathf.RoundToInt(textureRect.x);
        int y = Mathf.RoundToInt(textureRect.y);
        int width = Mathf.RoundToInt(textureRect.width);
        int height = Mathf.RoundToInt(textureRect.height);

        Texture2D output = new(width, height, TextureFormat.RGBA32, false);
        if (source.isReadable)
        {
            output.SetPixels(source.GetPixels(x, y, width, height));
            output.Apply();
            return output;
        }

        RenderTexture previous = RenderTexture.active;
        RenderTexture renderTexture = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        try
        {
            Graphics.Blit(source, renderTexture);
            RenderTexture.active = renderTexture;
            output.ReadPixels(new Rect(x, y, width, height), 0, 0);
            output.Apply();
            return output;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTexture);
        }
    }

    [HideFromIl2Cpp]
    private static Transform FindNamedChild(Transform root, string name, int maxDepth)
    {
        if (root == null || maxDepth < 0)
        {
            return null;
        }

        if (string.Equals(root.name, name, StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindNamedChild(root.GetChild(i), name, maxDepth - 1);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static string NormalizeKey(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        StringBuilder builder = new(value.Length);
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    private static string SafeFileName(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        StringBuilder builder = new(value.Length);
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('_');
            }
        }

        return builder.ToString().Trim('_');
    }
}
