using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BaldursGateStyleOblivion.Classification;

namespace ActorResearch;

internal sealed record WikiSource(string Title, string Url, long RevisionId, string RevisionTimestamp, string Text);

internal static class Uesp
{
    public static async Task<WikiSource?> Read(Actor actor, string work, bool refresh)
    {
        if (string.IsNullOrWhiteSpace(actor.Name)) return null;
        var title = "Oblivion:" + actor.Name;
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
            using var response = await client.GetAsync(Url(title));
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"UESP API: {(int)response.StatusCode}; trying web search instead.");
                return null;
            }
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var person = PersonArticle(document.RootElement);
            WikiSource? source;
            if (person is not null)
            {
                using var personResponse = await client.GetAsync(Url(person));
                if (!personResponse.IsSuccessStatusCode) return null;
                using var personDocument = JsonDocument.Parse(await personResponse.Content.ReadAsStringAsync());
                source = Parse(personDocument.RootElement);
            }
            else source = Parse(document.RootElement);
            if (source is null) { Console.WriteLine($"UESP article not found: {title}; trying web search instead."); return null; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(source, ActorConfiguration.JsonOptions));
            Console.WriteLine($"UESP article: {source.Title}, revision {source.RevisionId}");
            return source;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            Console.WriteLine("UESP API request unavailable; trying web search instead.");
            return null;
        }
    }

    internal static string? PersonArticle(JsonElement root)
    {
        foreach (var page in root.GetProperty("query").GetProperty("pages").EnumerateObject())
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
        foreach (var page in root.GetProperty("query").GetProperty("pages").EnumerateObject())
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
