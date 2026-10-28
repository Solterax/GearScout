using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace GearScout;

internal static class RetainerMenuMatching
{
    public static int FindEntry(byte[] encodedTemplate, IReadOnlyList<string> entries)
    {
        var template = SeString.Parse(encodedTemplate);
        // Match literal localized text before the first unevaluated macro (such as slots filled).
        var literal = template.Payloads.TakeWhile(p => p is TextPayload).Cast<TextPayload>();
        var prefix = string.Concat(literal.Select(p => p.Text)).Trim();
        if (prefix.Length == 0) return -1;
        var dynamic = template.Payloads.Any(p => p is not TextPayload);
        var found = -1;
        for (var index = 0; index < entries.Count; index++)
        {
            var text = entries[index].Trim();
            if (!(dynamic ? text.StartsWith(prefix, StringComparison.Ordinal) :
                    string.Equals(text, prefix, StringComparison.Ordinal))) continue;
            if (found != -1) return -1; // Never select an arbitrary action from an ambiguous menu.
            found = index;
        }
        return found;
    }
}
