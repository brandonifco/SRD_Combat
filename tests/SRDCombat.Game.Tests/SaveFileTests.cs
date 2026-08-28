using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Game;

namespace SRDCombat.Game.Tests;

public sealed class SaveFileTests : IDisposable
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "srdcombat-savefile-tests", Guid.NewGuid().ToString("N"));

    public SaveFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        SaveFile.OperationCompletedForTesting = null;
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void CrashPrefixesOfNewRunAndEveryContinuationSelectionPreserveTheCommittedState()
    {
        var foreign = Save(90);
        var current = Save(1);
        var next = Save(2);
        var afterNext = Save(3);

        AssertCrashMatrix(
            "new-run",
            path =>
            {
                File.WriteAllText(path, foreign);
                File.WriteAllText(path + ".old", foreign);
                File.WriteAllText(path + ".bak", foreign);
            },
            (path, json) => SaveFile.BeginNewRun(path, json),
            SaveFileOperation.NewRunCommitted,
            beforeCommit: null,
            committed: current,
            next: next,
            afterNext: afterNext);

        AssertCrashMatrix(
            "continuing-primary",
            path => EstablishPrimary(path, current),
            (path, json) => SaveFile.ContinueWrite(path, json),
            SaveFileOperation.ContinuationCommitted,
            beforeCommit: current,
            committed: next,
            next: afterNext,
            afterNext: Save(4));

        AssertCrashMatrix(
            "continuing-old-after-corrupt-primary",
            path => EstablishOldFallback(path, current),
            (path, json) => SaveFile.ContinueWrite(path, json),
            SaveFileOperation.ContinuationCommitted,
            beforeCommit: current,
            committed: next,
            next: afterNext,
            afterNext: Save(5));

        AssertCrashMatrix(
            "continuing-backup-after-corrupt-primary",
            path => EstablishBackupFallback(path, current),
            (path, json) => SaveFile.ContinueWrite(path, json),
            SaveFileOperation.ContinuationCommitted,
            beforeCommit: current,
            committed: next,
            next: afterNext,
            afterNext: Save(6));
    }

    [Fact]
    public void NewRunMarkerMasksResidueBeforeItIsRemovedAndContinuationRefusesIt()
    {
        var path = CasePath("marker");
        var foreign = Save(90);
        File.WriteAllText(path, foreign);
        File.WriteAllText(path + ".old", foreign);
        File.WriteAllText(path + ".bak", foreign);

        var crash = InjectAfter(1);
        try
        {
            Assert.Throws<InjectedCrash>(() => SaveFile.BeginNewRun(path, Save(1)));
        }
        finally
        {
            RemoveInjection();
        }

        // This assertion runs before fixture disposal: the injected crash left the real
        // state intact, including the durable marker and every foreign slot it masks.
        Assert.True(File.Exists(path + ".new"));
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(path + ".old"));
        Assert.True(File.Exists(path + ".bak"));
        Assert.Null(SaveFile.LoadRun(path).Saved);
        Assert.Throws<InvalidOperationException>(() => SaveFile.ContinueWrite(path, Save(2)));
    }

    [Fact]
    public void ExistingUnmarkedSaveStillLoadsAndAContinuationKeepsItsBackup()
    {
        var path = CasePath("legacy");
        var first = Save(1);
        var second = Save(2);
        SaveFile.BeginNewRun(path, first);
        SaveFile.ContinueWrite(path, second);

        Assert.Equal(second, LoadedJson(path));
        File.Delete(path);
        Assert.Equal(first, LoadedJson(path));
    }

    private void AssertCrashMatrix(
        string name,
        Action<string> setup,
        Action<string, string> write,
        SaveFileOperation commit,
        string? beforeCommit,
        string committed,
        string next,
        string afterNext)
    {
        var template = CasePath(name + "-count");
        setup(template);
        var operations = Capture(() => write(template, committed));
        var commitPrefix = operations.IndexOf(commit) + 1;
        Assert.True(commitPrefix > 0, $"{name} must expose its commit checkpoint.");

        for (var prefix = 1; prefix <= operations.Count; prefix++)
        {
            var source = CasePath($"{name}-source-{prefix}");
            setup(source);
            CrashAfter(prefix, () => write(source, committed));
            var recovered = prefix < commitPrefix ? beforeCommit : committed;
            AssertRecovered(source, recovered);

            for (var nextPrefix = 1; ; nextPrefix++)
            {
                var crossProduct = CasePath($"{name}-source-{prefix}-next-{nextPrefix}");
                setup(crossProduct);
                CrashAfter(prefix, () => write(crossProduct, committed));
                var recoveredForNext = prefix < commitPrefix ? beforeCommit : committed;

                // A pre-commit new-run crash correctly has no run to continue. Recovery
                // starts that new run again to a real commit; only then can its next save
                // be a continuation write.
                if (recoveredForNext is null)
                {
                    SaveFile.BeginNewRun(crossProduct, committed);
                    recoveredForNext = committed;
                }

                var countPath = CasePath($"{name}-source-{prefix}-next-count-{nextPrefix}");
                setup(countPath);
                CrashAfter(prefix, () => write(countPath, committed));
                if (prefix < commitPrefix && beforeCommit is null)
                {
                    SaveFile.BeginNewRun(countPath, committed);
                }

                var nextOperations = Capture(() => SaveFile.ContinueWrite(countPath, next));
                if (nextPrefix > nextOperations.Count)
                {
                    break;
                }

                CrashAfter(nextPrefix, () => SaveFile.ContinueWrite(crossProduct, next));
                var nextCommitPrefix = nextOperations.IndexOf(SaveFileOperation.ContinuationCommitted) + 1;
                AssertRecovered(crossProduct, nextPrefix < nextCommitPrefix ? recoveredForNext : next);
            }
        }

        var completed = CasePath(name + "-completed");
        setup(completed);
        write(completed, committed);
        SaveFile.ContinueWrite(completed, next);
        SaveFile.ContinueWrite(completed, afterNext);
        Assert.Equal(afterNext, LoadedJson(completed));
    }

    private string CasePath(string name)
    {
        var directory = Path.Combine(_directory, name);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "save.json");
    }

    private static void EstablishPrimary(string path, string current)
    {
        SaveFile.BeginNewRun(path, Save(10));
        SaveFile.ContinueWrite(path, current);
    }

    private static void EstablishOldFallback(string path, string current)
    {
        EstablishPrimary(path, current);
        File.Move(path, path + ".old", overwrite: true);
        File.WriteAllText(path, "corrupt primary");
    }

    private static void EstablishBackupFallback(string path, string current)
    {
        EstablishPrimary(path, current);
        File.Copy(path, path + ".bak", overwrite: true);
        File.Delete(path);
        if (File.Exists(path + ".old"))
        {
            File.Delete(path + ".old");
        }
    }

    private static List<SaveFileOperation> Capture(Action action)
    {
        var operations = new List<SaveFileOperation>();
        SaveFile.OperationCompletedForTesting = operations.Add;
        try
        {
            action();
            return operations;
        }
        finally
        {
            RemoveInjection();
        }
    }

    private static void CrashAfter(int prefix, Action action)
    {
        InjectAfter(prefix);
        try
        {
            Assert.Throws<InjectedCrash>(action);
        }
        finally
        {
            // Restore only the hook. Deliberately do not remove any files: assertions
            // must inspect the exact filesystem state the injected crash left behind.
            RemoveInjection();
        }
    }

    private static int InjectAfter(int prefix)
    {
        var seen = 0;
        SaveFile.OperationCompletedForTesting = _ =>
        {
            seen++;
            if (seen == prefix)
            {
                throw new InjectedCrash();
            }
        };
        return prefix;
    }

    private static void RemoveInjection() => SaveFile.OperationCompletedForTesting = null;

    private static void AssertRecovered(string path, string? expected)
    {
        var loaded = SaveFile.LoadRun(path);
        if (expected is null)
        {
            Assert.Null(loaded.Saved);
            return;
        }

        Assert.NotNull(loaded.Saved);
        Assert.Equal(expected, ContentSerializer.Serialize(loaded.Saved!));
    }

    private static string LoadedJson(string path)
    {
        var loaded = SaveFile.LoadRun(path);
        Assert.NotNull(loaded.Saved);
        return ContentSerializer.Serialize(loaded.Saved!);
    }

    private static string Save(int seed) => RunSave.ToJson(GauntletRun.Start(Content, seed: seed));

    private sealed class InjectedCrash : Exception;
}
