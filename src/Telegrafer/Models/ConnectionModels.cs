using System.Runtime.Serialization;
using Telegrafer.Communication;
using Telegrafer.Utils;

namespace Telegrafer.Models
{
    public enum ConnectionSortMode
    {
        Name,
        Address,
        LastUsed,
    }

    /// <summary>
    /// A named group of saved connections, e.g. a plant or a test system. The connections of a group can use
    /// any hosts and ports.
    /// </summary>
    [DataContract]
    public sealed class ConnectionGroup : ReactiveObject
    {
        private string name = string.Empty;
        private bool isExpanded = true;

        [DataMember(Order = 0)]
        public Guid Id { get; set; } = Guid.NewGuid();

        [DataMember(Order = 1)]
        public string Name
        {
            get => this.name;
            set => this.RaiseAndSetIfChanged(ref this.name, value ?? string.Empty);
        }

        [DataMember(Order = 2)]
        public bool IsExpanded
        {
            get => this.isExpanded;
            set => this.RaiseAndSetIfChanged(ref this.isExpanded, value);
        }

        public ConnectionGroup Clone()
        {
            return new ConnectionGroup { Id = this.Id, Name = this.Name, IsExpanded = this.IsExpanded };
        }
    }

    /// <summary>
    /// A saved TCP connection with its protocol settings.
    /// </summary>
    [DataContract]
    public sealed class ConnectionProfile : ReactiveObject
    {
        private string name = string.Empty;
        private string host = string.Empty;
        private int port;
        private string? description;
        private Guid? groupId;
        private bool addStxEtx = true;
        private LineEnding lineEnding;
        private TelegramEncoding encoding;
        private DateTime? lastUsedUtc;

        [DataMember(Order = 0)]
        public Guid Id { get; set; } = Guid.NewGuid();

        [DataMember(Order = 1)]
        public string Name
        {
            get => this.name;
            set
            {
                this.RaiseAndSetIfChanged(ref this.name, value ?? string.Empty);
                this.RaisePropertyChanged(nameof(this.DisplayName));
            }
        }

        [DataMember(Order = 2)]
        public string Host
        {
            get => this.host;
            set
            {
                this.RaiseAndSetIfChanged(ref this.host, value?.Trim() ?? string.Empty);
                this.RaisePropertyChanged(nameof(this.Address));
                this.RaisePropertyChanged(nameof(this.DisplayName));
            }
        }

        [DataMember(Order = 3)]
        public int Port
        {
            get => this.port;
            set
            {
                this.RaiseAndSetIfChanged(ref this.port, value);
                this.RaisePropertyChanged(nameof(this.Address));
                this.RaisePropertyChanged(nameof(this.DisplayName));
            }
        }

        [DataMember(Order = 4)]
        public string? Description
        {
            get => this.description;
            set => this.RaiseAndSetIfChanged(ref this.description, string.IsNullOrWhiteSpace(value) ? null : value);
        }

        [DataMember(Order = 5)]
        public Guid? GroupId
        {
            get => this.groupId;
            set => this.RaiseAndSetIfChanged(ref this.groupId, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether STX and ETX are added to every sent telegram.
        /// </summary>
        [DataMember(Order = 6)]
        public bool AddStxEtx
        {
            get => this.addStxEtx;
            set => this.RaiseAndSetIfChanged(ref this.addStxEtx, value);
        }

        [DataMember(Order = 7)]
        public LineEnding LineEnding
        {
            get => this.lineEnding;
            set => this.RaiseAndSetIfChanged(ref this.lineEnding, value);
        }

        [DataMember(Order = 8)]
        public TelegramEncoding Encoding
        {
            get => this.encoding;
            set => this.RaiseAndSetIfChanged(ref this.encoding, value);
        }

        [DataMember(Order = 9)]
        public DateTime? LastUsedUtc
        {
            get => this.lastUsedUtc;
            set => this.RaiseAndSetIfChanged(ref this.lastUsedUtc, value);
        }

        public string Address => HostAndPort.Format(this.Host, this.Port);

        public string DisplayName => string.IsNullOrWhiteSpace(this.Name) ? this.Address : this.Name;

        public ConnectionProfile Clone()
        {
            var clone = new ConnectionProfile { Id = this.Id };
            clone.CopyFrom(this);
            clone.LastUsedUtc = this.LastUsedUtc;
            return clone;
        }

        /// <summary>
        /// Copies all values that can be edited by the user.
        /// </summary>
        public void CopyFrom(ConnectionProfile other)
        {
            this.Name = other.Name;
            this.Host = other.Host;
            this.Port = other.Port;
            this.Description = other.Description;
            this.GroupId = other.GroupId;
            this.AddStxEtx = other.AddStxEtx;
            this.LineEnding = other.LineEnding;
            this.Encoding = other.Encoding;
        }
    }

    /// <summary>
    /// Global settings and defaults for new connections.
    /// </summary>
    [DataContract]
    public sealed class AppSettings : ReactiveObject
    {
        public const int MinLogEntries = 100;
        public const int MaxLogEntriesLimit = 100_000;

        private bool defaultAddStxEtx = true;
        private LineEnding defaultLineEnding;
        private TelegramEncoding defaultEncoding;
        private int maxLogEntries = 10_000;
        private bool autoScroll = true;
        private bool restoreTabs = true;
        private ConnectionSortMode sortMode;

        [DataMember(Order = 0)]
        public bool DefaultAddStxEtx
        {
            get => this.defaultAddStxEtx;
            set => this.RaiseAndSetIfChanged(ref this.defaultAddStxEtx, value);
        }

        [DataMember(Order = 1)]
        public LineEnding DefaultLineEnding
        {
            get => this.defaultLineEnding;
            set => this.RaiseAndSetIfChanged(ref this.defaultLineEnding, value);
        }

        [DataMember(Order = 2)]
        public TelegramEncoding DefaultEncoding
        {
            get => this.defaultEncoding;
            set => this.RaiseAndSetIfChanged(ref this.defaultEncoding, value);
        }

        /// <summary>
        /// Gets or sets the number of telegrams that are kept in the history of a tab.
        /// </summary>
        [DataMember(Order = 3)]
        public int MaxLogEntries
        {
            get => this.maxLogEntries;
            set => this.RaiseAndSetIfChanged(ref this.maxLogEntries, Math.Clamp(value, MinLogEntries, MaxLogEntriesLimit));
        }

        [DataMember(Order = 4)]
        public bool AutoScroll
        {
            get => this.autoScroll;
            set => this.RaiseAndSetIfChanged(ref this.autoScroll, value);
        }

        [DataMember(Order = 5)]
        public bool RestoreTabs
        {
            get => this.restoreTabs;
            set => this.RaiseAndSetIfChanged(ref this.restoreTabs, value);
        }

        [DataMember(Order = 6)]
        public ConnectionSortMode SortMode
        {
            get => this.sortMode;
            set => this.RaiseAndSetIfChanged(ref this.sortMode, value);
        }

        public AppSettings Clone()
        {
            var clone = new AppSettings();
            clone.CopyFrom(this);
            return clone;
        }

        public void CopyFrom(AppSettings other)
        {
            this.DefaultAddStxEtx = other.DefaultAddStxEtx;
            this.DefaultLineEnding = other.DefaultLineEnding;
            this.DefaultEncoding = other.DefaultEncoding;
            this.MaxLogEntries = other.MaxLogEntries;
            this.AutoScroll = other.AutoScroll;
            this.RestoreTabs = other.RestoreTabs;
            this.SortMode = other.SortMode;
        }

        public ConnectionProfile CreateConnection()
        {
            return new ConnectionProfile
            {
                AddStxEtx = this.DefaultAddStxEtx,
                LineEnding = this.DefaultLineEnding,
                Encoding = this.DefaultEncoding,
            };
        }
    }

    /// <summary>
    /// Content of the connection file and of exported connection files.
    /// </summary>
    [DataContract]
    public sealed class ConnectionLibraryData
    {
        public const int CurrentVersion = 1;

        [DataMember(Order = 0)]
        public int Version { get; set; } = CurrentVersion;

        /// <summary>
        /// Gets or sets the global settings; <see langword="null"/> in exported files.
        /// </summary>
        [DataMember(Order = 1)]
        public AppSettings? Settings { get; set; }

        [DataMember(Order = 2)]
        public List<ConnectionGroup> Groups { get; set; } = new();

        [DataMember(Order = 3)]
        public List<ConnectionProfile> Connections { get; set; } = new();
    }
}
