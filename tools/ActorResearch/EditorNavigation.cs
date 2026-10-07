namespace ActorResearch;

internal static class EditorNavigation
{
    public static string ReadTemplate(string file)
    {
        var pages = new[]
        {
            ("catalog.html", "/", "Actors"),
            ("lists.html", "/lists", "Creature lists"),
            ("geography.html", "/geography", "Geography & dungeons"),
            ("equipment.html", "/equipment", "Equipment"),
            ("creation.html", "/creation", "Character creation"),
            ("combat.html", "/combat", "Combat workbench"),
            ("enhancements.html", "/artifacts", "Artifacts / enchantments / alchemy"),
            ("magic.html", "/magic", "Magic analysis"),
            ("merchants.html", "/merchants", "Merchants"),
            ("loot.html", "/loot", "World loot"),
            ("rewards.html", "/rewards", "Rewards & artifacts")
        };
        var links = string.Join("", pages.Select(page =>
            $"<a href=\"{page.Item2}\"{(page.Item1 == file ? " aria-current=\"page\"" : "")}>{page.Item3}</a>"));
        var navigation = """
            <style>
            .editor-nav{display:flex;align-items:center;flex-wrap:wrap;gap:6px;margin:0 0 22px;padding:10px 12px;background:#20262e;border:1px solid #343e4b;border-radius:8px;font:14px system-ui}
            .editor-nav[hidden]{display:none}
            .editor-nav .editor-brand{font-size:12px;font-weight:650;color:#aebbc9;margin-right:12px;white-space:nowrap}
            .editor-nav a{display:inline-block;margin:0;padding:8px 10px;border-radius:5px;color:#becbd9;text-decoration:none;white-space:nowrap;line-height:1.3}
            .editor-nav a:hover{background:#2c3541;color:#fff}
            .editor-nav a[aria-current=page]{background:#3a5372;color:#fff;font-weight:600}
            .editor-nav a:focus-visible{outline:2px solid #aacfff;outline-offset:2px}
            @media(max-width:640px){.editor-nav .editor-brand{flex-basis:100%;margin:0 0 4px}.editor-nav a{padding:8px}}
            </style>
            <nav class="editor-nav" aria-label="Editor sections"><span class="editor-brand">Overhaul editor</span>
            """ + links + "</nav>";
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file)).Replace("<!--EDITOR_NAV-->", navigation);
    }
}
