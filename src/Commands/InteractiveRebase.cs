using System.Text;

namespace SourceGit.Commands
{
    public class InteractiveRebase : Command
    {
        public InteractiveRebase(string repo, string basedOn, bool autoStash, bool noVerify, bool selectedSquash = false)
        {
            WorkingDirectory = repo;
            Context = repo;
            Editor = EditorType.RebaseEditor;

            var builder = new StringBuilder(512);
            builder.Append("-c core.commentChar=\"^\" -c core.commentString=\"±\" rebase -i ");
            if (selectedSquash)
                builder.Append("--no-autosquash --no-autostash --no-update-refs --no-fork-point --reapply-cherry-picks --keep-empty --empty=keep ");
            else
                builder.Append("--autosquash ");
            if (autoStash)
                builder.Append("--autostash ");
            if (noVerify)
                builder.Append("--no-verify ");

            Args = builder.Append(basedOn).ToString();
        }
    }
}
