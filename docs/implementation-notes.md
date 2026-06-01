# Implementation Notes

## References

- `wtksana/vcmod`: hand sorting, hand slot ordering, BepInEx project layout, IL2CPP notes.
- `TovaK66/BetterCards`: card view hierarchy probing, card config naming, GUID handling, card UI sprite lookup.

## Hand grouping design

The mod separates pure grouping policy from Unity state:

- `VcCardGrouper.Core.HandGroupingPlanner`: stable groups, visible cards, active group, blocked state.
- `HandGroupingController`: reads `PlayerModel`, applies visibility to `CardSlot`, draws the group bar.
- `ComboTargetReader`: reads the game's current combo target when a likely field/property is available.

The UI currently hides inactive numeric groups by adding `CanvasGroup` to hand slots and scaling hidden slots down. This is less risky than `SetActive(false)`, but `UseSetActiveForHiddenCards` is available for tighter visual collapse if playtesting shows it is safe.

## Reference alignment pass

The current implementation intentionally follows these lessons from the two reference mods:

- From `wtksana/vcmod`: hand changes should wait until the hand UI is ready. `HandGroupingController.IsHandViewReady` checks that visible `CardSlot` objects have caught up with `CardPileModel.Count` before applying grouping.
- From `wtksana/vcmod`: data order and visual order need to stay aligned. `SortCardPileForPlan` reorders `CardPileModel` with `TrySwapCards`, and `SortSlotsForPlan` then mirrors that order in the slot hierarchy.
- From `wtksana/vcmod`: after changing hand order, call `RefreshCardsUI`, `CardLayoutGroup.ForceLayoutRefresh`, `LayoutRebuilder.ForceRebuildLayoutImmediate`, and `Canvas.ForceUpdateCanvases`.
- From `wtksana/vcmod`: do not use `UnityEngine.Input`; grouping buttons are drawn through `OnGUI` and do not poll the old input API.
- From `TovaK66/BetterCards`: use `CardView._appliedCardConfig` before `CardModel.CardConfig` where possible, because evolved or modified cards may render from the applied config.
- From `TovaK66/BetterCards`: strip `(Clone)` from `CardConfig.name` before using it as a stable key or exported file name.
- From both mods: treat `WildCostType` as the primary W-card signal, with `CardConfig.name` as fallback.

Open risk after this pass: `ComboTargetReader` still uses reflection to discover the game's current combo target. This is the least certain part until we can inspect the actual `Pancake.dll` interop assembly or run the mod in game logs.

## Card face replacement design

`CardFaceReplacement` scans visible hand `CardView` objects on a short interval:

- finds the likely card art `Image`
- exports the original sprite once
- loads replacement PNGs from `VcCardGrouper/card-faces`
- swaps the `Image.sprite`

The image lookup avoids obvious cost, mana, combo, frame, border, lock, badge, and text images, then picks the largest remaining non-sliced image.

## Validation still needed in game

- Confirm the actual `PlayerModel` combo target member name.
- Confirm whether hidden slots should use alpha/scale or `SetActive(false)`.
- Confirm card art node selection on normal hand cards, hover-enlarged cards, and reward/select-card modals.
- Confirm exported card face crops are correct when the source sprite comes from an atlas.
