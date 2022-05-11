using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using System.Runtime.Serialization;
using Avalonia.ReactiveUI;
using ReactiveUI;
using System.Reactive.Disposables;
using System.Collections.Generic;
using System.Linq;
using Sytem;

namespace Telegrafer.Views
{
    public partial class CommView : UserControl
    {
        static CommView()
        {
            TextRunsProperty.Changed.Subscribe(OnTextRunsChanged);
        }

        public CommView()
        {
            InitializeComponent();
            _lineTransformer = new MyLineTransformer(this._textEditor);
            this._textEditor.TextArea.TextView.LineTransformers.Add(_lineTransformer);
        }

        private static void OnTextRunsChanged(AvaloniaPropertyChangedEventArgs e)
        {
            (e.Sender as CommView)?.OnTextRunsChanged((ObservableCollection<MyTextRun>?)e.OldValue, (ObservableCollection<MyTextRun>?)e.NewValue);
        }

        private void OnTextRunsChanged(ObservableCollection<MyTextRun>? oldValue, ObservableCollection<MyTextRun>? newValue)
        {
            if (oldValue is not null)
            {
                oldValue.CollectionChanged -= OnTextRunsCollectionChanged;
            }

            if (newValue is not null)
            {
                newValue.CollectionChanged += OnTextRunsCollectionChanged;
            }
        }

        private void OnTextRunsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // naive implementation to just build the string each time
            var strBuilder = new StringBuilder();

            var lineTypeMap = new List<TextRunType>();

            foreach (var run in this.TextRuns)
            {
                foreach (var line in run.Text.SplitLines())
                {
                    strBuilder.Append(line);
                    foreach (var sep in line.Separator)
                    {
                        strBuilder.Append(sep switch
                        {
                            '\n' => "\\n",
                            '\r' => "\\r",
                            var c => $"0x{(int)c:X}",
                        });
                    }
                    strBuilder.AppendLine();
                    lineTypeMap.Add(run.Type);
                }
            }

            this._lineTransformer.LineTypeMap = lineTypeMap;

            this._textEditor.Text = strBuilder.ToString();
            this._textEditor.ScrollToEnd();
        }

        /// <summary>
        /// Document property.
        /// </summary>
        public static readonly StyledProperty<ObservableCollection<MyTextRun>> TextRunsProperty
            = AvaloniaProperty.Register<CommView, ObservableCollection<MyTextRun>>(nameof(TextRuns));
        private MyLineTransformer _lineTransformer;

        /// <summary>
        /// Gets/Sets the document displayed by the text editor.
        /// This is a dependency property.
        /// </summary>
        public ObservableCollection<MyTextRun> TextRuns
        {
            get => GetValue(TextRunsProperty);
            set => SetValue(TextRunsProperty, value);
        }
    }


    [DataContract]

    public enum TextRunType
    {
        [EnumMember] Error,
        [EnumMember] Info,
        [EnumMember] Local,
        [EnumMember] Remote,
    }

    [DataContract]
    public class MyTextRun
    {
        public MyTextRun(string text, TextRunType type)
        {
            Text = text;
            Type = type;
        }

        [DataMember]
        public string Text { get; }

        [DataMember]
        public TextRunType Type { get; }

        public override string? ToString()
        {
            return Text.ToString();
        }

        public static MyTextRun Error(string text)
        {
            return new MyTextRun(text, TextRunType.Error);
        }

        public static MyTextRun Info(string text)
        {
            return new MyTextRun(text, TextRunType.Info);
        }

        public static MyTextRun Local(string text)
        {
            return new MyTextRun(text, TextRunType.Local);
        }

        public static MyTextRun Remote(string text)
        {
            return new MyTextRun(text, TextRunType.Remote);
        }
    }

    class MyLineTransformer : DocumentColorizingTransformer
    {
        private TextEditor textEditor;

        public MyLineTransformer(TextEditor textEditor)
        {
            this.textEditor = textEditor;
        }

        public IReadOnlyList<TextRunType> LineTypeMap { get; set; } = new List<TextRunType>();

        protected override void ColorizeLine(DocumentLine line)
        {
            var text = textEditor.Text.Substring(line.Offset, line.EndOffset - line.Offset);
            IBrush brush = Brushes.White;

            if (this.LineTypeMap.IsInBounds(line.LineNumber - 1))
            {
                var type = this.LineTypeMap[line.LineNumber - 1];
                brush = RunTypeToBrush(type);
            }

            ChangeLinePart(
                line.Offset,
                line.EndOffset,
                vl => ChangeVisualLine(vl, brush, Brushes.Transparent, FontStyle.Normal, FontWeight.Normal, false));
        }

        private IBrush RunTypeToBrush(TextRunType textRunType)
        {
            return textRunType switch
            {
                TextRunType.Error => Brushes.Red,
                TextRunType.Info => Brushes.Orange,
                TextRunType.Local => Brushes.Green,
                TextRunType.Remote => Brushes.LightBlue,
            };
        }


        void ChangeVisualLine(
            VisualLineElement visualLine,
            IBrush foreground,
            IBrush background,
            FontStyle fontStyle,
            FontWeight fontWeigth,
            bool isUnderline)
        {
            if (foreground != null)
                visualLine.TextRunProperties.ForegroundBrush = foreground;

            if (background != null)
                visualLine.TextRunProperties.BackgroundBrush = background;

            visualLine.TextRunProperties.Underline = isUnderline;

            if (visualLine.TextRunProperties.Typeface.Style != fontStyle ||
                visualLine.TextRunProperties.Typeface.Weight != fontWeigth)
            {
                visualLine.TextRunProperties.Typeface = new Typeface(
                    visualLine.TextRunProperties.Typeface.FontFamily, fontStyle, fontWeigth);
            }

        }
    }
}
