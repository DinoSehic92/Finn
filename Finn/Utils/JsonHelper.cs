using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Finn.Utils
{
    /// <summary>
    /// Central JSON serialization helpers. Writes using System.Text.Json
    /// and reads with fallback to Newtonsoft.Json for backward compatibility
    /// with save files created before the migration.
    /// </summary>
    public static class JsonHelper
    {
        /// <summary>Shared System.Text.Json options used for all persistence.</summary>
        public static JsonSerializerOptions Options { get; } = CreateOptions();

        private static JsonSerializerOptions CreateOptions()
        {
            var opts = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                PropertyNameCaseInsensitive = true,
                // Include non-public members marked with [JsonInclude]
                IncludeFields = false,
            };

            // Avalonia Color, Point etc. need converters
            opts.Converters.Add(new ColorJsonConverter());
            opts.Converters.Add(new PointJsonConverter());

            return opts;
        }

        /// <summary>
        /// Serialize <paramref name="value"/> to a JSON string using System.Text.Json.
        /// </summary>
        public static string Serialize<T>(T value) =>
            JsonSerializer.Serialize(value, Options);

        /// <summary>
        /// Serialize <paramref name="value"/> directly to a stream (no intermediate string).
        /// </summary>
        public static async Task SerializeAsync<T>(T value, Stream stream) =>
            await JsonSerializer.SerializeAsync(stream, value, Options);

        /// <summary>
        /// Deserialize JSON with automatic fallback:
        /// 1. Try System.Text.Json first (new format).
        /// 2. If that fails, try Newtonsoft.Json (old format / backward compat).
        /// 3. If both fail, returns default.
        /// </summary>
        public static T? Deserialize<T>(string json) where T : class
        {
            // Try System.Text.Json first
            try
            {
                var result = JsonSerializer.Deserialize<T>(json, Options);
                if (result != null)
                    return result;
            }
            catch { /* fall through to Newtonsoft */ }

            // Fallback to Newtonsoft.Json for old save files
            try
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
            }
            catch { return default; }
        }

        /// <summary>
        /// Keeps the last <paramref name="maxBackups"/> numbered backup files,
        /// removing older ones. Backup names follow the pattern: file.bak, file.bak.1, file.bak.2, etc.
        /// </summary>
        public static void RotateBackups(string filePath, int maxBackups = 3)
        {
            string bakBase = filePath + ".bak";

            // Delete the oldest if it would exceed the limit
            string oldest = maxBackups > 1 ? $"{bakBase}.{maxBackups - 1}" : bakBase;
            if (File.Exists(oldest))
                try { File.Delete(oldest); } catch { }

            // Shift existing backups: .bak.2 → .bak.3, .bak.1 → .bak.2, .bak → .bak.1
            for (int i = maxBackups - 2; i >= 1; i--)
            {
                string src = $"{bakBase}.{i}";
                string dst = $"{bakBase}.{i + 1}";
                if (File.Exists(src))
                    try { File.Move(src, dst, overwrite: true); } catch { }
            }

            // Move current .bak → .bak.1
            if (maxBackups > 1 && File.Exists(bakBase))
                try { File.Move(bakBase, $"{bakBase}.1", overwrite: true); } catch { }

            // Copy current file as the new .bak
            if (File.Exists(filePath))
                try { File.Copy(filePath, bakBase, overwrite: true); } catch { }
        }
    }

    /// <summary>
    /// System.Text.Json converter for Avalonia.Media.Color.
    /// Writes as "#AARRGGBB" hex string. Reads both the hex string format
    /// and the legacy Newtonsoft object format {"A":255,"R":214,"G":64,"B":69}
    /// so old save files remain readable.
    /// </summary>
    internal class ColorJsonConverter : JsonConverter<Avalonia.Media.Color>
    {
        public override Avalonia.Media.Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var str = reader.GetString();
                return str != null ? Avalonia.Media.Color.Parse(str) : default;
            }

            // Legacy Newtonsoft object format: {"A":255,"R":214,"G":64,"B":69}
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                byte a = 255, r = 0, g = 0, b = 0;
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                        return Avalonia.Media.Color.FromArgb(a, r, g, b);

                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        var prop = reader.GetString();
                        reader.Read();
                        switch (prop)
                        {
                            case "A": a = reader.GetByte(); break;
                            case "R": r = reader.GetByte(); break;
                            case "G": g = reader.GetByte(); break;
                            case "B": b = reader.GetByte(); break;
                            default: reader.Skip(); break;
                        }
                    }
                }
            }

            return default;
        }

        public override void Write(Utf8JsonWriter writer, Avalonia.Media.Color value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }

    /// <summary>
    /// System.Text.Json converter for Avalonia.Point.
    /// Serializes as an object with X and Y properties.
    /// </summary>
    internal class PointJsonConverter : JsonConverter<Avalonia.Point>
    {
        public override Avalonia.Point Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException();

            double x = 0, y = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    return new Avalonia.Point(x, y);

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var prop = reader.GetString();
                    reader.Read();
                    if (string.Equals(prop, "X", StringComparison.OrdinalIgnoreCase))
                        x = reader.GetDouble();
                    else if (string.Equals(prop, "Y", StringComparison.OrdinalIgnoreCase))
                        y = reader.GetDouble();
                }
            }
            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter writer, Avalonia.Point value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("X", value.X);
            writer.WriteNumber("Y", value.Y);
            writer.WriteEndObject();
        }
    }
}
