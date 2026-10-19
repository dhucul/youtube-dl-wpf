using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;

internal static class FlightChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All => [
        ("Next Flight literal JSON and adjacent UTF8 byte-counted text rows preserve boundaries", () => Run(Boundaries)),
        ("Next Flight text rows cannot truncate multibyte Unicode or consume another record", () => Run(Truncated)),
        ("Next Flight literal arguments and duplicate JSON fields cannot become executable data", () => Run(Literals)),
        ("Next Flight row and byte limits reject oversized or malformed streams", () => Run(Limits))
    ];
    private static string TextRow(string id, string text) => id + ":T" + Encoding.UTF8.GetByteCount(text).ToString("x") + "," + text;
    private static void Boundaries()
    {
        var text = "{\"title\":\"日本語 🎮\"}"; var wire = ":HL[\"style.css\",\"style\"]\n" + TextRow("a", text) + "b:{\"next\":true}\nc:I[12,[]]\n";
        var values = NextFlightReader.ParseWire(Encoding.UTF8.GetBytes(wire));
        if (values.Count != 2 || values[0].GetProperty("title").GetString() != "日本語 🎮" || !values[1].GetProperty("next").GetBoolean()) throw new InvalidOperationException("Flight length was treated as UTF16 characters or a newline boundary.");
    }
    private static void Truncated()
    {
        foreach (var text in new[] { "a:T4,🎮", "a:T3,🎮", "a:Tff,{}", "a:T2,{}bad" })
        {
            if (text == "a:T4,🎮") { if (NextFlightReader.ParseWire(Encoding.UTF8.GetBytes(text)).Count != 0) throw new InvalidOperationException("Non-JSON text became a record."); }
            else Reject(() => NextFlightReader.ParseWire(Encoding.UTF8.GetBytes(text)));
        }
    }
    private static void Literals()
    {
        var wire = "a:{\"one\":1}\n"; var html = "<script>self.__next_f.push([1," + JsonSerializer.Serialize(wire) + "])</script>";
        if (NextFlightReader.Read(html).Count != 1) throw new InvalidOperationException("A literal Flight call failed.");
        Reject(() => NextFlightReader.Read(html.Replace("])</script>", "]);evil()</script>")));
        Reject(() => NextFlightReader.ParseWire(Encoding.UTF8.GetBytes("a:{\"one\":1,\"one\":2}\n")));
    }
    private static void Limits()
    {
        Reject(() => NextFlightReader.ParseWire(new byte[8 * 1024 * 1024 + 1]));
        foreach (var wire in new[] { "not-a-row:{}", "a:Tnope,{}", "a:{invalid}" }) Reject(() => NextFlightReader.ParseWire(Encoding.UTF8.GetBytes(wire)));
    }
    private static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new InvalidOperationException("Malformed Flight data was accepted."); }
    private static Task Run(Action action) { action(); return Task.CompletedTask; }
}
