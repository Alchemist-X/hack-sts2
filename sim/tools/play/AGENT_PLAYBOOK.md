# Agent playbook: playing Sts2Sim from a shell

You are playing Slay the Spire 2 (v0.111.0 rules) against a headless simulator, one
action at a time, through `sim/tools/play/sim_play.py`. Your two jobs: play well, and
notice when the simulator misbehaves.

## Setup

```bash
scripts/sim_server.sh                       # build + start the server, waits for /health
alias sp='python3 sim/tools/play/sim_play.py'
sp health
sp new --seed ABC123 --character IRONCLAD   # prints "game r1" and the first observation
sp new --combat NIBBITS_WEAK --deck STRIKE_IRONCLAD STRIKE_IRONCLAD DEFEND_IRONCLAD BASH --seed 1
sp catalog --kind encounters --grep NIBBIT  # look up ids
scripts/sim_server.sh stop                  # when finished
```

Characters: `IRONCLAD`, `SILENT`, `DEFECT`, `REGENT`, `NECROBINDER`. Server URL comes
from `--url` or `SIM_PLAY_URL` (default `http://127.0.0.1:8765`).

## The loop

1. `sp show r1` — read the whole observation. Never act from memory.
2. Think: what phase am I in, what are the legal tokens, what does the enemy intend?
3. `sp step r1 "<token>"` — exactly one token copied from `legal_actions`.
4. Read the `--- log ---` (what happened) and the `--- state ---` (the new observation).
   Indices change after every action: hand cards shift left when one is played, dead
   enemies drop out. Re-read before the next step.
5. Repeat until `terminal` (`Victory` / `Defeat`).

Quote tokens that contain `->`: an unquoted `->` is a shell redirect. Unquoted
shortcuts also work: `sp step r1 play 0 1` and `sp step r1 play 0 @1` both mean
`play 0 -> 1`. Advanced: `sp step r1 "play 0 -> 1; play 2; end"` sends several tokens in
order and stops at the first rejection — use only when nothing between them can
change (no draw, no prompt).

Exit codes of `step`: `0` ok, `2` illegal token (game unchanged; the legal list is
printed), `3` simulator crash (the game is dead; report it), `4` server unreachable.

To keep an audit trail, pass `--log run.jsonl` to every `step` (or export
`SIM_PLAY_TRANSCRIPT=run.jsonl`), or at the end run `sp transcript r1 run.jsonl`, which
replays the game's token history into one observation/action record per step and checks
the replay ends in the same state.

## Reading the observation

- Header: phase, act, floor, turn, HP / max HP, block, energy, gold, relics, potions.
- `combat.hand`: `index`, card `id`, `cost`, `type` (Attack / Skill / Power / Status /
  Curse), `target` (AnyEnemy needs `-> <enemy index>`; Self / AllEnemies / None do not),
  `vars` (the numbers: `damage`, `block`, magic numbers), `playable` + `reason`.
  Card text is not available; infer the effect from id, type, keywords and vars.
- `combat.enemies`: `index`, hp, block, `intents` (`Attack` with `damage` x `times`,
  `Defend`, `Buff`, `Debuff`, ...), `powers` (e.g. `VULNERABLE`, `WEAK`, `STRENGTH`).
  The intent damage shown is what will hit you at end of turn, before your block.
- `legal_actions`: every legal token with a description. Only these tokens are valid.

## Token grammar

| Phase | Tokens |
| --- | --- |
| Combat | `play <hand> [-> <enemy>]`, `end`, `potion <slot> [-> <enemy>]`, `discard <slot>` |
| Prompt (choose cards, also mid-combat) | `pick <option>`, `confirm`, `cancel` |
| Map | `travel <option>` |
| Rewards | `reward <index>`, `proceed` |
| CardReward | `card <index>`, `skip` |
| Shop | `buy <index>`, `remove`, `leave` |
| RestSite | `rest <option>`, `leave` |
| Treasure | `open`, `take <index>`, `skip` |
| Event | `event <option>`, `next` |

## Slay the Spire 2 basics

- **Energy**: you get `max_energy` (usually 3) each turn; each card costs its `cost`.
  Unused energy is lost. Play cards until you cannot or should not, then `end`.
- **Block vs damage**: block absorbs incoming damage this turn and mostly disappears at
  the start of your next turn. Block only matters if an enemy intends to attack. If the
  total incoming attack is 0, spend all energy on damage.
- **Intents**: plan around them. Incoming = sum of damage x times over attacking enemies.
  Block enough to stay healthy when the hit is big; otherwise race. Kill the enemy with
  the most dangerous intent first; a dead enemy deals no damage.
- **Status effects**: Vulnerable on an enemy = it takes 50% more attack damage; Weak on
  an attacker = it deals 25% less; Strength adds to every hit. Apply Vulnerable (e.g.
  `BASH`) before your big attacks.
- **End turn**: `end` is the only way the enemies act and you draw a new hand. If you
  have no playable cards left, end the turn.
- **Potions**: one-shot. Use them in hard fights (elites, bosses) rather than hoarding
  until you die. Full potion slots waste potion rewards.
- **Map**: elites give relics but hit hard; rest sites heal; shops need gold; `?` rooms
  are events. Early in an act take some fights to strengthen the deck; take elites only
  when HP is healthy. Look ahead at where each option leads.
- **Rest site**: rest (heal ~30% max HP) when HP is low or a boss/elite is next;
  otherwise upgrade (smith) a key card.
- **Card rewards**: take cards that fix a real weakness (more damage, more block, scaling);
  skipping is fine. A thin, focused deck draws its best cards more often.
- **HP is a resource** across the whole run; there is no healing between fights except
  rest sites, relics, events, and some cards.

## Common mistakes

- Wrong index: indices are positions in the current observation, not card ids. After
  playing hand card 0, the old card 1 is now card 0.
- Stale state: always use the observation returned by your last `step` (or `show`).
- Forgetting `-> <enemy>` on an AnyEnemy card, or adding one to an untargeted card.
- Forgetting to `end` the turn, or ending it with energy and useful cards left.
- Ignoring a `Prompt` phase: some cards and events ask you to pick cards; you must
  `pick` and then `confirm`.
- Blocking when no enemy is attacking, or attacking when a lethal hit is coming.

## Reporting a suspected simulator bug

Report when: the server returns a crash (exit code 3 / HTTP 500), a token listed in
`legal_actions` is rejected, the game is stuck (non-terminal with no legal actions), or a
rule visibly contradicts the game (wrong damage after Vulnerable / Strength, block not
expiring, a card doing nothing, an enemy acting against its intent).

Include:

1. game id and `sp history <id>` output (seed, config, full token list),
2. the step number (0-based index into `tokens`) and the token,
3. what you expected versus what the log / observation showed, with the numbers,
4. the relevant observation (`sp show <id> --json`, or the record from your transcript).

Crashes are also appended automatically by the server to `tmp/play-crashes.jsonl`.
For a bulk smoke test without an LLM: `sp random --runs 20 --seed 1` (uniform-random
legal policy; exits 1 if any run crashed, got a legal token rejected, or got stuck).
