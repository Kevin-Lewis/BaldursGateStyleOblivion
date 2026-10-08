# Named NPC expertise review

OpenAI independently evaluated 846 named NPC records without explicit level overrides using the configured `gpt-6-luna` model and verified English UESP evidence. The prompt does not receive previous tiers or ask for promotions, demotions or a target distribution.

Tier reflects combat or combat-adjacent class capability: martial skills, relevant magic, stealth, theft, security, infiltration, assassination, and practical adventuring alchemy. Ordinary expertise in painting, cooking, smithing, commerce, scholarship, administration or leadership does not establish higher actor power. Ordinary civilians generally fit tier 1 or 2; higher tiers require relevant evidence. Social manipulation counts when connected to covert work or dangerous encounters, rather than generic persuasion or political status.

Results: 835 supported evaluations, 11 inconclusive evaluations retaining existing assignments, and 350 tier changes. Among the reviewed records, tier 1 increased from 5 to 54 and tier 2 from 232 to 396. These are observed results, not imposed quotas. Protected actors are excluded from these distributions.

All 319 explicit level overrides, all unselected actor assignments, unrelated actor fields and group settings were verified unchanged. Trainer skill floors remain separate from tiers, so smithing or commerce trainers do not need artificially elevated tiers to offer their intended training. One recommendation lacked verified citations and was rejected; its existing assignment was preserved alongside ten inconclusive responses.

The full comparison and source references are in `NamedNpcExpertiseReview.json`. Refresh the actor website to see saved tiers and notes. Rerun Synthesis to apply the assignments and regenerate actor stat reports for gameplay.

The CLI supports `--review` with an explicit `--formkey` or `--formkeys` selection. It checks for level overrides before research and before saving, and refuses to overwrite assignments manually edited during a request. The prompt is `tools/ActorResearch/expertise-prompt.txt`; validated API results are cached in the existing research directory.
