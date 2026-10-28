# Selected-commit squash regression harness

Run with .NET 10 and Git on `PATH`:

```sh
dotnet run --project tests/SelectedCommitSquash/SelectedCommitSquash.csproj
```

This package-free harness links the production preparation query, reorder model,
interactive-rebase command arguments, and todo/message serialization. Only app
process setup and minimal commit metadata are stubbed. Tests operate exclusively
on disposable repositories and do not push or alter developer Git configuration.

Coverage includes contiguous and noncontiguous selection, reversed selection
order, full messages, read-only preparation, root/merge/unrelated/detached
rejection, dirty index/worktree/untracked files, stale branch/HEAD/dirty state,
in-progress operations, empty commits, SHA-256 repositories, final-tree and
unselected-commit preservation, configured autosquash/updateRefs isolation,
and real Git conflict continuation and abort. Editor subprocesses use the
production todo/message writer, including edited combined messages.

The test `Jobs` adapter mirrors the thin preview action/message setup; it does
not instantiate the Avalonia view-model. Actual menu/popup/message-editor
interaction and the application's sequence-editor entry point require separate
UI/build verification. This harness is not a substitute for NativeAOT or compiled
Avalonia-binding validation.

## Actual application editors

After building the app, set `SOURCEGIT_TEST_APP` to its `SourceGit.dll` path and
rerun this harness. It then launches the actual app's `--rebase-todo-editor` and
`--rebase-message-editor` entry points instead of the harness adapters. This mode
also verifies that stale original HEAD, branch, or base causes Git to reject the
sequence editor before changing history, without leaving an active rebase.
Without this variable the actual-app guard scenario reports an explicit note;
the production guard predicate is still tested.

For example, on Linux:

```sh
export AVALONIA_TELEMETRY_OPTOUT=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet build src/SourceGit.csproj -c Debug
SOURCEGIT_TEST_APP="$PWD/src/bin/Debug/net10.0/SourceGit.dll" \
  dotnet run --project tests/SelectedCommitSquash
```

`AVALONIA_TELEMETRY_OPTOUT=1` is the documented opt-out in the installed
Avalonia.BuildServices package README. Sandbox environments may also need writable
`DOTNET_CLI_HOME`, `XDG_RUNTIME_DIR`, `XDG_CONFIG_HOME`, `XDG_CACHE_HOME`,
`XDG_DATA_HOME`, and `NUGET_HTTP_CACHE_PATH`; serial MSBuild (`-m:1`) and
`-p:UseSharedCompilation=false` avoid restricted compiler-server sockets.
