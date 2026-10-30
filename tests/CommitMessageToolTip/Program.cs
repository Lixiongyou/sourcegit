using System.Diagnostics;
using System.Text;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SourceGit.Models;
using SourceGit.Views;
using History = SourceGit.ViewModels.Histories;
using Repository = SourceGit.ViewModels.Repository;

var root = Path.Combine(Path.GetTempPath(), "sourcegit-tooltip-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(root, "config"));
Environment.SetEnvironmentVariable("XDG_CACHE_HOME", Path.Combine(root, "cache"));
SourceGit.Native.OS.SetupBasicDirectories();
SourceGit.App.BuildAvaloniaApp().UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var window = new Window { Width = 900, Height = 650 };
try
{
    window.Show();
    Checks.Run(window, root);
}
finally
{
    window.Close();
    foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        File.SetAttributes(file, FileAttributes.Normal);
    Directory.Delete(root, true);
}

static class Checks
{
    private static void Pump(int milliseconds = 40)
    {
        using var cancellation = new CancellationTokenSource(milliseconds);
        Dispatcher.UIThread.MainLoop(cancellation.Token);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
        Console.WriteLine("PASS tooltip: " + message);
    }

    private static History NewHistory(string root, string name)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        return new History(new Repository(false, path, Path.Combine(path, ".git")));
    }

    private static Commit NewCommit(string sha, string subject) => new() { SHA = sha, Subject = subject };
    private static ScrollViewer Scroller(TestTip tip) => (ScrollViewer)ToolTip.GetTip(tip);
    private static string Message(TestTip tip) => ((TextBlock)Scroller(tip).Content).Text;

    private static void Open(TestTip tip)
    {
        ToolTip.SetIsOpen(tip, false);
        ToolTip.SetIsOpen(tip, true);
        Pump();
    }

    private static void Attach(Window window, TestTip tip)
    {
        window.Content = tip;
        Pump();
    }

    public static void Run(Window window, string root)
    {
        var history = NewHistory(root, "one");
        var otherHistory = NewHistory(root, "two");
        var first = NewCommit("aaaaaaaaaa", "A clipped subject 中文消息");
        var second = NewCommit("bbbbbbbbbb", "Second subject");
        var tip = new TestTip
        {
            History = history,
            DataContext = first,
            Width = 240,
            Height = 26,
            Margin = new Thickness(20),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.Transparent,
            Child = new TextBlock { Text = first.Subject }
        };
        Attach(window, tip);
        Check(tip.Requests.Count == 0 && Message(tip) == first.Subject, "message query is lazy and subject is the initial fallback");
        var bounds = tip.Bounds;
        Open(tip);
        Check(tip.Requests.Count == 1, "opening requests the complete commit message once");
        Open(tip);
        Check(tip.Requests.Count == 1, "reopening while loading does not duplicate the request");

        var longWord = new string('x', 300);
        var full = first.Subject + "\n\nMulti-line body 中文正文\n" + longWord + "\n" +
                   string.Join("\n", Enumerable.Range(1, 100).Select(i => $"Body line {i}")) + "\nEND OF MESSAGE";
        tip.Requests[0].SetResult(full);
        Pump();
        Check(Message(tip) == full, "full message preserves body, blank lines, Unicode and long words");
        Check(tip.Bounds == bounds, "opening a message does not change the row dimensions");
        var scroll = Scroller(tip);
        Check(((TextBlock)scroll.Content).TextWrapping == TextWrapping.Wrap &&
              scroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled,
            "message wraps instead of requiring horizontal scrolling");
        Check(scroll.Bounds.Height <= 480 && scroll.Extent.Height > scroll.Viewport.Height,
            "long message is height-bounded and vertically scrollable");
        var popup = TopLevel.GetTopLevel(scroll);
        if (Environment.GetEnvironmentVariable("SOURCEGIT_TOOLTIP_SCREENSHOT") is { Length: > 0 } screenshot)
            popup.CaptureRenderedFrame()!.Save(screenshot);
        var point = scroll.TranslatePoint(new Point(30, 30), popup).Value;
        popup.MouseMove(point);
        popup.MouseWheel(point, new Vector(0, -1000));
        Pump();
        Check(scroll.Offset.Y > 0 && scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - 1,
            "mouse wheel reaches the end of a long message");
        Open(tip);
        Check(tip.Requests.Count == 1, "successful result is reused on repeated hover");

        tip.DataContext = second;
        Check(!ToolTip.GetIsOpen(tip) && Message(tip) == second.Subject, "recycling closes the old tooltip and restores the new subject");
        Open(tip);
        var stale = tip.Requests[^1];
        tip.DataContext = first;
        Open(tip);
        var current = tip.Requests[^1];
        current.SetResult("current complete message");
        Pump();
        stale.SetResult("WRONG previous row message");
        Pump();
        Check(Message(tip) == "current complete message", "out-of-order completion cannot overwrite a recycled row");

        tip.DataContext = second;
        Open(tip);
        var oldRepository = tip.Requests[^1];
        tip.History = otherHistory;
        Open(tip);
        tip.Requests[^1].SetResult("new repository message");
        Pump();
        oldRepository.SetResult("WRONG old repository message");
        Pump();
        Check(Message(tip) == "new repository message", "switching repositories rejects the old result");

        tip.DataContext = first;
        Open(tip);
        var detached = tip.Requests[^1];
        window.Content = null;
        Pump();
        Check(!ToolTip.GetIsOpen(tip) && ToolTip.GetTip(tip) == null, "detaching closes and clears tooltip state");
        Attach(window, tip);
        detached.SetResult("WRONG detached result");
        Pump();
        Check(Message(tip) == first.Subject, "reattaching does not accept a detached request");

        Open(tip);
        tip.Requests[^1].SetException(new IOException("simulated query failure"));
        Pump();
        Check(Message(tip) == first.Subject, "failed asynchronous query keeps subject fallback without crashing");
        var beforeRetry = tip.Requests.Count;
        Open(tip);
        Check(tip.Requests.Count == beforeRetry + 1, "a later opening retries a failed request");
        tip.Requests[^1].SetResult(string.Empty);
        Pump();
        Check(Message(tip) == first.Subject, "empty result also keeps the subject fallback");
        tip.ThrowNext = true;
        Open(tip);
        Check(Message(tip) == first.Subject, "a synchronous query exception also preserves the fallback");
        Open(tip);
        ToolTip.SetIsOpen(tip, false);
        tip.Requests[^1].SetResult("completed after pointer left");
        Pump();
        Check(!ToolTip.GetIsOpen(tip), "late completion never reopens a dismissed tooltip");

        CheckIssuePriority(window, history, tip);
        CheckRealGitMessage(window, root);
    }

    private static string Git(string directory, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new Exception("Git fixture failed: " + error);
        return output.TrimEnd();
    }

    private static void CheckRealGitMessage(Window window, string root)
    {
        var path = Path.Combine(root, "real-git");
        Directory.CreateDirectory(path);
        Git(path, "init", "-q", "--template=");
        var subject = "fix: a complete real Git subject that is wider than the history column 中文完整提交标题";
        var body = "First body paragraph.\n\nSecond paragraph 中文正文.\nFinal line.";
        Git(path, "-c", "user.name=Tooltip test", "-c", "user.email=tooltip@example.invalid",
            "-c", "commit.gpgsign=false", "-c", "core.hooksPath=", "commit", "-q", "--allow-empty", "-m", subject, "-m", body);
        var sha = Git(path, "rev-parse", "HEAD");
        var priorGit = SourceGit.Native.OS.GitExecutable;
        try
        {
            SourceGit.Native.OS.GitExecutable = SourceGit.Native.OS.FindGitExecutable();
            var commit = NewCommit(sha, subject);
            var history = new History(new Repository(false, path, Path.Combine(path, ".git")))
            {
                IsLoading = false,
                Commits = [commit]
            };
            var histories = new SourceGit.Views.Histories { DataContext = history };
            window.Width = 800;
            window.Content = histories;
            Pump(150);
            var tip = histories.GetVisualDescendants().OfType<CommitMessageToolTip>().Single();
            Check(ReferenceEquals(tip.History, history) && ReferenceEquals(tip.DataContext, commit),
                "production Histories XAML binds the tooltip to its history and commit");
            var list = histories.GetVisualDescendants().OfType<HistoriesCommitList>().Single();
            var columnWidths = list.Columns.Select(column => column.ActualWidth).ToArray();
            var bounds = tip.Bounds;
            ToolTip.SetShowDelay(tip, 0);
            window.MouseMove(new Point(800, 600));
            var point = tip.TranslatePoint(new Point(30, 13), window).Value;
            window.MouseMove(point);
            window.MouseMove(point + new Vector(1, 0));
            var message = (TextBlock)((ScrollViewer)ToolTip.GetTip(tip)).Content;
            var expected = subject + "\n\n" + body;
            var timer = Stopwatch.StartNew();
            while (message.Text != expected && timer.Elapsed < TimeSpan.FromSeconds(5))
                Pump();
            Check(message.Text == expected, "production Git query supplies the real subject and complete multi-paragraph body");
            Check(ToolTip.GetIsOpen(tip) && tip.Bounds == bounds &&
                  columnWidths.SequenceEqual(list.Columns.Select(column => column.ActualWidth)) &&
                  history.SelectedCommits.Count == 0,
                "real Histories hover preserves column widths, row dimensions and selection");
            if (Environment.GetEnvironmentVariable("SOURCEGIT_TOOLTIP_HISTORY_SCREENSHOT") is { Length: > 0 } screenshot)
                window.CaptureRenderedFrame()!.Save(screenshot);
            Check(Git(path, "rev-parse", "HEAD") == sha && Git(path, "status", "--porcelain").Length == 0,
                "hover query leaves repository HEAD, index and worktree unchanged");
        }
        finally
        {
            SourceGit.Native.OS.GitExecutable = priorGit;
        }
    }

    private static void CheckIssuePriority(Window window, History history, TestTip tip)
    {
        var commit = NewCommit("cccccccccc", "#42 ordinary subject after link");
        var subject = new CommitSubjectPresenter
        {
            Subject = commit.Subject,
            Height = 26,
            FontFamily = FontFamily.Default,
            CodeFontFamily = FontFamily.Default,
            FontSize = 13,
            Foreground = Brushes.Black,
            LinkForeground = Brushes.Blue,
            IssueTrackers = new AvaloniaList<IssueTracker>
            {
                new() { RegexString = "#([0-9]+)", URLTemplate = "https://example.invalid/issues/$1" }
            }
        };
        tip.History = history;
        tip.DataContext = commit;
        tip.Child = subject;
        ToolTip.SetShowDelay(tip, 0);
        ToolTip.SetShowDelay(subject, 0);
        Attach(window, tip);
        window.MouseMove(new Point(800, 600));
        Pump();
        var link = subject.TranslatePoint(new Point(8, 13), window).Value;
        window.MouseMove(link);
        window.MouseMove(link + new Vector(1, 0));
        Pump(500);
        Check((string)ToolTip.GetTip(subject) == "https://example.invalid/issues/42" &&
              ToolTip.GetIsOpen(subject) && !ToolTip.GetIsOpen(tip), "issue URL tooltip takes priority over the message host");
        var ordinary = subject.TranslatePoint(new Point(100, 13), window).Value;
        var before = tip.Requests.Count;
        window.MouseMove(ordinary);
        window.MouseMove(ordinary + new Vector(1, 0));
        Pump(500);
        Check(ToolTip.GetTip(subject) == null && ToolTip.GetIsOpen(tip) && tip.Requests.Count == before + 1,
            "moving from issue link to ordinary text opens the complete-message tooltip");
        tip.Requests[^1].SetResult("#42 ordinary subject after link\n\nComplete body");
        Pump();
        Check(Message(tip).EndsWith("Complete body", StringComparison.Ordinal), "message remains available after leaving an issue link");
        window.MouseMove(new Point(800, 600));
        Pump();
        Check(!ToolTip.GetIsOpen(tip), "moving away dismisses the message tooltip");
    }
}

sealed class TestTip : CommitMessageToolTip
{
    public List<TaskCompletionSource<string>> Requests { get; } = [];
    public bool ThrowNext { get; set; }

    protected override Task<string> GetMessageAsync(History history, Commit commit)
    {
        if (ThrowNext)
        {
            ThrowNext = false;
            throw new IOException("simulated synchronous failure");
        }
        var request = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Requests.Add(request);
        return request.Task;
    }
}
