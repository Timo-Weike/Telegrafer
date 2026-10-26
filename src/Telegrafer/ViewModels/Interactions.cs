using Telegrafer.Communication;

namespace Telegrafer.ViewModels
{
    /// <summary>
    /// Dialogs that view models request from the main window.
    /// </summary>
    public static class Interactions
    {
        /// <summary>
        /// Shows the connection editor. The result is <see langword="true"/> if the user saved the connection.
        /// </summary>
        public static Interaction<ConnectionEditorViewModel, bool> EditConnection { get; } = new();

        /// <summary>
        /// Shows a confirmation or a text input. The result is <see langword="true"/> if the user confirmed.
        /// </summary>
        public static Interaction<PromptViewModel, bool> Prompt { get; } = new();

        public static Interaction<SettingsViewModel, bool> EditSettings { get; } = new();

        /// <summary>
        /// Asks for the path of a file to write; <see langword="null"/> if the user canceled.
        /// </summary>
        public static Interaction<FileDialogRequest, string?> SaveFile { get; } = new();

        /// <summary>
        /// Asks for the path of a file to read; <see langword="null"/> if the user canceled.
        /// </summary>
        public static Interaction<FileDialogRequest, string?> OpenFile { get; } = new();
    }

    public sealed class FileTypeFilter
    {
        public FileTypeFilter(string name, params string[] extensions)
        {
            this.Name = name;
            this.Extensions = extensions;
        }

        public string Name { get; }

        public IReadOnlyList<string> Extensions { get; }
    }

    public sealed class FileDialogRequest
    {
        public static readonly IReadOnlyList<FileTypeFilter> LogFilters = new[]
        {
            new FileTypeFilter("CSV-Datei (Semikolon)", "csv"),
            new FileTypeFilter("Textdatei", "txt"),
        };

        public static readonly IReadOnlyList<FileTypeFilter> ConnectionFilters = new[]
        {
            new FileTypeFilter("Telegrafer-Verbindungen", "json"),
        };

        public FileDialogRequest(string title, string? fileName, IReadOnlyList<FileTypeFilter> filters)
        {
            this.Title = title;
            this.FileName = fileName;
            this.Filters = filters;
        }

        public string Title { get; }

        public string? FileName { get; }

        public IReadOnlyList<FileTypeFilter> Filters { get; }
    }

    /// <summary>
    /// Value with a display name for selection lists.
    /// </summary>
    public sealed class Option<T>
    {
        public Option(T value, string displayName)
        {
            this.Value = value;
            this.DisplayName = displayName;
        }

        public T Value { get; }

        public string DisplayName { get; }

        public override string ToString()
        {
            return this.DisplayName;
        }
    }

    public static class ProtocolOptions
    {
        public static readonly IReadOnlyList<Option<LineEnding>> LineEndings = new[]
        {
            new Option<LineEnding>(LineEnding.None, "Keins"),
            new Option<LineEnding>(LineEnding.Cr, "CR (0x0D)"),
            new Option<LineEnding>(LineEnding.Lf, "LF (0x0A)"),
            new Option<LineEnding>(LineEnding.CrLf, "CR LF (0x0D 0x0A)"),
        };

        public static readonly IReadOnlyList<Option<TelegramEncoding>> Encodings = new[]
        {
            new Option<TelegramEncoding>(TelegramEncoding.Ascii, "ASCII"),
            new Option<TelegramEncoding>(TelegramEncoding.Latin1, "ISO-8859-1 (Latin-1)"),
            new Option<TelegramEncoding>(TelegramEncoding.Utf8, "UTF-8"),
        };

        public static Option<T> Find<T>(IReadOnlyList<Option<T>> options, T value)
        {
            foreach (var option in options)
            {
                if (EqualityComparer<T>.Default.Equals(option.Value, value))
                {
                    return option;
                }
            }

            return options[0];
        }
    }

    public static class ConnectionStateTexts
    {
        public static string Get(ConnectionState state)
        {
            switch (state)
            {
                case ConnectionState.Connecting:
                    return "Verbindung wird hergestellt …";
                case ConnectionState.Connected:
                    return "Verbunden";
                case ConnectionState.Failed:
                    return "Verbindungsfehler";
                case ConnectionState.Lost:
                    return "Verbindung unerwartet beendet";
                default:
                    return "Getrennt";
            }
        }
    }
}
