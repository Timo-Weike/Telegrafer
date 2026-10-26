using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using Telegrafer.Communication;

namespace Telegrafer.ViewModels
{
    public enum TelegramLogKind
    {
        Sent,
        Received,
        Info,
        Error,
    }

    /// <summary>
    /// One entry of the telegram history of a connection.
    /// </summary>
    public sealed class TelegramLogEntry
    {
        private TelegramLogEntry(
            DateTime timestamp,
            TelegramLogKind kind,
            string text,
            string connectionName,
            string address,
            byte[]? data,
            TelegramEncoding encoding,
            IReadOnlyList<TextRange> controlRanges,
            TimeSpan? responseTime,
            bool isIncomplete)
        {
            this.Timestamp = timestamp;
            this.Kind = kind;
            this.Text = text;
            this.ConnectionName = connectionName;
            this.Address = address;
            this.Data = data;
            this.Encoding = encoding;
            this.ControlRanges = controlRanges;
            this.ResponseTime = responseTime;
            this.IsIncomplete = isIncomplete;
        }

        public DateTime Timestamp { get; }

        public TelegramLogKind Kind { get; }

        /// <summary>
        /// Gets the readable telegram text with control characters as "&lt;STX&gt;" etc. or a message.
        /// </summary>
        public string Text { get; }

        public string ConnectionName { get; }

        public string Address { get; }

        /// <summary>
        /// Gets the transmitted bytes of a sent or received telegram.
        /// </summary>
        public byte[]? Data { get; }

        public TelegramEncoding Encoding { get; }

        /// <summary>
        /// Gets the ranges of <see cref="Text"/> that stand for control characters or raw bytes.
        /// </summary>
        public IReadOnlyList<TextRange> ControlRanges { get; }

        /// <summary>
        /// Gets the time between the last sent telegram and this received telegram.
        /// </summary>
        public TimeSpan? ResponseTime { get; }

        /// <summary>
        /// Gets a value indicating whether a received telegram started with STX but did not end with ETX.
        /// </summary>
        public bool IsIncomplete { get; }

        public static TelegramLogEntry Sent(DateTime timestamp, byte[] data, TelegramEncoding encoding, string connectionName, string address)
        {
            var ranges = new List<TextRange>();
            var text = TelegramCodec.FormatForDisplay(data, encoding, ranges);
            return new TelegramLogEntry(timestamp, TelegramLogKind.Sent, text, connectionName, address, data, encoding, ranges, null, false);
        }

        public static TelegramLogEntry Received(
            DateTime timestamp,
            byte[] data,
            TelegramEncoding encoding,
            string connectionName,
            string address,
            TimeSpan? responseTime,
            bool isIncomplete)
        {
            var ranges = new List<TextRange>();
            var text = TelegramCodec.FormatForDisplay(data, encoding, ranges);
            return new TelegramLogEntry(timestamp, TelegramLogKind.Received, text, connectionName, address, data, encoding, ranges, responseTime, isIncomplete);
        }

        public static TelegramLogEntry Info(DateTime timestamp, string message, string connectionName, string address)
        {
            return new TelegramLogEntry(timestamp, TelegramLogKind.Info, message, connectionName, address, null, default, Array.Empty<TextRange>(), null, false);
        }

        public static TelegramLogEntry Error(DateTime timestamp, string message, string connectionName, string address)
        {
            return new TelegramLogEntry(timestamp, TelegramLogKind.Error, message, connectionName, address, null, default, Array.Empty<TextRange>(), null, false);
        }
    }

    /// <summary>
    /// Telegram history that can add and remove ranges of entries with one change notification.
    /// </summary>
    /// <remarks>
    /// The range notifications are consumed by <see cref="Views.CommView"/>; the collection is not meant to be bound
    /// to an ItemsControl.
    /// </remarks>
    public sealed class TelegramLog : ObservableCollection<TelegramLogEntry>
    {
        public void AddRange(IReadOnlyList<TelegramLogEntry> entries)
        {
            if (entries.Count == 0)
            {
                return;
            }

            this.CheckReentrancy();

            var startIndex = this.Count;
            ((List<TelegramLogEntry>)this.Items).AddRange(entries);

            this.RaiseCountChanged();
            this.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, (IList)entries.ToList(), startIndex));
        }

        public void RemoveOldest(int count)
        {
            count = Math.Min(count, this.Count);

            if (count <= 0)
            {
                return;
            }

            this.CheckReentrancy();

            var items = (List<TelegramLogEntry>)this.Items;
            var removed = items.GetRange(0, count);
            items.RemoveRange(0, count);

            this.RaiseCountChanged();
            this.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, (IList)removed, 0));
        }

        private void RaiseCountChanged()
        {
            this.OnPropertyChanged(new PropertyChangedEventArgs(nameof(this.Count)));
            this.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        }
    }

    /// <summary>
    /// Text formats of the telegram history for the display, the clipboard and the export.
    /// </summary>
    public static class TelegramLogFormatter
    {
        public const string TimeFormat = "HH:mm:ss.fff";

        public static string GetLabel(TelegramLogKind kind)
        {
            switch (kind)
            {
                case TelegramLogKind.Sent:
                    return "Gesendet";
                case TelegramLogKind.Received:
                    return "Empfangen";
                case TelegramLogKind.Error:
                    return "Fehler";
                default:
                    return "Info";
            }
        }

        public static string GetSymbol(TelegramLogKind kind)
        {
            switch (kind)
            {
                case TelegramLogKind.Sent:
                    return "→";
                case TelegramLogKind.Received:
                    return "←";
                case TelegramLogKind.Error:
                    return "!";
                default:
                    return "•";
            }
        }

        /// <summary>
        /// Gets the additional information that is written behind the telegram.
        /// </summary>
        public static string GetSuffix(TelegramLogEntry entry, bool includeResponseTime = true)
        {
            var suffix = string.Empty;

            if (entry.IsIncomplete)
            {
                suffix += "  [unvollständig, ohne ETX]";
            }

            if (includeResponseTime && entry.ResponseTime is TimeSpan responseTime)
            {
                suffix += "  (Antwort nach " + FormatDuration(responseTime) + ")";
            }

            return suffix;
        }

        public static string FormatDuration(TimeSpan duration)
        {
            return duration.TotalMilliseconds < 1000
                ? duration.TotalMilliseconds.ToString("0", CultureInfo.CurrentCulture) + " ms"
                : duration.TotalSeconds.ToString("0.000", CultureInfo.CurrentCulture) + " s";
        }

        /// <summary>
        /// Formats a duration with at most seven characters for the response time column, e.g. "21 ms" or "1,25 s".
        /// </summary>
        public static string FormatShortDuration(TimeSpan duration)
        {
            var milliseconds = duration.TotalMilliseconds;
            var seconds = duration.TotalSeconds;

            if (milliseconds < 999.5)
            {
                return milliseconds.ToString("0", CultureInfo.CurrentCulture) + " ms";
            }

            if (seconds < 9.995)
            {
                return seconds.ToString("0.00", CultureInfo.CurrentCulture) + " s";
            }

            if (seconds < 99.95)
            {
                return seconds.ToString("0.0", CultureInfo.CurrentCulture) + " s";
            }

            return Math.Min(seconds, 99_999).ToString("0", CultureInfo.CurrentCulture) + " s";
        }

        /// <summary>
        /// Formats an entry with date, direction, connection and address, e.g. for the clipboard.
        /// </summary>
        public static string FormatTextLine(TelegramLogEntry entry)
        {
            return entry.Timestamp.ToString("yyyy-MM-dd " + TimeFormat, CultureInfo.InvariantCulture)
                + "  " + GetLabel(entry.Kind).PadRight(9)
                + "  " + entry.ConnectionName + (entry.ConnectionName == entry.Address ? string.Empty : " (" + entry.Address + ")")
                + "  " + entry.Text
                + GetSuffix(entry);
        }

        public static string FormatText(IEnumerable<TelegramLogEntry> entries)
        {
            var builder = new StringBuilder();

            foreach (var entry in entries)
            {
                builder.AppendLine(FormatTextLine(entry));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Formats the entries as CSV with semicolons, which spreadsheet programs with German settings open directly.
        /// </summary>
        public static string FormatCsv(IEnumerable<TelegramLogEntry> entries)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Zeitstempel;Richtung;Verbindung;Adresse;Telegramm;Antwortzeit (ms);Unvollständig;Bytes (hex)");

            foreach (var entry in entries)
            {
                builder
                    .Append(Csv(entry.Timestamp.ToString("yyyy-MM-dd " + TimeFormat, CultureInfo.InvariantCulture))).Append(';')
                    .Append(Csv(GetLabel(entry.Kind))).Append(';')
                    .Append(Csv(entry.ConnectionName)).Append(';')
                    .Append(Csv(entry.Address)).Append(';')
                    .Append(Csv(entry.Text)).Append(';')
                    .Append(entry.ResponseTime is TimeSpan responseTime
                        ? responseTime.TotalMilliseconds.ToString("0.0", CultureInfo.CurrentCulture)
                        : string.Empty).Append(';')
                    .Append(entry.IsIncomplete ? "ja" : string.Empty).Append(';')
                    .Append(entry.Data is null ? string.Empty : TelegramCodec.FormatAsHex(entry.Data))
                    .AppendLine();
            }

            return builder.ToString();
        }

        private static string Csv(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
