using System.Text.Json;
using BaldursGateStyleOblivion.Classification;

namespace ActorResearch;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help")
            {
                Console.WriteLine("ActorResearch edit|catalog|research --reports <directory> --config <actor-classification.json> [--list-config <creature-lists.json>] [--formkey <ID:Plugin>] [--formkeys <text-file>] [--model <model>] [--work <directory>] [--output <html>] [--anchors <json-file>] [--source-page <UESP-title>] [--port <port>] [--parallelism <1-8>] [--no-open] [--refresh]");
                return 0;
            }
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 1; i < args.Length; i++)
            {
                if (args[i] is "--refresh" or "--no-open") options.Add(args[i], "true");
                else if (!args[i].StartsWith("--") || i + 1 >= args.Length || args[i + 1].StartsWith("--")) throw new ArgumentException($"Missing option value: {args[i]}");
                else options.Add(args[i], args[++i]);
            }
            string Get(string key, string fallback) => options.GetValueOrDefault(key, fallback);
            var config = Path.GetFullPath(Get("--config", "BaldursGateStyleOblivion/actor-classification.json"));
            var work = Path.GetFullPath(Get("--work", "research"));
            var settings = ActorConfiguration.Load(config);
            var actors = Catalog.Read(Path.GetFullPath(Get("--reports", "artifacts/phase0-data/Reports")));
            if (args[0] == "edit")
            {
                await Editor.Run(actors, config, int.Parse(Get("--port", "5078")), !options.ContainsKey("--no-open"),
                    Path.GetFullPath(Get("--reports", "artifacts/phase0-data/Reports")),
                    Path.GetFullPath(Get("--list-config", Path.Combine(Path.GetDirectoryName(config)!, "creature-lists.json"))),
                    Path.GetFullPath(Get("--geography-config", Path.Combine(Path.GetDirectoryName(config)!, "geography.json"))));
                return 0;
            }
            if (args[0] == "research")
            {
                var keys = options.TryGetValue("--formkeys", out var list) ? File.ReadAllLines(list).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()).ToArray()
                    : options.TryGetValue("--formkey", out var key) ? [key] : throw new ArgumentException("Select actors with --formkey or --formkeys; research never starts an implicit full batch.");
                var selected = keys.Distinct(StringComparer.OrdinalIgnoreCase).Select(key => actors.FirstOrDefault(actor => string.Equals(actor.FormKey, key, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException($"Actor is absent from reports: {key}")).ToArray();
                var anchorsPath = Get("--anchors", Path.Combine(work, "anchors.json"));
                var anchors = File.Exists(anchorsPath) ? File.ReadAllText(anchorsPath) : "[]";
                using var parsedAnchors = JsonDocument.Parse(anchors);
                var parallelism = int.Parse(Get("--parallelism", "4"));
                if (parallelism is < 1 or > 8) throw new ArgumentException("Parallelism must be between 1 and 8.");
                var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
                await Parallel.ForEachAsync(selected.GroupBy(actor => actor.Name, StringComparer.OrdinalIgnoreCase),
                    new ParallelOptions { MaxDegreeOfParallelism = parallelism }, async (group, _) =>
                {
                    foreach (var actor in group)
                        try { await Research.Run(actor, config, work, Get("--model", settings.ResearchModel), options.ContainsKey("--refresh"), anchors, options.GetValueOrDefault("--source-page")); }
                        catch (Exception exception) when (exception is InvalidDataException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException or IOException or UnauthorizedAccessException)
                        { failures.Add(actor.FormKey); Console.Error.WriteLine($"Research failed for {actor.Name} ({actor.FormKey}): {exception.Message}"); }
                });
                if (!failures.IsEmpty) Console.Error.WriteLine("Unfinished research: " + string.Join(", ", failures.Order()));
            }
            else if (args[0] != "catalog") throw new ArgumentException($"Unknown command: {args[0]}");
            var output = Path.GetFullPath(Get("--output", "artifacts/actor-catalog.html"));
            Catalog.Write(actors, ActorConfiguration.Load(config), config, output);
            Console.WriteLine($"Catalog: {output} ({actors.Length} actors)");
            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
