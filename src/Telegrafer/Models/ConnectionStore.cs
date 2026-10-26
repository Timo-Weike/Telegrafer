using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Telegrafer.Utils;

namespace Telegrafer.Models
{
    public sealed class ConnectionLoadResult
    {
        public ConnectionLoadResult(ConnectionLibraryData data, bool fileExisted, bool canSave, string? warning)
        {
            this.Data = data;
            this.FileExisted = fileExisted;
            this.CanSave = canSave;
            this.Warning = warning;
        }

        public ConnectionLibraryData Data { get; }

        public bool FileExisted { get; }

        /// <summary>
        /// Gets a value indicating whether the data may be saved. It is <see langword="false"/> if an existing
        /// file could not be read, so that it is not overwritten with empty data.
        /// </summary>
        public bool CanSave { get; }

        public string? Warning { get; }
    }

    /// <summary>
    /// Reads and writes the saved connections, groups and settings as JSON file.
    /// </summary>
    /// <remarks>
    /// The file contains no .NET type names, so that it stays readable after classes have been renamed. It is
    /// written to a temporary file first and then replaces the previous file, which is kept as ".bak" copy.
    /// </remarks>
    public sealed class ConnectionStore
    {
        public const string DefaultFileName = "connections.json";

        private static readonly JsonSerializerSettings SerializerSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            TypeNameHandling = TypeNameHandling.None,
            Converters = { new StringEnumConverter() },
        };

        public ConnectionStore(string filePath)
        {
            this.FilePath = filePath;
        }

        public string FilePath { get; }

        public ConnectionLoadResult Load()
        {
            if (!File.Exists(this.FilePath))
            {
                return new ConnectionLoadResult(Normalize(new ConnectionLibraryData()), fileExisted: false, canSave: true, warning: null);
            }

            string json;

            try
            {
                json = File.ReadAllText(this.FilePath, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new ConnectionLoadResult(
                    Normalize(new ConnectionLibraryData()),
                    fileExisted: true,
                    canSave: false,
                    warning: $"Die gespeicherten Verbindungen konnten nicht gelesen werden: {ex.Message} "
                        + "Änderungen werden in dieser Sitzung nicht gespeichert, damit die Datei erhalten bleibt.");
            }

            try
            {
                return new ConnectionLoadResult(Normalize(Deserialize(json)), fileExisted: true, canSave: true, warning: null);
            }
            catch (InvalidDataException ex)
            {
                var backupPath = this.FilePath + ".defekt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

                try
                {
                    File.Move(this.FilePath, backupPath);
                }
                catch (Exception moveException) when (moveException is IOException || moveException is UnauthorizedAccessException)
                {
                    return new ConnectionLoadResult(
                        Normalize(new ConnectionLibraryData()),
                        fileExisted: true,
                        canSave: false,
                        warning: $"Die Datei mit den gespeicherten Verbindungen ist beschädigt ({ex.Message}) und konnte nicht "
                            + $"gesichert werden ({moveException.Message}). Änderungen werden in dieser Sitzung nicht gespeichert.");
                }

                return new ConnectionLoadResult(
                    Normalize(new ConnectionLibraryData()),
                    fileExisted: true,
                    canSave: true,
                    warning: $"Die Datei mit den gespeicherten Verbindungen ist beschädigt ({ex.Message}). "
                        + $"Sie wurde unverändert als „{Path.GetFileName(backupPath)}“ gesichert.");
            }
        }

        public string Serialize(ConnectionLibraryData data)
        {
            return JsonConvert.SerializeObject(data, SerializerSettings);
        }

        /// <summary>
        /// Writes the serialized data atomically.
        /// </summary>
        public void Write(string json)
        {
            var directory = Path.GetDirectoryName(this.FilePath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = this.FilePath + ".tmp";

            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }
            }

            if (File.Exists(this.FilePath))
            {
                File.Replace(temporaryPath, this.FilePath, this.FilePath + ".bak", ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, this.FilePath);
            }
        }

        /// <summary>
        /// Writes groups and connections into a file that can be imported on another computer.
        /// </summary>
        public static void Export(string path, IEnumerable<ConnectionGroup> groups, IEnumerable<ConnectionProfile> connections)
        {
            var data = new ConnectionLibraryData
            {
                Groups = groups.Select(g => g.Clone()).ToList(),
                Connections = connections
                    .Select(c =>
                    {
                        var clone = c.Clone();
                        clone.LastUsedUtc = null;
                        return clone;
                    })
                    .ToList(),
            };

            File.WriteAllText(path, JsonConvert.SerializeObject(data, SerializerSettings), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        /// <exception cref="InvalidDataException">The file does not contain connection data.</exception>
        public static ConnectionLibraryData Import(string path)
        {
            var data = Normalize(Deserialize(File.ReadAllText(path, Encoding.UTF8)));
            data.Settings = null;
            return data;
        }

        /// <summary>
        /// Adds the addresses that were saved by earlier versions in "appstate.json" as connections.
        /// </summary>
        /// <returns>The addresses that could not be read.</returns>
        public static IReadOnlyList<string> AddLegacyConnections(ConnectionLibraryData data, IEnumerable<string> addresses, AppSettings settings)
        {
            var invalid = new List<string>();

            foreach (var address in addresses)
            {
                if (!HostAndPort.TryParse(address, out var host, out var port))
                {
                    invalid.Add(address);
                    continue;
                }

                if (data.Connections.Any(c => c.Port == port && string.Equals(c.Host, host, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                // Without a name the address is shown; the user can name the connection later.
                var connection = settings.CreateConnection();
                connection.Host = host;
                connection.Port = port;
                data.Connections.Add(connection);
            }

            return invalid;
        }

        private static ConnectionLibraryData Deserialize(string json)
        {
            ConnectionLibraryData? data;

            try
            {
                data = JsonConvert.DeserializeObject<ConnectionLibraryData>(json, SerializerSettings);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(ex.Message, ex);
            }

            if (data is null)
            {
                throw new InvalidDataException("Die Datei enthält keine Verbindungsdaten.");
            }

            return data;
        }

        private static ConnectionLibraryData Normalize(ConnectionLibraryData data)
        {
            data.Groups ??= new List<ConnectionGroup>();
            data.Connections ??= new List<ConnectionProfile>();
            data.Groups.RemoveAll(g => g is null);
            data.Connections.RemoveAll(c => c is null);

            var groupIds = new HashSet<Guid>();

            foreach (var group in data.Groups)
            {
                if (group.Id == Guid.Empty || !groupIds.Add(group.Id))
                {
                    group.Id = Guid.NewGuid();
                    groupIds.Add(group.Id);
                }
            }

            var connectionIds = new HashSet<Guid>();

            foreach (var connection in data.Connections)
            {
                if (connection.Id == Guid.Empty || !connectionIds.Add(connection.Id))
                {
                    connection.Id = Guid.NewGuid();
                    connectionIds.Add(connection.Id);
                }

                if (connection.GroupId is Guid groupId && !groupIds.Contains(groupId))
                {
                    connection.GroupId = null;
                }
            }

            return data;
        }
    }
}
