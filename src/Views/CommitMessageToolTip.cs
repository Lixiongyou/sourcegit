using System;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public class CommitMessageToolTip : Border
    {
        public static readonly StyledProperty<ViewModels.Histories> HistoryProperty =
            AvaloniaProperty.Register<CommitMessageToolTip, ViewModels.Histories>(nameof(History));

        public ViewModels.Histories History
        {
            get => GetValue(HistoryProperty);
            set => SetValue(HistoryProperty, value);
        }

        public CommitMessageToolTip()
        {
            ToolTip.AddToolTipOpeningHandler(this, OnToolTipOpening);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == DataContextProperty || change.Property == HistoryProperty)
                ResetMessage();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            ResetMessage();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            ToolTip.SetIsOpen(this, false);
            ToolTip.SetTip(this, null);
            _message = null;
            _isLoading = false;
            _isLoaded = false;
        }

        protected virtual Task<string> GetMessageAsync(ViewModels.Histories history, Models.Commit commit)
        {
            return history.GetCommitFullMessageAsync(commit);
        }

        private void ResetMessage()
        {
            ToolTip.SetIsOpen(this, false);
            _isLoading = false;
            _isLoaded = false;
            _message = null;

            if (DataContext is not Models.Commit commit || History == null)
            {
                ToolTip.SetTip(this, null);
                return;
            }

            _message = new TextBlock { Text = commit.Subject, TextWrapping = TextWrapping.Wrap };
            ToolTip.SetTip(this, new ScrollViewer
            {
                MaxHeight = 480,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _message
            });
        }

        private async void OnToolTipOpening(object sender, CancelRoutedEventArgs e)
        {
            if (e.Cancel || _isLoading || _isLoaded || _message == null ||
                DataContext is not Models.Commit commit || History is not { } history)
                return;

            var message = _message;
            var sha = commit.SHA;
            _isLoading = true;

            string fullMessage;
            try
            {
                fullMessage = await GetMessageAsync(history, commit);
            }
            catch (Exception)
            {
                // A failed hover query must not interrupt the history view.
                fullMessage = string.Empty;
            }

            // DataGrid rows can be recycled while the query is running.
            if (_message != message || !this.IsAttachedToVisualTree() || History != history ||
                DataContext != commit || !commit.SHA.Equals(sha, StringComparison.Ordinal))
                return;

            _isLoading = false;
            if (!string.IsNullOrEmpty(fullMessage))
            {
                message.Text = fullMessage;
                _isLoaded = true;
            }
        }

        private TextBlock _message;
        private bool _isLoading;
        private bool _isLoaded;
    }
}
