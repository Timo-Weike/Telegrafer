namespace Telegrafer.Utils
{
    public static class FileNames
    {
        /// <summary>
        /// Replaces characters that are not allowed in file names, e.g. the colon of "host:port".
        /// </summary>
        public static string Sanitize(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Verbindung";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var characters = name.Trim().Select(c => invalid.Contains(c) || c == ':' ? '_' : c).ToArray();

            return new string(characters);
        }
    }
}
