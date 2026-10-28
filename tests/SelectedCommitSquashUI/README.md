# Selected-commit squash UI lifecycle

This Avalonia.Headless harness references the actual SourceGit app, including its
compiled XAML, view-model, message editor, and Git command infrastructure. It uses
only a disposable local repository. No successful rewrite or remote operation is
started by this UI harness (the adjacent package-free harness covers real Git
rewrites and actual app editor subprocesses).

```sh
export AVALONIA_TELEMETRY_OPTOUT=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet run --project tests/SelectedCommitSquashUI
```

Checks cover DataContext assignment before showing the owner window, deferred
initial message-editor opening, selected/skipped preview actions and messages,
editing and reopening the combined message, message-editor cancellation, preview
cancellation, repeated opening, unchanged refs/index/worktree after cancellation,
and keeping the preview open after empty-message or newly dirty-worktree Start
rejection. Repository construction uses the real constructor, with only its
current-branch field injected to avoid starting background watchers/fetch timers.

These are real control/lifecycle checks but not pixel or Windows NativeAOT tests.
The documented Avalonia telemetry opt-out is used for builds. See the adjacent
SelectedCommitSquash README for writable sandbox-directory environment settings.
