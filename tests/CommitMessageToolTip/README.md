# Commit-message tooltip regression

With .NET 10, Git on `PATH` and the app's native Skia dependencies installed:

```sh
dotnet run --project tests/CommitMessageToolTip -c Release -p:DisableAOT=true
```

This harness runs the production tooltip host and commit subject presenter in
Avalonia Headless with the real application styles. Controlled asynchronous
responses cover lazy loading, repeated openings, complete multi-line and Unicode
messages, failure and empty-result fallbacks, retries, row recycling, repository
changes, detach/reattach and late completion after dismissal. Real pointer input
checks issue-link tooltip priority, returning to the complete-message tooltip,
dismissal and scrolling to the end of a long body. It also checks that opening
the tooltip does not change the row dimensions.

The final integration case creates a disposable Git repository, renders the real
Histories XAML and exercises the unmodified production message query by hovering
the commit row. It checks the actual bindings, a real multi-paragraph message,
and preservation of column widths, row dimensions and selection.
No user repositories, Git configuration or saved preferences are changed. It uses
isolated XDG configuration/cache paths on Linux. The controlled asynchronous seam
is limited to obtaining the message; production control lifecycle, layout,
tooltip service, rendering and input remain active. Set
`SOURCEGIT_TOOLTIP_SCREENSHOT` to a PNG path to capture the rendered long-message
tooltip for visual review, or `SOURCEGIT_TOOLTIP_HISTORY_SCREENSHOT` to capture
the actual Histories list with its complete-message tooltip.
Windows manual QA should additionally check the real Histories list in both
layouts, selection/double-click behavior, high-DPI scaling and screen edges.
