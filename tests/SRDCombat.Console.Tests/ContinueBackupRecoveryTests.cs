using System.Diagnostics;
using System.Text.Json.Nodes;
using SRDCombat.Content;
using SRDCombat.Game;

namespace SRDCombat.Console.Tests;

/// <summary>
/// The executable-boundary half of #703: an explicit JSON <c>null</c> in the primary
/// save — <c>"ladder": null</c> here — must not crash past <see cref="SaveFile"/>'s
/// <c>.bak</c> fallback. <see cref="RunSaveTests"/> (in <c>SRDCombat.Game.Tests</c>)
/// pins that <c>RunSave.FromJson</c> itself refuses a null member by name; this proves
/// the whole live path from <c>--continue</c> at the process boundary: a
/// null-poisoned primary next to a valid <c>.bak</c> recovers and plays on, exit code
/// 0, exactly as a torn or truncated primary already does.
/// </summary>
/// <remarks>
/// Mirrors <c>ProgramRefusalTests</c>' own process-launch pattern (real built
/// executable, explicit content directory, closed stdin so the run reaches its first
/// prompt and quits on EOF rather than blocking), but needs its own copy of the
/// launcher because this scenario has to seed save files into the child's working
/// directory before the process starts — something none of that class's scenarios
/// need.
/// </remarks>
public class ContinueBackupRecoveryTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void ContinuingAgainstANullPoisonedPrimaryWithAValidBackupLoadsTheBackup()
    {
        var contentDirectory = Path.Combine(RepositoryRoot(), "data", "srd");
        var workingDirectory = Directory.CreateTempSubdirectory("srdcombat-console-tests-").FullName;

        // A real, resolvable run — GauntletRun.Resume checks every id a draft names
        // against the loaded content, so the backup has to be more than syntactically
        // valid JSON for --continue to reach exit 0 rather than a content-drift refusal.
        var content = ContentLoader.Load(contentDirectory);
        var run = GauntletRun.Start(content);
        var validJson = RunSave.ToJson(run);

        // The primary: the same save, poisoned with an explicit JSON null on a required
        // collection — exactly the shape #703 reports (`"ladder": null`). Before this
        // fix, RunSave.FromJson dereferenced saved.Ladder.Count and threw
        // NullReferenceException, which SaveFile.TryReadRun's catch filter
        // (JsonException/InvalidDataException/IOException) does not catch, so it
        // unwound straight past the .bak fallback below and crashed the process.
        var poisoned = JsonNode.Parse(validJson)!.AsObject();
        poisoned["ladder"] = null;

        var savePath = Path.Combine(workingDirectory, "srdcombat-save.json");
        File.WriteAllText(savePath, poisoned.ToJsonString());
        File.WriteAllText(savePath + ".bak", validJson);

        var result = RunConsole(workingDirectory, contentDirectory, "--continue");

        Assert.Equal(0, result.ExitCode);

        // The console client resolves --continue's default save path relative to its own
        // working directory rather than echoing back an absolute one, so the printed
        // notice names the same relative path it was given, not `savePath` above.
        Assert.Contains(
            "'srdcombat-save.json' was missing or unreadable; loaded the backup instead.",
            result.StandardOutput,
            StringComparison.Ordinal);
        Assert.True(
            string.IsNullOrWhiteSpace(result.StandardError),
            $"Expected no stderr; got: {result.StandardError}");
    }

    private static ProcessResult RunConsole(string workingDirectory, string contentDirectory, params string[] flags)
    {
        var consoleDll = Path.Combine(AppContext.BaseDirectory, "SRDCombat.Console.dll");

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

        // Explicit trailing positional argument, so the child never has to walk up from
        // workingDirectory looking for data/srd — it would not find it there.
        startInfo.ArgumentList.Add(contentDirectory);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the console client process.");

        // Closed immediately: --continue here reaches a real fight's first command
        // prompt, and CommandLoop treats EOF the same as typing "quit".
        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"SRDCombat.Console did not exit within {Timeout} for arguments [{string.Join(' ', flags)}].");
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
