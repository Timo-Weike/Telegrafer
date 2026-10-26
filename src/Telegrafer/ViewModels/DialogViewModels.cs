using System.Globalization;
using System.Reactive;
using Telegrafer.Communication;
using Telegrafer.Models;
using Telegrafer.Utils;

namespace Telegrafer.ViewModels
{
    /// <summary>
    /// Creates or edits a saved connection. The values are written to the profile only when the user saves.
    /// </summary>
    public sealed class ConnectionEditorViewModel : ViewModelBase
    {
        private readonly ConnectionProfile profile;
        private string name;
        private string host;
        private string port;
        private string description;
        private Option<ConnectionGroup?> selectedGroup;
        private bool addStxEtx;
        private Option<LineEnding> selectedLineEnding;
        private Option<TelegramEncoding> selectedEncoding;
        private string? hostError;
        private string? portError;
        private bool hostEdited;
        private bool portEdited;

        public ConnectionEditorViewModel(string title, ConnectionProfile profile, IEnumerable<ConnectionGroup> groups)
        {
            this.Title = title;
            this.profile = profile;
            this.name = profile.Name;
            this.host = profile.Host;
            this.port = profile.Port > 0 ? profile.Port.ToString(CultureInfo.InvariantCulture) : string.Empty;
            this.description = profile.Description ?? string.Empty;
            this.addStxEtx = profile.AddStxEtx;
            this.selectedLineEnding = ProtocolOptions.Find(ProtocolOptions.LineEndings, profile.LineEnding);
            this.selectedEncoding = ProtocolOptions.Find(ProtocolOptions.Encodings, profile.Encoding);

            this.GroupOptions = new[] { new Option<ConnectionGroup?>(null, "Ohne Gruppe") }
                .Concat(groups
                    .OrderBy(g => g.Name, NaturalStringComparer.Instance)
                    .Select(g => new Option<ConnectionGroup?>(g, g.Name)))
                .ToList();
            this.selectedGroup = this.GroupOptions.FirstOrDefault(o => o.Value is not null && o.Value.Id == profile.GroupId)
                ?? this.GroupOptions[0];

            this.Validate();

            var canSave = this.WhenAnyValue(x => x.HostError, x => x.PortError, (h, p) => h is null && p is null);
            this.SaveCommand = ReactiveCommand.Create(this.Apply, canSave);
            this.CancelCommand = ReactiveCommand.Create(() => { });
        }

        public string Title { get; }

        public string Name
        {
            get => this.name;
            set => this.RaiseAndSetIfChanged(ref this.name, value ?? string.Empty);
        }

        /// <summary>
        /// Gets or sets the host. A complete address like "192.168.1.10:5000" is split into host and port on save.
        /// </summary>
        public string Host
        {
            get => this.host;
            set
            {
                this.RaiseAndSetIfChanged(ref this.host, value ?? string.Empty);
                this.hostEdited = true;
                this.Validate();
            }
        }

        public string Port
        {
            get => this.port;
            set
            {
                this.RaiseAndSetIfChanged(ref this.port, value ?? string.Empty);
                this.portEdited = true;
                this.Validate();
            }
        }

        public string Description
        {
            get => this.description;
            set => this.RaiseAndSetIfChanged(ref this.description, value ?? string.Empty);
        }

        public IReadOnlyList<Option<ConnectionGroup?>> GroupOptions { get; }

        public Option<ConnectionGroup?> SelectedGroup
        {
            get => this.selectedGroup;
            set => this.RaiseAndSetIfChanged(ref this.selectedGroup, value ?? this.GroupOptions[0]);
        }

        public bool AddStxEtx
        {
            get => this.addStxEtx;
            set => this.RaiseAndSetIfChanged(ref this.addStxEtx, value);
        }

        public IReadOnlyList<Option<LineEnding>> LineEndings => ProtocolOptions.LineEndings;

        public Option<LineEnding> SelectedLineEnding
        {
            get => this.selectedLineEnding;
            set => this.RaiseAndSetIfChanged(ref this.selectedLineEnding, value ?? ProtocolOptions.LineEndings[0]);
        }

        public IReadOnlyList<Option<TelegramEncoding>> Encodings => ProtocolOptions.Encodings;

        public Option<TelegramEncoding> SelectedEncoding
        {
            get => this.selectedEncoding;
            set => this.RaiseAndSetIfChanged(ref this.selectedEncoding, value ?? ProtocolOptions.Encodings[0]);
        }

        public string? HostError
        {
            get => this.hostError;
            private set => this.RaiseAndSetIfChanged(ref this.hostError, value);
        }

        public string? PortError
        {
            get => this.portError;
            private set => this.RaiseAndSetIfChanged(ref this.portError, value);
        }

        /// <summary>
        /// Gets the host error once the user has edited the field, so that an empty new form shows no errors.
        /// </summary>
        public string? HostMessage => this.hostEdited ? this.HostError : null;

        /// <summary>
        /// Gets the port error once the user has edited the field.
        /// </summary>
        public string? PortMessage => this.portEdited ? this.PortError : null;

        public ReactiveCommand<Unit, Unit> SaveCommand { get; }

        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        private void Validate()
        {
            if (HostAndPort.TryParse(this.host, out _, out var portOfHost))
            {
                // A complete address was entered into the host field.
                this.HostError = string.IsNullOrWhiteSpace(this.port) || (HostAndPort.TryParsePort(this.port, out var enteredPort) && enteredPort == portOfHost)
                    ? null
                    : "Port doppelt angegeben – bitte nur einmal.";
                this.PortError = null;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(this.host))
                {
                    this.HostError = "Bitte Host oder IP-Adresse eingeben.";
                }
                else if (!HostAndPort.IsValidHost(this.host.Trim()))
                {
                    this.HostError = "Der Host darf keine Leerzeichen enthalten.";
                }
                else
                {
                    this.HostError = null;
                }

                this.PortError = HostAndPort.TryParsePort(this.port, out _)
                    ? null
                    : "Gültig: 1 bis 65535";
            }

            this.RaisePropertyChanged(nameof(HostMessage));
            this.RaisePropertyChanged(nameof(PortMessage));
        }

        private void Apply()
        {
            if (!HostAndPort.TryParse(this.Host, out var hostName, out var portNumber))
            {
                hostName = this.Host.Trim();
                HostAndPort.TryParsePort(this.port, out portNumber);
            }

            this.profile.Name = this.Name.Trim();
            this.profile.Host = hostName;
            this.profile.Port = portNumber;
            this.profile.Description = this.Description.Trim();
            this.profile.GroupId = this.SelectedGroup.Value?.Id;
            this.profile.AddStxEtx = this.AddStxEtx;
            this.profile.LineEnding = this.SelectedLineEnding.Value;
            this.profile.Encoding = this.SelectedEncoding.Value;
        }
    }

    /// <summary>
    /// Confirmation or text input.
    /// </summary>
    public sealed class PromptViewModel : ViewModelBase
    {
        private string text;

        public PromptViewModel(string title, string message, string confirmText, string? text = null, bool isDestructive = false)
        {
            this.Title = title;
            this.Message = message;
            this.ConfirmText = confirmText;
            this.ShowInput = text is not null;
            this.text = text ?? string.Empty;
            this.IsDestructive = isDestructive;

            var canConfirm = this.WhenAnyValue(x => x.Text, t => !this.ShowInput || !string.IsNullOrWhiteSpace(t));
            this.ConfirmCommand = ReactiveCommand.Create(() => { }, canConfirm);
            this.CancelCommand = ReactiveCommand.Create(() => { });
        }

        public string Title { get; }

        public string Message { get; }

        public string ConfirmText { get; }

        public bool ShowInput { get; }

        public bool IsDestructive { get; }

        public string Text
        {
            get => this.text;
            set => this.RaiseAndSetIfChanged(ref this.text, value ?? string.Empty);
        }

        public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }

        public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    }

    /// <summary>
    /// Edits a copy of the global settings, which is written back when the user saves.
    /// </summary>
    public sealed class SettingsViewModel : ViewModelBase
    {
        private readonly AppSettings target;
        private bool defaultAddStxEtx;
        private Option<LineEnding> defaultLineEnding;
        private Option<TelegramEncoding> defaultEncoding;
        private double maxLogEntries;
        private bool autoScroll;
        private bool restoreTabs;

        public SettingsViewModel(AppSettings settings)
        {
            this.target = settings;
            this.defaultAddStxEtx = settings.DefaultAddStxEtx;
            this.defaultLineEnding = ProtocolOptions.Find(ProtocolOptions.LineEndings, settings.DefaultLineEnding);
            this.defaultEncoding = ProtocolOptions.Find(ProtocolOptions.Encodings, settings.DefaultEncoding);
            this.maxLogEntries = settings.MaxLogEntries;
            this.autoScroll = settings.AutoScroll;
            this.restoreTabs = settings.RestoreTabs;

            this.SaveCommand = ReactiveCommand.Create(this.Apply);
            this.CancelCommand = ReactiveCommand.Create(() => { });
        }

        public bool DefaultAddStxEtx
        {
            get => this.defaultAddStxEtx;
            set => this.RaiseAndSetIfChanged(ref this.defaultAddStxEtx, value);
        }

        public IReadOnlyList<Option<LineEnding>> LineEndings => ProtocolOptions.LineEndings;

        public Option<LineEnding> DefaultLineEnding
        {
            get => this.defaultLineEnding;
            set => this.RaiseAndSetIfChanged(ref this.defaultLineEnding, value ?? ProtocolOptions.LineEndings[0]);
        }

        public IReadOnlyList<Option<TelegramEncoding>> Encodings => ProtocolOptions.Encodings;

        public Option<TelegramEncoding> DefaultEncoding
        {
            get => this.defaultEncoding;
            set => this.RaiseAndSetIfChanged(ref this.defaultEncoding, value ?? ProtocolOptions.Encodings[0]);
        }

        public double MaxLogEntries
        {
            get => this.maxLogEntries;
            set => this.RaiseAndSetIfChanged(ref this.maxLogEntries, value);
        }

        public double MinLogEntries => AppSettings.MinLogEntries;

        public double MaxLogEntriesLimit => AppSettings.MaxLogEntriesLimit;

        public bool AutoScroll
        {
            get => this.autoScroll;
            set => this.RaiseAndSetIfChanged(ref this.autoScroll, value);
        }

        public bool RestoreTabs
        {
            get => this.restoreTabs;
            set => this.RaiseAndSetIfChanged(ref this.restoreTabs, value);
        }

        public ReactiveCommand<Unit, Unit> SaveCommand { get; }

        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        private void Apply()
        {
            this.target.DefaultAddStxEtx = this.DefaultAddStxEtx;
            this.target.DefaultLineEnding = this.DefaultLineEnding.Value;
            this.target.DefaultEncoding = this.DefaultEncoding.Value;
            this.target.MaxLogEntries = (int)Math.Round(this.MaxLogEntries);
            this.target.AutoScroll = this.AutoScroll;
            this.target.RestoreTabs = this.RestoreTabs;
        }
    }
}
