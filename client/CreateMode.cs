using Godot;
using SRDCombat.Content;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;
using SRDCombat.Game;

namespace SRDCombat.Viewer;

/// <summary>
/// Party creation under the mouse: four drafts built by clicking, every option shown
/// with its printed SRD text.
/// </summary>
/// <remarks>
/// <para>
/// The Phase 5 charter, both halves. <b>Every choice carries its description</b>: the
/// left column lists options, and clicking one only <em>browses</em> it — the panel
/// shows the SRD's own text — while the Take button is the separate act of committing,
/// so nothing advances on selection. <b>The client holds no rules</b>: menus come from
/// <c>CharacterCreation</c>, scores from <c>AbilityScoreRules</c>, and
/// <c>CharacterResolver</c> has the final word on the Summary step, its refusal shown
/// with its reason and the character restarted rather than swallowed.
/// </para>
/// <para>
/// When the fourth character is kept, this node replaces itself with a
/// <see cref="PlayMode"/> whose <see cref="PlayMode.CreatedDrafts"/> are the four
/// drafts — from there the run is indistinguishable from one started any other way.
/// </para>
/// </remarks>
public partial class CreateMode : FightScreen
{
    private enum Step
    {
        Name,
        Class,
        Species,
        Background,
        Method,
        ArrayAssign,
        PointBuy,
        IncreaseShape,
        IncreasePrimary,
        IncreaseSecondary,
        Skills,
        Style,
        Order,
        Weapon,
        Armor,
        Shield,
        Mastery,
        Spells,
        Summary,
    }

    private SrdContent _content = null!;
    private readonly List<CharacterDraft> _drafts = [];
    private Step _step = Step.Name;

    // The character being built.
    private string _name = string.Empty;
    private ClassDefinition? _class;
    private SpeciesDefinition? _species;
    private BackgroundDefinition? _background;
    private Dictionary<Ability, int> _scores = [];
    private List<int> _arrayRemaining = [];
    private AbilityIncreaseChoice _increaseShape;
    private Ability? _primaryIncrease;
    private Ability? _secondaryIncrease;
    private readonly List<string> _skills = [];
    private FightingStyle _style = FightingStyle.Unspecified;
    private DivineOrder _divineOrder = DivineOrder.Unspecified;
    private string? _weaponId;
    private string? _armorId;
    private bool _shield;
    private readonly List<string> _masteries = [];
    private readonly List<string> _spells = [];
    private string? _refusal;
    private PartyMember? _preview;

    // What the mouse can hit, rebuilt every frame the way the screen is drawn.
    private readonly List<(Rect2 Rect, int Index)> _rows = [];
    private readonly List<(Rect2 Rect, string Name, Action Act)> _actions = [];
    private int _browsed = -1;
    private int _rowScroll;
    private int _textScroll;
    private bool _probeStarted;

    protected override string Title => "SRD_Combat — creating a party";

    protected override void OnReady() => _content = LoadContent();

    public override void _Process(double delta)
    {
        QueueRedraw();
        RunProbeIfAsked();
    }

    // ── Input ───────────────────────────────────────────────────────────────────

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true } key && _step == Step.Name)
        {
            if (key.Keycode == Key.Backspace && _name.Length > 0)
            {
                _name = _name[..^1];
            }
            else if (key.Keycode == Key.Enter)
            {
                CommitName();
            }
            else if (key.Unicode is >= 32 and < 127 && _name.Length < 24)
            {
                _name += (char)key.Unicode;
            }

            return;
        }

        if (@event is not InputEventMouseButton { Pressed: true } click)
        {
            return;
        }

        if (click.ButtonIndex == MouseButton.WheelDown || click.ButtonIndex == MouseButton.WheelUp)
        {
            var delta = click.ButtonIndex == MouseButton.WheelDown ? 1 : -1;

            if (click.Position.X < PanelX)
            {
                _rowScroll = Math.Max(0, _rowScroll + delta);
            }
            else
            {
                _textScroll = Math.Max(0, _textScroll + delta);
            }

            return;
        }

        if (click.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        foreach (var (rect, _, act) in _actions.ToArray())
        {
            if (rect.HasPoint(click.Position))
            {
                act();
                return;
            }
        }

        foreach (var (rect, index) in _rows.ToArray())
        {
            if (rect.HasPoint(click.Position))
            {
                BrowseOrToggle(index);
                return;
            }
        }
    }

    /// <summary>
    /// A click on a row: on single-choice steps it browses (the Take button commits);
    /// on multi-choice steps it toggles, because the option's text is already beside it
    /// or one click away on the browse panel.
    /// </summary>
    private void BrowseOrToggle(int index)
    {
        _textScroll = 0;

        switch (_step)
        {
            case Step.ArrayAssign:
                AssignArrayValue(index);
                return;
            case Step.Skills:
                Toggle(_skills, SkillOptions()[index], SkillAllowance());
                return;
            case Step.Mastery:
                Toggle(_masteries, MasteryRows()[index].Id, CharacterCreation.MasteryAllowance(_class!, 1));
                return;
            case Step.IncreasePrimary:
                _primaryIncrease = _background!.AbilityScores[index];
                _step = Step.IncreaseSecondary;
                _browsed = -1;
                return;
            case Step.IncreaseSecondary:
                _secondaryIncrease = _background!.AbilityScores
                    .Where(ability => ability != _primaryIncrease)
                    .ElementAt(index);
                AdvancePastIncreases();
                return;
            default:
                _browsed = index;
                return;
        }
    }

    private static void Toggle(List<string> chosen, string id, int allowance)
    {
        if (!chosen.Remove(id) && chosen.Count < allowance)
        {
            chosen.Add(id);
        }
    }

    // ── The steps ───────────────────────────────────────────────────────────────

    private void CommitName()
    {
        if (string.IsNullOrWhiteSpace(_name))
        {
            _name = $"Hero {_drafts.Count + 1}";
        }

        _step = Step.Class;
        _browsed = -1;
    }

    private void Take()
    {
        if (_browsed < 0)
        {
            return;
        }

        switch (_step)
        {
            case Step.Class:
                _class = CharacterCreation.Classes(_content)[_browsed];
                _step = Step.Species;
                break;
            case Step.Species:
                _species = CharacterCreation.Species(_content)[_browsed];
                _step = Step.Background;
                break;
            case Step.Background:
                _background = CharacterCreation.Backgrounds(_content)[_browsed];
                _step = Step.Method;
                break;
            case Step.Style:
                _style = _browsed switch
                {
                    0 => FightingStyle.Archery,
                    1 => FightingStyle.Defense,
                    _ => FightingStyle.Unspecified,
                };
                _step = AfterStyle();
                break;
            case Step.Order:
                _divineOrder = _browsed switch
                {
                    0 => DivineOrder.Protector,
                    1 => DivineOrder.Thaumaturge,
                    _ => DivineOrder.Unspecified,
                };
                _step = Step.Weapon;
                break;
            case Step.Weapon:
                _weaponId = CharacterCreation.WeaponOptions(_content, _class!, _divineOrder)[_browsed].Id;
                _step = Step.Armor;
                break;
            case Step.Armor:
                var armors = CharacterCreation.ArmorOptions(_content, _class!, _divineOrder);
                _armorId = _browsed < armors.Count ? armors[_browsed].Id : null;
                _step = CharacterCreation.MayCarryShield(_class!) ? Step.Shield : AfterShield();
                break;
            case Step.Spells:
                Toggle(_spells, SpellRows()[_browsed].Id, int.MaxValue);
                return;
            default:
                return;
        }

        _browsed = -1;
        _rowScroll = 0;
        _textScroll = 0;
    }

    private void ChooseMethod(bool pointBuy)
    {
        if (pointBuy)
        {
            _scores = Enum.GetValues<Ability>().ToDictionary(ability => ability, _ => 8);
            _step = Step.PointBuy;
        }
        else
        {
            _scores = [];
            _arrayRemaining = [.. AbilityScoreRules.StandardArray];
            _step = Step.ArrayAssign;
        }
    }

    /// <summary>Array assignment: a click on a remaining value gives it to the next unassigned ability.</summary>
    private void AssignArrayValue(int index)
    {
        if (index >= _arrayRemaining.Count)
        {
            return;
        }

        var ability = Enum.GetValues<Ability>().First(candidate => !_scores.ContainsKey(candidate));
        _scores[ability] = _arrayRemaining[index];
        _arrayRemaining.RemoveAt(index);

        if (_scores.Count == 6)
        {
            _step = Step.IncreaseShape;
        }
    }

    private void AdjustPointBuy(Ability ability, int direction)
    {
        var target = _scores[ability] + direction;

        if (target is < 8 or > 15)
        {
            return;
        }

        var trial = new Dictionary<Ability, int>(_scores) { [ability] = target };

        if (AbilityScoreRules.PointCost(trial) is { } cost && cost <= AbilityScoreRules.PointBudget)
        {
            _scores = trial;
        }
    }

    private void ChooseIncreaseShape(AbilityIncreaseChoice shape)
    {
        _increaseShape = shape;

        if (shape == AbilityIncreaseChoice.OneEach)
        {
            AdvancePastIncreases();
        }
        else
        {
            _step = Step.IncreasePrimary;
        }
    }

    private void AdvancePastIncreases()
    {
        _step = Step.Skills;
        _browsed = -1;
        _rowScroll = 0;
    }

    private void FinishSkills()
    {
        if (_skills.Count != SkillAllowance())
        {
            return;
        }

        _step = CharacterCreation.GrantsFightingStyle(_class!, 1) ? Step.Style : AfterStyle();
        _browsed = -1;
        _rowScroll = 0;
    }

    /// <summary>
    /// Divine Order sits between Style and Weapon on purpose: Protector widens the
    /// weapon and armor menus the next two steps draw.
    /// </summary>
    private Step AfterStyle() =>
        CharacterCreation.GrantsDivineOrder(_class!, 1) ? Step.Order : Step.Weapon;

    private Step AfterShield() =>
        CharacterCreation.MasteryAllowance(_class!, 1) > 0 ? Step.Mastery
        : CharacterCreation.SpellOptions(_content, _class!).Count > 0 ? Step.Spells
        : Step.Summary;

    private void ChooseShield(bool carry)
    {
        _shield = carry;
        _step = AfterShield();
        _browsed = -1;
    }

    private void FinishMastery()
    {
        _step = CharacterCreation.SpellOptions(_content, _class!).Count > 0 ? Step.Spells : Step.Summary;
        _browsed = -1;
        _rowScroll = 0;
    }

    private void FinishSpells()
    {
        _step = Step.Summary;
        _browsed = -1;
    }

    /// <summary>Builds the draft and asks the resolver — whose word is final — for the preview.</summary>
    private CharacterDraft BuildDraft()
    {
        var draft = new CharacterDraft
        {
            Name = _name,
            ClassId = _class!.Id,
            SpeciesId = _species!.Id,
            BackgroundId = _background!.Id,
            Level = 1,
            BaseAbilityScores = _scores,
            IncreaseChoice = _increaseShape,
            ChosenSkills = [.. _skills],
            FightingStyle = _style,
            DivineOrder = _divineOrder,
            WeaponIds = _weaponId is null ? [] : [_weaponId],
            ArmorId = _armorId,
            HasShield = _shield,
            WeaponMasteryIds = [.. _masteries],
            ChosenSpellIds = [.. _spells],
        };

        return _increaseShape == AbilityIncreaseChoice.TwoAndOne
            ? draft with { PrimaryIncrease = _primaryIncrease, SecondaryIncrease = _secondaryIncrease }
            : draft;
    }

    private void Keep()
    {
        if (_preview is null)
        {
            return;
        }

        _drafts.Add(BuildDraft());

        if (_drafts.Count == 4)
        {
            var play = new PlayMode { CreatedDrafts = _drafts.ToArray() };
            GetParent().AddChild(play);
            QueueFree();
            return;
        }

        StartOver();
    }

    private void StartOver()
    {
        _name = string.Empty;
        _class = null;
        _species = null;
        _background = null;
        _scores = [];
        _arrayRemaining = [];
        _primaryIncrease = null;
        _secondaryIncrease = null;
        _increaseShape = AbilityIncreaseChoice.TwoAndOne;
        _skills.Clear();
        _style = FightingStyle.Unspecified;
        _divineOrder = DivineOrder.Unspecified;
        _weaponId = null;
        _armorId = null;
        _shield = false;
        _masteries.Clear();
        _spells.Clear();
        _refusal = null;
        _preview = null;
        _browsed = -1;
        _rowScroll = 0;
        _textScroll = 0;
        _step = Step.Name;
    }

    // ── Drawing ─────────────────────────────────────────────────────────────────

    private const int RowsX = 24;
    private const int RowsWidth = 270;
    private const int PanelX = 320;
    private const int RowHeight = 24;
    private const int RowsTop = 96;
    private const int VisibleRows = 25;

    public override void _Draw()
    {
        _rows.Clear();
        _actions.Clear();

        DrawRect(new Rect2(0, 0, ScreenWidth, ScreenHeight), Background);
        DrawString(TextFont, new Vector2(24, 34), Title, fontSize: 20, modulate: Ink);
        DrawString(
            TextFont,
            new Vector2(24, 58),
            $"Character {_drafts.Count + 1} of 4 — {Caption(_step)}",
            fontSize: 14,
            modulate: Dim);
        DrawString(
            TextFont,
            new Vector2(24, 78),
            "Click an option to read its printed text; Take commits it.",
            fontSize: 12,
            modulate: Dim);

        switch (_step)
        {
            case Step.Name:
                DrawName();
                break;
            case Step.Method:
                DrawMethod();
                break;
            case Step.ArrayAssign:
                DrawArrayAssign();
                break;
            case Step.PointBuy:
                DrawPointBuy();
                break;
            case Step.IncreaseShape:
                DrawIncreaseShape();
                break;
            case Step.Shield:
                DrawShield();
                break;
            case Step.Summary:
                DrawSummary();
                break;
            default:
                DrawRowsAndPanel();
                break;
        }
    }

    private void DrawName()
    {
        DrawString(TextFont, new Vector2(RowsX, 140), "Type a name, then continue:", fontSize: 14, modulate: Ink);
        DrawRect(new Rect2(RowsX, 156, 300, 30), GridLine);
        DrawString(TextFont, new Vector2(RowsX + 8, 177), _name + "_", fontSize: 15, modulate: Ink);
        Action(new Vector2(RowsX, 210), "Continue", CommitName);
    }

    private void DrawMethod()
    {
        DrawString(
            TextFont,
            new Vector2(RowsX, 140),
            "Ability scores — the printed methods:",
            fontSize: 14,
            modulate: Ink);
        DrawString(
            TextFont,
            new Vector2(RowsX, 170),
            "Standard Array: assign 15, 14, 13, 12, 10, 8.",
            fontSize: 13,
            modulate: Dim);
        DrawString(
            TextFont,
            new Vector2(RowsX, 190),
            $"Point Buy: {AbilityScoreRules.PointBudget} points; 8:0  9:1  10:2  11:3  12:4  13:5  14:7  15:9.",
            fontSize: 13,
            modulate: Dim);

        Action(new Vector2(RowsX, 220), "Standard Array", () => ChooseMethod(pointBuy: false));
        Action(new Vector2(RowsX + 160, 220), "Point Buy", () => ChooseMethod(pointBuy: true));
    }

    private void DrawArrayAssign()
    {
        var y = 140;

        foreach (var ability in Enum.GetValues<Ability>())
        {
            var value = _scores.TryGetValue(ability, out var assigned) ? assigned.ToString() : "—";
            var next = !_scores.ContainsKey(ability)
                && Enum.GetValues<Ability>().First(a => !_scores.ContainsKey(a)) == ability;

            DrawString(
                TextFont,
                new Vector2(RowsX, y),
                $"{ability,-13} {value}{(next ? "   ← next" : string.Empty)}",
                fontSize: 14,
                modulate: next ? Ink : Dim);
            y += 22;
        }

        DrawString(TextFont, new Vector2(RowsX, y + 14), "Click a value to assign it:", fontSize: 13, modulate: Ink);

        var x = (float)RowsX;

        for (var index = 0; index < _arrayRemaining.Count; index++)
        {
            var rect = new Rect2(x, y + 26, 44, 26);
            DrawRect(rect, GridLine);
            DrawString(TextFont, rect.Position + new Vector2(12, 19), _arrayRemaining[index].ToString(), fontSize: 14, modulate: Ink);
            _rows.Add((rect, index));
            x += 52;
        }
    }

    private void DrawPointBuy()
    {
        var spent = AbilityScoreRules.PointCost(_scores)!.Value;
        var y = 140;

        foreach (var ability in Enum.GetValues<Ability>())
        {
            DrawString(TextFont, new Vector2(RowsX, y + 18), $"{ability,-13} {_scores[ability],2}", fontSize: 14, modulate: Ink);

            var minus = new Rect2(RowsX + 170, y, 26, 24);
            var plus = new Rect2(RowsX + 202, y, 26, 24);
            var held = ability;

            DrawRect(minus, GridLine);
            DrawString(TextFont, minus.Position + new Vector2(9, 17), "−", fontSize: 14, modulate: Ink);
            DrawRect(plus, GridLine);
            DrawString(TextFont, plus.Position + new Vector2(8, 17), "+", fontSize: 14, modulate: Ink);

            _actions.Add((minus, $"minus-{ability}", () => AdjustPointBuy(held, -1)));
            _actions.Add((plus, $"plus-{ability}", () => AdjustPointBuy(held, +1)));
            y += 30;
        }

        DrawString(
            TextFont,
            new Vector2(RowsX, y + 16),
            $"{AbilityScoreRules.PointBudget - spent} points left",
            fontSize: 14,
            modulate: Ink);
        Action(new Vector2(RowsX, y + 32), "Done", () => _step = Step.IncreaseShape);
    }

    private void DrawIncreaseShape()
    {
        DrawString(
            TextFont,
            new Vector2(RowsX, 140),
            $"{_background!.Name} raises {string.Join(", ", _background.AbilityScores)}:",
            fontSize: 14,
            modulate: Ink);

        Action(new Vector2(RowsX, 170), "+2 to one and +1 to another", () => ChooseIncreaseShape(AbilityIncreaseChoice.TwoAndOne));
        Action(new Vector2(RowsX, 210), "+1 to each of the three", () => ChooseIncreaseShape(AbilityIncreaseChoice.OneEach));
    }

    private void DrawShield()
    {
        DrawString(TextFont, new Vector2(RowsX, 140), "Carry a Shield? (+2 AC)", fontSize: 14, modulate: Ink);
        Action(new Vector2(RowsX, 170), "Shield", () => ChooseShield(true));
        Action(new Vector2(RowsX + 120, 170), "No Shield", () => ChooseShield(false));
    }

    private void DrawSummary()
    {
        _preview = null;
        _refusal = null;

        try
        {
            _preview = PregeneratedParty.Resolve(_content, BuildDraft(), level: 1);
        }
        catch (Exception refusal) when (refusal is ArgumentException or InvalidOperationException)
        {
            _refusal = refusal.Message;
        }

        if (_refusal is not null)
        {
            DrawWrapped($"The rules refuse this character: {_refusal}", new Vector2(RowsX, 140), 110);
            Action(new Vector2(RowsX, 240), "Start over", StartOver);
            return;
        }

        var sheet = _preview!.Sheet;
        var y = 140f;

        void Line(string text, Color colour)
        {
            DrawString(TextFont, new Vector2(RowsX, y), text, fontSize: 14, modulate: colour);
            y += 22;
        }

        Line($"{sheet.Name} — {_species!.Name} {_class!.Name} ({_background!.Name})", Ink);
        Line($"AC {sheet.ArmorClass}   {sheet.MaximumHitPoints} hit points   Speed {sheet.SpeedFeet} ft.", Ink);
        Line(
            string.Join("   ", Enum.GetValues<Ability>().Select(ability =>
                $"{ability.ToString()[..3].ToUpperInvariant()} {sheet.AbilityScores[ability]}")),
            Dim);

        if (_preview.Combatant.Stats.Character is { Spells.Count: > 0 } caster)
        {
            Line($"Prepares: {string.Join(", ", caster.Spells.Select(spell => spell.Name))}.", Dim);
        }

        if (sheet.DivineOrder != DivineOrder.Unspecified)
        {
            Line($"Divine Order: {sheet.DivineOrder}.", Dim);
        }

        if (sheet.UnimplementedFeatures.Count > 0)
        {
            y += 6;
            DrawWrapped(
                $"Printed but not yet implemented: {string.Join(", ", sheet.UnimplementedFeatures)}.",
                new Vector2(RowsX, y),
                110);
            y += 44;
        }

        Action(new Vector2(RowsX, y + 16), "Keep this character", Keep);
        Action(new Vector2(RowsX + 210, y + 16), "Start over", StartOver);
    }

    // ── The browse-and-take steps ───────────────────────────────────────────────

    private void DrawRowsAndPanel()
    {
        var rows = RowCaptions();

        for (var visible = 0; visible < VisibleRows; visible++)
        {
            var index = _rowScroll + visible;

            if (index >= rows.Count)
            {
                break;
            }

            var rect = new Rect2(RowsX, RowsTop + (visible * RowHeight), RowsWidth, RowHeight - 2);
            var chosen = IsChosen(index);
            var marker = chosen ? "✓ " : "  ";

            if (index == _browsed)
            {
                DrawRect(rect, GridLine);
            }

            DrawString(
                TextFont,
                rect.Position + new Vector2(4, 16),
                Trim(marker + rows[index], 40),
                fontSize: 13,
                modulate: chosen ? ActiveRing : Ink);
            _rows.Add((rect, index));
        }

        if (rows.Count > VisibleRows)
        {
            DrawString(
                TextFont,
                new Vector2(RowsX, RowsTop + (VisibleRows * RowHeight) + 14),
                $"scroll for more ({rows.Count} options)",
                fontSize: 11,
                modulate: Dim);
        }

        DrawPanel();
        DrawStepActions();
    }

    private void DrawStepActions()
    {
        var y = (float)(ScreenHeight - 60);

        switch (_step)
        {
            case Step.Class or Step.Species or Step.Background or Step.Style or Step.Order or Step.Weapon or Step.Armor:
                if (_browsed >= 0)
                {
                    Action(new Vector2(RowsX, y), $"Take {Trim(RowCaptions()[_browsed], 22)}", Take);
                }

                break;
            case Step.Skills:
                DrawString(
                    TextFont,
                    new Vector2(RowsX, y - 10),
                    $"{_skills.Count} of {SkillAllowance()} chosen — click to toggle.",
                    fontSize: 12,
                    modulate: Dim);

                if (_skills.Count == SkillAllowance())
                {
                    Action(new Vector2(RowsX, y), "Continue", FinishSkills);
                }

                break;
            case Step.Mastery:
                DrawString(
                    TextFont,
                    new Vector2(RowsX, y - 10),
                    $"{_masteries.Count} of up to {CharacterCreation.MasteryAllowance(_class!, 1)} — click to toggle.",
                    fontSize: 12,
                    modulate: Dim);
                Action(new Vector2(RowsX, y), "Continue", FinishMastery);
                break;
            case Step.IncreasePrimary or Step.IncreaseSecondary:
                DrawString(
                    TextFont,
                    new Vector2(RowsX, y - 10),
                    _step == Step.IncreasePrimary ? "Click the ability taking +2." : "Click the ability taking +1.",
                    fontSize: 12,
                    modulate: Dim);
                break;
            case Step.Spells:
                if (_browsed >= 0)
                {
                    var id = SpellRows()[_browsed].Id;
                    Action(
                        new Vector2(RowsX, y),
                        _spells.Contains(id) ? "Put back" : $"Take {Trim(SpellRows()[_browsed].Name, 18)}",
                        Take);
                }

                Action(new Vector2(RowsX + 220, y), "Done", FinishSpells);
                break;
            default:
                break;
        }
    }

    private void DrawPanel()
    {
        if (_browsed < 0 || _browsed >= RowCaptions().Count)
        {
            DrawString(
                TextFont,
                new Vector2(PanelX, RowsTop + 8),
                "Click an option to read about it.",
                fontSize: 13,
                modulate: Dim);
            return;
        }

        var lines = PanelLines().Skip(_textScroll).Take(30).ToArray();
        var y = RowsTop + 8f;

        foreach (var line in lines)
        {
            DrawString(TextFont, new Vector2(PanelX, y), line, fontSize: 13, modulate: Ink);
            y += 20;
        }
    }

    // ── What each step lists and describes ──────────────────────────────────────

    private IReadOnlyList<string> RowCaptions() => _step switch
    {
        Step.Class => CharacterCreation.Classes(_content).Select(definition => definition.Name).ToArray(),
        Step.Species => CharacterCreation.Species(_content).Select(definition => definition.Name).ToArray(),
        Step.Background => CharacterCreation.Backgrounds(_content).Select(definition => definition.Name).ToArray(),
        Step.IncreasePrimary => _background!.AbilityScores.Select(ability => ability.ToString()).ToArray(),
        Step.IncreaseSecondary => _background!.AbilityScores
            .Where(ability => ability != _primaryIncrease)
            .Select(ability => ability.ToString())
            .ToArray(),
        Step.Skills => SkillOptions().Select(skill => $"{skill} ({SkillRules.AbilityFor(skill)})").ToArray(),
        Step.Style =>
        [
            "Archery — +2 to ranged attack rolls",
            "Defense — +1 AC while wearing armor",
            "Skip — take an unexecuted printed style",
        ],
        Step.Order =>
        [
            "Protector — Martial weapons and Heavy armor",
            "Thaumaturge — an extra cantrip, Arcana/Religion bonus",
            "Skip — leave the printed feature unimplemented",
        ],
        Step.Weapon => CharacterCreation.WeaponOptions(_content, _class!, _divineOrder)
            .Select(weapon => $"{weapon.Name} ({weapon.Damage} {weapon.DamageType})")
            .ToArray(),
        Step.Armor => CharacterCreation.ArmorOptions(_content, _class!, _divineOrder)
            .Select(armor => $"{armor.Name} ({armor.Category})")
            .Append("No armor")
            .ToArray(),
        Step.Mastery => MasteryRows().Select(weapon => $"{weapon.Name} — {weapon.Mastery}").ToArray(),
        Step.Spells => SpellRows()
            .Select(spell => $"{spell.Name} ({(spell.IsCantrip ? "cantrip" : $"level {spell.Level}")})")
            .ToArray(),
        _ => [],
    };

    private IReadOnlyList<string> PanelLines()
    {
        var text = _step switch
        {
            Step.Class => DescribeClass(CharacterCreation.Classes(_content)[_browsed]),
            Step.Species => DescribeSpecies(CharacterCreation.Species(_content)[_browsed]),
            Step.Background => DescribeBackground(CharacterCreation.Backgrounds(_content)[_browsed]),
            Step.Skills => $"Proficiency in {SkillOptions()[Math.Min(_browsed, SkillOptions().Count - 1)]}.",
            Step.Style => "The Fighting Style feat. The two listed are the styles the engine executes; " +
                "Skip records an unexecuted printed style, reported on the sheet.",

            // The printed feature text itself, per the charter — both roles execute, so
            // unlike Style there is no inert pick here.
            Step.Order => CharacterCreation.DivineOrderText(_class!),
            Step.Weapon => DescribeWeapon(CharacterCreation.WeaponOptions(_content, _class!, _divineOrder)[_browsed]),
            Step.Armor => _browsed < CharacterCreation.ArmorOptions(_content, _class!, _divineOrder).Count
                ? $"{CharacterCreation.ArmorOptions(_content, _class!, _divineOrder)[_browsed].Name}, " +
                  $"{CharacterCreation.ArmorOptions(_content, _class!, _divineOrder)[_browsed].Category} armor."
                : "No armor: Unarmored Defense where a class grants it, bare AC otherwise.",
            Step.Mastery => $"{MasteryRows()[_browsed].Name}: the {MasteryRows()[_browsed].Mastery} mastery property.",
            Step.Spells => DescribeSpell(SpellRows()[_browsed]),
            _ => string.Empty,
        };

        return Wrap(text, 74);
    }

    private static string DescribeClass(ClassDefinition definition)
    {
        var features = string.Join("\n\n", definition.Features
            .Where(feature => feature.GrantedAtLevel == 1)
            .Select(feature => $"{feature.Name}. {feature.Text}"));

        return $"{definition.Name} — d{definition.HitDieSides} hit die; primary " +
            $"{string.Join(" or ", definition.PrimaryAbilities)}; saves " +
            $"{string.Join(" and ", definition.SavingThrowProficiencies)}.\n\n" +
            $"Weapons: {definition.WeaponProficiencies}. Armor: " +
            $"{(string.IsNullOrEmpty(definition.ArmorTraining) ? "none" : definition.ArmorTraining)}.\n\n{features}";
    }

    private static string DescribeSpecies(SpeciesDefinition definition) =>
        $"{definition.Name} — {definition.Sizes[0]}, Speed {definition.SpeedFeet} ft.\n\n" +
        string.Join("\n\n", definition.Traits.Select(trait => $"{trait.Name}. {trait.Text}"));

    private static string DescribeBackground(BackgroundDefinition definition) =>
        $"{definition.Name} — raises {string.Join(", ", definition.AbilityScores)}.\n\n" +
        $"Feat: {definition.FeatName}. Skills: {string.Join(", ", definition.SkillProficiencies)}. " +
        $"Tool: {definition.ToolProficiency}.\n\nEquipment: {definition.Equipment}";

    private static string DescribeWeapon(WeaponDefinition weapon) =>
        $"{weapon.Name} — {weapon.Category} weapon, {weapon.Damage} {weapon.DamageType} damage" +
        (weapon.Properties == WeaponProperty.None ? "" : $", properties: {weapon.Properties}") +
        $". Mastery property: {weapon.Mastery}.";

    private static string DescribeSpell(SpellDefinition spell) =>
        $"{spell.Name} — {spell.CastingTimeText}, {spell.RangeText}, {spell.DurationText}.\n\n{spell.Text}";

    private IReadOnlyList<string> SkillOptions() => CharacterCreation.SkillChoices(_class!).Options;

    private int SkillAllowance() => CharacterCreation.SkillChoices(_class!).Count;

    private IReadOnlyList<WeaponDefinition> MasteryRows() =>
        CharacterCreation.MasteryOptions(_content, _class!, _divineOrder);

    private IReadOnlyList<SpellDefinition> SpellRows() => CharacterCreation.SpellOptions(_content, _class!);

    private bool IsChosen(int index) => _step switch
    {
        Step.Skills => _skills.Contains(SkillOptions()[index]),
        Step.Mastery => _masteries.Contains(MasteryRows()[index].Id),
        Step.Spells => _spells.Contains(SpellRows()[index].Id),
        _ => false,
    };

    private static string Caption(Step step) => step switch
    {
        Step.Name => "name",
        Step.Class => "class",
        Step.Species => "species",
        Step.Background => "background",
        Step.Method or Step.ArrayAssign or Step.PointBuy => "ability scores",
        Step.IncreaseShape or Step.IncreasePrimary or Step.IncreaseSecondary => "background increases",
        Step.Skills => "skills",
        Step.Style => "fighting style",
        Step.Order => "divine order",
        Step.Weapon => "weapon",
        Step.Armor => "armor",
        Step.Shield => "shield",
        Step.Mastery => "weapon mastery",
        Step.Spells => "spells",
        Step.Summary => "summary",
        _ => "?",
    };

    // ── Buttons, wrapping ───────────────────────────────────────────────────────

    private void Action(Vector2 position, string caption, Action act)
    {
        var width = (caption.Length * 8) + 20;
        var rect = new Rect2(position, new Vector2(width, 28));

        DrawRect(rect, GridLine);
        DrawString(TextFont, position + new Vector2(10, 19), caption, fontSize: 13, modulate: Ink);
        _actions.Add((rect, caption, act));
    }

    private void DrawWrapped(string text, Vector2 position, int width)
    {
        var y = position.Y;

        foreach (var line in Wrap(text, width))
        {
            DrawString(TextFont, new Vector2(position.X, y), line, fontSize: 13, modulate: Ink);
            y += 20;
        }
    }

    private static IReadOnlyList<string> Wrap(string text, int width)
    {
        var lines = new List<string>();

        foreach (var paragraph in text.Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var line = string.Empty;

            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length + word.Length + 1 > width && line.Length > 0)
                {
                    lines.Add(line);
                    line = string.Empty;
                }

                line += (line.Length > 0 ? " " : string.Empty) + word;
            }

            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    // ── The probe ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Drives creation through the real input path — synthesized clicks — capturing
    /// each step, then keeps a character and repeats until the run starts. The same
    /// verification loop the play screen has, pointed at this screen.
    /// </summary>
    private void RunProbeIfAsked()
    {
        if (_probeStarted || ArgumentValue("probe") is not { } directory)
        {
            return;
        }

        _probeStarted = true;
        RunProbe(directory);
    }

    private async void RunProbe(string directory)
    {
        var spellCaptured = false;

        for (var character = 1; character <= 4; character++)
        {
            var first = character == 1;

            await Frame();
            ClickAction("Continue");

            await Frame();

            if (first)
            {
                await CaptureFrame(Path.Combine(directory, "create-1-classes.png"));
            }

            // A different class each time — Fighter, Rogue, Cleric, Wizard — so the
            // probe walks every menu shape: a style, four skills, no armor, a spell
            // list. A probe that made four Fighters would never see the caster steps.
            ClickRow(character - 1);
            await Frame();

            if (first)
            {
                await CaptureFrame(Path.Combine(directory, "create-2-class-browsed.png"));
            }

            ClickTake();
            await Frame();
            ClickRow(0);
            await Frame();
            ClickTake();
            await Frame();
            ClickRow(0);
            await Frame();
            ClickTake();
            await Frame();

            if (first)
            {
                await CaptureFrame(Path.Combine(directory, "create-3-method.png"));
            }

            ClickAction("Standard Array");

            for (var chip = 0; chip < 6; chip++)
            {
                await Frame();
                ClickRow(0);
            }

            await Frame();
            ClickAction("+2 to one and +1 to another");
            await Frame();
            ClickRow(0);
            await Frame();
            ClickRow(0);

            // Skills, then everything class-shaped: the loop clicks the first rows and
            // the step's own Continue/Done/Take buttons wherever they appear.
            await Frame();

            for (var pick = 0; pick < 6 && _step == Step.Skills; pick++)
            {
                ClickRow(pick);
                await Frame();
            }

            ClickAction("Continue");
            await Frame();

            if (_step == Step.Style)
            {
                ClickRow(0);
                await Frame();
                ClickTake();
                await Frame();
            }

            // The Cleric's Divine Order: the probe takes Protector, so the widened
            // weapon and armor menus are the ones it walks next.
            if (_step == Step.Order)
            {
                ClickRow(0);
                await Frame();
                ClickTake();
                await Frame();
            }

            ClickRow(0);
            await Frame();

            if (first)
            {
                await CaptureFrame(Path.Combine(directory, "create-4-weapon-browsed.png"));
            }

            ClickTake();
            await Frame();
            ClickRow(0);
            await Frame();
            ClickTake();
            await Frame();

            if (_step == Step.Shield)
            {
                ClickAction("No Shield");
                await Frame();
            }

            if (_step == Step.Mastery)
            {
                ClickRow(0);
                await Frame();
                ClickAction("Continue");
                await Frame();
            }

            if (_step == Step.Spells)
            {
                ClickRow(0);
                await Frame();
                ClickTake();
                await Frame();

                if (!spellCaptured)
                {
                    spellCaptured = true;
                    await CaptureFrame(Path.Combine(directory, "create-5-spell-taken.png"));
                }

                ClickAction("Done");
                await Frame();
            }

            if (first)
            {
                await CaptureFrame(Path.Combine(directory, "create-6-summary.png"));
            }

            // The fourth Keep frees this node and hands the drafts to the play screen,
            // whose own probe takes the story from there — run-0-interlude.png is the
            // capture that proves the handoff.
            ClickAction("Keep this character");
            await Frame();
        }
    }

    private async Task Frame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void ClickRow(int index)
    {
        var row = _rows.FirstOrDefault(candidate => candidate.Index == index);

        if (row.Rect.Size != Vector2.Zero)
        {
            Click(row.Rect.GetCenter());
        }
    }

    private void ClickTake()
    {
        var take = _actions.FirstOrDefault(candidate => candidate.Name.StartsWith("Take", StringComparison.Ordinal));

        if (take.Name is not null)
        {
            Click(take.Rect.GetCenter());
        }
    }

    private void ClickAction(string name)
    {
        var action = _actions.FirstOrDefault(candidate => candidate.Name == name);

        if (action.Name == name)
        {
            Click(action.Rect.GetCenter());
        }
    }

    /// <summary>A real click, pushed through the viewport, not a call around the input layer.</summary>
    private void Click(Vector2 position)
    {
        GetViewport().PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = position,
            GlobalPosition = position,
        });
    }
}
