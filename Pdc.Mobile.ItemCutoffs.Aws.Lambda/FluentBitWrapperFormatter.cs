using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;
using System.Text.Json;

namespace Pdc.Mobile.ItemCutoffs.Aws.Lambda
{
    /// <summary>
    /// Wraps each Serilog log event in the FluentBit envelope format expected by the
    /// PDC-PARSE-LOG-JSON ingest pipeline:
    ///   { "@timestamp": "...", "log": "<compact-json-string>" }
    /// The pipeline parses the "log" field and promotes all fields to document root.
    /// </summary>
    public class FluentBitWrapperFormatter : ITextFormatter
    {
        private static readonly CompactJsonFormatter CompactJson = new();

        public void Format(LogEvent logEvent, TextWriter output)
        {
            ///////////////////////////
            // RENDER INNER COMPACT JSON
            ///////////////////////////

            var sb = new System.Text.StringBuilder();
            using (var innerWriter = new StringWriter(sb))
            {
                CompactJson.Format(logEvent, innerWriter);
            }

            var compactJson = sb.ToString().TrimEnd('\r', '\n');

            ///////////////////////////
            // WRITE FLUENTBIT ENVELOPE
            ///////////////////////////

            var timestamp = logEvent.Timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            output.Write("{\"@timestamp\":\"");
            output.Write(timestamp);
            output.Write("\",\"log\":");
            output.Write(JsonSerializer.Serialize(compactJson));
            output.WriteLine("}");
        }
    }
}
