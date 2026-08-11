namespace SRDCombat.Content.Validation;

/// <summary>How seriously to take a validation issue.</summary>
public enum ValidationSeverity
{
    /// <summary>
    /// Something is suspicious but the content is still usable — most often a printed
    /// value in the SRD that disagrees with what the rules would derive.
    /// </summary>
    Warning,

    /// <summary>The content is wrong. Loading refuses.</summary>
    Error,
}

/// <summary>One problem found in a content file.</summary>
/// <param name="Severity">Whether this blocks loading.</param>
/// <param name="Code">
/// A stable dotted code — <c>monster.hit_points.disagree_with_dice</c>. Tests assert
/// against these rather than against message text.
/// </param>
/// <param name="Subject">The id of the item at fault, or the file when it has no item.</param>
/// <param name="Message">A human-readable explanation naming the actual values.</param>
public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Code,
    string Subject,
    string Message)
{
    public override string ToString() => $"[{Severity}] {Code} ({Subject}): {Message}";
}

/// <summary>The outcome of validating a content file.</summary>
/// <param name="Issues">Everything found, in the order it was found.</param>
public sealed record ValidationResult(IReadOnlyList<ValidationIssue> Issues)
{
    public static ValidationResult Empty { get; } = new([]);

    public IEnumerable<ValidationIssue> Errors =>
        Issues.Where(issue => issue.Severity == ValidationSeverity.Error);

    public IEnumerable<ValidationIssue> Warnings =>
        Issues.Where(issue => issue.Severity == ValidationSeverity.Warning);

    public bool HasErrors => Errors.Any();

    /// <summary>Throws when anything is wrong, naming every error rather than just the first.</summary>
    public void ThrowIfInvalid(string fileDescription)
    {
        if (!HasErrors)
        {
            return;
        }

        var detail = string.Join(Environment.NewLine, Errors.Select(error => "  " + error));

        throw new ContentValidationException(
            $"{fileDescription} failed validation:{Environment.NewLine}{detail}",
            this);
    }
}

/// <summary>Thrown when content fails validation on load.</summary>
public sealed class ContentValidationException : Exception
{
    public ContentValidationException(string message, ValidationResult result)
        : base(message) => Result = result;

    public ContentValidationException()
        : this("Content failed validation.", ValidationResult.Empty)
    {
    }

    public ContentValidationException(string message)
        : this(message, ValidationResult.Empty)
    {
    }

    public ContentValidationException(string message, Exception innerException)
        : base(message, innerException) => Result = ValidationResult.Empty;

    /// <summary>Everything that was wrong, not just what the message quoted.</summary>
    public ValidationResult Result { get; }
}
