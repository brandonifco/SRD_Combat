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
