namespace SRDCombat.Viewer.Tests;

/// <summary>
/// The pure half of #705's predicate mechanism: whether a <see cref="ProbeExpectation"/>
/// is satisfied by a given <see cref="ProbeSnapshot"/>, and what it says when it is not.
/// <c>PlayMode.Assert</c> — reading <c>_focus</c> and <c>_notice</c> off a live scene and
/// throwing on a failed expectation — needs a running Godot node and stays probe-only,
/// the same split every Godot-adjacent seam in this project already makes
/// (<c>ProbeFaultsTests</c> is the model this follows). What is plain-value here is the
/// comparison itself: given a focus and a notice, does the expectation hold.
/// </summary>
/// <remarks>
/// #719's review found that every "passes" test here had only ever been knocked out by
/// a stub for the matching "fails" test's own defect (an "always accept" mutation), and
/// no stub ever exercised the opposite direction (an "always reject" mutation, which
/// would make every one of these "passes" tests go red on its own). Each predicate below
/// now has both directions represented so each fact has its own red in the table.
/// </remarks>
public class ProbeExpectationTests
{
    [Fact]
    public void FocusIsPassesWhenTheTopLayerIsTheExpectedType()
    {
        var snapshot = new ProbeSnapshot(new PlayFocus.Board(), null);

        Assert.Null(new ProbeExpectation.FocusIs(typeof(PlayFocus.Board)).Failure(snapshot));
    }

    [Fact]
    public void FocusIsFailsAndNamesBothTypesWhenTheTopLayerDiffers()
    {
        var snapshot = new ProbeSnapshot(new PlayFocus.Board(), null);

        var failure = new ProbeExpectation.FocusIs(typeof(PlayFocus.AttackMenu)).Failure(snapshot);

        Assert.NotNull(failure);
        Assert.Contains("AttackMenu", failure);
        Assert.Contains("Board", failure);
    }

    [Fact]
    public void NoticeCodeIsNullPassesWhenNoNoticeWasPrinted()
    {
        var snapshot = new ProbeSnapshot(new PlayFocus.Board(), null);

        Assert.Null(new ProbeExpectation.NoticeCodeIs(null).Failure(snapshot));
    }

    [Fact]
    public void NoticeCodeIsReadsTheBracketedCodeOffTheNoticeLine()
    {
        // The exact shape PlayMode.RefreshAfterAction writes: "{message}  [{code}]".
        var snapshot = new ProbeSnapshot(
            new PlayFocus.Board(),
            "Brenna has already used its action  [action.spent]");

        Assert.Null(new ProbeExpectation.NoticeCodeIs("action.spent").Failure(snapshot));
    }

    [Fact]
    public void NoticeCodeIsFailsAndQuotesTheWholeNoticeWhenTheCodeDiffers()
    {
        var snapshot = new ProbeSnapshot(
            new PlayFocus.Board(),
            "Brenna has already used its action  [action.spent]");

        // The named instance this predicate closes (#521): a step that expects no
        // refusal at all must not pass just because some other code was printed.
        var failure = new ProbeExpectation.NoticeCodeIs(null).Failure(snapshot);

        Assert.NotNull(failure);
        Assert.Contains("action.spent", failure);

        // Tightened at #719's review: the message names the extracted code twice
        // ("got action.spent" and the parenthesised full line), so a bare
        // Contains("action.spent") alone still passes an implementation that dropped
        // the full-notice half of the message entirely. This checks the prose that can
        // only come from quoting the whole notice, not the code alone.
        Assert.Contains("Brenna has already used its action", failure);
    }

    [Fact]
    public void UnchangedPassesWhenBeforeAndAfterAreEqual()
    {
        var snapshot = new ProbeSnapshot(new PlayFocus.Board(), null);

        Assert.Null(new ProbeExpectation.Unchanged<int>("hp", 12, 12).Failure(snapshot));
    }

    [Fact]
    public void UnchangedFailsAndNamesTheResourceAndBothValuesWhenTheyDiffer()
    {
        var snapshot = new ProbeSnapshot(new PlayFocus.Board(), null);

        var failure = new ProbeExpectation.Unchanged<int>("hp", 12, 9).Failure(snapshot);

        Assert.NotNull(failure);
        Assert.Contains("hp", failure);
        Assert.Contains("12", failure);
        Assert.Contains("9", failure);
    }

    [Fact]
    public void ChangedPassesWhenBeforeAndAfterDiffer()
    {
        var snapshot = new ProbeSnapshot(new PlayFocus.Board(), null);

        Assert.Null(new ProbeExpectation.Changed<int>("round", 1, 2).Failure(snapshot));
    }

    [Fact]
    public void ChangedFailsAndNamesTheResourceAndTheStuckValueWhenTheyAreEqual()
    {
        var snapshot = new ProbeSnapshot(new PlayFocus.Board(), null);

        // The no-op shape this predicate exists to catch (#719 review): a click that
        // found nothing leaves "before" and "after" identical.
        var failure = new ProbeExpectation.Changed<int>("round", 3, 3).Failure(snapshot);

        Assert.NotNull(failure);
        Assert.Contains("round", failure);
        Assert.Contains("3", failure);
    }

    [Fact]
    public void EqualsExpectedPassesWhenActualMatchesExpected()
    {
        var snapshot = new ProbeSnapshot(new PlayFocus.Board(), null);

        Assert.Null(new ProbeExpectation.EqualsExpected<string?>("hint", "Bonus Action.", "Bonus Action.").Failure(snapshot));
    }

    [Fact]
    public void EqualsExpectedFailsAndNamesTheResourceAndBothValuesWhenTheyDiffer()
    {
        var snapshot = new ProbeSnapshot(new PlayFocus.Board(), null);

        var failure = new ProbeExpectation.EqualsExpected<string?>("hint", null, "Bonus Action.").Failure(snapshot);

        Assert.NotNull(failure);
        Assert.Contains("hint", failure);
        Assert.Contains("Bonus Action.", failure);
    }

    [Fact]
    public void DecreasedPassesWhenAfterIsLessThanBefore()
    {
        var snapshot = new ProbeSnapshot(new PlayFocus.Board(), null);

        Assert.Null(new ProbeExpectation.Decreased("movement", 30, 20).Failure(snapshot));
    }

    [Fact]
    public void DecreasedFailsAndNamesTheResourceAndBothValuesWhenAfterDidNotDrop()
    {
        var snapshot = new ProbeSnapshot(new PlayFocus.Board(), null);

        // Equal, not merely greater — a move that spent zero feet is still a move that
        // did nothing, and must fail this exactly the way an increase would.
        var failure = new ProbeExpectation.Decreased("movement", 30, 30).Failure(snapshot);

        Assert.NotNull(failure);
        Assert.Contains("movement", failure);
        Assert.Contains("30", failure);
    }

    [Fact]
    public void NonEmptyPassesWhenTheValueIsNonEmpty()
    {
        var snapshot = new ProbeSnapshot(new PlayFocus.Board(), null);

        Assert.Null(new ProbeExpectation.NonEmpty("hint", "Bonus Action.").Failure(snapshot));
    }

    [Fact]
    public void NonEmptyFailsAndNamesTheResourceWhenTheValueIsNull()
    {
        var snapshot = new ProbeSnapshot(new PlayFocus.Board(), null);

        // The shape this predicate exists for (#719, third review): a broken hint
        // registration reads back as null here, not as an empty string.
        var failure = new ProbeExpectation.NonEmpty("hint", null).Failure(snapshot);

        Assert.NotNull(failure);
        Assert.Contains("hint", failure);
    }

    [Fact]
    public void NonEmptyFailsAndNamesTheResourceWhenTheValueIsEmpty()
    {
        var snapshot = new ProbeSnapshot(new PlayFocus.Board(), null);

        var failure = new ProbeExpectation.NonEmpty("hint", string.Empty).Failure(snapshot);

        Assert.NotNull(failure);
        Assert.Contains("hint", failure);
    }

    [Fact]
    public void NoticePresentPassesWhenANoticeWasPrinted()
    {
        var snapshot = new ProbeSnapshot(new PlayFocus.Board(), "out of range  [attack.out_of_range]");

        Assert.Null(new ProbeExpectation.NoticePresent("the attack").Failure(snapshot));
    }

    [Fact]
    public void NoticePresentFailsAndNamesTheResourceWhenNoNoticeWasPrinted()
    {
        var snapshot = new ProbeSnapshot(new PlayFocus.Board(), null);

        var failure = new ProbeExpectation.NoticePresent("the attack").Failure(snapshot);

        Assert.NotNull(failure);
        Assert.Contains("the attack", failure);
    }

    [Fact]
    public void AnyOfPassesWhenAtLeastOneOptionPasses()
    {
        var snapshot = new ProbeSnapshot(new PlayFocus.Board(), null);

        var anyOf = new ProbeExpectation.AnyOf(
            "evidence the attack resolved or was refused",
            [
                new ProbeExpectation.Changed<int>("the combat log length", 3, 3),
                new ProbeExpectation.NoticePresent("the attack"),
            ]);

        // The log did not grow, but a notice printed — exactly the refusal shape play-4
        // and play-8 both need to accept.
        var snapshotWithNotice = new ProbeSnapshot(new PlayFocus.Board(), "out of range  [attack.out_of_range]");

        Assert.Null(anyOf.Failure(snapshotWithNotice));
    }

    [Fact]
    public void AnyOfFailsAndListsEveryOptionsFailureWhenAllFail()
    {
        var snapshot = new ProbeSnapshot(new PlayFocus.Board(), null);

        // The no-op shape this predicate exists to catch (#719, second review): the
        // click found nothing, so neither the log grew nor a notice was printed.
        var anyOf = new ProbeExpectation.AnyOf(
            "evidence the attack resolved or was refused",
            [
                new ProbeExpectation.Changed<int>("the combat log length", 3, 3),
                new ProbeExpectation.NoticePresent("the attack"),
            ]);

        var failure = anyOf.Failure(snapshot);

        Assert.NotNull(failure);
        Assert.Contains("evidence the attack resolved or was refused", failure);
        Assert.Contains("the combat log length", failure);
        Assert.Contains("the attack", failure);
    }
}
