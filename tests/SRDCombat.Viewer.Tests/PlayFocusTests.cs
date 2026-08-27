using SRDCombat.Game;
using SRDCombat.Viewer.Ui;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// The landing table: what each focus answers to the five questions the screen asks of
/// whatever has the player's attention.
/// </summary>
/// <remarks>
/// <para>
/// #327's fourth acceptance criterion, made concrete. Each column replaces an expression
/// that used to be written by hand in <c>PlayMode</c>, where a new modal could simply fail
/// to appear in one of them — and where the failure was silent, because a modal that
/// answers nothing still draws.
/// </para>
/// <para>
/// <b>The rows are typed out, not derived.</b> Reading the answers off the type under test
/// would pass for any table at all.
/// </para>
/// <para>
/// The theory travels by focus <em>name</em> rather than by focus: <c>PlayFocus</c> is
/// <c>internal</c>, and a public test method may not name it. The lookup below is the whole
/// of that workaround.
/// </para>
/// </remarks>
public class PlayFocusTests
{
    private sealed record Row(
        PlayFocus Focus,
        EscapeMeaning Escape,
        bool TakesRowKeys,
        bool SuppressesHotkeys,
        bool SuppressesBoard,
        bool HoldsTurnOpen);

    private static readonly Dictionary<string, Row> Table = new(StringComparer.Ordinal)
    {
        ["Board"] = new(new PlayFocus.Board(), EscapeMeaning.AskToQuit, false, false, false, false),
        ["AttackMenu"] = new(new PlayFocus.AttackMenu(), EscapeMeaning.DropToBoard, true, false, false, true),
        ["SpellMenu"] = new(new PlayFocus.SpellMenu(), EscapeMeaning.DropToBoard, true, false, false, true),
        ["SlotMenu"] = new(new PlayFocus.SlotMenu(FightTestData.AnySpell()), EscapeMeaning.DropToBoard, true, false, false, true),
        ["Targeting"] = new(new PlayFocus.Targeting(TargetKind.Attack), EscapeMeaning.DropToBoard, false, true, false, true),

        // S2 (#501). Two rows where the obvious answer is wrong, both preserved verbatim:
        // the stall suppresses the board (a guard that never fires, kept deliberately), and
        // the card's Esc *commits* rather than cancelling.
        ["Shop"] = new(new PlayFocus.Shop(), EscapeMeaning.CloseSelf, false, false, true, false),
        ["Outcome"] = new(new PlayFocus.Outcome(), EscapeMeaning.Commit, false, false, false, false),

        // S3 (#502). HoldsTurnOpen is false on purpose: _Process never asks whether this
        // card is up, so an auto-end-turn can fire underneath it. That is today's
        // behaviour and #510 is where it is questioned — not here.
        ["QuitConfirm"] = new(new PlayFocus.QuitConfirm(), EscapeMeaning.LeaveTheGame, false, false, false, false),
    };

    public static TheoryData<string> FocusNames
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var name in Table.Keys)
            {
                data.Add(name);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(FocusNames))]
    public void EachFocusAnswersItsRowOfTheLandingTable(string name)
    {
        var row = Table[name];

        Assert.Equal(name, row.Focus.GetType().Name);
        Assert.Equal(row.Escape, row.Focus.Escape);
        Assert.Equal(row.TakesRowKeys, row.Focus.TakesRowKeys);
        Assert.Equal(row.SuppressesHotkeys, row.Focus.SuppressesHotkeys);
        Assert.Equal(row.SuppressesBoard, row.Focus.SuppressesBoard);
        Assert.Equal(row.HoldsTurnOpen, row.Focus.HoldsTurnOpen);
    }

    /// <summary>
    /// Every concrete focus in the client appears in the landing table above.
    /// </summary>
    /// <remarks>
    /// Without this, a subtype added in S2 or S3 could ship with no row asserted and the
    /// table would still be green — documenting a subset of the screen rather than the
    /// screen. Reflection precisely because the point is to catch the type nobody
    /// remembered to add here.
    /// </remarks>
    [Fact]
    public void TheLandingTableCoversEveryFocusInTheAssembly()
    {
        var concrete = typeof(PlayMode).Assembly
            .GetTypes()
            .Where(type => type.IsSubclassOf(typeof(PlayFocus)) && !type.IsAbstract)
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(concrete, Table.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Every <see cref="EscapeMeaning"/> routes somewhere.
    /// </summary>
    /// <remarks>
    /// <c>CloseSelf</c> has no focus answering it in this slice, so nothing else would
    /// exercise it — and a meaning with no route is a modal whose Esc key does nothing,
    /// which is exactly the silence this refactor exists to end. Enumerated rather than
    /// listed, so a sixth meaning fails here rather than at a player's keyboard.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryEscapeMeaning))]
    public void EveryEscapeMeaningHasARoute(string meaning)
    {
        var route = PlayFocusRouter.EscapeRoute(Enum.Parse<EscapeMeaning>(meaning));

        Assert.NotEqual(RouteAction.Unhandled, route);
        Assert.NotEqual(RouteAction.Ignore, route);
    }

    public static TheoryData<string> EveryEscapeMeaning
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var meaning in Enum.GetNames<EscapeMeaning>())
            {
                data.Add(meaning);
            }

            return data;
        }
    }

    /// <summary>
    /// Esc on the outcome card advances the run rather than dismissing it.
    /// </summary>
    /// <remarks>
    /// Asserted on its own as well as in the table, because this is the row a later editor
    /// is most likely to "tidy": every other modal in the client backs out on Esc, and this
    /// one awards experience, loot and the autosave. Getting it wrong loses a fight's
    /// rewards and nothing on screen would say so.
    /// </remarks>
    [Fact]
    public void TheOutcomeCardCommitsOnEscapeRatherThanCancelling()
    {
        Assert.Equal(EscapeMeaning.Commit, new PlayFocus.Outcome().Escape);
        Assert.NotEqual(EscapeMeaning.CloseSelf, new PlayFocus.Outcome().Escape);
        Assert.NotEqual(EscapeMeaning.DropToBoard, new PlayFocus.Outcome().Escape);
    }

    /// <summary>
    /// The stall carries its own notice, so closing it clears the notice by construction.
    /// </summary>
    /// <remarks>
    /// The notice used to be a second field beside the open/closed flag, and Esc had to
    /// remember to null it. A purchase is now one <c>ReplaceTop</c>, and the pop takes the
    /// notice with it — there is no longer a pair to leave half-done.
    /// </remarks>
    [Fact]
    public void TheShopsNoticeRidesTheLayerAndLeavesWithIt()
    {
        var focus = new FocusStack<PlayFocus>(new PlayFocus.Board());
        focus.Push(new PlayFocus.Shop());

        Assert.Null(focus.Topmost<PlayFocus.Shop>()!.Notice);

        focus.ReplaceTop(new PlayFocus.Shop("Bought: a Potion of Healing."));

        Assert.Equal("Bought: a Potion of Healing.", focus.Topmost<PlayFocus.Shop>()!.Notice);

        focus.Pop();

        Assert.Null(focus.Topmost<PlayFocus.Shop>());
    }

    /// <summary>
    /// The quit card does not hold the turn open, and that is a preserved oddity rather
    /// than a considered answer.
    /// </summary>
    /// <remarks>
    /// <c>_Process</c> never asks whether this card is up, so <c>NothingLeftButEndTurn</c>
    /// can end a turn underneath "LEAVE THE GAME?". #510 is the issue that questions it. A
    /// no-behaviour-change refactor is not the place to fix it, and asserting it here is
    /// what stops it being fixed by accident.
    /// </remarks>
    [Fact]
    public void TheQuitCardDoesNotHoldTheTurnOpenAndThatIsDeliberatelyPreserved()
    {
        Assert.False(new PlayFocus.QuitConfirm().HoldsTurnOpen);
    }

    [Fact]
    public void CloseSelfClosesOneLayerRatherThanDroppingToTheBoard()
    {
        // The distinction the enum exists to make. Collapsing these two onto one action
        // would compile, pass every other test here, and quietly change where Esc lands
        // the player the moment a focus answers CloseSelf.
        Assert.Equal(RouteAction.CloseTopLayer, PlayFocusRouter.EscapeRoute(EscapeMeaning.CloseSelf));
        Assert.Equal(RouteAction.DropToBoard, PlayFocusRouter.EscapeRoute(EscapeMeaning.DropToBoard));
    }
}
