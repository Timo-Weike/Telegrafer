using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows.Input;
using Telegrafer.ViewModels;

namespace Telegrafer.Views
{
    /// <summary>
    /// Shows the telegram history of a connection, one line per entry.
    /// </summary>
    /// <remarks>
    /// The history is shown in a read-only AvaloniaEdit document, which renders only the visible lines. New entries
    /// are appended and old entries are removed incrementally instead of rebuilding the whole text.
    /// </remarks>
    public partial class CommView : UserControl
    {
        public static readonly StyledProperty<TelegramLog?> EntriesProperty =
            AvaloniaProperty.Register<CommView, TelegramLog?>(nameof(Entries));

        public static readonly StyledProperty<bool> AutoScrollProperty =
            AvaloniaProperty.Register<CommView, bool>(nameof(AutoScroll), defaultValue: true);

        public static readonly StyledProperty<ICommand?> TakeOverCommandProperty =
            AvaloniaProperty.Register<CommView, ICommand?>(nameof(TakeOverCommand));

        public static readonly StyledProperty<bool> WordWrapProperty =
            AvaloniaProperty.Register<CommView, bool>(nameof(WordWrap), defaultValue: true);

        // One entry per document line; kept in sync with the document.
        private readonly List<TelegramLogEntry> lineEntries = new();
        private ScrollViewer? scrollViewer;
        private TelegramLog? observedLog;
        private TelegramLogEntry? contextEntry;
        private bool scrollToEndScheduled;

        static CommView()
        {
            EntriesProperty.Changed.Subscribe(OnEntriesPropertyChanged);
            AutoScrollProperty.Changed.Subscribe(OnAutoScrollPropertyChanged);
            WordWrapProperty.Changed.Subscribe(e => (e.Sender as CommView)?.ApplyWordWrap());
        }

        public CommView()
        {
            InitializeComponent();

            this._textEditor.Document.UndoStack.SizeLimit = 0;
            this._textEditor.TextArea.TextView.LineTransformers.Add(new LogLineColorizer(this.lineEntries));
            this._textEditor.TextArea.AddHandler(PointerPressedEvent, this.OnTextAreaPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);

            // The editor of AvaloniaEdit 0.10 does not implement ScrollToVerticalOffset, so the scroll viewer of its
            // template is used directly. The editor installs its own search panel (Ctrl+F).
            this._textEditor.TemplateApplied += (_, e) => this.scrollViewer = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
            this._contextMenu.ContextMenuOpening += this.OnContextMenuOpening;
            this._contextMenu.MenuClosed += (_, _) => this.contextEntry = null;
            this.ApplyWordWrap();
        }

        /// <summary>
        /// Gets or sets the history that is shown.
        /// </summary>
        public TelegramLog? Entries
        {
            get => GetValue(EntriesProperty);
            set => SetValue(EntriesProperty, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether the view scrolls to every new entry. Otherwise the current
        /// position is kept, also when old entries are removed.
        /// </summary>
        public bool AutoScroll
        {
            get => GetValue(AutoScrollProperty);
            set => SetValue(AutoScrollProperty, value);
        }

        /// <summary>
        /// Gets or sets the command that copies a telegram into the input; its parameter is the entry.
        /// </summary>
        public ICommand? TakeOverCommand
        {
            get => GetValue(TakeOverCommandProperty);
            set => SetValue(TakeOverCommandProperty, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether long telegrams are wrapped. Otherwise they can be scrolled
        /// horizontally and the columns stay aligned.
        /// </summary>
        public bool WordWrap
        {
            get => GetValue(WordWrapProperty);
            set => SetValue(WordWrapProperty, value);
        }

        private void ApplyWordWrap()
        {
            this._textEditor.WordWrap = this.WordWrap;
            this._textEditor.HorizontalScrollBarVisibility = this.WordWrap
                ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
                : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        }

        private static void OnEntriesPropertyChanged(AvaloniaPropertyChangedEventArgs e)
        {
            (e.Sender as CommView)?.Observe(e.NewValue as TelegramLog);
        }

        private static void OnAutoScrollPropertyChanged(AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Sender is CommView view && view.AutoScroll)
            {
                view.ScrollToEndIfEnabled();
            }
        }

        private void Observe(TelegramLog? log)
        {
            if (this.observedLog is not null)
            {
                this.observedLog.CollectionChanged -= this.OnEntriesCollectionChanged;
            }

            this.observedLog = log;

            if (log is not null)
            {
                log.CollectionChanged += this.OnEntriesCollectionChanged;
            }

            this.Rebuild();
        }

        private void OnEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null && e.NewStartingIndex == this.lineEntries.Count)
            {
                this.Append(e.NewItems.Cast<TelegramLogEntry>().ToList());
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems is not null && e.OldStartingIndex == 0)
            {
                this.RemoveFirst(e.OldItems.Count);
            }
            else
            {
                this.Rebuild();
            }
        }

        private void Append(IReadOnlyList<TelegramLogEntry> entries)
        {
            var builder = new StringBuilder();

            foreach (var entry in entries)
            {
                if (this.lineEntries.Count > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(LogLineFormat.Format(entry));
                this.lineEntries.Add(entry);
            }

            var document = this._textEditor.Document;
            document.Insert(document.TextLength, builder.ToString());
            this.ScrollToEndIfEnabled();
        }

        private void RemoveFirst(int count)
        {
            if (count >= this.lineEntries.Count)
            {
                this.Rebuild();
                return;
            }

            var textView = this._textEditor.TextArea.TextView;
            var document = this._textEditor.Document;
            var keepPosition = !this.AutoScroll && this.scrollViewer is not null;

            // Height of the removed lines; the view is moved up by this height, so that the visible lines stay.
            var removedHeight = keepPosition ? textView.GetVisualTopByDocumentLine(count + 1) : 0;
            var offset = this.scrollViewer?.Offset ?? default;

            document.Remove(0, document.GetLineByNumber(count + 1).Offset);
            this.lineEntries.RemoveRange(0, count);

            if (keepPosition)
            {
                this.scrollViewer!.Offset = new Vector(offset.X, Math.Max(0, offset.Y - removedHeight));
            }
        }

        private void Rebuild()
        {
            this.lineEntries.Clear();

            var builder = new StringBuilder();

            if (this.observedLog is not null)
            {
                foreach (var entry in this.observedLog)
                {
                    if (this.lineEntries.Count > 0)
                    {
                        builder.Append('\n');
                    }

                    builder.Append(LogLineFormat.Format(entry));
                    this.lineEntries.Add(entry);
                }
            }

            this._textEditor.Document.Text = builder.ToString();
            this.ScrollToEndIfEnabled();
        }

        private void ScrollToEndIfEnabled()
        {
            if (!this.AutoScroll || this.scrollToEndScheduled)
            {
                return;
            }

            // Runs after the layout pass, when the height of the new lines is known.
            this.scrollToEndScheduled = true;
            Dispatcher.UIThread.Post(
                () =>
                {
                    this.scrollToEndScheduled = false;

                    if (this.AutoScroll)
                    {
                        this._textEditor.ScrollToEnd();
                    }
                },
                DispatcherPriority.Background);
        }

        private TelegramLogEntry? GetEntry(int lineNumber)
        {
            var index = lineNumber - 1;
            return index >= 0 && index < this.lineEntries.Count ? this.lineEntries[index] : null;
        }

        private void OnTextAreaPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this._textEditor).Properties.IsRightButtonPressed)
            {
                var position = this._textEditor.GetPositionFromPoint(e.GetPosition(this._textEditor));
                this.contextEntry = position.HasValue ? this.GetEntry(position.Value.Line) : null;
            }
        }

        private void OnContextMenuOpening(object? sender, CancelEventArgs e)
        {
            // Opened with the keyboard: the entry of the caret line is used.
            this.contextEntry ??= this.GetEntry(this._textEditor.TextArea.Caret.Line);

            this._copyTelegramItem.IsEnabled = this.contextEntry is not null;
            this._copyLineItem.IsEnabled = this.contextEntry is not null;
            this._takeOverItem.IsEnabled = this.contextEntry?.Data is not null
                && this.TakeOverCommand?.CanExecute(this.contextEntry) == true;
        }

        private async void OnCopyTelegram(object? sender, RoutedEventArgs e)
        {
            if (this.contextEntry is not null)
            {
                await this.SetClipboardTextAsync(this.contextEntry.Text);
            }
        }

        private async void OnCopyLine(object? sender, RoutedEventArgs e)
        {
            if (this.contextEntry is not null)
            {
                await this.SetClipboardTextAsync(TelegramLogFormatter.FormatTextLine(this.contextEntry));
            }
        }

        private void OnTakeOver(object? sender, RoutedEventArgs e)
        {
            if (this.contextEntry is not null && this.TakeOverCommand?.CanExecute(this.contextEntry) == true)
            {
                this.TakeOverCommand.Execute(this.contextEntry);
            }
        }

        private void OnCopySelection(object? sender, RoutedEventArgs e)
        {
            this._textEditor.Copy();
        }

        private void OnSearch(object? sender, RoutedEventArgs e)
        {
            AvaloniaEdit.ApplicationCommands.Find.Execute(null, this._textEditor.TextArea);
        }

        private void OnSelectAll(object? sender, RoutedEventArgs e)
        {
            this._textEditor.SelectAll();
        }

        private async Task SetClipboardTextAsync(string text)
        {
            var clipboard = Application.Current?.Clipboard;

            if (clipboard is null)
            {
                return;
            }

            try
            {
                await clipboard.SetTextAsync(text);
            }
            catch (Exception ex)
            {
                // The clipboard can be locked by another application; the history itself is not affected.
                Logger.TryGet(LogEventLevel.Warning, LogArea.Control)?.Log(this, "Copying to the clipboard failed: {Exception}", ex);
            }
        }
    }

    /// <summary>
    /// Layout of a history line: "HH:mm:ss.fff  ← Empfangen    21 ms  &lt;telegram&gt;", with the response time
    /// in a column of its own.
    /// </summary>
    internal static class LogLineFormat
    {
        public const int LabelStart = 14;
        public const int LabelLength = 11;
        public const int ResponseStart = LabelStart + LabelLength + 2;
        public const int ResponseLength = 7;
        public const int ContentStart = ResponseStart + ResponseLength + 2;

        public static string Format(TelegramLogEntry entry)
        {
            var responseTime = entry.ResponseTime is TimeSpan duration ? TelegramLogFormatter.FormatShortDuration(duration) : string.Empty;

            return entry.Timestamp.ToString(TelegramLogFormatter.TimeFormat, CultureInfo.InvariantCulture)
                + "  "
                + (TelegramLogFormatter.GetSymbol(entry.Kind) + " " + TelegramLogFormatter.GetLabel(entry.Kind)).PadRight(LabelLength)
                + "  "
                + responseTime.PadLeft(ResponseLength)
                + "  "
                + GetContent(entry)
                + TelegramLogFormatter.GetSuffix(entry, includeResponseTime: false);
        }

        public static int GetSuffixStart(TelegramLogEntry entry)
        {
            return ContentStart + entry.Text.Length;
        }

        private static string GetContent(TelegramLogEntry entry)
        {
            // Messages must not break the "one line per entry" layout; telegrams contain no line breaks because
            // control characters are shown as <CR> and <LF>.
            return entry.Data is null ? entry.Text.Replace('\r', ' ').Replace('\n', ' ') : entry.Text;
        }
    }

    /// <summary>
    /// Colors the time, the direction, the telegram, its control characters and the additional information.
    /// </summary>
    internal sealed class LogLineColorizer : DocumentColorizingTransformer
    {
        private static readonly IBrush TimeBrush = new ImmutableSolidColorBrush(Color.Parse("#6E7681"));
        private static readonly IBrush SuffixBrush = new ImmutableSolidColorBrush(Color.Parse("#8B949E"));
        private static readonly IBrush ControlBrush = new ImmutableSolidColorBrush(Color.Parse("#D2A8FF"));
        private static readonly IBrush ControlBackgroundBrush = new ImmutableSolidColorBrush(Color.Parse("#2A2140"));
        private static readonly IBrush SentLabelBrush = new ImmutableSolidColorBrush(Color.Parse("#56D364"));
        private static readonly IBrush SentTextBrush = new ImmutableSolidColorBrush(Color.Parse("#C3EBCB"));
        private static readonly IBrush ReceivedLabelBrush = new ImmutableSolidColorBrush(Color.Parse("#58A6FF"));
        private static readonly IBrush ReceivedTextBrush = new ImmutableSolidColorBrush(Color.Parse("#C6E0FF"));
        private static readonly IBrush InfoBrush = new ImmutableSolidColorBrush(Color.Parse("#D29922"));
        private static readonly IBrush InfoTextBrush = new ImmutableSolidColorBrush(Color.Parse("#CDB98A"));
        private static readonly IBrush ErrorBrush = new ImmutableSolidColorBrush(Color.Parse("#F85149"));
        private static readonly IBrush ErrorTextBrush = new ImmutableSolidColorBrush(Color.Parse("#FFA198"));

        private readonly IReadOnlyList<TelegramLogEntry> entries;

        public LogLineColorizer(IReadOnlyList<TelegramLogEntry> entries)
        {
            this.entries = entries;
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            var index = line.LineNumber - 1;

            if (index < 0 || index >= this.entries.Count)
            {
                return;
            }

            var entry = this.entries[index];
            var (labelBrush, textBrush) = GetBrushes(entry.Kind);
            var contentStart = line.Offset + LogLineFormat.ContentStart;
            var suffixStart = line.Offset + LogLineFormat.GetSuffixStart(entry);

            this.Colorize(line, line.Offset, line.Offset + LogLineFormat.LabelStart, TimeBrush, null);
            this.Colorize(line, line.Offset + LogLineFormat.LabelStart, line.Offset + LogLineFormat.ResponseStart, labelBrush, null);
            this.Colorize(line, line.Offset + LogLineFormat.ResponseStart, contentStart, SuffixBrush, null);
            this.Colorize(line, contentStart, suffixStart, textBrush, null);

            foreach (var range in entry.ControlRanges)
            {
                this.Colorize(line, contentStart + range.Start, contentStart + range.Start + range.Length, ControlBrush, ControlBackgroundBrush);
            }

            this.Colorize(line, suffixStart, line.EndOffset, SuffixBrush, null);
        }

        private void Colorize(DocumentLine line, int start, int end, IBrush foreground, IBrush? background)
        {
            start = Math.Max(start, line.Offset);
            end = Math.Min(end, line.EndOffset);

            if (start >= end)
            {
                return;
            }

            this.ChangeLinePart(start, end, element =>
            {
                element.TextRunProperties.ForegroundBrush = foreground;

                if (background is not null)
                {
                    element.TextRunProperties.BackgroundBrush = background;
                }
            });
        }

        private static (IBrush Label, IBrush Text) GetBrushes(TelegramLogKind kind)
        {
            switch (kind)
            {
                case TelegramLogKind.Sent:
                    return (SentLabelBrush, SentTextBrush);
                case TelegramLogKind.Received:
                    return (ReceivedLabelBrush, ReceivedTextBrush);
                case TelegramLogKind.Error:
                    return (ErrorBrush, ErrorTextBrush);
                default:
                    return (InfoBrush, InfoTextBrush);
            }
        }
    }
}
