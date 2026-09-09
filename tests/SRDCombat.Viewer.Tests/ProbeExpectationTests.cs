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
}
