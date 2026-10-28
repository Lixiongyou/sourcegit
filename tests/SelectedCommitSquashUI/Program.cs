using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SourceGit.Models;
using SourceGit.Views;
using VM = SourceGit.ViewModels;

SourceGit.Native.OS.SetupBasicDirectories();
SourceGit.Native.OS.GitExecutable = "git";
AppBuilder.Configure<SourceGit.App>().WithInterFont().UseSkia()
    .ConfigureFonts(manager => manager.AddFontCollection(new Avalonia.Media.Fonts.EmbeddedFontCollection(new Uri("fonts:SourceGit"), new Uri("avares://SourceGit/Resources/Fonts"))))
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var path = Path.Combine(Path.GetTempPath(), "sourcegit-squash-ui-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(path);
try
{
    Git("init", "-b", "main"); Git("config", "user.name", "Test"); Git("config", "user.email", "test@example.invalid"); Git("config", "commit.gpgsign", "false");
    Commit("root"); var a = Commit("first"); var skip = Commit("skipped"); var b = Commit("last");
    var plan = new SourceGit.Commands.QuerySelectedCommitSquash(path).GetResultAsync(new[] { a, b }).GetAwaiter().GetResult();
    var repo = new VM.Repository(false, path, Path.Combine(path, ".git"));
    typeof(VM.Repository).GetField("_currentBranch", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(repo, new Branch { Name = "main", FullName = "refs/heads/main", Head = b, IsLocal = true });
    for (var iteration = 0; iteration < 2; iteration++)
    {
        var before = Git("show-ref") + Git("reflog", "--format=%H %gs");
        var vm = new VM.InteractiveRebase(repo, plan);
        var view = new InteractiveRebase();
        view.DataContext = vm;
        Pump();
        Check(!view.IsVisible && view.OwnedWindows.Count == 0, "DataContext before Show does not open premature message editor");
        Check(vm.Items.Select(x => x.Commit.SHA).SequenceEqual(new[] { skip, b, a }), "Preview groups only selected commits at oldest target");
        Check(vm.Items[0].Action == InteractiveRebaseAction.Pick && vm.Items[1].Action == InteractiveRebaseAction.Fixup && vm.Items[2].Action == InteractiveRebaseAction.Reword, "Preview actions preserve skipped commit");
        Check(vm.PreSelected.FullMessage.Contains("first") && vm.PreSelected.FullMessage.Contains("last") && !vm.PreSelected.FullMessage.Contains("skipped"), "Combined editor contains selected messages only");
        view.Show(); Pump();
        var editor = view.OwnedWindows.OfType<CommitMessageEditor>().Single();
        Check(editor.IsVisible && editor.Owner == view, "Message editor opens only after visible owner");
        if (iteration == 0)
        {
            var messageControl = editor.FindControl<CommitMessageToolBox>("Editor");
            messageControl.CommitMessage = "Edited combined message";
            typeof(CommitMessageEditor).GetMethod("SaveAndClose", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, new object[] { null, new RoutedEventArgs() });
            Pump();
            Check(vm.PreSelected.FullMessage == "Edited combined message" && vm.PreSelected.IsMessageUserEdited, "Editor save updates actual target view-model");
            view.OpenCommitMessageEditor(vm.PreSelected); Pump();
            editor = view.OwnedWindows.OfType<CommitMessageEditor>().Single();
            Check(editor.FindControl<CommitMessageToolBox>("Editor").CommitMessage == "Edited combined message", "Reopening editor retains edited message");
        }
        editor.Close(); Pump();
        Check(view.IsVisible, "Cancel message editor returns to preview");
        if (iteration == 0 && Environment.GetEnvironmentVariable("SOURCEGIT_SQUASH_SCREENSHOT") is { Length: > 0 } screenshot)
            view.CaptureRenderedFrame().Save(screenshot);
        if (iteration == 1)
        {
            vm.PreSelected.FullMessage = " ";
            typeof(InteractiveRebase).GetMethod("OnStartJobs", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { null, new RoutedEventArgs() });
            Check(!view.IsEnabled, "Start disables preview during asynchronous validation");
            typeof(InteractiveRebase).GetMethod("OnStartJobs", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { null, new RoutedEventArgs() });
            for (var i = 0; i < 20 && view.FindControl<ProgressBar>("Running").IsVisible; i++) Pump();
            Check(view.IsEnabled, "Rejected repeated Start restores preview interaction");
            Check(view.IsVisible && !vm.WasRebaseStarted, "Blank-message Start leaves preview open without rewriting");
            vm.PreSelected.FullMessage = "fixed";
            File.WriteAllText(Path.Combine(path, "dirty"), "untracked");
            typeof(InteractiveRebase).GetMethod("OnStartJobs", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { null, new RoutedEventArgs() });
            for (var i = 0; i < 20 && view.FindControl<ProgressBar>("Running").IsVisible; i++) Pump();
            Check(view.IsVisible && !vm.WasRebaseStarted, "Dirty-after-preview Start leaves preview open without rewriting");
            File.Delete(Path.Combine(path, "dirty"));
        }
        view.Close(); Pump();
        Check(!view.IsVisible && !vm.WasRebaseStarted, "Cancel preview never starts rebase");
        Check(before == Git("show-ref") + Git("reflog", "--format=%H %gs") && Git("status", "--porcelain").Length == 0, "Cancel and repeated preview leave refs/index/worktree unchanged");
    }
}
finally
{
    // Git object files are read-only on Windows. This is only the owned fixture;
    // do not traverse reparse points while clearing their read-only attribute.
    foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    }))
    {
        var attributes = File.GetAttributes(file);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
    }
    Directory.Delete(path, true);
}
Console.WriteLine("Selected squash UI lifecycle passed.");

void Pump() { using var cancellation = new CancellationTokenSource(250); Dispatcher.UIThread.MainLoop(cancellation.Token); }
void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
string Commit(string name) { File.WriteAllText(Path.Combine(path, name), name); Git("add", "-A"); Git("commit", "-m", name); return Git("rev-parse", "HEAD").Trim(); }
string Git(params string[] args)
{
    var info = new ProcessStartInfo("git") { WorkingDirectory = path, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var arg in args) info.ArgumentList.Add(arg);
    using var process = Process.Start(info); var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync(); process.WaitForExit();
    if (process.ExitCode != 0) throw new Exception(error.Result);
    return output.Result;
}
