using System.Diagnostics;

namespace SourceGit.Commands
{
    // Only application process configuration is replaced; all preparation and
    // serialization logic is linked from production and uses real Git repos.
    public class Command
    {
        public enum EditorType { None, RebaseEditor }
        public string WorkingDirectory { get; set; }
        public string Context { get; set; }
        public string Args { get; set; }
        public EditorType Editor { get; set; }
        public bool RaiseError { get; set; }
        public class Result
        {
            public bool IsSuccess { get; set; }
            public string StdOut { get; set; }
            public string StdErr { get; set; }
        }
        protected Result ReadToEnd() => ReadToEndAsync().GetAwaiter().GetResult();
        protected async Task<Result> ReadToEndAsync()
        {
            using var process = Process.Start(new ProcessStartInfo("git", Args)
            {
                WorkingDirectory = WorkingDirectory, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true,
            });
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new() { IsSuccess = process.ExitCode == 0, StdOut = await output, StdErr = await error };
        }
        protected void RaiseException(string message) => throw new InvalidOperationException(message);
    }
}
namespace SourceGit.Models
{
    public class Commit
    {
        public string SHA { get; set; }
        public List<string> Parents { get; set; } = [];
        public User Author { get; set; }
        public ulong AuthorTime { get; set; }
        public User Committer { get; set; }
        public ulong CommitterTime { get; set; }
        public string Subject { get; set; }
        public void ParseParents(string text) => Parents.AddRange(text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        public void ParseDecorators(string text) { }
    }
}
