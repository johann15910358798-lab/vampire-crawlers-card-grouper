using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Il2CppInterop.Runtime.Attributes;
using Nosebleed.Pancake.Modal;
using Nosebleed.Pancake.Models;
using Nosebleed.Pancake.View;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using VcCardGrouper.Core;

namespace VcCardGrouper;

public sealed class HandGroupingController : MonoBehaviour
{
    private const float ReferenceWidth = 1920f;
    private const float ReferenceHeight = 1080f;
    private const float DefaultGroupBarReferenceX = 420f;
    private const float DefaultGroupBarReferenceY = 844f;
    private const float GroupButtonWidth = 58f;
    private const float GroupButtonHeight = 34f;
    private const float GroupButtonGap = 8f;
    private const float WildButtonWidth = 72f;
    private const int LayoutRefreshFrames = 3;

    private static PlayerModel _player;
    private static ConfigEntry<bool> _enableGrouping;
    private static ConfigEntry<int> _enableWhenHandCountAtLeast;
    private static ConfigEntry<int> _fallbackMaxCost;
    private static ConfigEntry<float> _groupBarReferenceX;
    private static ConfigEntry<float> _groupBarReferenceY;
    private static ConfigEntry<float> _refreshIntervalSeconds;
    private static ConfigEntry<bool> _hideInactiveCards;
    private static ConfigEntry<bool> _useSetActiveForHiddenCards;
    private static ConfigEntry<bool> _showComboBlockedText;
    private static int? _manualActiveCost;
    private static bool _refreshRequested = true;
    private static CardSlotHolder _pendingLayoutRefreshCardGroup;
    private static int _pendingLayoutRefreshFrames;

    private HandGroupingPlan _latestPlan;
    private float _refreshTimer;
    private GUIStyle _groupStyle;
    private GUIStyle _activeGroupStyle;
    private GUIStyle _wildStyle;
    private GUIStyle _statusStyle;

    public HandGroupingController(IntPtr ptr) : base(ptr)
    {
    }

    [HideFromIl2Cpp]
    public static void Configure(ConfigFile config)
    {
        _enableGrouping = config.Bind(
            "HandGrouping",
            "Enable",
            true,
            "Enable hand grouping after the hand count reaches the configured threshold.");

        _enableWhenHandCountAtLeast = config.Bind(
            "HandGrouping",
            "EnableWhenHandCountAtLeast",
            20,
            "Enable grouping when the hand has this many cards or more.");

        _fallbackMaxCost = config.Bind(
            "HandGrouping",
            "FallbackMaxCost",
            10,
            "Fallback highest numeric cost if the game card library cannot be scanned.");

        _groupBarReferenceX = config.Bind(
            "HandGrouping",
            "GroupBarReferenceX",
            DefaultGroupBarReferenceX,
            "Group bar X in 1920x1080 reference coordinates.");

        _groupBarReferenceY = config.Bind(
            "HandGrouping",
            "GroupBarReferenceY",
            DefaultGroupBarReferenceY,
            "Group bar Y in 1920x1080 reference coordinates.");

        _refreshIntervalSeconds = config.Bind(
            "HandGrouping",
            "RefreshIntervalSeconds",
            0.15f,
            "How often the hand grouping state refreshes.");

        _hideInactiveCards = config.Bind(
            "HandGrouping",
            "HideInactiveCards",
            true,
            "Hide non-active numeric groups while keeping the current numeric group and W visible.");

        _useSetActiveForHiddenCards = config.Bind(
            "HandGrouping",
            "UseSetActiveForHiddenCards",
            false,
            "Use GameObject.SetActive(false) for hidden cards. More compact, but riskier than alpha/scale hiding.");

        _showComboBlockedText = config.Bind(
            "HandGrouping",
            "ShowComboBlockedText",
            true,
            "Show a small 'cannot combo' message when the active group has no card.");
    }

    [HideFromIl2Cpp]
    public static void SetPlayer(PlayerModel player)
    {
        _player = player;
        _refreshRequested = true;
    }

    [HideFromIl2Cpp]
    public static void ClearPlayer(PlayerModel player)
    {
        if (_player == player)
        {
            _player = null;
            _manualActiveCost = null;
            _refreshRequested = true;
        }
    }

    [HideFromIl2Cpp]
    public static void RequestRefresh()
    {
        _refreshRequested = true;
    }

    [HideFromIl2Cpp]
    public static void NotifyCardPlayed(bool wasPlayed)
    {
        if (!wasPlayed)
        {
            return;
        }

        _manualActiveCost = null;
        _refreshRequested = true;
    }

    private void Update()
    {
        CardFaceReplacement.ScanVisibleCards(_player);
        ProcessGroupingRefresh();
    }

    private void LateUpdate()
    {
        ProcessPendingLayoutRefresh();
    }

    private void OnGUI()
    {
        if (_latestPlan == null || !_latestPlan.GroupingEnabled || HasActiveModal())
        {
            return;
        }

        EnsureGuiStyles();
        DrawGroupBar(_latestPlan);
    }

    [HideFromIl2Cpp]
    private void ProcessGroupingRefresh()
    {
        _refreshTimer -= Time.unscaledDeltaTime;
        if (!_refreshRequested && _refreshTimer > 0f)
        {
            return;
        }

        _refreshRequested = false;
        _refreshTimer = Mathf.Clamp(_refreshIntervalSeconds?.Value ?? 0.15f, 0.05f, 2f);

        PlayerModel player = _player;
        if (!CanGroupHand(player))
        {
            RestoreAllSlots(player);
            _latestPlan = null;
            return;
        }

        if (!IsHandViewReady(player))
        {
            _refreshRequested = true;
            return;
        }

        HandGroupingPlan plan = BuildPlan(player);
        _latestPlan = plan;
        ApplyPlanToHand(player, plan);
    }

    [HideFromIl2Cpp]
    private static bool CanGroupHand(PlayerModel player)
    {
        if (_enableGrouping?.Value != true || player == null || !player.IsInEncounter || HasActiveModal())
        {
            return false;
        }

        CardPileModel cardPile = player.HandPile?.CardPile;
        return cardPile != null && cardPile.Count > 1;
    }

    [HideFromIl2Cpp]
    private static bool HasActiveModal()
    {
        try
        {
            return ModalManager.Exists && ModalManager.Instance != null && ModalManager.Instance.HasModalActive;
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogWarning($"Unable to read modal state: {ex.Message}");
            return false;
        }
    }

    [HideFromIl2Cpp]
    private static HandGroupingPlan BuildPlan(PlayerModel player)
    {
        List<HandCardSnapshot> cards = SnapshotHand(player);
        int? desiredCost = _manualActiveCost ?? ComboTargetReader.TryReadDesiredCost(player);
        IReadOnlyList<int> stableCosts = CardCostCatalog.GetStableNumericCosts(_fallbackMaxCost?.Value ?? 10);
        int threshold = Mathf.Max(1, _enableWhenHandCountAtLeast?.Value ?? 20);

        return HandGroupingPlanner.CreatePlan(new HandGroupingRequest(stableCosts, cards, desiredCost, threshold));
    }

    [HideFromIl2Cpp]
    private static List<HandCardSnapshot> SnapshotHand(PlayerModel player)
    {
        List<HandCardSnapshot> cards = new();
        CardPileModel cardPile = player?.HandPile?.CardPile;
        if (cardPile == null)
        {
            return cards;
        }

        for (int i = 0; i < cardPile.Count; i++)
        {
            if (!cardPile.TryPeekIndex(i, out CardModel card) || card == null)
            {
                continue;
            }

            bool isWild = CardRules.IsWildCard(card);
            int cost = isWild ? -1 : CardRules.GetSortCost(card);
            bool canPlay = CardRules.CanPlay(player, card);
            cards.Add(new HandCardSnapshot(CardRules.GetStableCardId(card, i), cost, isWild, canPlay));
        }

        return cards;
    }

    [HideFromIl2Cpp]
    private static void ApplyPlanToHand(PlayerModel player, HandGroupingPlan plan)
    {
        if (plan.GroupingEnabled)
        {
            SortCardPileForPlan(player, plan);
        }

        CardSlotHolder cardGroup = player?.HandPile?.View?.CardGroup;
        if (cardGroup == null)
        {
            return;
        }

        List<CardSlotEntry> slots = SnapshotSlots(cardGroup, player);
        if (slots.Count == 0)
        {
            return;
        }

        if (!plan.GroupingEnabled || _hideInactiveCards?.Value != true)
        {
            foreach (CardSlotEntry entry in slots)
            {
                SetSlotVisible(entry.Slot, true);
            }

            ScheduleHandLayoutRefresh(cardGroup);
            return;
        }

        foreach (CardSlotEntry entry in slots)
        {
            bool visible = plan.VisibleCardIds.Contains(entry.CardId);
            SetSlotVisible(entry.Slot, visible);
        }

        SortSlotsForPlan(slots, plan);
        ScheduleHandLayoutRefresh(cardGroup);
    }

    [HideFromIl2Cpp]
    private static bool IsHandViewReady(PlayerModel player)
    {
        CardPileModel cardPile = player?.HandPile?.CardPile;
        CardSlotHolder cardGroup = player?.HandPile?.View?.CardGroup;
        if (cardPile == null || cardGroup == null)
        {
            return false;
        }

        CardSlot[] slots = cardGroup.GetComponentsInChildren<CardSlot>(true);
        int cardSlotCount = 0;
        foreach (CardSlot slot in slots)
        {
            if (slot != null && GetSlotCard(slot) != null)
            {
                cardSlotCount++;
            }
        }

        return cardSlotCount >= cardPile.Count;
    }

    [HideFromIl2Cpp]
    private static void SortCardPileForPlan(PlayerModel player, HandGroupingPlan plan)
    {
        CardPileModel cardPile = player?.HandPile?.CardPile;
        if (cardPile == null || cardPile.Count <= 1)
        {
            return;
        }

        List<CardPileEntry> entries = new();
        for (int i = 0; i < cardPile.Count; i++)
        {
            if (!cardPile.TryPeekIndex(i, out CardModel card) || card == null)
            {
                continue;
            }

            bool isWild = CardRules.IsWildCard(card);
            int cost = isWild ? -1 : CardRules.GetSortCost(card);
            entries.Add(new CardPileEntry(card, cost, isWild, i));
        }

        entries.Sort((x, y) => CompareCardPileEntries(x, y, plan));

        bool changed = false;
        for (int targetIndex = 0; targetIndex < entries.Count; targetIndex++)
        {
            CardModel desiredCard = entries[targetIndex].Card;
            CardModel currentCard = GetCardAt(cardPile, targetIndex);
            if (currentCard == null || desiredCard == null || currentCard == desiredCard)
            {
                continue;
            }

            if (cardPile.Contains(desiredCard))
            {
                cardPile.TrySwapCards(currentCard, desiredCard);
                changed = true;
            }
        }

        if (changed)
        {
            player.HandPile.View?.RefreshCardsUI(player);
        }
    }

    [HideFromIl2Cpp]
    private static CardModel GetCardAt(CardPileModel cardPile, int index)
    {
        return cardPile.TryPeekIndex(index, out CardModel card) ? card : null;
    }

    [HideFromIl2Cpp]
    private static int CompareCardPileEntries(CardPileEntry x, CardPileEntry y, HandGroupingPlan plan)
    {
        int rankCompare = GetEntryRank(x.Cost, x.IsWild, plan).CompareTo(GetEntryRank(y.Cost, y.IsWild, plan));
        if (rankCompare != 0)
        {
            return rankCompare;
        }

        int costCompare = x.Cost.CompareTo(y.Cost);
        return costCompare != 0 ? costCompare : x.OriginalIndex.CompareTo(y.OriginalIndex);
    }

    [HideFromIl2Cpp]
    private static List<CardSlotEntry> SnapshotSlots(CardSlotHolder cardGroup, PlayerModel player)
    {
        CardSlot[] allSlots = cardGroup.GetComponentsInChildren<CardSlot>(true);
        List<CardSlotEntry> slots = new();
        for (int i = 0; i < allSlots.Length; i++)
        {
            CardSlot slot = allSlots[i];
            CardModel card = GetSlotCard(slot);
            if (slot == null || card == null)
            {
                continue;
            }

            bool isWild = CardRules.IsWildCard(card);
            int cost = isWild ? -1 : CardRules.GetSortCost(card);
            slots.Add(new CardSlotEntry(
                slot,
                card,
                CardRules.GetStableCardId(card, i),
                cost,
                isWild,
                CardRules.CanPlay(player, card),
                slot.transform.GetSiblingIndex()));
        }

        return slots;
    }

    [HideFromIl2Cpp]
    private static CardModel GetSlotCard(CardSlot slot)
    {
        try
        {
            InteractableCard interactableCard = slot?.SlottedInteractableCard;
            return interactableCard?.CardView?.CardModel;
        }
        catch
        {
            return null;
        }
    }

    [HideFromIl2Cpp]
    private static void SortSlotsForPlan(List<CardSlotEntry> slots, HandGroupingPlan plan)
    {
        slots.Sort((x, y) => CompareSlotEntries(x, y, plan));
        for (int i = 0; i < slots.Count; i++)
        {
            slots[i].Slot.transform.SetSiblingIndex(i);
            SetCardRenderOrder(slots[i].Slot, i);
        }
    }

    [HideFromIl2Cpp]
    private static int CompareSlotEntries(CardSlotEntry x, CardSlotEntry y, HandGroupingPlan plan)
    {
        int xRank = GetEntryRank(x.Cost, x.IsWild, plan);
        int yRank = GetEntryRank(y.Cost, y.IsWild, plan);
        int rankCompare = xRank.CompareTo(yRank);
        if (rankCompare != 0)
        {
            return rankCompare;
        }

        int costCompare = x.Cost.CompareTo(y.Cost);
        return costCompare != 0 ? costCompare : x.OriginalIndex.CompareTo(y.OriginalIndex);
    }

    [HideFromIl2Cpp]
    private static int GetEntryRank(int cost, bool isWild, HandGroupingPlan plan)
    {
        if (plan.ActiveCost.HasValue && !isWild && cost == plan.ActiveCost.Value)
        {
            return 0;
        }

        if (isWild)
        {
            return 1;
        }

        return 2;
    }

    private readonly struct CardPileEntry
    {
        public CardPileEntry(CardModel card, int cost, bool isWild, int originalIndex)
        {
            Card = card;
            Cost = cost;
            IsWild = isWild;
            OriginalIndex = originalIndex;
        }

        public CardModel Card { get; }
        public int Cost { get; }
        public bool IsWild { get; }
        public int OriginalIndex { get; }
    }

    [HideFromIl2Cpp]
    private static void SetSlotVisible(CardSlot slot, bool visible)
    {
        if (slot == null)
        {
            return;
        }

        if (_useSetActiveForHiddenCards?.Value == true)
        {
            if (slot.gameObject.activeSelf != visible)
            {
                slot.gameObject.SetActive(visible);
            }

            return;
        }

        CanvasGroup canvasGroup = slot.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = slot.gameObject.AddComponent<CanvasGroup>();
        }

        canvasGroup.alpha = visible ? 1f : 0.02f;
        canvasGroup.blocksRaycasts = visible;
        canvasGroup.interactable = visible;
        slot.transform.localScale = visible ? Vector3.one : new Vector3(0.01f, 0.01f, 0.01f);
    }

    [HideFromIl2Cpp]
    private static void RestoreAllSlots(PlayerModel player)
    {
        CardSlotHolder cardGroup = player?.HandPile?.View?.CardGroup;
        if (cardGroup == null)
        {
            return;
        }

        CardSlot[] allSlots = cardGroup.GetComponentsInChildren<CardSlot>(true);
        foreach (CardSlot slot in allSlots)
        {
            if (slot != null)
            {
                SetSlotVisible(slot, true);
            }
        }

        ScheduleHandLayoutRefresh(cardGroup);
    }

    [HideFromIl2Cpp]
    private static void SetCardRenderOrder(CardSlot slot, int index)
    {
        CardView cardView = slot?.SlottedInteractableCard?.CardView;
        if (cardView == null)
        {
            return;
        }

        SortingGroup sortingGroup = cardView.GetComponentInChildren<SortingGroup>(true);
        if (sortingGroup != null)
        {
            sortingGroup.sortingOrder = index;
        }

        cardView.transform.SetSiblingIndex(index);
        cardView.TweenContainer?.SetSiblingIndex(index);
    }

    [HideFromIl2Cpp]
    private static void ScheduleHandLayoutRefresh(CardSlotHolder cardGroup)
    {
        _pendingLayoutRefreshCardGroup = cardGroup;
        _pendingLayoutRefreshFrames = LayoutRefreshFrames;
        RefreshHandLayout(cardGroup);
    }

    [HideFromIl2Cpp]
    private static void ProcessPendingLayoutRefresh()
    {
        if (_pendingLayoutRefreshFrames <= 0)
        {
            return;
        }

        _pendingLayoutRefreshFrames--;
        RefreshHandLayout(_pendingLayoutRefreshCardGroup);
        if (_pendingLayoutRefreshFrames == 0)
        {
            _pendingLayoutRefreshCardGroup = null;
        }
    }

    [HideFromIl2Cpp]
    private static void RefreshHandLayout(CardSlotHolder cardGroup)
    {
        if (cardGroup == null)
        {
            return;
        }

        CardLayoutGroup layoutGroup = cardGroup.GetComponentInChildren<CardLayoutGroup>(true);
        if (layoutGroup == null)
        {
            return;
        }

        layoutGroup.SelectedIndex = -1;
        layoutGroup.ForceLayoutRefresh();
        if (layoutGroup.transform is RectTransform rectTransform)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
        }

        Canvas.ForceUpdateCanvases();
    }

    [HideFromIl2Cpp]
    private void DrawGroupBar(HandGroupingPlan plan)
    {
        float scale = Mathf.Min(Screen.width / ReferenceWidth, Screen.height / ReferenceHeight);
        float x = (_groupBarReferenceX?.Value ?? DefaultGroupBarReferenceX) * scale;
        float y = (_groupBarReferenceY?.Value ?? DefaultGroupBarReferenceY) * scale;
        float buttonWidth = GroupButtonWidth * scale;
        float buttonHeight = GroupButtonHeight * scale;
        float gap = GroupButtonGap * scale;

        foreach (CostGroupState group in plan.Groups)
        {
            Rect rect = new(x, y, buttonWidth, buttonHeight);
            GUIStyle style = group.IsActive ? _activeGroupStyle : _groupStyle;
            if (GUI.Button(rect, $"{group.Cost}/{group.Count}", style))
            {
                _manualActiveCost = group.Cost;
                _refreshRequested = true;
            }

            x += buttonWidth + gap;
        }

        Rect wildRect = new(x + gap, y, WildButtonWidth * scale, buttonHeight);
        GUI.Button(wildRect, $"W/{plan.WildCount}", _wildStyle);

        if (_showComboBlockedText?.Value == true && plan.ComboBlocked)
        {
            Rect statusRect = new((_groupBarReferenceX?.Value ?? DefaultGroupBarReferenceX) * scale, y + buttonHeight + 6f * scale, 260f * scale, 24f * scale);
            GUI.Label(statusRect, plan.StatusText, _statusStyle);
        }
    }

    [HideFromIl2Cpp]
    private void EnsureGuiStyles()
    {
        if (_groupStyle != null)
        {
            return;
        }

        _groupStyle = CreateGroupStyle(new Color(0.09f, 0.08f, 0.11f, 0.86f), new Color(0.88f, 0.82f, 0.68f, 1f));
        _activeGroupStyle = CreateGroupStyle(new Color(0.55f, 0.36f, 0.14f, 0.95f), new Color(1f, 0.93f, 0.7f, 1f));
        _wildStyle = CreateGroupStyle(new Color(0.06f, 0.24f, 0.32f, 0.92f), new Color(0.82f, 0.97f, 1f, 1f));
        _statusStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 16,
            fontStyle = FontStyle.Bold
        };
        _statusStyle.normal.textColor = new Color(1f, 0.74f, 0.45f, 1f);
    }

    [HideFromIl2Cpp]
    private static GUIStyle CreateGroupStyle(Color backgroundColor, Color textColor)
    {
        Texture2D texture = new(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, backgroundColor);
        texture.Apply();

        GUIStyle style = new(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            border = new RectOffset(4, 4, 4, 4)
        };
        style.normal.background = texture;
        style.hover.background = texture;
        style.active.background = texture;
        style.normal.textColor = textColor;
        style.hover.textColor = Color.white;
        style.active.textColor = Color.white;
        return style;
    }

    private readonly struct CardSlotEntry
    {
        public CardSlotEntry(CardSlot slot, CardModel card, string cardId, int cost, bool isWild, bool canPlay, int originalIndex)
        {
            Slot = slot;
            Card = card;
            CardId = cardId;
            Cost = cost;
            IsWild = isWild;
            CanPlay = canPlay;
            OriginalIndex = originalIndex;
        }

        public CardSlot Slot { get; }
        public CardModel Card { get; }
        public string CardId { get; }
        public int Cost { get; }
        public bool IsWild { get; }
        public bool CanPlay { get; }
        public int OriginalIndex { get; }
    }
}
