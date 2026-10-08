using System.Text.Json;
using BaldursGateStyleOblivion.Classification;
using ActorResearch;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Reject(Action action)
{
    try { action(); } catch (InvalidDataException) { return; }
    throw new Exception("Expected invalid research to fail.");
}
var source = "https://en.uesp.net/wiki/Oblivion:Mannimarco";
var proposal = new ActorOverride { Name = "Example", PowerTier = 6, Handling = ActorHandling.MajorBoss,
    Description = "An important antagonist.", Reason = "A sourced combat assessment.", Sources = [source] };
Research.Validate(proposal, [source]);
Research.Validate(proposal, ["https://en.m.uesp.net/wiki/Oblivion%3AMannimarco#Notes"]);
Reject(() => Research.Validate(proposal, []));
proposal.Sources = ["https://uesp.net.evil.example/actor"];
Reject(() => Research.Validate(proposal, [proposal.Sources[0]]));
proposal.Sources = [$"([UESP]({source}))"];
Research.Validate(proposal, [source]);
Check(proposal.Sources[0] == source, "Markdown citations must normalize to the consulted URL");
proposal.Sources = ["[UESP](https://en.uesp.net/wiki/Oblivion:Unconsulted)"];
Reject(() => Research.Validate(proposal, [source]));
proposal.Sources = [source];
proposal.Description = string.Join(' ', Enumerable.Repeat("word", 31));
Reject(() => Research.Validate(proposal, [source]));
proposal.Description = "An important antagonist.";
var directory = Path.Combine(Path.GetTempPath(), "actor-research-" + Guid.NewGuid());
Directory.CreateDirectory(Path.Combine(directory, "proposals"));
try
{
    const string key = "016487:Oblivion.esm";
    var config = Path.Combine(directory, "actor-classification.json");
    File.WriteAllText(config, """{"Rules":[{"Id":"fallback","Priority":"Fallback","Evidence":"Always","Values":{"PowerTier":1}}]}""");
    Check(ConfigurationEditor.AddActor(config, key, proposal), "Research must write directly into the configuration");
    ConfigurationEditor.Actor(config, key, "Example", 7, "MajorBoss");
    var updated = ActorConfiguration.Load(config);
    Check(updated.Rules.Count == 1 && updated.FormKeyOverrides[key].PowerTier == 7, "Editing must preserve rules and update the tier");
    Check(updated.FormKeyOverrides[key].Description == proposal.Description, "Editing must preserve LLM notes");
    Check(!ConfigurationEditor.AddActor(config, key, proposal), "Research must not overwrite an existing edit");
    Check(ActorConfiguration.Load(config).FormKeyOverrides[key].PowerTier == 7, "Existing edits must survive research");
    var expected = ActorConfiguration.Load(config).FormKeyOverrides[key];
    Check(ConfigurationEditor.ReviewActor(config, key, proposal, expected), "Fresh evaluation may update an existing tier-only override");
    Check(ActorConfiguration.Load(config).FormKeyOverrides[key].Handling == ActorHandling.MajorBoss, "Review must preserve handling");
    expected = ActorConfiguration.Load(config).FormKeyOverrides[key];
    ConfigurationEditor.Actor(config, key, "Example", 7, "MajorBoss", 24, false);
    var protectedText = File.ReadAllText(config);
    Check(!ConfigurationEditor.ReviewActor(config, key, proposal, expected) && File.ReadAllText(config) == protectedText, "New level overrides must prevent all research writes");
    expected = ActorConfiguration.Load(config).FormKeyOverrides[key];
    Check(!ConfigurationEditor.ReviewActor(config, key, proposal, expected) && File.ReadAllText(config) == protectedText, "Preexisting level overrides must remain byte-for-byte intact");
    ConfigurationEditor.Actor(config, key, "Example", 7, "MajorBoss");
    expected = ActorConfiguration.Load(config).FormKeyOverrides[key];
    ConfigurationEditor.Actor(config, key, "Example", 5, "Named");
    Check(!ConfigurationEditor.ReviewActor(config, key, proposal, expected), "Concurrent manual changes must survive review");
    ConfigurationEditor.Actor(config, key, "Example", 7, "MajorBoss");
    var original = File.ReadAllText(config);
    try { ConfigurationEditor.Actor(config, key, "Example", 11, null); throw new Exception("Invalid tier accepted"); }
    catch (ArgumentOutOfRangeException) { }
    Check(File.ReadAllText(config) == original, "Invalid edits must leave the file intact");
    ConfigurationEditor.Actor(config, key, "Example", null, null);
    Check(ActorConfiguration.Classify(ActorConfiguration.Load(config), key, "Oblivion.esm", new()).Tier?.Value == 1, "Clearing an override must restore group rules");
    const string other = "000123:Oblivion.esm";
    Check(ConfigurationEditor.AddActor(config, other, new() { Name = "Other", PowerTier = 3, Reason = "Keep this actor" }), "Second actor must be added");
    Check(ConfigurationEditor.DeleteActor(config, key.ToLowerInvariant()), "Deletion must match FormKeys case-insensitively");
    updated = ActorConfiguration.Load(config);
    Check(!updated.FormKeyOverrides.ContainsKey(key) && updated.FormKeyOverrides.ContainsKey(other), "Deletion must remove the entire entry and preserve other actors");
    Check(ActorConfiguration.Classify(updated, key, "Oblivion.esm", new()).Tier?.Value == 1, "Deleted actor must return to automatic rules");
    original = File.ReadAllText(config);
    Check(!ConfigurationEditor.DeleteActor(config, key) && File.ReadAllText(config) == original, "Repeated deletion must be a no-op");
    File.WriteAllText(config, """{"Groups":{"bandits":[{"Id":"Bandit","Priority":"Fallback","Evidence":"Always","Values":{"PowerTier":2}}]}}""");
    ConfigurationEditor.Group(config, "bandits", "Bandit", 4, true);
    Check(ActorConfiguration.Classify(ActorConfiguration.Load(config), key, "Oblivion.esm", new()).Tier?.Value == 4, "Group editor must update effective tiers");
    Console.WriteLine("Direct configuration saves, notes preservation, edit protection, validation, and group editing passed.");
}
finally { Directory.Delete(directory, true); }

using (var wikiDocument = JsonDocument.Parse("""
    {"query":{"pages":{"12404":{"title":"Oblivion:Mannimarco","fullurl":"https://en.uesp.net/wiki/Oblivion:Mannimarco",
    "extract":"Actor-specific evidence.","revisions":[{"revid":123,"timestamp":"2026-10-04T00:00:00Z"}]}}}}
    """))
{
    var wiki = Uesp.Parse(wikiDocument.RootElement);
    Check(wiki?.RevisionId == 123 && wiki.Text == "Actor-specific evidence.", "Wiki source and revision must be preserved");
}
using (var missing = JsonDocument.Parse("""{"query":{"pages":{"-1":{"title":"Missing","missing":""}}}}"""))
    Check(Uesp.Parse(missing.RootElement) is null, "Missing pages must not become evidence");
Console.WriteLine("Direct UESP article parsing and missing-page handling passed.");

using (var disambiguation = JsonDocument.Parse("""
    {"query":{"pages":{"1":{"title":"Oblivion:Umaril the Unfeathered","pageprops":{"disambiguation":""},
    "links":[{"title":"Oblivion:Umaril the Unfeathered (quest)"},{"title":"Oblivion:Umaril the Unfeathered (person)"}]}}}}
    """))
{
    Check(Uesp.Parse(disambiguation.RootElement) is null, "Disambiguation must not become actor evidence");
    Check(Uesp.PersonArticle(disambiguation.RootElement) == "Oblivion:Umaril the Unfeathered (person)", "Prefer person over quest article");
}
Console.WriteLine("UESP disambiguation resolution passed.");

using (var apiError = JsonDocument.Parse("""{"error":{"code":"maxlag"}}"""))
    Check(Uesp.Parse(apiError.RootElement) is null && Uesp.PersonArticle(apiError.RootElement) is null, "Wiki API errors must not abort a research batch.");

Check(Uesp.FamilyTitle("Dremora Valkynaz") == "Oblivion:Dremora", "Generic ranks must have a family source fallback.");
Check(Uesp.FamilyTitle("Bloodcrust Vampire") is null && Uesp.FamilyTitle("Mankar Camoran") is null, "Special actors must retain actor-specific research.");
