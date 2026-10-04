using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BaldursGateStyleOblivion.Classification;

namespace ActorResearch;

internal sealed record WikiSource(string Title, string Url, long RevisionId, string RevisionTimestamp, string Text);

internal static class Uesp
{
    public static async Task<WikiSource?> Read(Actor actor, string work, bool refresh, string? pageTitle = null)
    {
        if (string.IsNullOrWhiteSpace(actor.Name)) return null;
        var alias = actor.Name switch
        {
            "Umaril" => "Umaril the Unfeathered (person)",
            "A Stranger" or "Corvus Umbranox" or "The Gray Fox" => "Gray Fox",
            "Emperor Uriel Septim" => "Uriel Septim VII",
            "GateKeeper" => "Gatekeeper",
            "The Sunken One" => "Sunken One",
            "Bat gro-Orkul's Ghost" => "Bat gro-Orkul",
            _ => actor.Name
        };
        if (pageTitle is null && actor.RecordType == "Creature" && actor.SourcePlugin == "DLCBattlehornCastle.esp"
            && actor.EditorID == "DLCBattlehornLich") pageTitle = "Oblivion:Battlehorn Castle Creatures";
        var title = pageTitle ?? "Oblivion:" + actor.Name;
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(title)));
        var path = Path.Combine(work, "cache", name + ".wiki.json");
        if (!refresh && File.Exists(path))
        {
            var cached = JsonSerializer.Deserialize<WikiSource>(File.ReadAllText(path), ActorConfiguration.JsonOptions);
            if (cached is not null && !cached.Text.Contains("may refer to", StringComparison.OrdinalIgnoreCase)) return cached;
        }
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OblivionActorResearch/0.1 (personal mod development; low-volume wiki research)");
        string Url(string page) => "https://en.uesp.net/w/api.php?action=query&format=json&redirects=1&explaintext=1&exlimit=1&prop=extracts%7Cinfo%7Crevisions%7Cpageprops%7Clinks&pllimit=50&inprop=url&rvprop=ids%7Ctimestamp&titles=" + Uri.EscapeDataString(page);
        try
        {
            foreach (var candidate in pageTitle is null ? new[] { "Oblivion:" + actor.Name, "Oblivion:" + alias, "Shivering:" + actor.Name, "Shivering:" + alias, FamilyTitle(actor.Name) }.OfType<string>().Distinct() : new[] { pageTitle })
            {
                using var response = await client.GetAsync(Url(candidate));
                if (!response.IsSuccessStatusCode) continue;
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var person = PersonArticle(document.RootElement);
                WikiSource? source;
                if (person is not null)
                {
                    using var personResponse = await client.GetAsync(Url(person));
                    if (!personResponse.IsSuccessStatusCode) continue;
                    using var personDocument = JsonDocument.Parse(await personResponse.Content.ReadAsStringAsync());
                    source = Parse(personDocument.RootElement);
                }
                else source = Parse(document.RootElement);
                if (source is null) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(source, ActorConfiguration.JsonOptions));
                Console.WriteLine($"UESP article: {source.Title}, revision {source.RevisionId}");
                return source;
            }
            Console.WriteLine($"UESP article not found: {title}; trying web search instead.");
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            Console.WriteLine("UESP API request unavailable; trying web search instead.");
            return null;
        }
    }

    internal static string? FamilyTitle(string name)
    {
        if (name.Contains("Dremora", StringComparison.OrdinalIgnoreCase)) return "Oblivion:Dremora";
        if (name.Contains("Goblin", StringComparison.OrdinalIgnoreCase)) return "Oblivion:Goblins";
        if (name.StartsWith("Vampire ", StringComparison.OrdinalIgnoreCase)) return "Oblivion:Vampire";
        if (name.Contains("Dark Seducer", StringComparison.OrdinalIgnoreCase)) return "Shivering:Dark Seducer";
        if (name.Contains("Golden Saint", StringComparison.OrdinalIgnoreCase)) return "Shivering:Golden Saint";
        if (name.Contains("Grummite", StringComparison.OrdinalIgnoreCase)) return "Shivering:Grummite";
        if (name.Contains("Heretic", StringComparison.OrdinalIgnoreCase)) return "Shivering:Heretic";
        if (name.Contains("Zealot", StringComparison.OrdinalIgnoreCase)) return "Shivering:Zealot";
        return null;
    }

    internal static string? PersonArticle(JsonElement root)
    {
        if (!root.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages)) return null;
        foreach (var page in pages.EnumerateObject())
        {
            var value = page.Value;
            if (!value.TryGetProperty("pageprops", out var properties) || !properties.TryGetProperty("disambiguation", out _)) continue;
            if (!value.TryGetProperty("links", out var links)) return null;
            return links.EnumerateArray().Select(link => link.GetProperty("title").GetString()!)
                .FirstOrDefault(title => title.StartsWith("Oblivion:", StringComparison.Ordinal) && title.EndsWith(" (person)", StringComparison.Ordinal));
        }
        return null;
    }

    internal static WikiSource? Parse(JsonElement root)
    {
        if (!root.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages)) return null;
        foreach (var page in pages.EnumerateObject())
        {
            var value = page.Value;
            if (value.TryGetProperty("pageprops", out var properties) && properties.TryGetProperty("disambiguation", out _)) continue;
            if (value.TryGetProperty("missing", out _) || !value.TryGetProperty("extract", out var extract) || string.IsNullOrWhiteSpace(extract.GetString())) continue;
            var url = value.GetProperty("fullurl").GetString()!;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "en.uesp.net")
                throw new InvalidDataException("Unexpected UESP article URL.");
            var revision = value.GetProperty("revisions")[0];
            return new(value.GetProperty("title").GetString()!, url, revision.GetProperty("revid").GetInt64(), revision.GetProperty("timestamp").GetString()!, extract.GetString()!);
        }
        return null;
    }
}
