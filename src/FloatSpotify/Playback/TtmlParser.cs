using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace FloatSpotify.Playback;

internal static class TtmlParser
{
    // Deliberately support the provider's absolute clock-time profile. Unsupported
    // TTML profiles fall through to another provider, not fabricated word timings.
    public static IReadOnlyList<TimedLyric> Parse(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = 2_000_000
        });
        var doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        // The upstream absolute-time profile puts begin/end on section divs too.
        // Those are grouping bounds, not an instruction to add offsets to every word.
        if (doc.Descendants().Any(e => (string?)e.Attribute("timeContainer") == "seq"))
            throw new FormatException("Sequential TTML is not supported.");
        var result = new List<TimedLyric>();
        foreach (var p in doc.Descendants().Where(e => e.Name.LocalName == "p"))
        {
            var start = Time(p.Attribute("begin")?.Value);
            var end = Time(p.Attribute("end")?.Value);
            if (end <= start) throw new FormatException("Invalid TTML line interval.");
            var words = new List<TimedWord>();
            var text = new System.Text.StringBuilder();
            var complete = true;
            void Visit(XElement element)
            {
                foreach (var node in element.Nodes())
                {
                    if (node is XText literal)
                    {
                        text.Append(literal.Value);
                        if (words.Count > 0 && string.IsNullOrWhiteSpace(literal.Value))
                            words[^1] = words[^1] with { Text = words[^1].Text + literal.Value };
                        else if (literal.Value.Length > 0)
                            complete = false;
                    }
                    else if (node is XElement span)
                    {
                        // Translation/background vocals require a multi-lane renderer;
                        // do not splice them into the foreground word timeline.
                        if (span.Attributes().Any(a => a.Name.LocalName == "role" &&
                            (a.Value == "x-bg" || a.Value == "x-translation" || a.Value == "x-roman")))
                            continue;
                        if (span.Name.LocalName == "br")
                        {
                            text.Append('\n');
                            if (words.Count > 0) words[^1] = words[^1] with { Text = words[^1].Text + "\n" };
                            continue;
                        }
                        if (span.Attribute("begin") is not null && !span.Descendants().Any(e => e.Attribute("begin") is not null))
                        {
                            var a = Time(span.Attribute("begin")?.Value);
                            var b = Time(span.Attribute("end")?.Value);
                            text.Append(span.Value);
                            if (a < start || b > end || b <= a) complete = false;
                            else words.Add(new TimedWord(a, b, span.Value));
                        }
                        else Visit(span);
                    }
                }
            }
            Visit(p);
            if (string.IsNullOrWhiteSpace(text.ToString())) continue;
            if (!complete || string.Concat(words.Select(w => w.Text)) != text.ToString() ||
                words.Zip(words.Skip(1)).Any(pair => pair.First.End > pair.Second.Start))
                words.Clear();
            result.Add(new TimedLyric(start, text.ToString()) { End = end, Words = words });
        }
        return result.OrderBy(line => line.At).ToArray();
    }

    private static TimeSpan Time(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new FormatException("Missing TTML timestamp.");
        var scale = value.EndsWith("ms", StringComparison.Ordinal) ? 0.001 : 1d;
        if (value.EndsWith("ms", StringComparison.Ordinal)) value = value[..^2];
        else if (value.EndsWith('s')) value = value[..^1];
        var parts = value.Split(':');
        if (parts.Length > 3) throw new FormatException("Unsupported TTML timestamp.");
        double seconds = 0;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ||
                !double.IsFinite(number) || number < 0)
                throw new FormatException("Invalid TTML timestamp.");
            seconds = seconds * 60 + number;
        }
        if (seconds * scale > 86400) throw new FormatException("TTML timestamp out of range.");
        return TimeSpan.FromSeconds(seconds * scale);
    }
}
