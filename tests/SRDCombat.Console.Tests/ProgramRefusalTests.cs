using System.Diagnostics;

namespace SRDCombat.Console.Tests;

/// <summary>
/// <see cref="ConsoleArgumentsTests"/> pins <c>ConsoleArguments</c>' parse functions
/// directly — it proves what an error string says once a flag has already reached a
/// <c>TryParse*</c>/<c>TryResolve*</c> call, never that <c>Program.cs</c> actually calls
/// it. That gap is exactly how a Codex finding on #605 slipped through: <c>--difficulty</c>
/// was only ever read inside the <c>--one-fight</c> branch, so a <c>--difficulty</c> on
/// the ordinary gauntlet path — valid or not — reached no parser at all and was silently
/// dropped, and nothing here would have gone red for it.
///
/// These tests close that gap by launching the real built executable (mirroring
/// <c>FlagRefusalTests</c>'s and <c>ConsoleArgumentsTests</c>' own source-check tests,
/// but for the flags whose refusal *can* be observed at the process boundary without a
/// live <c>Node</c>) and asserting on its actual exit code and stderr — the same
/// boundary a player hits. <c>Program.cs</c> stays untestable as a class (top-level
/// statements, #317), so this is process-level rather than a direct call; the content
/// directory is passed explicitly as a positional argument so the child process does not
/// depend on its working directory to find <c>data/srd</c>, and a temp working directory
/// keeps it from writing an autosave into the repository.
/// </summary>
public class ProgramRefusalTests
{
    // Generous relative to the ~0.3s a real run takes; only guards against a genuine
    // hang (e.g. a scenario that unexpectedly blocks on stdin instead of hitting EOF).
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void AnInvalidSeedIsRefusedAtTheExecutableBoundary()
    {
        var result = RunConsole("--seed", "abc");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--seed abc", result.StandardError);
        Assert.Contains("not a whole number", result.StandardError);
    }

    [Fact]
    public void AnInvalidLevelIsRefusedAtTheExecutableBoundary()
    {
        var result = RunConsole("--level", "99");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--level 99", result.StandardError);
        Assert.Contains("1-5", result.StandardError);
    }

    /// <summary>
    /// The Codex finding this class exists to pin: <c>--difficulty</c> with no
    /// <c>--one-fight</c> used to start the gauntlet normally, reading nothing. It must
    /// now be refused the same as every other misused flag rather than starting quietly.
    /// </summary>
    [Fact]
    public void ADifficultyFlagWithoutOneFightIsRefusedRatherThanStartingTheGauntlet()
    {
        var result = RunConsole("--difficulty", "low");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--difficulty refused", result.StandardError);
        Assert.Contains("--one-fight", result.StandardError);
        Assert.DoesNotContain("gauntlet of", result.StandardOutput);
    }

    [Fact]
    public void AnInvalidDifficultyWithOneFightIsRefusedAtTheExecutableBoundary()
    {
        var result = RunConsole("--one-fight", "--difficulty", "extreme");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--difficulty extreme", result.StandardError);
        Assert.Contains("low, moderate, high", result.StandardError);
    }

    [Fact]
    public void ContinuingWithALevelIsRefusedAtTheExecutableBoundary()
    {
        var result = RunConsole("--continue", "--level", "3");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--level refused", result.StandardError);
        Assert.Contains("--continue", result.StandardError);
    }

    /// <summary>
    /// The asymmetric half of the same gate: <c>--create --level</c> has somewhere for
    /// the level to go (a fresh run's starting level, forwarded per
    /// <see cref="ConsoleArgumentsTests.CreateRunStartForwardsTheResolvedLevelRatherThanDefaultingToOne"/>)
    /// and must reach interactive creation rather than being refused the way
    /// <c>--continue --level</c> is. Stdin is closed immediately, so
    /// <c>PartyCreator.CreateParty</c> reads EOF on the first prompt and abandons — the
    /// process still exits 0 with nothing on stderr, proving the gate told the two flags
    /// apart rather than refusing both.
    /// </summary>
    [Fact]
    public void CreatingWithALevelIsNotRefusedAndReachesInteractiveCreation()
    {
        var result = RunConsole("--create", "--level", "3");

        Assert.Equal(0, result.ExitCode);
        Assert.True(
            string.IsNullOrWhiteSpace(result.StandardError),
            $"Expected no stderr; got: {result.StandardError}");
        Assert.Contains("Creation abandoned; no run started.", result.StandardOutput);
    }

    private static ProcessResult RunConsole(params string[] flags)
    {
        var consoleDll = Path.Combine(AppContext.BaseDirectory, "SRDCombat.Console.dll");
        var contentDirectory = Path.Combine(RepositoryRoot(), "data", "srd");
        var workingDirectory = Directory.CreateTempSubdirectory("srdcombat-console-tests-").FullName;

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(consoleDll);

        foreach (var flag in flags)
        {
            startInfo.ArgumentList.Add(flag);
        }

        // The content directory as an explicit trailing positional argument, so the
        // child process never has to walk up from workingDirectory looking for
        // data/srd — it would not find it there.
        startInfo.ArgumentList.Add(contentDirectory);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the console client process.");

        // Closed immediately rather than left open: every scenario here either refuses
        // before reading any input, or (the --create case) reaches a prompt that must
        // see EOF right away to abandon creation instead of blocking on a read.
        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"SRDCombat.Console did not exit within {Timeout} for arguments " +
                $"[{string.Join(' ', flags)}] — a refusal scenario should never block on input.");
        }

        return new ProcessResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SRDCombat.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find SRDCombat.sln above '{AppContext.BaseDirectory}'.");
    }

    private readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
