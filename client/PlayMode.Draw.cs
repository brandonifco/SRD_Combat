using Godot;
using SRDCombat.Core.Combat;
using SRDCombat.Content;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;
using SRDCombat.Game;

using SRDCombat.Viewer.Ui;

namespace SRDCombat.Viewer;

/// <summary>Everything the screen paints: the board overlay, every menu and card, and the status and resource lines they read from.</summary>
public partial class PlayMode : FightScreen
{
    /// <summary>How dark the fog is where nothing can be seen.</summary>
    private const float FogOpacity = 0.55f;

    /// <summary>Fog texture pixels per battlefield square — the feathering's resolution.</summary>
    private const int FogPixelsPerSquare = 8;

    /// <summary>
    /// Renders the fog one pixel per square and upscales it bilinearly, so the interior
    /// stays a solid shadow while the boundary ramps smoothly over about a square.
    /// </summary>
    private ImageTexture? BuildFogTexture(Battlefield field)
    {
        if (_unseen.Count == 0)
        {
            return null;
        }

        var image = Image.CreateEmpty(field.Width, field.Height, false, Image.Format.Rgba8);

        foreach (var square in _unseen)
        {
            image.SetPixel(square.X, square.Y, new Color(0f, 0f, 0f, FogOpacity));
        }

        image.Resize(
            field.Width * FogPixelsPerSquare,
            field.Height * FogPixelsPerSquare,
            Image.Interpolation.Bilinear);

        return ImageTexture.CreateFromImage(image);
    }

    public override void _Draw()
    {
        if (_phase != Phase.Fighting || _encounter is not { } encounter)
        {
            DrawChrome(_subtitle, StatusLine(null));
            DrawInterlude();
            DrawQuitCard();
            return;
        }

        var active = encounter.ActiveCombatant;
        var commanded = CommandedCombatant();

        // The field first, floor to ceiling; every piece of chrome floats over it.
        DrawBackdrop();
        DrawGrid();

        // Advice under the tokens: where a walk could end, and who a click would attack.
        foreach (var square in _reachable)
        {
            DrawRect(
                new Rect2(GridLeft + (square.X * CellPixels), GridTop + (square.Y * CellPixels), CellPixels, CellPixels),
                new Color(PartyColour, 0.16f));
        }

        // The fog of war, drawn smooth: the per-square set is painted into a small
        // image and upscaled bilinearly (BuildFogTexture), so the shadow's edge
        // feathers across a square instead of stepping — the blockiness was the other
        // half of the 2026-08-21 request. One texture draw whatever the fog's shape.
        if (_fogTexture is { } fog)
        {
            DrawTextureRect(
                fog,
                new Rect2(GridLeft, GridTop, GridWidth * CellPixels, GridHeight * CellPixels),
                false);
        }

        // The keyboard's cursor, drawn over the advice so it is never lost in it.
        if (_cursor is { } caret)
        {
            DrawRect(
                new Rect2(GridLeft + (caret.X * CellPixels), GridTop + (caret.Y * CellPixels), CellPixels, CellPixels),
                ActiveRing,
                filled: false,
                width: 3f);
        }

        // Holds first, then the walk: a held token is how somebody *looked* before a
        // blow whose picture has not played, and where anybody stands is the walk's own
        // question. Together they make the screen tell the fight in order — the walk,
        // the swing, the damage on its last frame, and only then the fall — where live
        // state alone showed the victim on the floor before the monster took a step.
        var tokens = WithWalk(WithHeldAppearances(TokensFrom(encounter, _labels)));

        // What the fog withholds: a monster standing where nobody in the party can see
        // draws no token and earns no ring — the fog would otherwise be a tint over a
        // perfectly visible figure. The panel keeps its row (initiative order is
        // knowledge the party has from the fight itself) with its state withheld.
        var unseenIds = tokens
            .Where(token => !token.IsParty && _unseen.Contains(new GridPosition(token.X, token.Y)))
            .Select(token => token.Id)
            .ToHashSet();
        var shown = tokens.Where(token => !unseenIds.Contains(token.Id)).ToList();

        if (commanded is not null && Armed is null)
        {
            foreach (var enemy in encounter.EnemiesOf(commanded))
            {
                if (!enemy.IsDead
                    && !_unseen.Contains(enemy.Position)
                    && AttackChoice.BestFor(commanded, enemy, encounter.Combatants) is not null)
                {
                    DrawCircle(CentreOf(enemy.Position), (CellPixels / 2f) - 4, MonsterColour, filled: false, width: 2);
                }
            }
        }

        DrawTokens(shown, active?.Id);
        DrawHeading(_subtitle, StatusLine(commanded));
        DrawTurnOrder(tokens, active?.Id, unseenIds);
        DrawLog(encounter.Log, encounter.Log.Count, tokens.Count);

        // The bottom strip's own veil, before anything is written on it.
        if (active is not null || commanded is not null || _notice is not null)
        {
            DrawRect(BottomStrip, Veil);
        }

        // Who is up, and with what — class and level for a character, AC, hit points,
        // and the attacks they carry. TurnBanner composes it so the console client and
        // this screen cannot drift; the letter is this fight's label for the token.
        if (active is { } upNow)
        {
            var lines = TurnBanner.Lines(upNow);
            var colour = upNow.SideId == PregeneratedParty.SideId ? PartyColour : MonsterColour;

            DrawString(
                TextFont,
                new Vector2(UiLeft, BannerTop),
                Trim($"{_labels.Of(upNow)}  {lines[0]}", 90),
                fontSize: 13,
                modulate: colour);

            if (lines.Count > 1)
            {
                DrawString(
                    TextFont,
                    new Vector2(UiLeft, BannerTop + 18),
                    Trim(lines[1], 95),
                    fontSize: 12,
                    modulate: Dim);
            }
        }

        if (commanded is { } character)
        {
            // Greyed while an act plays out: the input gates above make the row inert
            // over that window, and a button that looks pressable while it is not
            // would be the display lying about it.
            var inkNow = ActInProgress ? Dim : Ink;

            foreach (var (rect, caption, _) in _buttons)
            {
                DrawRect(rect, GridLine);
                DrawRect(rect, Dim, filled: false, width: 1);
                DrawString(
                    TextFont,
                    new Vector2(rect.Position.X + 11, rect.Position.Y + 19),
                    caption,
                    fontSize: 13,
                    modulate: inkNow);
            }

            DrawString(
                TextFont,
                new Vector2(UiLeft, ButtonRowTop + 28 + 16),
                ResourceLine(character),
                fontSize: 12,
                modulate: Dim);

            // A separate line from ResourceLine on purpose (#534): that method's
            // contract is "what this character has left to spend", and every entry in
            // it is expendable — slots, uses, potions. A passive item spends nothing,
            // so it gets its own row rather than corrupting that grammar.
            var equipment = EquipmentLine(character);

            if (equipment.Length > 0)
            {
                DrawString(
                    TextFont,
                    new Vector2(UiLeft, ButtonRowTop + 28 + 34),
                    Trim(equipment, 95),
                    fontSize: 12,
                    modulate: Dim);
            }
        }

        if (_notice is { } notice)
        {
            DrawString(
                TextFont,
                new Vector2(UiLeft, ButtonRowTop + 28 + 56),
                Trim(notice, 78),
                fontSize: 13,
                modulate: MonsterColour);
        }

        // Clearing and drawing have separate lifecycles (S5, #504 round 3). The row list
        // (one now, not three — #505) is emptied unconditionally, every frame, regardless of
        // phase, of whether anyone is commanded, or of what the stack holds — a *stronger*
        // form of HitTest's invariant than the three DrawXMenu methods used to give it
        // themselves before #505 (a closed menu's rows are gone before the traversal below
        // even runs, not merely "cleared by whichever method used to own that list"). Only
        // then does the traversal decide whether it gets repopulated.
        ClearMenuRows();

        // Which card is showing is the focus stack's answer, not four conditions written
        // out by hand — that was the third and last copy of the modal order, and it is
        // gone. What this is *not*, deliberately: a z-order mechanism.
        //
        // S5 first shipped this as a `foreach (layer in _focus.BottomUp)` dispatch, on the
        // reading that draw order should follow stack order. Review knocked that out by
        // reversing the traversal: every capture stayed byte-identical, because **no two of
        // these cards can draw in the same frame**. A row menu draws only when it is
        // _focus.Top, so at most one of the three; and the outcome card only exists once
        // the fight is complete, which is exactly when CommandedCombatant() returns null
        // ("_encounter is { IsComplete: false }") and every menu case is dead. An ordering
        // loop whose order provably cannot matter is a mechanism that looks like it decides
        // something and does not, which is the shape this project keeps having to catch.
        // So the loop is not here, and FocusStack.BottomUp is not described as draw order.
        //
        // Two cards genuinely can be up at once — Esc during the closing animation leaves
        // QuitConfirm open and _Process then pushes Outcome above it — and that pair's
        // order is still hand-written below, by name, for the reason on DrawQuitCard.
        // When a second pair of stack-traversed cards can coexist, the loop earns its place
        // and this comment is the note that says so.
        //
        // PlayFocus.Board and PlayFocus.Targeting draw no card and appear here at all: that
        // is correct, not an omission — Targeting changes how the *board* draws, which the
        // board has read off the stack since S1.
        switch (_focus.Top)
        {
            case PlayFocus.SpellMenu when commanded is { } spellCaster:
                DrawSpellMenu(spellCaster);
                break;

            case PlayFocus.AttackMenu when commanded is { } attacker:
                DrawAttackMenu(attacker);
                break;

            case PlayFocus.SlotMenu { Spell: { } spell } when commanded is { } slotCaster:
                DrawSlotMenu(slotCaster, spell);
                break;

            case PlayFocus.TradeMenu when commanded is { } trader:
                DrawTradeMenu(trader);
                break;
        }

        // Not switched on Top with the menus above: the outcome card draws while it is
        // anywhere in the stack, including underneath QuitConfirm in the Esc-during-the-
        // closing-animation state. Holds, not Top — the pre-S5 reading, kept because it is
        // the correct one and Top would silently blank the card under the quit question.
        if (_focus.Holds<PlayFocus.Outcome>())
        {
            DrawOutcomeCard();
        }

        // Last, so it sits over everything it might explain.
        DrawHint();

        // The one card not drawn off the stack's order (see DrawQuitCard's remarks):
        // it stays named here, after the hint, rather than folding into a loop that would
        // put it under a tooltip that can be raised while it is up.
        DrawQuitCard();
    }

    /// <summary>
    /// The card that asks whether Esc really meant to leave. It says what quitting
    /// costs — the save keeps the state after the last <em>cleared</em> fight, so a
    /// fight in progress restarts — because that cost is exactly what an accidental
    /// exit was paying without asking.
    /// </summary>
    /// <remarks>
    /// <b>Drawn last, by name, after <see cref="DrawHint"/> — not decided by
    /// <c>_focus.Top</c> the way the other cards are (S5, #504).</b>
    /// <see cref="PlayFocus.QuitConfirm"/> is a layer like any other, but a tooltip must
    /// never occlude the question that closes the game, and the hint genuinely can be
    /// raised while this card is up: <c>AdvanceHover</c> runs from <c>_Process</c> in
    /// <see cref="Phase.Fighting"/> regardless of quit state, so a hint from before Esc was
    /// pressed can still appear after it. Making <c>QuitConfirm</c> draw in its stack
    /// position would put it under the hint and change a pixel. One documented exception is
    /// cheaper than a sixth trait on <see cref="PlayFocus"/> for a single case.
    /// </remarks>
    private void DrawQuitCard()
    {
        if (!_focus.Holds<PlayFocus.QuitConfirm>())
        {
            return;
        }

        const int width = 470;
        const int height = 118;
        var left = (ScreenWidth - width) / 2f;
        var top = (ScreenHeight - height) / 2f;
        var card = new Rect2(left, top, width, height);

        DrawRect(card, Background);
        DrawRect(card, ActiveRing, filled: false, width: 2);

        DrawString(
            TextFont,
            new Vector2(left + 24, top + 42),
            "LEAVE THE GAME?",
            fontSize: 26,
            modulate: ActiveRing);

        DrawString(
            TextFont,
            new Vector2(left + 24, top + 70),
            "The run is saved after each cleared fight; a fight in progress restarts.",
            fontSize: 12,
            modulate: Ink);

        DrawString(
            TextFont,
            new Vector2(left + 24, top + 96),
            "[esc] again quits — any other key or click stays",
            fontSize: 12,
            modulate: Dim);
    }

    /// <summary>
    /// The hint the pointer has earned, in a panel beside it.
    /// </summary>
    /// <remarks>
    /// Placed against the pointer rather than in a fixed corner — a tip you have to look
    /// away to read is a tip you stop reading — and nudged back inside the window rather
    /// than being allowed off the edge, since the row it explains runs to the screen's
    /// bottom right where a naive panel would fall straight off.
    /// </remarks>
    private void DrawHint()
    {
        if (_hint is not { } hint)
        {
            return;
        }

        var lines = hint.Split('\n')
            .SelectMany(line => Wrap(line, HintWidthCharacters))
            .ToArray();

        var width = lines.Max(line => TextFont.GetStringSize(line, fontSize: 12).X) + 20;
        var height = (lines.Length * 17) + 14;

        var x = Math.Min(_pointer.X + 16, ScreenWidth - width - 8);
        var y = _pointer.Y + 22 + height > ScreenHeight
            ? _pointer.Y - height - 10
            : _pointer.Y + 22;

        var panel = new Rect2(Math.Max(8, x), Math.Max(8, y), width, height);

        DrawRect(panel, Background);
        DrawRect(panel, ActiveRing, filled: false, width: 1);

        for (var index = 0; index < lines.Length; index++)
        {
            DrawString(
                TextFont,
                new Vector2(panel.Position.X + 10, panel.Position.Y + 18 + (index * 17)),
                lines[index],
                fontSize: 12,
                modulate: Ink);
        }
    }

    /// <summary>How wide a hint runs before it wraps, in characters.</summary>
    private const int HintWidthCharacters = 52;

    /// <summary>
    /// The card that names how the fight ended and waits to be dismissed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It says *why* as well as what, because an objective rung can end with enemies
    /// still standing and a bare "you win" over a field of live goblins is the confusing
    /// part rather than the satisfying one.
    /// </para>
    /// <para>
    /// <b>Called only from <c>_Draw</c>'s traversal, when the layer it is visiting is
    /// <see cref="PlayFocus.Outcome"/> (S5, #504 round 3)</b> — no guard of its own is
    /// needed here, unlike the row menus: nothing is ever pushed above
    /// <see cref="PlayFocus.Outcome"/> (qc's #504 review checked every <c>Push</c> site; its
    /// own <c>Escape</c> is <c>Commit</c>, not <c>AskToQuit</c>, so even the quit card cannot
    /// land on top of it), so the traversal encountering this layer at all is already the
    /// whole answer.
    /// </para>
    /// </remarks>
    private void DrawOutcomeCard()
    {
        if (_encounter is not { } encounter)
        {
            return;
        }

        var won = encounter.WinningSide == PregeneratedParty.SideId;
        var heading = won ? "BATTLE WON" : "BATTLE LOST";

        var why = encounter.Objective.Kind switch
        {
            ObjectiveKind.SurviveRounds when won =>
                $"The party held out for {encounter.Objective.Rounds} rounds.",
            ObjectiveKind.KillLeader when won =>
                "The leader is down — the rest break off.",
            _ => won ? "Every enemy is down." : "The party has fallen.",
        };

        // Centred on the window, not the field: the camera may have carried the field
        // anywhere, and the card is being said to the player, not to a square.
        const int width = 460;
        const int height = 132;
        var left = (ScreenWidth - width) / 2f;
        var top = (ScreenHeight - height) / 2f;
        var card = new Rect2(left, top, width, height);

        DrawRect(card, Background);
        DrawRect(card, won ? ActiveRing : MonsterColour, filled: false, width: 2);

        DrawString(
            TextFont,
            new Vector2(left + 24, top + 46),
            heading,
            fontSize: 30,
            modulate: won ? ActiveRing : MonsterColour);

        DrawString(TextFont, new Vector2(left + 24, top + 78), why, fontSize: 13, modulate: Ink);

        DrawString(
            TextFont,
            new Vector2(left + 24, top + 106),
            "any key or click for the results",
            fontSize: 12,
            modulate: Dim);
    }

    /// <summary>The between-fights screen: the run's own words, and a way onward.</summary>
    private void DrawInterlude()
    {
        if (Shopping is not null && _run is { } shopping)
        {
            DrawShop(shopping);
            return;
        }

        var y = UiTop + 8f;

        // Wrapped, not Trim()'d: a refusal names both the problem and the remedy, and
        // a hard 100-character cut can (and did — qc's #470 review, measured) sever the
        // remedy from a message compiled with both in one string. Every _interlude
        // entry goes through this, not just the one qc measured, because the next
        // multi-flag refusal is one string concatenation away from the same cut.
        foreach (var line in _interlude)
        {
            if (line.Length == 0)
            {
                y += 10;
                continue;
            }

            foreach (var wrapped in Wrap(line, 100))
            {
                DrawString(TextFont, new Vector2(UiLeft, y), wrapped, fontSize: 14, modulate: Ink);
                y += 22;
            }
        }

        if (_phase == Phase.Interlude)
        {
            _continueButton = new Rect2(UiLeft, y + 16, 110, 32);

            DrawRect(_continueButton, GridLine);
            DrawRect(_continueButton, Dim, filled: false, width: 1);
            DrawString(
                TextFont,
                new Vector2(_continueButton.Position.X + 18, _continueButton.Position.Y + 21),
                "Continue",
                fontSize: 14,
                modulate: Ink);

            if (_shopAvailable)
            {
                _shopButton = new Rect2(UiLeft + 126, y + 16, 110, 32);

                DrawRect(_shopButton, GridLine);
                DrawRect(_shopButton, Dim, filled: false, width: 1);
                DrawString(
                    TextFont,
                    new Vector2(_shopButton.Position.X + 30, _shopButton.Position.Y + 21),
                    "Shop",
                    fontSize: 14,
                    modulate: Ink);
            }
        }
    }

    /// <summary>
    /// The merchant's stall: every offer at its printed price, the purse in the
    /// header, the unaffordable dimmed the way the console stars them — a thing worth
    /// saving toward is worth seeing.
    /// </summary>
    private void DrawShop(GauntletRun run)
    {
        _shopRows.Clear();

        var offers = Shop.Offers(_content!, run.Party, run.States);
        var y = UiTop + 8f;

        DrawString(
            TextFont,
            new Vector2(UiLeft, y),
            $"A merchant is here. The purse holds {Shop.Price(run.GoldCopper)}. Click to buy.",
            fontSize: 14,
            modulate: Ink);

        y += 26;

        if (offers.Count == 0)
        {
            DrawString(
                TextFont,
                new Vector2(UiLeft, y),
                "Nothing here would improve anybody.",
                fontSize: 13,
                modulate: Dim);
            y += 22;
        }

        foreach (var offer in offers)
        {
            // What the price buys, under the price. The lines are the offer's own —
            // a shopper choosing between a suit of armor and a blade is comparing
            // rules, and rules are never this client's to compute.
            var effects = offer.Effect.Lines;
            var affordable = offer.CostCopper <= run.GoldCopper;
            var rect = new Rect2(UiLeft, y, ShopRowWidth, 19 + (effects.Count * 15));

            _shopRows.Add((rect, offer));

            DrawRect(rect, GridLine);
            DrawString(
                TextFont,
                new Vector2(rect.Position.X + 8, rect.Position.Y + 14),
                offer.Description,
                fontSize: 12,
                modulate: affordable ? Ink : Dim);

            var line = rect.Position.Y + 28;

            foreach (var effect in effects)
            {
                DrawString(
                    TextFont,
                    new Vector2(rect.Position.X + 20, line),
                    effect,
                    fontSize: 11,
                    modulate: affordable ? Dim : new Color(Dim, 0.55f));
                line += 15;
            }

            y += rect.Size.Y + 4;
        }

        if (Shopping?.Notice is { } notice)
        {
            y += 6;
            DrawString(TextFont, new Vector2(UiLeft, y + 12), Trim(notice, 78), fontSize: 13, modulate: MonsterColour);
            y += 18;
        }

        _shopBackButton = new Rect2(UiLeft, y + 12, 110, 32);

        DrawRect(_shopBackButton, GridLine);
        DrawRect(_shopBackButton, Dim, filled: false, width: 1);
        DrawString(
            TextFont,
            new Vector2(_shopBackButton.Position.X + 30, _shopBackButton.Position.Y + 21),
            "Back",
            fontSize: 14,
            modulate: Ink);
    }

    /// <summary>What this character has left to spend. Read off the state, never computed.</summary>
    private static string ResourceLine(Combatant character)
    {
        var parts = new List<string>();

        var slots = character.Features.SpellSlotsRemaining
            .Where(pair => pair.Value > 0)
            .OrderBy(pair => pair.Key)
            .Select(pair => $"L{pair.Key} ×{pair.Value}")
            .ToArray();

        if (slots.Length > 0)
        {
            parts.Add("slots " + string.Join("  ", slots));
        }

        if (character.Stats.Has(ClassFeature.Rage))
        {
            parts.Add($"Rage ×{character.Features.RagesRemaining}");
        }

        if (character.Stats.Has(ClassFeature.SecondWind))
        {
            parts.Add($"Second Wind ×{character.Features.SecondWindRemaining}");
        }

        if (character.Stats.Has(ClassFeature.ActionSurge))
        {
            parts.Add($"Action Surge ×{character.Features.ActionSurgeRemaining}");
        }

        if (character.Stats.Has(ClassFeature.ChannelDivinity))
        {
            parts.Add($"Channel Divinity ×{character.Features.ChannelDivinityRemaining}");
        }

        if (character.Inventory.TotalPotions > 0)
        {
            parts.Add($"potions ×{character.Inventory.TotalPotions}");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// What this character carries and what it resolves to (#534) — separate from
    /// <see cref="ResourceLine"/> because a passive item spends nothing, so it does not
    /// belong in a line whose every other entry is a use counting down. Empty for a
    /// character with nothing equipped.
    /// </summary>
    private static string EquipmentLine(Combatant character)
    {
        if (character.Stats.Character is not { } features || features.MagicItemNames.Count == 0)
        {
            return string.Empty;
        }

        return "Equipment: " + MagicItemReadout.Describe(
            features.MagicItemNames,
            features.SpellAttackItemBonus,
            character.Stats.IgnoresHalfCoverOnSpellAttacks,
            features.SpellAttackBonus);
    }

    /// <summary>
    /// Empties the row-menu list. Called once, unconditionally, before <c>_Draw</c>'s
    /// traversal decides whether it gets repopulated (S5, #504 round 3).
    /// </summary>
    /// <remarks>
    /// This is what makes <c>HitTest</c>'s invariant hold now — "a closed menu holds no
    /// rectangles". Before #505 this cleared three separate lists, one per menu, because
    /// each of <see cref="DrawSpellMenu"/>, <see cref="DrawAttackMenu"/> and
    /// <see cref="DrawSlotMenu"/> filled its own; now there is one list and one traversal
    /// repopulates it for whichever menu <c>_focus.Top</c> names. A menu that was just
    /// popped is no longer in <c>_focus.BottomUp</c> at all, so a traversal keyed on
    /// presence could never have cleared it — clearing first, then walking, is what keeps
    /// that from being a stale-rectangle regression.
    /// </remarks>
    private void ClearMenuRows()
    {
        _menuRows.Clear();
    }

    /// <summary>
    /// The spells this character could cast this instant, in the order the spell menu
    /// lists them — the same rule that decides whether the Cast button is there at all, so
    /// the list can never offer a row whose only possible answer is a refusal.
    /// </summary>
    /// <remarks>
    /// Pulled out on its own (#505) so <see cref="RunSlotMenuProbe"/> can find the same row
    /// <see cref="DrawSpellMenu"/> will draw for a given spell without reading
    /// <c>_menuRows</c>'s contents — the unified list carries only a rectangle and an
    /// <see cref="Action"/> now, not the spell that closed over it, so the probe recomputes
    /// the ordering instead of reaching into the row for a payload it no longer has.
    /// </remarks>
    private static IEnumerable<SpellDefinition> CastableSpells(Combatant character) =>
        character.Stats.Character is not { } features
            ? []
            : features.Spells
                .Where(spell => TurnOptions.CanCastNow(character, spell))
                .OrderBy(spell => spell.Level)
                .ThenBy(spell => spell.Name, StringComparer.Ordinal);

    /// <summary>
    /// The spell list overlay. Called only from <c>_Draw</c>'s traversal, when
    /// <see cref="PlayFocus.SpellMenu"/> is both the layer being visited and
    /// <c>_focus.Top</c> — this method itself no longer checks either (S5, #504 round 3).
    /// <c>_menuRows</c> is <em>not</em> cleared here any more: <c>ClearMenuRows</c> empties
    /// it, unconditionally, before the traversal runs at all, whether or not this method
    /// gets called this frame. The unguarded <c>(PlayFocus.RowMenu)_focus.Top</c> cast below
    /// relies on that same invariant — it is what every row this call adds is stamped with
    /// (<see cref="MenuRowList"/>, #505).
    /// </summary>
    private void DrawSpellMenu(Combatant character)
    {
        if (character.Stats.Character is null)
        {
            return;
        }

        // Over the board, as an overlay. These lists used to live under the second
        // button row; fullscreen gave that ground to the board, and every row below
        // already paints its own filled backdrop, so only the header needs one.
        var top = UiTop + 28;

        DrawRect(
            new Rect2(UiLeft - 8, top - 24, 470, 30),
            new Color(Background.R, Background.G, Background.B, 0.9f));

        DrawString(TextFont, new Vector2(UiLeft, top - 6), "SPELLS — click one, or arrows and Enter", fontSize: 12, modulate: Dim);

        var y = top + 6;

        var castable = CastableSpells(character);
        var menu = (PlayFocus.RowMenu)_focus.Top;

        foreach (var spell in castable)
        {
            var rect = new Rect2(UiLeft, y, 260, 20);
            _menuRows.Add(menu, rect, () => ChooseSpell(spell));

            DrawRect(rect, GridLine);

            if (_menuRows.CountFor(menu) - 1 == menu.MenuIndex)
            {
                DrawRect(rect, ActiveRing, filled: false, width: 2f);
            }

            DrawString(
                TextFont,
                new Vector2(rect.Position.X + 8, rect.Position.Y + 15),
                spell.IsCantrip ? $"{spell.Name} — cantrip" : $"{spell.Name} — level {spell.Level}",
                fontSize: 12,
                modulate: Ink);

            y += 24;
        }
    }

    /// <summary>
    /// The attack list overlay. See <see cref="DrawSpellMenu"/>'s remarks: called only when
    /// <see cref="PlayFocus.AttackMenu"/> is both the layer being visited and
    /// <c>_focus.Top</c>, and <c>_menuRows</c> is cleared by <c>ClearMenuRows</c> before
    /// the traversal runs, not by this method.
    /// </summary>
    private void DrawAttackMenu(Combatant character)
    {
        // Over the board, as an overlay. These lists used to live under the second
        // button row; fullscreen gave that ground to the board, and every row below
        // already paints its own filled backdrop, so only the header needs one.
        var top = UiTop + 28;

        DrawRect(
            new Rect2(UiLeft - 8, top - 24, 470, 30),
            new Color(Background.R, Background.G, Background.B, 0.9f));

        DrawString(TextFont, new Vector2(UiLeft, top - 6), "ATTACKS — click one, or arrows and Enter", fontSize: 12, modulate: Dim);

        var y = top + 6;

        var menu = (PlayFocus.RowMenu)_focus.Top;

        foreach (var attack in character.Stats.Attacks)
        {
            var rect = new Rect2(UiLeft, y, 300, 20);
            _menuRows.Add(menu, rect, () => ChooseAttack(attack));

            if (_menuRows.CountFor(menu) - 1 == menu.MenuIndex)
            {
                DrawRect(rect, ActiveRing, filled: false, width: 2f);
            }

            var dice = string.Join(" + ", attack.Damage.Select(damage => $"{damage.Amount} {damage.Type}"));
            var reach = attack.NormalRangeFeet is { } normal
                ? attack.LongRangeFeet is { } far ? $"{normal}/{far} ft." : $"{normal} ft."
                : $"reach {attack.ReachFeet ?? 5} ft.";

            DrawRect(rect, GridLine);
            DrawString(
                TextFont,
                new Vector2(rect.Position.X + 8, rect.Position.Y + 15),
                $"{attack.Name} — {dice}, {reach}",
                fontSize: 12,
                modulate: Ink);

            y += 24;
        }
    }

    /// <summary>The slot levels this caster could burn on this spell, lowest first.</summary>
    private static List<int> SlotLevelsFor(Combatant caster, SpellDefinition spell)
    {
        if (spell.IsCantrip)
        {
            return [];
        }

        return Enumerable.Range(spell.Level, 10 - spell.Level)
            .Where(level => caster.Features.SpellSlotsRemaining.GetValueOrDefault(level) > 0)
            .ToList();
    }

    /// <summary>
    /// The slot-level overlay. See <see cref="DrawSpellMenu"/>'s remarks: called only when
    /// <see cref="PlayFocus.SlotMenu"/> is both the layer being visited and
    /// <c>_focus.Top</c>, with <paramref name="spell"/> the very layer's own
    /// <see cref="PlayFocus.SlotMenu.Spell"/> rather than re-derived here. <c>_menuRows</c>
    /// is cleared by <c>ClearMenuRows</c> before the traversal runs, not by this method.
    /// </summary>
    private void DrawSlotMenu(Combatant character, SpellDefinition spell)
    {
        // Over the board, as an overlay. These lists used to live under the second
        // button row; fullscreen gave that ground to the board, and every row below
        // already paints its own filled backdrop, so only the header needs one.
        var top = UiTop + 28;

        DrawRect(
            new Rect2(UiLeft - 8, top - 24, 470, 30),
            new Color(Background.R, Background.G, Background.B, 0.9f));

        DrawString(
            TextFont,
            new Vector2(UiLeft, top - 6),
            $"SLOT for {spell.Name} — click a level, or arrows and Enter",
            fontSize: 12,
            modulate: Dim);

        var y = top + 6;

        var menu = (PlayFocus.RowMenu)_focus.Top;

        foreach (var level in SlotLevelsFor(character, spell))
        {
            var rect = new Rect2(UiLeft, y, 260, 20);
            _menuRows.Add(menu, rect, () => ChooseSlot(level));

            if (_menuRows.CountFor(menu) - 1 == menu.MenuIndex)
            {
                DrawRect(rect, ActiveRing, filled: false, width: 2f);
            }

            var left = character.Features.SpellSlotsRemaining.GetValueOrDefault(level);

            DrawRect(rect, GridLine);
            DrawString(
                TextFont,
                new Vector2(rect.Position.X + 8, rect.Position.Y + 15),
                $"level {level} slot — {left} left" + (level > spell.Level ? " (upcast)" : string.Empty),
                fontSize: 12,
                modulate: Ink);

            y += 24;
        }
    }

    /// <summary>
    /// The Trade menu: the acting character's potions, weakest potency first, each with
    /// how many are carried. See <see cref="DrawSpellMenu"/>'s remarks: called only when
    /// <see cref="PlayFocus.TradeMenu"/> is both the layer being visited and
    /// <c>_focus.Top</c>, and <c>_menuRows</c> is cleared by <c>ClearMenuRows</c> before
    /// the traversal runs, not by this method.
    /// </summary>
    private void DrawTradeMenu(Combatant character)
    {
        // Over the board, as an overlay. These lists used to live under the second
        // button row; fullscreen gave that ground to the board, and every row below
        // already paints its own filled backdrop, so only the header needs one.
        var top = UiTop + 28;

        DrawRect(
            new Rect2(UiLeft - 8, top - 24, 470, 30),
            new Color(Background.R, Background.G, Background.B, 0.9f));

        DrawString(TextFont, new Vector2(UiLeft, top - 6), "TRADE — click a potion, or arrows and Enter", fontSize: 12, modulate: Dim);

        var y = top + 6;

        var menu = (PlayFocus.RowMenu)_focus.Top;

        // Weakest potency first, matching InventoryState.Weakest and the console's own
        // default — the potion a client reaches for by default is never the wrong end of
        // that trade by much.
        foreach (var (potency, count) in character.Inventory.Potions.OrderBy(pair => pair.Key))
        {
            var rect = new Rect2(UiLeft, y, 260, 20);
            _menuRows.Add(menu, rect, () => ChooseTradePotency(potency));

            DrawRect(rect, GridLine);

            if (_menuRows.CountFor(menu) - 1 == menu.MenuIndex)
            {
                DrawRect(rect, ActiveRing, filled: false, width: 2f);
            }

            DrawString(
                TextFont,
                new Vector2(rect.Position.X + 8, rect.Position.Y + 15),
                $"{PotionRules.PrintedName(potency)} ×{count}",
                fontSize: 12,
                modulate: Ink);

            y += 24;
        }
    }

    private string StatusLine(Combatant? commanded)
    {
        if (_phase == Phase.RunOver)
        {
            return "the run is over — [esc] quit";
        }

        if (_phase == Phase.Interlude)
        {
            return "between fights — Continue when ready   [esc] quit";
        }

        if (_encounter is { IsComplete: true } encounter)
        {
            return _focus.Holds<PlayFocus.Outcome>()
                ? "the fight is over — any key or click for the results"
                : encounter.WinningSide == PregeneratedParty.SideId
                    ? "the party wins — [esc] quit"
                    : "the party has fallen — [esc] quit";
        }

        if (Armed is { Kind: TargetKind.Spell, Spell: { } spell } castingAt)
        {
            return castingAt.Slot is { } slot
                ? $"choose a target for {spell.Name} (level {slot} slot) — click it, Tab cycles, Enter takes it; Esc cancels"
                : $"choose a target for {spell.Name} — click it, Tab cycles, Enter takes it; Esc cancels";
        }

        if (Armed is { Kind: TargetKind.Attack } swingingAt)
        {
            return swingingAt.Attack is { } attack
                ? $"choose a target for {attack.Name} — click it, Tab cycles, Enter takes it; Esc cancels"
                : "choose a target — Tab cycles, Enter attacks with the best weapon; Esc cancels";
        }

        if (Armed is { Kind: TargetKind.Potion })
        {
            return "choose who drinks the potion — click it, Tab cycles, Enter takes it; Esc cancels";
        }

        if (commanded is { } active)
        {
            var turn = active.Turn;
            return $"{active.Name}'s turn — Action {Tick(turn.HasAction)}  Bonus {Tick(turn.HasBonusAction)}  " +
                   $"Move {turn.MovementFeet} ft — click, or arrows and Enter; keys are on the buttons   [esc] quit";
        }

        return "the other side is acting…   [esc] quit";
    }

    private static string Tick(bool available) => available ? "✓" : "✗";
}
