using System.Net.Http;
using System.Text.Json;

namespace GearScout;

public static class XivGearImporter
{
    private static readonly string[] Slots =
        ["Weapon", "OffHand", "Head", "Body", "Hand", "Legs", "Feet", "Ears", "Neck", "Wrist", "RingLeft", "RingRight"];
    private static readonly HashSet<string> Jobs = new(StringComparer.Ordinal)
    {
        "GLA", "PGL", "MRD", "LNC", "ARC", "CNJ", "THM", "ACN", "ROG",
        "PLD", "MNK", "WAR", "DRG", "BRD", "WHM", "BLM", "SMN", "SCH", "NIN",
        "MCH", "DRK", "AST", "SAM", "RDM", "BLU", "GNB", "DNC", "RPR", "SGE", "VPR", "PCT",
        "CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL", "MIN", "BTN", "FSH",
    };

    public static Uri ValidateUrl(string input)
    {
        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
            !uri.IsDefaultPort || uri.Host is not ("xivgear.app" or "www.xivgear.app" or "share.xivgear.app"))
            throw new ArgumentException("Paste a full HTTPS link from xivgear.app.");
        return uri;
    }

    public static async Task<List<TrackedSet>> ImportAsync(HttpClient http, string input, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        token = deadline.Token;
        var source = ValidateUrl(input).AbsoluteUri;
        // Let XIVGear interpret both modern and legacy links, including selected-set parameters.
        var endpoint = "https://api.xivgear.app/basedata?url=" + Uri.EscapeDataString(source);
        using var response = await http.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 4 * 1024 * 1024)
            throw new InvalidDataException("This sheet is too large (maximum 4 MB).");
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(bytes, token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > 4 * 1024 * 1024)
                throw new InvalidDataException("This sheet is too large (maximum 4 MB).");
            buffer.Write(bytes, 0, read);
        }
        return Parse(System.Text.Encoding.UTF8.GetString(buffer.ToArray()), source);
    }

    public static List<TrackedSet> Parse(string json, string source)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("XIVGear did not return a gear set.");
        var sheetJob = Text(root, "job");
        var nodes = root.TryGetProperty("sets", out var sets) && sets.ValueKind == JsonValueKind.Array
            ? sets.EnumerateArray().ToArray() : [root];
        var result = new List<TrackedSet>();
        foreach (var node in nodes)
        {
            if (node.ValueKind != JsonValueKind.Object ||
                (node.TryGetProperty("isSeparator", out var sep) && sep.ValueKind == JsonValueKind.True))
                continue;
            var job = (Text(node, "job") ?? sheetJob ?? "").Trim().ToUpperInvariant();
            if (!Jobs.Contains(job))
                throw new InvalidDataException("A set has no supported job. Export it from a job-specific XIVGear sheet.");
            if (!node.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Object)
                continue;
            var set = new TrackedSet { Name = Text(node, "name") ?? "Untitled set", Job = job, SourceUrl = source };
            foreach (var slot in Slots)
            {
                if (!items.TryGetProperty(slot, out var item) || item.ValueKind == JsonValueKind.Null)
                    continue;
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var id) ||
                    !id.TryGetUInt32(out var itemId) || itemId == 0)
                    throw new InvalidDataException($"Invalid or custom item in {slot}. Only game item IDs are supported.");
                set.Pieces.Add(new GearPiece { Slot = slot, ItemId = itemId });
            }
            if (set.Pieces.Count > 0) result.Add(set);
        }
        if (result.Count == 0) throw new InvalidDataException("No gear pieces were found in this link.");
        return result;
    }

    private static string? Text(JsonElement node, string key) =>
        node.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
