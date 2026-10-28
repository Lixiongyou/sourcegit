using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    // Read-only preparation. Never infer ancestry from the filtered history view.
    public class QuerySelectedCommitSquash : Command
    {
        public QuerySelectedCommitSquash(string repo)
        {
            WorkingDirectory = repo;
            Context = repo;
            Editor = EditorType.None;
            RaiseError = false;
        }

        public async Task<Models.SelectedCommitSquash> GetResultAsync(IEnumerable<string> selected)
        {
            var hashes = new HashSet<string>(selected, StringComparer.Ordinal);
            if (hashes.Count < 2 || hashes.Any(x => x == null || (x.Length != 40 && x.Length != 64) || x.Any(c => !Uri.IsHexDigit(c))))
                throw new InvalidOperationException("Select at least two distinct commits to squash.");

            await CheckWorktreeAsync().ConfigureAwait(false);
            var branch = await ReadAsync("symbolic-ref --quiet HEAD").ConfigureAwait(false);
            if (!branch.StartsWith("refs/heads/", StringComparison.Ordinal))
                throw new InvalidOperationException("Squashing selected commits requires a checked-out local branch.");

            var head = await ReadAsync("rev-parse --verify HEAD").ConfigureAwait(false);
            var history = await ReadAsync($"rev-list --first-parent --parents {head}").ConfigureAwait(false);
            var remaining = new HashSet<string>(hashes, StringComparer.Ordinal);
            var range = new List<string>();
            string onto = null;
            foreach (var line in history.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 2)
                    throw new InvalidOperationException("The history to rewrite contains a merge commit. Squashing selected commits only supports linear history.");
                if (parts.Length < 2)
                    throw new InvalidOperationException("The selection includes the root commit or commits outside the current branch. Select commits after the root on the current branch.");

                range.Add(parts[0]);
                remaining.Remove(parts[0]);
                if (remaining.Count == 0)
                {
                    onto = parts[1];
                    break;
                }
            }

            if (onto == null)
                throw new InvalidOperationException("All selected commits must belong to the current branch.");

            var on = await new QuerySingleCommit(WorkingDirectory, onto).GetResultAsync().ConfigureAwait(false);
            var commits = await new QueryCommitsForInteractiveRebase(WorkingDirectory, onto).GetResultAsync().ConfigureAwait(false);
            if (on == null || !range.SequenceEqual(commits.Select(x => x.Commit.SHA)))
                throw new InvalidOperationException("The branch changed while preparing the squash. Select the commits again.");

            var plan = new Models.SelectedCommitSquash
            {
                Head = head,
                Branch = branch,
                On = on,
                Selected = hashes.ToList(),
                Commits = commits,
            };
            await ValidateAsync(plan).ConfigureAwait(false);
            return plan;
        }

        public async Task ValidateAsync(Models.SelectedCommitSquash plan)
        {
            await CheckWorktreeAsync().ConfigureAwait(false);
            var branch = await ReadAsync("symbolic-ref --quiet HEAD").ConfigureAwait(false);
            var head = await ReadAsync("rev-parse --verify HEAD").ConfigureAwait(false);
            if (!branch.Equals(plan.Branch, StringComparison.Ordinal) || !head.Equals(plan.Head, StringComparison.Ordinal))
                throw new InvalidOperationException("The current branch or HEAD changed. Close this preview and select the commits again.");
        }

        private async Task CheckWorktreeAsync()
        {
            var gitDir = await ReadAsync("rev-parse --absolute-git-dir").ConfigureAwait(false);
            foreach (var name in new[] { "rebase-merge", "rebase-apply", "sequencer", "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "BISECT_START" })
            {
                var path = Path.Combine(gitDir, name);
                if (File.Exists(path) || Directory.Exists(path))
                    throw new InvalidOperationException("Finish or abort the current Git operation before squashing selected commits.");
            }

            var status = await ReadAsync("status --porcelain=v1 --untracked-files=normal --ignore-submodules=none").ConfigureAwait(false);
            if (!string.IsNullOrEmpty(status))
                throw new InvalidOperationException("Commit or stash all worktree and index changes, including untracked files, before squashing selected commits.");
        }

        private async Task<string> ReadAsync(string args)
        {
            Args = args;
            var result = await ReadToEndAsync().ConfigureAwait(false);
            if (!result.IsSuccess)
                throw new InvalidOperationException($"Cannot prepare selected commit squash: {result.StdErr.Trim()}");
            return result.StdOut.Trim();
        }
    }
}
