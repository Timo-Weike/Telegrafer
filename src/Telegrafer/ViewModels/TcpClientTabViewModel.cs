using ReactiveUI;
using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Windows.Input;
using Telegrafer.Communication;
using Telegrafer.Models;
using Telegrafer.Utils;

namespace Telegrafer.ViewModels
{
    /// <summary>
    /// Tab of a client connection. It is either linked to a saved connection or uses the entered address.
    /// </summary>
    [DataContract]
    public class TcpClientTabViewModel : TabViewModelBase
    {
        private readonly SerialDisposable profileSubscription = new();
        private Func<Guid?, string?> groupNameProvider = _ => null;

        public TcpClientTabViewModel()
        {
            this.TcpClientProperty = new TcpClientViewModel();

            this.ConnectCommand = ReactiveCommand.CreateFromTask(
                async () =>
                {
                    await this.TryConnect();
                }, this.WhenAnyValue(x => x.CanConnect));

            this.WhenAnyValue(x => x.IpAndPort, x => x.TcpClient.State, x => x.Profile)
                .Subscribe(x =>
                {
                    var (address, state, linkedProfile) = x;

                    this.CanConnect = state != ConnectionState.Connected
                        && state != ConnectionState.Connecting
                        && (linkedProfile is not null || HostAndPort.TryParse(address, out _, out _));
                });

            this.WhenAnyValue(x => x.TcpClient.State, x => x.TcpClient.StateDetail)
                .Subscribe(_ =>
                {
                    this.RaisePropertyChanged(nameof(State));
                    this.RaisePropertyChanged(nameof(StateText));
                    this.RaisePropertyChanged(nameof(IsBusy));
                    this.RaisePropertyChanged(nameof(HeaderToolTip));
                });
        }

        private string ipAndPort = string.Empty;

        /// <summary>
        /// Gets or sets the entered address of a connection that is not saved, e.g. "192.168.1.10:5000".
        /// </summary>
        [DataMember]
        public string IpAndPort
        {
            get => ipAndPort;
            set
            {
                var newValue = value ?? string.Empty;

                if (newValue == ipAndPort)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref ipAndPort, newValue);
                this.UpdateTitles();
            }
        }

        private Guid? connectionId;

        /// <summary>
        /// Gets or sets the id of the linked saved connection.
        /// </summary>
        [DataMember]
        public Guid? ConnectionId
        {
            get => this.Profile?.Id ?? connectionId;
            set => connectionId = value;
        }

        [DataMember]
        public bool AddStxEtx
        {
            get => this.TcpClient.AddStxEtx;
            set => this.TcpClient.AddStxEtx = value;
        }

        [DataMember]
        public LineEnding LineEnding
        {
            get => this.TcpClient.LineEnding;
            set => this.TcpClient.LineEnding = value;
        }

        [DataMember]
        public TelegramEncoding Encoding
        {
            get => this.TcpClient.Encoding;
            set => this.TcpClient.Encoding = value;
        }

        private ConnectionProfile? profile;

        public ConnectionProfile? Profile
        {
            get { return profile; }
            private set
            {
                this.RaiseAndSetIfChanged(ref profile, value);
                this.RaisePropertyChanged(nameof(IsLinked));
            }
        }

        public bool IsLinked => this.Profile is not null;

        private bool canConnect;

        public bool CanConnect
        {
            get { return canConnect; }
            set { this.RaiseAndSetIfChanged(ref canConnect, value); }
        }

        public ICommand ConnectCommand { get; }

        private TcpClientViewModel TcpClientProperty;

        public TcpClientViewModel TcpClient
        {
            get { return TcpClientProperty; }
            set { this.RaiseAndSetIfChanged(ref TcpClientProperty, value); }
        }

        public ConnectionState State => this.TcpClient.State;

        public string StateText => ConnectionStateTexts.Get(this.State);

        /// <summary>
        /// Gets a value indicating whether the tab is connected or connecting, so that its address cannot be changed.
        /// </summary>
        public bool IsBusy => this.State == ConnectionState.Connected || this.State == ConnectionState.Connecting;

        public string Header
        {
            get
            {
                if (this.Profile is not null)
                {
                    return this.Profile.DisplayName;
                }

                if (string.IsNullOrEmpty(this.IpAndPort))
                {
                    return "Neue Verbindung";
                }
                else
                {
                    return this.IpAndPort;
                }
            }
        }

        public string HeaderToolTip
        {
            get
            {
                var address = this.Profile?.Address ?? this.IpAndPort;
                var text = string.IsNullOrEmpty(address) || address == this.Header ? this.Header : $"{this.Header} ({address})";
                return text + Environment.NewLine + "Status: " + this.StateText;
            }
        }

        /// <summary>
        /// Gets the second line of the connection header: address, group and description.
        /// </summary>
        public string Subtitle
        {
            get
            {
                if (this.Profile is null)
                {
                    return "Nicht gespeicherte Verbindung";
                }

                var parts = new List<string>();

                if (this.Profile.DisplayName != this.Profile.Address)
                {
                    parts.Add(this.Profile.Address);
                }

                var groupName = this.groupNameProvider(this.Profile.GroupId);

                if (groupName is not null)
                {
                    parts.Add(groupName);
                }

                if (!string.IsNullOrWhiteSpace(this.Profile.Description))
                {
                    parts.Add(this.Profile.Description!);
                }

                return parts.Count > 0 ? string.Join("  ·  ", parts) : "Gespeicherte Verbindung";
            }
        }

        /// <summary>
        /// Links the tab to a saved connection or removes the link, and applies the global settings.
        /// </summary>
        public void Attach(ConnectionProfile? linkedProfile, AppSettings settings, Func<Guid?, string?> getGroupName)
        {
            this.groupNameProvider = getGroupName;
            this.TcpClient.MaxLogEntries = settings.MaxLogEntries;

            if (ReferenceEquals(this.Profile, linkedProfile))
            {
                this.UpdateTitles();
                return;
            }

            this.Profile = linkedProfile;
            this.connectionId = linkedProfile?.Id;

            if (linkedProfile is null)
            {
                this.profileSubscription.Disposable = null;
                this.UpdateTitles();
                return;
            }

            this.TcpClient.AddStxEtx = linkedProfile.AddStxEtx;
            this.TcpClient.LineEnding = linkedProfile.LineEnding;
            this.TcpClient.Encoding = linkedProfile.Encoding;

            // The protocol settings are kept in sync in both directions, so that a change in the tab is saved
            // with the connection and a change in the connection editor applies to the open tab.
            this.profileSubscription.Disposable = new CompositeDisposable(
                linkedProfile.WhenAnyValue(p => p.AddStxEtx).Subscribe(v => this.TcpClient.AddStxEtx = v),
                linkedProfile.WhenAnyValue(p => p.LineEnding).Subscribe(v => this.TcpClient.LineEnding = v),
                linkedProfile.WhenAnyValue(p => p.Encoding).Subscribe(v => this.TcpClient.Encoding = v),
                this.TcpClient.WhenAnyValue(c => c.AddStxEtx).Subscribe(v => linkedProfile.AddStxEtx = v),
                this.TcpClient.WhenAnyValue(c => c.LineEnding).Subscribe(v => linkedProfile.LineEnding = v),
                this.TcpClient.WhenAnyValue(c => c.Encoding).Subscribe(v => linkedProfile.Encoding = v),
                linkedProfile.WhenAnyValue(p => p.DisplayName, p => p.Address, p => p.Description, p => p.GroupId)
                    .Subscribe(_ => this.UpdateTitles()));
        }

        /// <summary>
        /// Updates header and subtitle, e.g. after a group was renamed.
        /// </summary>
        public void UpdateTitles()
        {
            if (this.Profile is not null && ipAndPort != this.Profile.Address)
            {
                this.RaiseAndSetIfChanged(ref ipAndPort, this.Profile.Address, nameof(IpAndPort));
            }

            this.TcpClient.ConnectionName = this.Header;
            this.TcpClient.Address = this.Profile?.Address ?? this.IpAndPort;
            this.RaisePropertyChanged(nameof(Header));
            this.RaisePropertyChanged(nameof(HeaderToolTip));
            this.RaisePropertyChanged(nameof(Subtitle));
        }

        private async Task<bool> TryConnect()
        {
            string host;
            int port;

            if (this.Profile is ConnectionProfile linkedProfile)
            {
                if (!HostAndPort.IsValidHost(linkedProfile.Host) || linkedProfile.Port < 1 || linkedProfile.Port > 65535)
                {
                    this.TcpClient.ReportError("Die gespeicherte Adresse ist ungültig. Bitte die Verbindung bearbeiten.");
                    return false;
                }

                host = linkedProfile.Host;
                port = linkedProfile.Port;
                linkedProfile.LastUsedUtc = DateTime.UtcNow;
            }
            else if (!HostAndPort.TryParse(this.IpAndPort, out host, out port))
            {
                this.TcpClient.ReportError($"„{this.IpAndPort}“ ist keine gültige Adresse. Erwartet wird Host:Port, z. B. 192.168.1.10:5000.");
                return false;
            }

            this.UpdateTitles();

            return await this.TcpClient.ConnectAsync(host, port);
        }
    }
}
