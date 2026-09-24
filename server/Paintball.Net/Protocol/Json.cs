using System;
using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Paintball.Net.Protocol
{
    /// <summary>
    /// Defensive Lesezugriffe auf eingehende Client-Nachrichten (NFR-10): Typprüfung,
    /// Längenlimits, nur endliche Zahlen, Wertebereiche geklemmt.
    /// </summary>
    public readonly struct Msg
    {
        private readonly JsonElement _root;

        public Msg(JsonElement root) { _root = root; }

        public string Type => Str("t", 16);

        public bool Has(string name) => _root.TryGetProperty(name, out JsonElement v) && v.ValueKind != JsonValueKind.Null;

        public string Str(string name, int maxLength, string fallback = null)
        {
            if (!_root.TryGetProperty(name, out JsonElement v) || v.ValueKind != JsonValueKind.String) return fallback;
            string s = v.GetString();
            return s == null || s.Length > maxLength ? fallback : s;
        }

        public bool TryNum(string name, out double value)
        {
            value = 0;
            if (!_root.TryGetProperty(name, out JsonElement v) || v.ValueKind != JsonValueKind.Number) return false;
            if (!v.TryGetDouble(out value) || !double.IsFinite(value)) return false;
            return true;
        }

        public double Num(string name, double fallback, double min, double max)
            => TryNum(name, out double v) ? Math.Clamp(v, min, max) : fallback;

        public int Int(string name, int fallback, int min, int max)
            => TryNum(name, out double v) ? (int)Math.Clamp(Math.Round(v), min, max) : fallback;

        public bool Bool(string name, bool fallback)
        {
            if (!_root.TryGetProperty(name, out JsonElement v)) return fallback;
            return v.ValueKind == JsonValueKind.True || (v.ValueKind != JsonValueKind.False && fallback);
        }

        public static bool TryParse(string text, out Msg msg, out string error)
        {
            msg = default;
            error = null;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 4 });
                if (doc.RootElement.ValueKind != JsonValueKind.Object) { error = "bad_message"; return false; }
                msg = new Msg(doc.RootElement.Clone());
                if (msg.Type == null) { error = "bad_message"; return false; }
                return true;
            }
            catch (JsonException)
            {
                error = "bad_json";
                return false;
            }
        }
    }

    /// <summary>Kompakter JSON-Writer für Server-Nachrichten.</summary>
    public static class Json
    {
        private static readonly JsonWriterOptions Options = new() { Indented = false, SkipValidation = true };

        public static string Write(Action<Utf8JsonWriter> body)
        {
            var buffer = new ArrayBufferWriter<byte>(256);
            using (var w = new Utf8JsonWriter(buffer, Options))
            {
                w.WriteStartObject();
                body(w);
                w.WriteEndObject();
            }
            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        public static void Num(this Utf8JsonWriter w, string name, double value, int decimals = 2)
            => w.WriteNumber(name, Math.Round(value, decimals));

        public static void R(this Utf8JsonWriter w, double value, int decimals = 2)
            => w.WriteNumberValue(Math.Round(value, decimals));

        public static void Vec(this Utf8JsonWriter w, string name, System.Numerics.Vector3 v, int decimals = 2)
        {
            w.WriteStartArray(name);
            w.R(v.X, decimals);
            w.R(v.Y, decimals);
            w.R(v.Z, decimals);
            w.WriteEndArray();
        }

        public static string Error(string code, string message = null)
            => Write(w =>
            {
                w.WriteString("t", "error");
                w.WriteString("code", code);
                if (message != null) w.WriteString("msg", message);
            });
    }
}
