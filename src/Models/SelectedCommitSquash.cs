using System.Collections.Generic;
using System.Linq;

namespace SourceGit.Models
{
    public class SelectedCommitSquash
    {
        public string Head { get; set; }
        public string Branch { get; set; }
        public Commit On { get; set; }
        public List<string> Selected { get; set; } = [];
        public List<InteractiveCommit> Commits { get; set; } = [];

        // The preview displays newest first. Moving selected commits next to the
        // oldest selected commit leaves every unselected commit in relative order.
        public List<InteractiveCommit> GetReorderedCommits()
        {
            var selected = new HashSet<string>(Selected);
            return Commits.Where(x => !selected.Contains(x.Commit.SHA))
                .Concat(Commits.Where(x => selected.Contains(x.Commit.SHA))).ToList();
        }
    }
}
