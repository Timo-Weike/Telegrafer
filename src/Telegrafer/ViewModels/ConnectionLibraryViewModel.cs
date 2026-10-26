using System.Globalization;
using System.Net;
using System.Reactive.Linq;
using System.Windows.Input;
using Avalonia.Threading;
using Telegrafer.Communication;
using Telegrafer.Models;
using Telegrafer.Utils;

namespace Telegrafer.ViewModels
{
    /// <summary>
    /// Group node of the connection tree. Connections without group are shown in a node without group model.
    /// </summary>
    public sealed class ConnectionGroupNode : ReactiveObject
    {
        private readonly ConnectionLibraryViewModel library;
        private readonly bool isFiltered;

        public ConnectionGroupNode(ConnectionLibraryViewModel library, ConnectionGroup? group, IReadOnlyList<ConnectionNode> connections, bool isFiltered)
        {
            this.library = library;
            this.Group = group;
            this.Connections = connections;
            this.isFiltered = isFiltered;
        }

        public ConnectionGroup? Group { get; }

        public string Name => this.Group?.Name ?? "Ohne Gruppe";

        public bool IsUngrouped => this.Group is null;

        public IReadOnlyList<ConnectionNode> Connections { get; }

        public int Count => this.Connections.Count;

        /// <summary>
        /// Gets or sets a value indicating whether the group is expanded. While searching, all groups are expanded
        /// without changing the saved state.
        /// </summary>
        public bool IsExpanded
        {
            get => this.isFiltered || (this.Group?.IsExpanded ?? this.library.UngroupedExpanded);
            set
            {
                if (this.isFiltered)
                {
                    return;
                }

                if (this.Group is not null)
                {
                    this.Group.IsExpanded = value;
                }
                else
                {
                    this.library.UngroupedExpanded = value;
                }

                this.RaisePropertyChanged();
            }
        }
    }

    /// <summary>
    /// Connection node of the connection tree and of the list of recently used connections.
    /// </summary>
    public sealed class ConnectionNode : ReactiveObject, IDisposable
    {
        private readonly ConnectionLibraryViewModel library;
        private readonly IDisposable profileSubscription;
        private ConnectionState? state;

        public ConnectionNode(ConnectionLibraryViewModel library, ConnectionProfile profile)
        {
            this.library = library;
            this.Profile = profile;
            this.profileSubscription = profile.Changed.Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(this.ToolTip));
                this.RaisePropertyChanged(nameof(this.AddressHint));
            });
        }

        public ConnectionProfile Profile { get; }

        /// <summary>
        /// Gets the address if it is not already shown as name, otherwise <see langword="null"/>.
        /// </summary>
        public string? AddressHint => this.Profile.DisplayName == this.Profile.Address ? null : this.Profile.Address;

        /// <summary>
        /// Gets or sets the state of the open tab of this connection; <see langword="null"/> if no tab is open.
        /// </summary>
        public ConnectionState? State
        {
            get => this.state;
            set
            {
                this.RaiseAndSetIfChanged(ref this.state, value);
                this.RaisePropertyChanged(nameof(this.ToolTip));
            }
        }

        /// <summary>
        /// Gets or sets a value that is required by the tree item style and has no meaning for connections.
        /// </summary>
        public bool IsExpanded { get; set; }

        public IReadOnlyList<MenuItemViewModel> MoveTargets => this.library.GetMoveTargets(this);

        public string ToolTip
        {
            get
            {
                var lines = new List<string> { this.Profile.DisplayName, this.Profile.Address };
                var groupName = this.library.GetGroupName(this.Profile.GroupId);

                if (groupName is not null)
                {
                    lines.Add("Gruppe: " + groupName);
                }

                if (!string.IsNullOrWhiteSpace(this.Profile.Description))
                {
                    lines.Add(this.Profile.Description!);
                }

                if (this.State is ConnectionState currentState)
                {
                    lines.Add("Status: " + ConnectionStateTexts.Get(currentState));
                }

                lines.Add("Doppelklick: öffnen und verbinden");

                return string.Join(Environment.NewLine, lines);
            }
        }

        public void Dispose()
        {
            this.profileSubscription.Dispose();
        }
    }

    public sealed class MoveConnectionRequest
    {
        public MoveConnectionRequest(ConnectionNode node, ConnectionGroup? group)
        {
            this.Node = node;
            this.Group = group;
        }

        public ConnectionNode Node { get; }

        public ConnectionGroup? Group { get; }
    }

    /// <summary>
    /// Saved connections and groups as shown in the side bar.
    /// </summary>
    public sealed class ConnectionLibraryViewModel : ViewModelBase
    {
        private const int RecentConnectionCount = 5;

        private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

        private readonly ConnectionStore store;
        private readonly Dictionary<Guid, ConnectionNode> nodes = new();
        private readonly Dictionary<object, IDisposable> modelSubscriptions = new();
        private readonly object writeLock = new();
        private readonly DispatcherTimer saveTimer;
        private ConnectionLibraryData data = new();
        private bool canSave;
        private bool isDirty;
        private bool rebuildScheduled;
        private long snapshotVersion;
        private long writtenVersion;
        private string searchText = string.Empty;
        private bool ungroupedExpanded = true;
        private bool isLoaded;
        private bool isEmpty = true;
        private bool hasNoSearchResults;
        private bool showRecent;
        private object? selectedItem;

        public ConnectionLibraryViewModel(ConnectionStore store)
        {
            this.store = store;
            this.Settings = new AppSettings();
            this.data.Settings = this.Settings;

            this.saveTimer = new DispatcherTimer { Interval = SaveDelay };
            this.saveTimer.Tick += this.OnSaveTimerTick;

            this.NewConnectionCommand = ReactiveCommand.CreateFromTask<object?>(this.NewConnectionAsync);
            this.EditConnectionCommand = ReactiveCommand.CreateFromTask<ConnectionNode>(node => this.EditConnectionAsync(node.Profile));
            this.DuplicateConnectionCommand = ReactiveCommand.CreateFromTask<ConnectionNode>(this.DuplicateConnectionAsync);
            this.DeleteConnectionCommand = ReactiveCommand.CreateFromTask<ConnectionNode>(this.DeleteConnectionAsync);
            this.MoveConnectionCommand = ReactiveCommand.Create<MoveConnectionRequest>(r => r.Node.Profile.GroupId = r.Group?.Id);
            this.NewGroupCommand = ReactiveCommand.CreateFromTask(this.NewGroupAsync);
            this.RenameGroupCommand = ReactiveCommand.CreateFromTask<ConnectionGroupNode>(this.RenameGroupAsync);
            this.DeleteGroupCommand = ReactiveCommand.CreateFromTask<ConnectionGroupNode>(this.DeleteGroupAsync);
            this.OpenCommand = ReactiveCommand.Create<ConnectionNode>(node => this.OpenRequested?.Invoke(node.Profile, false));
            this.OpenInNewTabCommand = ReactiveCommand.Create<ConnectionNode>(node => this.OpenRequested?.Invoke(node.Profile, true));
            this.OpenSelectedCommand = ReactiveCommand.Create(
                () =>
                {
                    if (this.SelectedItem is ConnectionNode node)
                    {
                        this.OpenRequested?.Invoke(node.Profile, false);
                    }
                });
            this.SortCommand = ReactiveCommand.Create<ConnectionSortMode>(mode => this.Settings.SortMode = mode);
            this.ClearSearchCommand = ReactiveCommand.Create(() => { this.SearchText = string.Empty; });
            this.ImportCommand = ReactiveCommand.CreateFromTask(this.ImportAsync);
            this.ExportCommand = ReactiveCommand.CreateFromTask(this.ExportAsync);

            this.WhenAnyValue(x => x.SearchText)
                .Skip(1)
                .Throttle(TimeSpan.FromMilliseconds(150))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => this.RebuildTree());
        }

        /// <summary>
        /// Raised when a connection is to be opened; the flag requests a new tab even if one is open already.
        /// </summary>
        public event Action<ConnectionProfile, bool>? OpenRequested;

        /// <summary>
        /// Raised after connections or groups have been changed.
        /// </summary>
        public event EventHandler? DataChanged;

        /// <summary>
        /// Raised with a message for the user, e.g. after an import or if saving failed.
        /// </summary>
        public event Action<string>? MessageRaised;

        public AppSettings Settings { get; }

        public ObservableCollection<ConnectionGroupNode> Nodes { get; } = new();

        public ObservableCollection<ConnectionNode> RecentConnections { get; } = new();

        public IReadOnlyList<ConnectionProfile> Connections => this.data.Connections;

        public string SearchText
        {
            get => this.searchText;
            set => this.RaiseAndSetIfChanged(ref this.searchText, value ?? string.Empty);
        }

        public bool UngroupedExpanded
        {
            get => this.ungroupedExpanded;
            set => this.RaiseAndSetIfChanged(ref this.ungroupedExpanded, value);
        }

        public bool IsLoaded
        {
            get => this.isLoaded;
            private set => this.RaiseAndSetIfChanged(ref this.isLoaded, value);
        }

        public bool IsEmpty
        {
            get => this.isEmpty;
            private set => this.RaiseAndSetIfChanged(ref this.isEmpty, value);
        }

        public bool HasNoSearchResults
        {
            get => this.hasNoSearchResults;
            private set => this.RaiseAndSetIfChanged(ref this.hasNoSearchResults, value);
        }

        public bool ShowRecent
        {
            get => this.showRecent;
            private set => this.RaiseAndSetIfChanged(ref this.showRecent, value);
        }

        public object? SelectedItem
        {
            get => this.selectedItem;
            set => this.RaiseAndSetIfChanged(ref this.selectedItem, value);
        }

        // The active sort mode is marked in the menu text, because menu flyouts of Avalonia 0.10 cut off icons.
        public string SortByNameHeader => GetSortHeader("Sortieren nach Name", ConnectionSortMode.Name);

        public string SortByAddressHeader => GetSortHeader("Sortieren nach Adresse", ConnectionSortMode.Address);

        public string SortByLastUseHeader => GetSortHeader("Sortieren nach letzter Verwendung", ConnectionSortMode.LastUsed);

        public ICommand NewConnectionCommand { get; }
        public ICommand EditConnectionCommand { get; }
        public ICommand DuplicateConnectionCommand { get; }
        public ICommand DeleteConnectionCommand { get; }
        public ICommand MoveConnectionCommand { get; }
        public ICommand NewGroupCommand { get; }
        public ICommand RenameGroupCommand { get; }
        public ICommand DeleteGroupCommand { get; }
        public ICommand OpenCommand { get; }
        public ICommand OpenInNewTabCommand { get; }
        public ICommand OpenSelectedCommand { get; }
        public ICommand SortCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand ExportCommand { get; }

        /// <summary>
        /// Loads the saved connections without blocking the UI thread. On the first start the addresses that
        /// earlier versions saved are taken over.
        /// </summary>
        /// <returns>A message for the user or <see langword="null"/>.</returns>
        public async Task<string?> LoadAsync(IReadOnlyList<string> legacyAddresses)
        {
            ConnectionLoadResult result;

            try
            {
                result = await Task.Run(() => this.store.Load());
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                this.IsLoaded = true;
                return "Die gespeicherten Verbindungen konnten nicht geladen werden: " + ex.Message;
            }

            var messages = new List<string>();

            if (result.Warning is not null)
            {
                messages.Add(result.Warning);
            }

            if (result.Data.Settings is not null)
            {
                this.Settings.CopyFrom(result.Data.Settings);
            }

            this.data = result.Data;
            this.data.Settings = this.Settings;
            this.canSave = result.CanSave;

            if (!result.FileExisted && legacyAddresses.Count > 0)
            {
                var invalid = ConnectionStore.AddLegacyConnections(this.data, legacyAddresses, this.Settings);
                this.isDirty = true;

                if (invalid.Count > 0)
                {
                    messages.Add("Folgende gespeicherte Adressen konnten nicht übernommen werden: " + string.Join(", ", invalid));
                }
            }

            foreach (var group in this.data.Groups)
            {
                this.Track(group);
            }

            foreach (var connection in this.data.Connections)
            {
                this.Track(connection);
            }

            this.Track(this.Settings);
            this.IsLoaded = true;
            this.RebuildTree();
            this.RebuildRecent();
            this.RaiseSortHeadersChanged();

            if (this.isDirty)
            {
                this.RequestSave();
            }

            this.DataChanged?.Invoke(this, EventArgs.Empty);

            return messages.Count > 0 ? string.Join(" ", messages) : null;
        }

        public ConnectionProfile? FindConnection(Guid id)
        {
            return this.data.Connections.FirstOrDefault(c => c.Id == id);
        }

        public string? GetGroupName(Guid? groupId)
        {
            return groupId is Guid id ? this.data.Groups.FirstOrDefault(g => g.Id == id)?.Name : null;
        }

        /// <summary>
        /// Shows the connection editor for a new connection and adds it if the user saves.
        /// </summary>
        public async Task<ConnectionProfile?> CreateConnectionAsync(ConnectionProfile profile, string title)
        {
            var editor = new ConnectionEditorViewModel(title, profile, this.data.Groups);

            if (!await Interactions.EditConnection.Handle(editor))
            {
                return null;
            }

            this.data.Connections.Add(profile);
            this.Track(profile);
            this.OnStructureChanged();

            return profile;
        }

        public async Task EditConnectionAsync(ConnectionProfile profile)
        {
            var copy = profile.Clone();
            var editor = new ConnectionEditorViewModel("Verbindung bearbeiten", copy, this.data.Groups);

            if (await Interactions.EditConnection.Handle(editor))
            {
                profile.CopyFrom(copy);
            }
        }

        public async Task EditSettingsAsync()
        {
            await Interactions.EditSettings.Handle(new SettingsViewModel(this.Settings));
        }

        /// <summary>
        /// Sets the connection states of the open tabs.
        /// </summary>
        public void UpdateStates(IReadOnlyDictionary<Guid, ConnectionState> states)
        {
            foreach (var node in this.nodes.Values)
            {
                node.State = states.TryGetValue(node.Profile.Id, out var state) ? state : null;
            }
        }

        public IReadOnlyList<MenuItemViewModel> GetMoveTargets(ConnectionNode node)
        {
            return new[] { (ConnectionGroup?)null }
                .Concat(this.data.Groups.OrderBy(g => g.Name, NaturalStringComparer.Instance))
                .Where(g => g?.Id != node.Profile.GroupId)
                .Select(g => new MenuItemViewModel
                {
                    Header = g?.Name ?? "Ohne Gruppe",
                    Command = this.MoveConnectionCommand,
                    CommandParameter = new MoveConnectionRequest(node, g),
                })
                .ToList();
        }

        /// <summary>
        /// Writes pending changes synchronously; used when the application exits.
        /// </summary>
        public void SaveNow()
        {
            this.saveTimer.Stop();

            if (!this.isDirty || !this.canSave || !this.IsLoaded)
            {
                return;
            }

            this.isDirty = false;
            var json = this.store.Serialize(this.data);
            this.WriteSnapshot(json, ++this.snapshotVersion);
        }

        private async Task NewConnectionAsync(object? parameter)
        {
            var profile = this.Settings.CreateConnection();
            profile.GroupId = (parameter as ConnectionGroupNode)?.Group?.Id;

            await this.CreateConnectionAsync(profile, "Neue Verbindung");
        }

        private async Task DuplicateConnectionAsync(ConnectionNode node)
        {
            var copy = node.Profile.Clone();
            copy.Id = Guid.NewGuid();
            copy.LastUsedUtc = null;
            copy.Name = node.Profile.DisplayName + " (Kopie)";

            await this.CreateConnectionAsync(copy, "Verbindung duplizieren");
        }

        private async Task DeleteConnectionAsync(ConnectionNode node)
        {
            var prompt = new PromptViewModel(
                "Verbindung löschen",
                $"Soll die gespeicherte Verbindung „{node.Profile.DisplayName}“ ({node.Profile.Address}) gelöscht werden? "
                    + "Ein geöffneter Tab bleibt als nicht gespeicherte Verbindung erhalten.",
                "Löschen",
                isDestructive: true);

            if (!await Interactions.Prompt.Handle(prompt))
            {
                return;
            }

            this.data.Connections.Remove(node.Profile);
            this.Untrack(node.Profile);

            if (this.nodes.Remove(node.Profile.Id, out var removedNode))
            {
                removedNode.Dispose();
            }

            this.OnStructureChanged();
        }

        private async Task NewGroupAsync()
        {
            var prompt = new PromptViewModel("Neue Gruppe", "Name der Gruppe, z. B. Anlage oder Testsystem:", "Anlegen", text: string.Empty);

            if (!await Interactions.Prompt.Handle(prompt))
            {
                return;
            }

            var group = new ConnectionGroup { Name = prompt.Text.Trim() };
            this.data.Groups.Add(group);
            this.Track(group);
            this.OnStructureChanged();
        }

        private async Task RenameGroupAsync(ConnectionGroupNode node)
        {
            if (node.Group is null)
            {
                return;
            }

            var prompt = new PromptViewModel("Gruppe umbenennen", "Neuer Name der Gruppe:", "Umbenennen", text: node.Group.Name);

            if (await Interactions.Prompt.Handle(prompt))
            {
                node.Group.Name = prompt.Text.Trim();
            }
        }

        private async Task DeleteGroupAsync(ConnectionGroupNode node)
        {
            if (node.Group is null)
            {
                return;
            }

            var group = node.Group;
            var connections = this.data.Connections.Where(c => c.GroupId == group.Id).ToList();
            var message = connections.Count == 0
                ? $"Soll die leere Gruppe „{group.Name}“ gelöscht werden?"
                : $"Soll die Gruppe „{group.Name}“ gelöscht werden? Ihre {connections.Count} Verbindung(en) bleiben erhalten und werden nach „Ohne Gruppe“ verschoben.";

            if (!await Interactions.Prompt.Handle(new PromptViewModel("Gruppe löschen", message, "Löschen", isDestructive: true)))
            {
                return;
            }

            foreach (var connection in connections)
            {
                connection.GroupId = null;
            }

            this.data.Groups.Remove(group);
            this.Untrack(group);
            this.OnStructureChanged();
        }

        private async Task ImportAsync()
        {
            var path = await Interactions.OpenFile.Handle(new FileDialogRequest("Verbindungen importieren", null, FileDialogRequest.ConnectionFilters));

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            ConnectionLibraryData imported;

            try
            {
                imported = await Task.Run(() => ConnectionStore.Import(path));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                this.MessageRaised?.Invoke("Import fehlgeschlagen: " + ex.Message);
                return;
            }

            // Groups with the same name are merged, connections that exist already (same id) are skipped.
            var groupMapping = new Dictionary<Guid, Guid>();
            var addedGroups = 0;

            foreach (var group in imported.Groups)
            {
                var existing = this.data.Groups.FirstOrDefault(g => g.Id == group.Id)
                    ?? this.data.Groups.FirstOrDefault(g => string.Equals(g.Name, group.Name, StringComparison.CurrentCultureIgnoreCase));

                if (existing is not null)
                {
                    groupMapping[group.Id] = existing.Id;
                    continue;
                }

                this.data.Groups.Add(group);
                this.Track(group);
                groupMapping[group.Id] = group.Id;
                addedGroups++;
            }

            var addedConnections = 0;
            var skippedConnections = 0;

            foreach (var connection in imported.Connections)
            {
                if (this.data.Connections.Any(c => c.Id == connection.Id))
                {
                    skippedConnections++;
                    continue;
                }

                connection.GroupId = connection.GroupId is Guid groupId && groupMapping.TryGetValue(groupId, out var mappedId) ? mappedId : null;
                this.data.Connections.Add(connection);
                this.Track(connection);
                addedConnections++;
            }

            this.OnStructureChanged();
            this.MessageRaised?.Invoke(
                $"Import abgeschlossen: {addedConnections} Verbindung(en) und {addedGroups} Gruppe(n) hinzugefügt"
                + (skippedConnections > 0 ? $", {skippedConnections} bereits vorhandene Verbindung(en) übersprungen." : "."));
        }

        private async Task ExportAsync()
        {
            var fileName = $"Telegrafer-Verbindungen_{DateTime.Now:yyyyMMdd}.json";
            var path = await Interactions.SaveFile.Handle(new FileDialogRequest("Verbindungen exportieren", fileName, FileDialogRequest.ConnectionFilters));

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var groups = this.data.Groups.ToList();
            var connections = this.data.Connections.ToList();

            try
            {
                await Task.Run(() => ConnectionStore.Export(path, groups, connections));
                this.MessageRaised?.Invoke($"{connections.Count} Verbindung(en) und {groups.Count} Gruppe(n) exportiert nach „{path}“.");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                this.MessageRaised?.Invoke("Export fehlgeschlagen: " + ex.Message);
            }
        }

        private void Track(ReactiveObject model)
        {
            this.modelSubscriptions[model] = model.Changed.Subscribe(e => this.OnModelChanged(model, e.PropertyName));
        }

        private void Untrack(ReactiveObject model)
        {
            if (this.modelSubscriptions.Remove(model, out var subscription))
            {
                subscription.Dispose();
            }
        }

        private void OnModelChanged(object model, string? propertyName)
        {
            this.RequestSave();

            switch (model)
            {
                case ConnectionProfile:
                    if (propertyName == nameof(ConnectionProfile.LastUsedUtc))
                    {
                        this.RebuildRecent();

                        if (this.Settings.SortMode == ConnectionSortMode.LastUsed)
                        {
                            this.ScheduleRebuild();
                        }
                    }
                    else if (propertyName == nameof(ConnectionProfile.DisplayName)
                        || propertyName == nameof(ConnectionProfile.Address)
                        || propertyName == nameof(ConnectionProfile.GroupId)
                        || propertyName == nameof(ConnectionProfile.Description))
                    {
                        this.ScheduleRebuild();
                        this.DataChanged?.Invoke(this, EventArgs.Empty);
                    }

                    break;
                case ConnectionGroup when propertyName == nameof(ConnectionGroup.Name):
                    this.ScheduleRebuild();
                    this.DataChanged?.Invoke(this, EventArgs.Empty);
                    break;
                case AppSettings when propertyName == nameof(AppSettings.SortMode):
                    this.ScheduleRebuild();
                    this.RaiseSortHeadersChanged();
                    break;
            }
        }

        private string GetSortHeader(string text, ConnectionSortMode mode)
        {
            return this.Settings.SortMode == mode ? "✓  " + text : text;
        }

        private void RaiseSortHeadersChanged()
        {
            this.RaisePropertyChanged(nameof(SortByNameHeader));
            this.RaisePropertyChanged(nameof(SortByAddressHeader));
            this.RaisePropertyChanged(nameof(SortByLastUseHeader));
        }

        private void OnStructureChanged()
        {
            this.RequestSave();
            this.RebuildTree();
            this.RebuildRecent();
            this.DataChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ScheduleRebuild()
        {
            if (this.rebuildScheduled)
            {
                return;
            }

            this.rebuildScheduled = true;
            Dispatcher.UIThread.Post(() =>
            {
                this.rebuildScheduled = false;
                this.RebuildTree();
            });
        }

        private void RebuildTree()
        {
            if (!this.IsLoaded)
            {
                return;
            }

            var filter = this.SearchText.Trim();
            var isFiltered = filter.Length > 0;
            var comparer = this.CreateComparer();
            var result = new List<ConnectionGroupNode>();

            foreach (var group in this.data.Groups.OrderBy(g => g.Name, NaturalStringComparer.Instance))
            {
                var groupMatches = isFiltered && Contains(group.Name, filter);
                var connections = this.data.Connections
                    .Where(c => c.GroupId == group.Id && (!isFiltered || groupMatches || this.Matches(c, filter)))
                    .OrderBy(c => c, comparer)
                    .Select(this.GetNode)
                    .ToList();

                if (isFiltered && connections.Count == 0 && !groupMatches)
                {
                    continue;
                }

                result.Add(new ConnectionGroupNode(this, group, connections, isFiltered));
            }

            var ungrouped = this.data.Connections
                .Where(c => c.GroupId is null && (!isFiltered || this.Matches(c, filter)))
                .OrderBy(c => c, comparer)
                .Select(this.GetNode)
                .ToList();

            if (ungrouped.Count > 0)
            {
                result.Add(new ConnectionGroupNode(this, null, ungrouped, isFiltered));
            }

            this.Nodes.Clear();

            foreach (var node in result)
            {
                this.Nodes.Add(node);
            }

            this.IsEmpty = this.data.Connections.Count == 0 && this.data.Groups.Count == 0;
            this.HasNoSearchResults = isFiltered && result.Count == 0;
            this.ShowRecent = !isFiltered && this.RecentConnections.Count > 0;
        }

        private void RebuildRecent()
        {
            var recent = this.data.Connections
                .Where(c => c.LastUsedUtc.HasValue)
                .OrderByDescending(c => c.LastUsedUtc)
                .Take(RecentConnectionCount)
                .Select(this.GetNode)
                .ToList();

            if (!recent.SequenceEqual(this.RecentConnections))
            {
                this.RecentConnections.Clear();

                foreach (var node in recent)
                {
                    this.RecentConnections.Add(node);
                }
            }

            this.ShowRecent = this.SearchText.Trim().Length == 0 && this.RecentConnections.Count > 0;
        }

        private ConnectionNode GetNode(ConnectionProfile profile)
        {
            if (!this.nodes.TryGetValue(profile.Id, out var node) || !ReferenceEquals(node.Profile, profile))
            {
                node?.Dispose();
                node = new ConnectionNode(this, profile);
                this.nodes[profile.Id] = node;
            }

            return node;
        }

        private bool Matches(ConnectionProfile connection, string filter)
        {
            return Contains(connection.DisplayName, filter)
                || Contains(connection.Address, filter)
                || Contains(connection.Description, filter)
                || Contains(this.GetGroupName(connection.GroupId), filter);
        }

        private static bool Contains(string? text, string filter)
        {
            return text is not null
                && CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, filter, CompareOptions.IgnoreCase) >= 0;
        }

        private IComparer<ConnectionProfile> CreateComparer()
        {
            switch (this.Settings.SortMode)
            {
                case ConnectionSortMode.Address:
                    return Comparer<ConnectionProfile>.Create((a, b) =>
                    {
                        var result = CompareByAddress(a, b);
                        return result != 0 ? result : NaturalStringComparer.Instance.Compare(a.DisplayName, b.DisplayName);
                    });
                case ConnectionSortMode.LastUsed:
                    return Comparer<ConnectionProfile>.Create((a, b) =>
                    {
                        // Most recently used first, never used connections last.
                        var result = Nullable.Compare(b.LastUsedUtc, a.LastUsedUtc);
                        return result != 0 ? result : NaturalStringComparer.Instance.Compare(a.DisplayName, b.DisplayName);
                    });
                default:
                    return Comparer<ConnectionProfile>.Create((a, b) =>
                    {
                        var result = NaturalStringComparer.Instance.Compare(a.DisplayName, b.DisplayName);
                        return result != 0 ? result : CompareByAddress(a, b);
                    });
            }
        }

        private static int CompareByAddress(ConnectionProfile a, ConnectionProfile b)
        {
            // IP addresses are compared numerically and sorted before host names.
            var isIpA = IPAddress.TryParse(a.Host, out var ipA);
            var isIpB = IPAddress.TryParse(b.Host, out var ipB);
            int result;

            if (isIpA && isIpB)
            {
                result = ipA!.MapToIPv6().GetAddressBytes().AsSpan().SequenceCompareTo(ipB!.MapToIPv6().GetAddressBytes());
            }
            else if (isIpA != isIpB)
            {
                result = isIpA ? -1 : 1;
            }
            else
            {
                result = NaturalStringComparer.Instance.Compare(a.Host, b.Host);
            }

            return result != 0 ? result : a.Port.CompareTo(b.Port);
        }

        private void RequestSave()
        {
            if (!this.IsLoaded)
            {
                return;
            }

            this.isDirty = true;

            if (this.canSave)
            {
                this.saveTimer.Stop();
                this.saveTimer.Start();
            }
        }

        private void OnSaveTimerTick(object? sender, EventArgs e)
        {
            this.saveTimer.Stop();

            if (!this.isDirty || !this.canSave)
            {
                return;
            }

            this.isDirty = false;

            // Serialized on the UI thread, written on a background thread. WriteInBackgroundAsync handles all
            // errors itself, so the task does not need to be awaited.
            var json = this.store.Serialize(this.data);
            var version = ++this.snapshotVersion;
            _ = this.WriteInBackgroundAsync(json, version);
        }

        private async Task WriteInBackgroundAsync(string json, long version)
        {
            try
            {
                await Task.Run(() => this.WriteSnapshot(json, version));
            }
            catch (Exception ex)
            {
                this.isDirty = true;
                this.MessageRaised?.Invoke("Die Verbindungen konnten nicht gespeichert werden: " + ex.Message);
            }
        }

        private void WriteSnapshot(string json, long version)
        {
            // The lock is taken on the writing thread only, so that SaveNow can wait for a running write without
            // blocking a continuation on the UI thread.
            lock (this.writeLock)
            {
                if (version <= this.writtenVersion)
                {
                    return;
                }

                this.store.Write(json);
                this.writtenVersion = version;
            }
        }
    }
}
