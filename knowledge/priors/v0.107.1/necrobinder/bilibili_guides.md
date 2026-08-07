# Bilibili-derived Necrobinder priors for v0.107.1

Captured 2026-08-07. These are compact decision rules, not transcripts. Card text and numeric values always come from the local v0.107.1 source corpus.

## Version gate

- The closest explicitly versioned source found is the multi-part “103版本亡灵契约师全卡牌应用分析+对局详解”.
- The A10 Osty teaching run was published in the same April 2026 window.
- The v0.107.1 patch changed Death March, Sic 'Em, The Scythe, and Debilitate, and also changed encounters. Treat exact values and matchup scripts from v103 as stale; preserve only drafting, routing, and engine principles after checking live state.

## In-context rules extracted from the A10 run

1. Choose a route as a development sequence, not as the next node. Count fires, shops, question marks, and forced elites before committing.
2. Early picks must solve the current damage/defense check. A strong long-term archetype piece does not excuse weak transition turns.
3. A first copy of a long-term scaling card can be speculative when its support is common, but unsupported narrow cards should be skipped.
4. Shop purchases should reinforce the cards already acquired; do not buy an archetype label merely because it is available.
5. Spend potions to protect HP before a dangerous elite or boss. Saving a potion while losing the run is not value.
6. Bone Shards is a particularly strong transitional card. Its AoE/block rate can justify sacrificing a small Osty; sequence it before rebuilding Osty when possible.
7. Do not add draw or energy engines that the current deck cannot exploit. Energy needs useful sinks; draw needs an affordable hand and sufficient card quality.
8. Rest when HP is the binding constraint and no upgrade immediately changes the next matchup.
9. Skip mediocre rewards. Deck size is a cost, especially when it dilutes reliable block and the few high-impact engine cards.

## Run-specific policy

- Sleight of Flesh is a high-ceiling card, but require a credible trigger package or a safe setup window. Doom, Weak, Vulnerable, Hang, and AoE debuffs are all relevant triggers; re-evaluate it upward as that density rises.
- Preserve Osty when Bodyguard/Sic 'Em/Necro Mastery makes it the scaling plan. If Osty is small and survival is the bottleneck, Bone Shards may be the correct conversion.
- Prefer one early high-rate damage card, one scalable defensive package, and an AoE answer over several speculative synergy pieces.
- At every map screen, score the complete reachable path and reject branches that force consecutive elites without a rest or adequate HP/potions.

## Sources

- https://www.bilibili.com/video/BV1gnd5BhEv1
- https://www.bilibili.com/video/BV1phdYBiE3p/
- https://steamdb.info/patchnotes/23811903/
