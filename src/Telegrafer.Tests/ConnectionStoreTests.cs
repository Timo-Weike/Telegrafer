using Telegrafer.Communication;
using Telegrafer.Models;
using Xunit;

namespace Telegrafer.Tests
{
    public sealed class ConnectionStoreTests : IDisposable
    {
        private readonly string directory;
        private readonly ConnectionStore store;

        public ConnectionStoreTests()
        {
            this.directory = Path.Combine(Path.GetTempPath(), "Telegrafer.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.directory);
            this.store = new ConnectionStore(Path.Combine(this.directory, ConnectionStore.DefaultFileName));
        }

        public void Dispose()
        {
            Directory.Delete(this.directory, recursive: true);
        }

        [Fact]
        public void Load_ReturnsEmptyDataWhenNoFileExists()
        {
            var result = this.store.Load();

            Assert.False(result.FileExisted);
            Assert.True(result.CanSave);
            Assert.Null(result.Warning);
            Assert.Empty(result.Data.Groups);
            Assert.Empty(result.Data.Connections);
        }

        [Fact]
        public void WriteAndLoad_KeepAllValues()
        {
            var group = new ConnectionGroup { Name = "TS System", IsExpanded = false };
            var connection = new ConnectionProfile
            {
                Name = "Steuerung 1",
                Host = "192.168.1.10",
                Port = 5000,
                Description = "Halle 3",
                GroupId = group.Id,
                AddStxEtx = false,
                LineEnding = LineEnding.CrLf,
                Encoding = TelegramEncoding.Latin1,
                LastUsedUtc = new DateTime(2026, 10, 6, 12, 30, 0, DateTimeKind.Utc),
            };
            var data = new ConnectionLibraryData
            {
                Settings = new AppSettings { DefaultAddStxEtx = false, MaxLogEntries = 2_000, SortMode = ConnectionSortMode.Address },
                Groups = { group },
                Connections = { connection },
            };

            this.store.Write(this.store.Serialize(data));
            var loaded = this.store.Load().Data;

            var loadedGroup = Assert.Single(loaded.Groups);
            Assert.Equal(group.Id, loadedGroup.Id);
            Assert.Equal("TS System", loadedGroup.Name);
            Assert.False(loadedGroup.IsExpanded);

            var loadedConnection = Assert.Single(loaded.Connections);
            Assert.Equal(connection.Id, loadedConnection.Id);
            Assert.Equal("Steuerung 1", loadedConnection.Name);
            Assert.Equal("192.168.1.10", loadedConnection.Host);
            Assert.Equal(5000, loadedConnection.Port);
            Assert.Equal("Halle 3", loadedConnection.Description);
            Assert.Equal(group.Id, loadedConnection.GroupId);
            Assert.False(loadedConnection.AddStxEtx);
            Assert.Equal(LineEnding.CrLf, loadedConnection.LineEnding);
            Assert.Equal(TelegramEncoding.Latin1, loadedConnection.Encoding);
            Assert.Equal(connection.LastUsedUtc, loadedConnection.LastUsedUtc);

            Assert.NotNull(loaded.Settings);
            Assert.False(loaded.Settings!.DefaultAddStxEtx);
            Assert.Equal(2_000, loaded.Settings.MaxLogEntries);
            Assert.Equal(ConnectionSortMode.Address, loaded.Settings.SortMode);
        }

        [Fact]
        public void Serialize_WritesReadableJsonWithoutTypeNames()
        {
            var json = this.store.Serialize(new ConnectionLibraryData
            {
                Connections = { new ConnectionProfile { Name = "Sorter", Host = "10.0.0.5", Port = 4000, LineEnding = LineEnding.Lf } },
            });

            Assert.DoesNotContain("$type", json);
            Assert.Contains("\"LineEnding\": \"Lf\"", json);
            Assert.Contains("\"Host\": \"10.0.0.5\"", json);
        }

        [Fact]
        public void Write_ReplacesTheFileAndKeepsTheLastVersionAsBackup()
        {
            this.store.Write(this.store.Serialize(new ConnectionLibraryData { Groups = { new ConnectionGroup { Name = "A" } } }));
            this.store.Write(this.store.Serialize(new ConnectionLibraryData { Groups = { new ConnectionGroup { Name = "B" } } }));

            Assert.Equal("B", Assert.Single(this.store.Load().Data.Groups).Name);
            Assert.Contains("\"A\"", File.ReadAllText(this.store.FilePath + ".bak"));
            Assert.False(File.Exists(this.store.FilePath + ".tmp"));
        }

        [Fact]
        public void Load_MovesACorruptFileAsideInsteadOfOverwritingIt()
        {
            File.WriteAllText(this.store.FilePath, "{ \"Connections\": [ { \"Name\": ");

            var result = this.store.Load();

            Assert.True(result.CanSave);
            Assert.NotNull(result.Warning);
            Assert.False(File.Exists(this.store.FilePath));
            Assert.Single(Directory.GetFiles(this.directory, ConnectionStore.DefaultFileName + ".defekt-*"));
        }

        [Fact]
        public void Load_RepairsDuplicateIdsAndUnknownGroups()
        {
            var id = Guid.NewGuid();
            File.WriteAllText(
                this.store.FilePath,
                "{ \"Connections\": [ "
                + $"{{ \"Id\": \"{id}\", \"Name\": \"A\", \"Host\": \"h\", \"Port\": 1, \"GroupId\": \"{Guid.NewGuid()}\" }}, "
                + $"{{ \"Id\": \"{id}\", \"Name\": \"B\", \"Host\": \"h\", \"Port\": 2 }} ] }}");

            var connections = this.store.Load().Data.Connections;

            Assert.Equal(2, connections.Count);
            Assert.NotEqual(connections[0].Id, connections[1].Id);
            Assert.Null(connections[0].GroupId);
        }

        [Fact]
        public void ExportAndImport_TransferGroupsAndConnectionsWithoutPersonalData()
        {
            var group = new ConnectionGroup { Name = "Storage System" };
            var connection = new ConnectionProfile { Name = "Lift", Host = "10.1.1.1", Port = 2000, GroupId = group.Id, LastUsedUtc = DateTime.UtcNow };
            var path = Path.Combine(this.directory, "export.json");

            ConnectionStore.Export(path, new[] { group }, new[] { connection });
            var imported = ConnectionStore.Import(path);

            Assert.Null(imported.Settings);
            Assert.Equal("Storage System", Assert.Single(imported.Groups).Name);
            var importedConnection = Assert.Single(imported.Connections);
            Assert.Equal("Lift", importedConnection.Name);
            Assert.Equal(group.Id, importedConnection.GroupId);
            Assert.Null(importedConnection.LastUsedUtc);
            Assert.NotNull(connection.LastUsedUtc);
        }

        [Fact]
        public void Import_RejectsFilesWithoutConnectionData()
        {
            var path = Path.Combine(this.directory, "invalid.json");
            File.WriteAllText(path, "not json");

            Assert.Throws<InvalidDataException>(() => ConnectionStore.Import(path));
        }

        [Fact]
        public void AddLegacyConnections_TakesOverTheAddressesOfEarlierVersions()
        {
            var data = new ConnectionLibraryData();
            var settings = new AppSettings { DefaultAddStxEtx = true, DefaultLineEnding = LineEnding.Cr };

            var invalid = ConnectionStore.AddLegacyConnections(
                data,
                new[] { "192.168.1.10:5000", "plc-01:4000", "192.168.1.10:5000", "no port" },
                settings);

            Assert.Equal(new[] { "no port" }, invalid);
            Assert.Equal(2, data.Connections.Count);
            Assert.Equal(string.Empty, data.Connections[0].Name);
            Assert.Equal("192.168.1.10:5000", data.Connections[0].DisplayName);
            Assert.Equal("192.168.1.10", data.Connections[0].Host);
            Assert.Equal(5000, data.Connections[0].Port);
            Assert.True(data.Connections[0].AddStxEtx);
            Assert.Equal(LineEnding.Cr, data.Connections[0].LineEnding);
            Assert.Equal("plc-01", data.Connections[1].Host);
        }
    }
}
