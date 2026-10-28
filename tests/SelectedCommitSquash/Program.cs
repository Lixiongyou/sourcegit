using System.Diagnostics;
using System.Text.Json;
using SourceGit.Commands;
using SourceGit.Models;

if (args.Length == 3 && args[0] == "--todo")
{
    JsonSerializer.Deserialize<InteractiveRebaseJobCollection>(File.ReadAllText(args[1])).WriteTodoList(args[2]);
    return 0;
}
if (args.Length == 3 && args[0] == "--message")
{
    var jobs = JsonSerializer.Deserialize<InteractiveRebaseJobCollection>(File.ReadAllText(args[1]));
    jobs.WriteCommitMessage(Path.Combine(Path.GetDirectoryName(args[1]), "rebase-merge", "done"), args[2]);
    return 0;
}
var tests = new (string Name, Func<Task> Run)[]
{
    ("owned fixture cleanup handles read-only Git files", ReadOnlyCleanup),
    ("contiguous preparation and full messages", Contiguous),
    ("noncontiguous and reversed selections", Noncontiguous),
    ("preparation does not mutate history or worktree", ReadOnly),
    ("root selection rejected", Root),
    ("merge in rewritten range rejected", Merge),
    ("merge older than range allowed", OlderMerge),
    ("other branch selection rejected", OtherBranch),
    ("detached HEAD rejected", Detached),
    ("single duplicate and malformed selections rejected", Invalid),
    ("dirty tracked, staged, and untracked files rejected", Dirty),
    ("dirty state introduced after preview rejected", StaleDirty),
    ("changed HEAD and branch rejected", StaleHead),
    ("in-progress operations rejected", InProgress),
    ("empty commits retained in preparation", Empty),
    ("SHA256 object format", Sha256),
    ("sequence editor rejects stale HEAD branch and onto", EditorGuard),
    ("actual noncontiguous squash preserves skipped commits and final tree", Squash),
    ("unselected autosquash subjects stay separate", Autosquash),
    ("unselected empty commits survive rewrite", EmptyRewrite),
    ("selected empty commits and empty result remain representable", SelectedEmpty),
    ("conflict can abort restoring original branch", ConflictAbort),
    ("conflict can continue with final edited message", ConflictContinue),
    ("stale editor state fails before rewriting", ActualEditorGuard),
};
var failures = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {test.Name}: {ex}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static void Check(bool success, string message) { if (!success) throw new Exception(message); }
static async Task Reject(Func<Task> action, string phrase = null)
{
    try { await action(); }
    catch (InvalidOperationException e) { Check(phrase == null || e.Message.Contains(phrase, StringComparison.OrdinalIgnoreCase), "Unexpected rejection: " + e.Message); return; }
    throw new Exception("Expected rejection");
}
static Task ReadOnlyCleanup()
{
    var repo = new Repo();
    var file = Path.Combine(repo.Path, ".git", "read-only-cleanup-test");
    File.WriteAllText(file, "owned test fixture");
    File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
    repo.Dispose();
    Check(!Directory.Exists(repo.Path), "Owned repository containing read-only files was not removed");
    return Task.CompletedTask;
}
static async Task Contiguous()
{
    using var r = new Repo();
    var a = r.Commit("a", "a", "First subject\n\nFirst body");
    var b = r.Commit("b", "b", "Second subject\n\nSecond body");
    var p = await r.Plan(b, a);
    Check(p.On.SHA == r.Root && p.Head == b && p.Branch == "refs/heads/main", "Wrong endpoints");
    Check(p.Commits.Select(c => c.Commit.SHA).SequenceEqual(new[] { b, a }), "Wrong commit order");
    Check(p.Commits[1].Message.Contains("First body") && p.Commits[0].Message.Contains("Second body"), "Full message lost");
}
static async Task Noncontiguous()
{
    using var r = new Repo(); var a = r.Commit("a"); var skip = r.Commit("skip"); var b = r.Commit("b"); var tail = r.Commit("tail");
    var p = await r.Plan(a, b);
    Check(p.Commits.Select(c => c.Commit.SHA).SequenceEqual(new[] { tail, b, skip, a }), "Skipped commits missing from rewrite");
    Check(p.Selected.Count == 2 && p.Selected.Contains(a) && p.Selected.Contains(b), "Wrong selected set");
    var reversed = await r.Plan(b, a);
    Check(reversed.Commits.Select(c => c.Commit.SHA).SequenceEqual(p.Commits.Select(c => c.Commit.SHA)), "Selection order affects chronology");
}
static async Task ReadOnly()
{
    using var r = new Repo(); var a = r.Commit("a"); var b = r.Commit("b");
    var before = r.Snapshot(); await r.Plan(b, a); Check(before == r.Snapshot(), "Preparation changed repo");
}
static async Task Root() { using var r = new Repo(); var a = r.Commit("a"); await Reject(() => r.Plan(a, r.Root), "root"); }
static async Task Merge()
{
    using var r = new Repo(); var a = r.Commit("a"); r.Git("checkout", "-b", "side"); r.Commit("side"); r.Git("checkout", "main"); var b = r.Commit("b"); r.Git("merge", "--no-ff", "side", "-m", "merge");
    await Reject(() => r.Plan(a, b), "merge");
}
static async Task OlderMerge()
{
    using var r = new Repo(); r.Git("checkout", "-b", "side"); r.Commit("side"); r.Git("checkout", "main"); r.Commit("main"); r.Git("merge", "--no-ff", "side", "-m", "merge"); var merge = r.Head; var a = r.Commit("a"); var b = r.Commit("b");
    Check((await r.Plan(a, b)).On.SHA == merge, "Merge before range should not prohibit squash");
}
static async Task OtherBranch()
{
    using var r = new Repo(); r.Git("checkout", "-b", "side"); var side = r.Commit("side"); r.Git("checkout", "main"); var a = r.Commit("a"); await Reject(() => r.Plan(a, side));
}
static async Task Detached()
{
    using var r = new Repo(); var a = r.Commit("a"); var b = r.Commit("b"); r.Git("checkout", "--detach"); await Reject(() => r.Plan(a, b));
}
static async Task Invalid()
{
    using var r = new Repo(); var a = r.Commit("a"); await Reject(() => r.Plan(a)); await Reject(() => r.Plan(a, a)); await Reject(() => r.Plan(a, "HEAD; touch injected")); await Reject(() => r.Plan(a, new string('f', 40))); Check(!File.Exists(Path.Combine(r.Path, "injected")), "Injection executed");
}
static async Task Dirty()
{
    using var r = new Repo(); var a = r.Commit("a"); var b = r.Commit("b");
    File.WriteAllText(Path.Combine(r.Path, "a"), "changed"); await Reject(() => r.Plan(a, b), "stash");
    r.Git("add", "a"); await Reject(() => r.Plan(a, b), "stash");
    r.Git("reset", "--hard", "HEAD"); File.WriteAllText(Path.Combine(r.Path, "new"), "untracked"); await Reject(() => r.Plan(a, b), "untracked");
}
static async Task StaleDirty()
{
    using var r = new Repo(); var a = r.Commit("a"); var b = r.Commit("b"); var p = await r.Plan(a, b);
    File.WriteAllText(Path.Combine(r.Path, "b"), "modified"); await Reject(() => r.Query.ValidateAsync(p), "stash");
}
static async Task StaleHead()
{
    using var r = new Repo(); var a = r.Commit("a"); var b = r.Commit("b"); var p = await r.Plan(a, b);
    r.Commit("c"); await Reject(() => r.Query.ValidateAsync(p), "changed"); r.Git("reset", "--hard", b); r.Git("checkout", "-b", "other"); await Reject(() => r.Query.ValidateAsync(p), "changed");
}
static async Task InProgress()
{
    using var r = new Repo(); var a = r.Commit("a"); var b = r.Commit("b"); var p = await r.Plan(a, b);
    foreach (var name in new[] { "rebase-merge", "rebase-apply", "sequencer", "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "BISECT_START" })
    {
        var path = Path.Combine(r.Path, ".git", name); File.WriteAllText(path, b);
        await Reject(() => r.Query.ValidateAsync(p), "operation"); File.Delete(path);
    }
}
static async Task Empty()
{
    using var r = new Repo(); var a = r.Commit("a"); r.Git("commit", "--allow-empty", "-m", "empty"); var empty = r.Head; var b = r.Commit("b"); var p = await r.Plan(a, b);
    Check(p.Commits.Any(x => x.Commit.SHA == empty), "Empty skipped commit vanished");
}
static async Task Sha256()
{
    using var r = new Repo(true); var a = r.Commit("a"); var b = r.Commit("b"); var p = await r.Plan(a, b); Check(p.Head.Length == 64 && p.On.SHA.Length == 64, "SHA256 unsupported");
}

static Task EditorGuard()
{
    var jobs = new InteractiveRebaseJobCollection { OrigHead = "head", ExpectedBranch = "refs/heads/main", Onto = "base" };
    Check(jobs.MatchesRebaseState("head", "refs/heads/main", "base"), "Valid state rejected");
    Check(!jobs.MatchesRebaseState("new", "refs/heads/main", "base"), "Stale HEAD accepted");
    Check(!jobs.MatchesRebaseState("head", "refs/heads/other", "base"), "Wrong branch accepted");
    Check(!jobs.MatchesRebaseState("head", "refs/heads/main", "new"), "Wrong base accepted");
    return Task.CompletedTask;
}

static InteractiveRebaseJobCollection Jobs(SelectedCommitSquash plan, string message)
{
    var selected = new HashSet<string>(plan.Selected);
    var result = new InteractiveRebaseJobCollection { OrigHead = plan.Head, Onto = plan.On.SHA, ExpectedBranch = plan.Branch };
    var ordered = plan.GetReorderedCommits();
    var first = true;
    foreach (var commit in ordered.AsEnumerable().Reverse())
    {
        var included = selected.Contains(commit.Commit.SHA);
        result.Jobs.Add(new() { SHA = commit.Commit.SHA, Action = !included ? InteractiveRebaseAction.Pick : first ? InteractiveRebaseAction.Reword : InteractiveRebaseAction.Fixup, Message = included ? message : commit.Message });
        if (included) first = false;
    }
    return result;
}
static async Task Squash()
{
    using var r = new Repo(); var a = r.Commit("a", message: "First\n\nBody α"); var skip = r.Commit("skip"); var b = r.Commit("b"); var tail = r.Commit("tail");
    var tree = r.Git("rev-parse", "HEAD^{tree}"); var p = await r.Plan(a, b); var jobs = Jobs(p, "Edited subject\n\nEdited body α\n");
    Check(jobs.Jobs.Select(x => x.SHA).SequenceEqual(new[] { a, b, skip, tail }), "Incorrect planned replay order");
    Check(r.Rebase(p, jobs).Code == 0, "Squash failed");
    Check(r.Git("rev-parse", "HEAD^{tree}") == tree, "Final tree changed");
    Check(r.Git("log", "--reverse", "--format=%s", r.Root + "..HEAD").Trim() == "Edited subject\nskip\ntail", "Unselected commits/order changed");
    Check(r.Git("log", "--format=%B", "-1", "HEAD~2").Contains("Edited body α"), "Edited combined message lost");
}
static async Task Autosquash()
{
    using var r = new Repo(); var a = r.Commit("a"); var skip = r.Commit("skip", message: "fixup! a"); var b = r.Commit("b");
    r.Git("config", "rebase.autosquash", "true"); r.Git("config", "rebase.updateRefs", "true"); r.Git("branch", "other", skip);
    var p = await r.Plan(a, b); Check(r.Rebase(p, Jobs(p, "combined")).Code == 0, "Squash with config failed");
    Check(r.Git("log", "--format=%s", r.Root + "..HEAD").Contains("fixup! a"), "Unselected fixup swallowed");
    Check(r.Git("rev-parse", "other").Trim() == skip, "Unrelated branch moved by updateRefs config");
}
static async Task EmptyRewrite()
{
    using var r = new Repo(); var a = r.Commit("a"); r.Git("commit", "--allow-empty", "-m", "empty"); var b = r.Commit("b"); var p = await r.Plan(a, b);
    Check(r.Rebase(p, Jobs(p, "combined")).Code == 0, "Squash around empty commit failed");
    Check(r.Git("log", "--format=%s", r.Root + "..HEAD").Contains("empty"), "Empty unselected commit lost");
}
static async Task SelectedEmpty()
{
    using var r = new Repo(); r.Git("commit", "--allow-empty", "-m", "empty one"); var a = r.Head; r.Git("commit", "--allow-empty", "-m", "empty two"); var b = r.Head; var p = await r.Plan(a, b);
    var result = r.Rebase(p, Jobs(p, "combined empty"));
    Check(result.Code == 0, "Squashing empty selected commits failed: " + result.Error);
    Check(r.Git("rev-list", "--count", r.Root + "..HEAD").Trim() == "1", "Expected one combined empty commit");
}

static async Task ActualEditorGuard()
{
    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SOURCEGIT_TEST_APP")))
    {
        Console.WriteLine("NOTE actual editor guard requires SOURCEGIT_TEST_APP; model guard covered separately");
        return;
    }
    foreach (var stale in new[] { "head", "branch", "onto" })
    {
        using var r = new Repo(); var a = r.Commit("a"); var b = r.Commit("b"); var p = await r.Plan(a, b); var jobs = Jobs(p, "combined");
        if (stale == "head") jobs.OrigHead = r.Root;
        else if (stale == "branch") jobs.ExpectedBranch = "refs/heads/other";
        else jobs.Onto = a;
        var result = r.Rebase(p, jobs);
        Check(result.Code != 0, "Stale " + stale + " was accepted by actual editor");
        Check(r.Head == b && r.Git("status", "--porcelain").Length == 0, "Rejected editor changed HEAD or worktree");
        Check(!Directory.Exists(Path.Combine(r.Path, ".git", "rebase-merge")), "Failed editor left rebase active");
    }
}

static async Task ConflictAbort()
{
    using var r = new Repo(); var a = r.Commit("f", "a\n"); r.Commit("f", "middle\n"); var b = r.Commit("f", "b\n"); var p = await r.Plan(a, b); var tree = r.Git("rev-parse", "HEAD^{tree}");
    Check(r.Rebase(p, Jobs(p, "combined")).Code != 0 && Directory.Exists(Path.Combine(r.Path, ".git", "rebase-merge")), "Expected conflict");
    r.Git("rebase", "--abort"); Check(r.Head == b && r.Git("rev-parse", "HEAD^{tree}") == tree && r.Git("status", "--porcelain").Length == 0, "Abort did not restore original state");
}
static async Task ConflictContinue()
{
    using var r = new Repo(); var a = r.Commit("f", "a\n"); r.Commit("f", "middle\n"); var b = r.Commit("f", "b\n"); var p = await r.Plan(a, b); var jobs = Jobs(p, "Edited combined message");
    Check(r.Rebase(p, jobs).Code != 0, "Expected reorder conflict");
    File.WriteAllText(Path.Combine(r.Path, "f"), "b\n"); r.Git("add", "f");
    var result = r.Continue();
    if (result.Code != 0 && Directory.Exists(Path.Combine(r.Path, ".git", "rebase-merge")))
    {
        File.WriteAllText(Path.Combine(r.Path, "f"), "b\n"); r.Git("add", "f"); result = r.Continue();
    }
    Check(result.Code == 0 && !Directory.Exists(Path.Combine(r.Path, ".git", "rebase-merge")), "Conflict continuation failed: " + result.Error);
    Check(r.Git("log", "--format=%s", r.Root + "..HEAD").Contains("Edited combined message"), "Combined message lost on continue");
    Check(File.ReadAllText(Path.Combine(r.Path, "f")) == "b\n", "Resolved final worktree lost");
}

sealed class Repo : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sourcegit-squash-tests-" + Guid.NewGuid().ToString("N"));
    public string Root { get; }
    public string Head => Git("rev-parse", "HEAD").Trim();
    public QuerySelectedCommitSquash Query => new(Path);
    public Repo(bool sha256 = false)
    {
        Directory.CreateDirectory(Path); Git("init", "-b", "main", "--object-format=" + (sha256 ? "sha256" : "sha1"));
        Git("config", "user.name", "Test"); Git("config", "user.email", "test@example.invalid"); Git("config", "commit.gpgsign", "false");
        Root = Commit("root");
    }
    public Task<SelectedCommitSquash> Plan(params string[] selected) => Query.GetResultAsync(selected);
    public string Commit(string file, string content = null, string message = null)
    {
        File.WriteAllText(System.IO.Path.Combine(Path, file), content ?? file); Git("add", "-A"); Git("commit", "-m", message ?? file); return Head;
    }
    private string Editor(string mode)
    {
        var dotnet = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "../../..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var assembly = Environment.GetEnvironmentVariable("SOURCEGIT_TEST_APP") ?? typeof(Repo).Assembly.Location;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SOURCEGIT_TEST_APP")))
            return $"\"{dotnet}\" \"{assembly}\" " + (mode == "--todo" ? "--rebase-todo-editor" : "--rebase-message-editor");
        return $"\"{dotnet}\" \"{assembly}\" {mode} \"{System.IO.Path.Combine(Path, ".git", "jobs.json")}\"";
    }
    public (int Code, string Output, string Error) Rebase(SelectedCommitSquash plan, InteractiveRebaseJobCollection jobs)
    {
        File.WriteAllText(System.IO.Path.Combine(Path, ".git", string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SOURCEGIT_TEST_APP")) ? "jobs.json" : "sourcegit.interactive_rebase"), JsonSerializer.Serialize(jobs));
        var command = new SourceGit.Commands.InteractiveRebase(Path, plan.On.SHA, false, false, true);
        var info = new ProcessStartInfo("git", $"-c sequence.editor=\"{Editor("--todo").Replace("\"", "\\\"")}\" -c core.editor=\"{Editor("--message").Replace("\"", "\\\"")}\" " + command.Args)
        { WorkingDirectory = Path, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var process = Process.Start(info); var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync(); process.WaitForExit(); return (process.ExitCode, output.Result, error.Result);
    }
    public (int Code, string Output, string Error) Continue() => Run("-c", "core.editor=" + Editor("--message"), "rebase", "--continue");
    public string Snapshot() => Git("show-ref") + Git("status", "--porcelain=v1") + Git("ls-files", "--stage") + Git("reflog", "--format=%H %gs");
    public string Git(params string[] args)
    {
        var result = Run(args); if (result.Code != 0) throw new Exception($"git {string.Join(' ', args)}: {result.Error}{result.Output}"); return result.Output;
    }
    public (int Code, string Output, string Error) Run(params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = Path, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info); var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync(); process.WaitForExit(); return (process.ExitCode, output.Result, error.Result);
    }
    public void Dispose()
    {
        // Git marks object files read-only on Windows. Only touch this fixture's
        // generated temporary directory, and never traverse reparse points.
        foreach (var file in Directory.EnumerateFiles(Path, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        }))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
        }
        Directory.Delete(Path, true);
    }
}
