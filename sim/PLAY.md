# Playing the simulator over HTTP (`Sts2Sim.Server`)

`sim/Sts2Sim.Server` serves the headless simulator (`RunSim` full runs, `CombatSim` standalone
combats) as a small JSON API on `127.0.0.1`, so an LLM agent driven from a shell (curl, or a short
Python client) can play whole runs, and a random policy can playtest it and collect crash repros.
Single process, games in memory, one lock per game, pure BCL (`HttpListener` + `System.Text.Json`).

## Start

```bash
dotnet build sim/Sts2Sim.sln -c Release
dotnet run -c Release --project sim/Sts2Sim.Server -- --port 8765 --crash-log tmp/play-crashes.jsonl
```

Flags: `--port N` (default 8765), `--bind ADDR` (default `127.0.0.1`, loopback only; `0.0.0.0`, `::`, `*`
or `+` listen on every interface, as the container image does, see `docs/docker.md`; otherwise one IP address
or host name), `--crash-log PATH` (default `tmp/play-crashes.jsonl`, relative to the working directory),
`--no-crash-log`, `--knowledge DIR` (rules text, see below; applies to serve, `--selftest` and `--replay`),
`--reveal-moves` (games default to `"reveal_moves": true`, see [Hidden information](#hidden-information);
default off, as in the game). Env `STS2SIM_VERSION` overrides the version `/health` reports (default: the
short git sha; the image sets it at build time).

At startup the server prints one stderr line saying what rules text it loaded, e.g.
`rules text: loaded from .../knowledge/source (discovered; versions v0.107.1,v0.111.0): 625 cards, 306 relics, ...`
or `rules text: no knowledge/source/<version> directory found ...` (observations then carry `null`
names/texts and render exactly as before).

In-process random playtest (the same handlers, no sockets):

```bash
dotnet run -c Release --project sim/Sts2Sim.Server -- --selftest 200 [--seed 1] [--character IRONCLAD] \
    [--ascension 0] [--content ported_only|all] [--policy random|biased] [--step-cap 6000]
```

It prints `runs, completed (wins, losses), crashed, capped, server errors, mean steps`, appends one
repro line per crash to the crash log, and exits 1 on any crash or server error. Without
`--character` it cycles through every character. `--policy biased` prefers card plays over ending
the turn and taking things over leaving or skipping, so runs reach deeper floors.

Replay a crash repro (default: the last line of the file):

```bash
dotnet run -c Release --project sim/Sts2Sim.Server -- --replay tmp/play-crashes.jsonl [--line N]
```

## Endpoints (JSON in, JSON out)

| Method and path | Body | Response |
| --- | --- | --- |
| `GET /health` | — | `{"ok":true,"version":"<git sha>","games":n}` |
| `GET /catalog` | — | `{"characters":[...],"encounters":[...],"cards":[...],"relics":[...],"potions":[...],"events":[...]}` |
| `GET /catalog?kind=K[&ids=A,B][&grep=X]` | — | `{"kind":K,"loaded":bool,"items":[{"id","name","text"}]}` for K = `cards relics potions powers events enemies keywords`; enemies items also carry `moves[{id,title}]`; `ids` filters by id (powers also match without `_POWER`), `grep` is a case-insensitive substring of id, name or text; unknown K is HTTP 400. Card text is a fresh unupgraded copy; event items have `text` null |
| `POST /runs` | `{"seed":"ABC123"\|123\|null,"character":"IRONCLAD","ascension":0,"content":"ported_only"\|"all","reveal_moves":false}` | `{"id":"r1","observation":OBS}` |
| `POST /combats` | `{"seed":1,"deck":[ids]\|null,"encounter":"NIBBITS_WEAK","ascension":0,"hp":null,"max_hp":null,"reveal_moves":false}` | `{"id":"c1","observation":OBS}` |
| `GET /games/{id}` | — | `{"observation":OBS}` |
| `POST /games/{id}/step` | `{"token":"play 0 -> 1"}` | `{"observation":OBS,"log":[...],"ok":true}` (log: see [Step log](#step-log)) |
| `GET /games/{id}/piles` | — | `{"game_id","in_combat","draw":[card]\|null,"discard":[card]\|null,"exhaust":[card]\|null,"hand":[card]\|null,"deck":[card]}` (see [Piles](#piles)) |
| `GET /games/{id}/history` | — | `{"id","kind","seed","config","tokens":[...],"steps":n,"crashed":false}` |
| `DELETE /games/{id}` | — | `{"ok":true}` |

Defaults: a run uses a random 10-character seed when `seed` is null, `IRONCLAD`, ascension 0 and
`ported_only` content (pulls restricted to ported encounters, events, cards, relics and potions, so
random play does not stop on unported content). A combat's `deck` defaults to the Ironclad starting
deck; a string combat seed is hashed like a run seed, a number is used as the numeric seed.
`reveal_moves` (bool, default the server's `--reveal-moves`, i.e. false) names enemy moves in intents and
in the enemy-turn log ([Hidden information](#hidden-information)); it is stored in the game's `config`
(`/history`, crash repros).

Errors:
- `400 {"ok":false,"error":"...","legal_actions":[...]}`: an illegal or unknown token. The game is
  unchanged and the token is not recorded. Bad create bodies also return 400 with a message.
- `500 {"ok":false,"error":"<Type>: <message>","crashed":true}`: the simulator threw. The game is
  marked crashed (later steps return 409) and one JSON line is appended to the crash log:
  `{"time","version","game_id","kind","seed","config","tokens":[...incl. the crashing one],"crash_token","steps","phase","error","content_gap","stack"}`.
  `content_gap` is true for `NotImplementedException` (an unported stub). `--replay` re-runs it.
- `404` unknown game or route; `409` step on a crashed game.

One curl example each:

```bash
curl -s localhost:8765/health
curl -s localhost:8765/catalog | python3 -m json.tool | head
curl -s -X POST localhost:8765/runs -d '{"seed":"ABC123","character":"IRONCLAD","ascension":0,"content":"ported_only"}'
curl -s -X POST localhost:8765/combats -d '{"seed":1,"encounter":"NIBBITS_WEAK"}'
curl -s localhost:8765/games/r1 | python3 -c 'import json,sys; print(json.load(sys.stdin)["observation"]["text"])'
curl -s -X POST localhost:8765/games/r1/step -d '{"token":"event 0"}'
curl -s localhost:8765/games/r1/history
curl -s -X DELETE localhost:8765/games/r1
```

## Observation (OBS)

Every field is always present; fields that do not apply to the current phase are `null`.

| Field | Content |
| --- | --- |
| `game_id`, `kind` | `"r1"` / `"c1"`; `"run"` or `"combat"` |
| `phase` | run: `Map Combat Rewards CardReward Shop RestSite Treasure Event Prompt Victory Defeat`; combat games: `Combat Victory Defeat` |
| `terminal`, `result` | `result` is `"win"` / `"loss"` once terminal, else null |
| `act`, `floor`, `turn` | act 1-based and total floor (runs only); turn while a combat is in progress |
| `player` | `character hp max_hp block energy max_energy gold [stars] powers[power] facing relics[{id,name,text,counter?}] potions[{slot,id,name,text}\|null] [orbs[{id,passive,evoke}] orb_slots] deck_size deck[ids, "+" = upgraded]` — `facing` is `"left"`/`"right"` in a SURROUNDED fight, else null |
| `combat` | `hand[card] draw_count discard_count exhaust_count draw_pile (sorted) discard_pile enemies[{index,uid,id,name,hp,max_hp,block,flank,behind,intents[{type,move,move_name,damage?,times?,card_count?}],powers[power]}] allies?[{index,uid,id,name,hp,max_hp,block,powers}] danger` |
| card (hand) | `{index,id,name,upgraded,cost (int or "X"),base_cost,star_cost?,type,target,rarity,keywords,vars{damage,block,...},effective,text,enchantments[{id,amount}],affliction{id,amount}\|null,dampened,playable,reason}`; card reward, prompt-free card lists and `/piles` use the same node without `playable`/`reason` and with `effective` null |
| enemy | `uid` stable creature id ([uid and index](#creature-uid-and-index)); intents show what the game shows (type, damage, hits, card count): `move` / `move_name` are null unless the game was created with `reveal_moves`, then `move` is the id of the move the intent belongs to, `move_name` its localised title (rules text) or null, text `[MOVE_ID (Title)]` before the intent; `flank` `"left"`/`"right"` (BACK_ATTACK_LEFT/RIGHT) or null; `behind` true when that enemy's attacks hit your back (x1.5) now, null outside SURROUNDED fights |
| power | `{id,amount,text,detail[,amount_raw]}`. `amount` is the number on the game's power icon (`PowerModel.DisplayAmount`, through the diff projection's rule `SimProjector.DisplayAmount`): HARDENED_SHELL the HP it can still lose this turn, SLOW the bonus in % (10 per card), TENDER the cards played this turn, every other power its amount. Powers whose icon number the projection has no rule for yet (`SimProjector.PowersWithDisplayAmountOverride` throws `ProjectionGapException`; now AUTOMATION, CACOPHONY, FERAL, MONOLOGUE, ORBIT, PALE_BLUE_DOT, PANACHE, SLOTH, TAG_TEAM, WITHERING_PRESENCE) fall back to the kernel amount and carry `amount_raw` (= that amount); no other power has the key. `text` is the description with the kernel amount, as the game fills `{Amount}` |
| power `detail` | what the player can see beyond the icon number, or null: SURROUNDED facing; DAMPEN `N cards downgraded until uN X dies` (the cards are marked `dampened`); CHAINS_OF_BINDING `N BOUND cards (ids); a BOUND card was played this turn: yes/no`; `target uN X` (the creature a power targets); the icon counters SLOTH `k/N cards played this turn`, TENDER `k cards played this turn`, WITHERING_PRESENCE `k cards until WITHER`; SLOW `+X% attack damage taken now` and THE_BOMB `N dmg to all enemies` (their descriptions); POISON `triggers Nx` (from visible ACCELERANT). Never who applied a power or kernel bookkeeping (applier ids, turn-start amounts, duration-tick flags) |
| `combat.danger` | `{incoming,hits,by_enemy[{uid,id,damage,times}],block,net_hp_loss,hp_after,timers[{source,id,text}]}` ([Danger](#danger-lines)) |
| `map` | `{"options":[{index,type,col,row,leads_to[types]}],"nodes":[{row,col,type,edges[cols],visited,current}]}` — `options` are the rooms you can travel to next and what each leads to; `nodes` is the whole act map (start, every room, boss), sorted by row then column, `edges` = the columns of the next row it connects to |
| `rewards` | `[{index,type,detail}]` — unclaimed rewards |
| `card_reward` | `{"cards":[card as in hand, without playability],"alternatives":[{index,id}],"can_skip","skip_consumes"}` — `skip_consumes` false (the normal case): `skip` closes the screen but the card reward stays on the rewards screen (re-open it with `reward <i>`; it is lost on `proceed`); true: skipping completes the reward; null when there is no skip |
| `shop` | `{"entries":[{index,category,id,name,text,price,sold,on_sale,base_price}],"removal_price"}` (`removal_price` null once used). `on_sale` (bool, cards only) halves the price; `base_price` is the pre-sale price reconstructed as 2 x `price` (exact up to 1 gold), null when not on sale. Text: `... 26g (sale, base ~52g)` |
| `rest` | `{"options":[{index,id,enabled}]}` (HEAL, SMITH, ...) |
| `treasure` | `{chest_opened,relic,relic_name,relic_text,relic_claimed,skipped,gold_gained}` |
| `event` | `{id,name,page,finished,options[{index,key,enabled,text,relic?}]}` — option `text` is `"Title: description"` from the game's event localisation with placeholders filled from the live event model (gold, HP loss, costs, the option's relic); unresolved values (an enchantment or card chosen at runtime) show `?`. Ancient relic options (Neow ...) have no option text of their own and show `Relic title: relic text`. The EventOption model carries no structured gold/HP cost, so there is no separate cost field: the cost is in the text |
| `prompt` | `{kind,site,min,max,picked[],options[{index,id,name,upgraded,text}],can_confirm,can_cancel,source,source_name,source_text,pile,effect,needs_confirm,auto_completes_at,desc}` — a selection screen inside an effect (Neow relic choices, Discovery, Smith upgrade, potion effects...). While a prompt is pending only `pick/confirm/cancel` are legal; the combat or run screen behind it is still shown. `source` is the card/relic/potion/event that opened it (`source_text` its rules text, which says what happens to the picks), `pile` the source pile (`Hand`, `Draw`, `Discard`, `Deck`... or null), `effect` a one-line reading of the kind (`remove the picked cards from your Deck`, `Upgrade the picked cards in your Hand`, `select cards from your Discard pile`...). Settlement: `needs_confirm` true = picks are collected until `confirm`; otherwise the prompt resolves by itself on pick number `auto_completes_at` (= `max`; a single-pick prompt settles on its one `pick`), and when `can_confirm` is true `confirm` ends it early. `desc` says this in words and is rendered under the prompt header; each `pick` desc ends in `- settles the prompt` when that pick resolves it |
| `legal_actions` | `[{token,desc}]` — every legal action exactly once |
| rules text fields | `name` / `text` on cards, relics, potions, powers, enemies, shop entries, event options and prompt options, `relic_name` / `relic_text` on treasure, `move_name` on intents (with `reveal_moves` only): filled from the game's localisation when rules text is loaded (see "Rules text"), null otherwise or for ids the localisation lacks. When non-null, `text` renders `ID (Name) ... — text` on the item's line; powers with text get an indented `ID — text` line under their owner |
| `text` | compact rendering: header (phase, act, floor, turn, HP, block, energy, gold), powers, relics, potions, orbs, deck, enemies with intents, hand, the phase screen, then the legal tokens one per line with their desc |

Cards are described by id, type, cost, target, keywords and their dynamic-variable values (`vars`:
upgrades and enchantments included, base values before strength/vulnerable etc.) plus `effective`
(below). Keywords (Exhaust, Ethereal, Sly ...) are listed in `keywords`, not repeated in `text`.
Hand indices shift after every play: always use the latest observation.

Combat text, line by line: header; `incoming:` danger line and one `! ...` line per timer; `powers:`
(`ID amount [detail]`); relics; potions; orbs; deck; `-- enemies` (`(you face right)` in SURROUNDED
fights), one line per live enemy `[index] uid ID (Name) hp/max [block b] [| left side (BEHIND you:
x1.5)] | intent: Attack 6 + Buff | POWER amount [detail], ...` (with `reveal_moves`:
`intent: [MOVE_ID (Title)] Attack 6 + Buff`), allies as `ally [k] uid ID hp/max | powers`; `-- hand`,
one line per card `[i] ID (Name) cost [(base Ne)] Type->Target (vars) [(eff ...)] {keywords}
[AFFLICTION] [ench ID n] [dampened] [UNPLAYABLE:reason] — text`. Power amounts in the text are the icon
numbers of the `power` row above.

### Hidden information

The observation shows what a player of the real game can see, no more:

- an enemy's intent is its type, damage per hit, hits and status-card count; the game never names the
  move, so `intents[].move` / `move_name` are null and the enemy-turn log tells a move by its effects.
  `"reveal_moves": true` in the create body (or `--reveal-moves` for the server default) adds the move
  id and title back, for debugging and ablations only; evaluations run with it off;
- the draw pile is listed sorted (`draw_pile`, `/piles`), never in draw order;
- powers show the icon number and player-visible details, never who applied them or kernel counters.

`GET /catalog?kind=enemies` still lists each monster's moves (a static reference, like a wiki); it
does not say which move comes next.

### Creature uid and index

- `uid` (`"u7"`) is the kernel's `CombatId`, allocated by `CombatState.AllocateCreatureId` when the
  creature joins the combat. It never changes and is never reused within a combat (through deaths,
  revivals, summons and reordering); ids restart each combat. The player is `player`, not a uid.
- `index` (what tokens use: `play 2 -> <index>`, `potion 0 -> <index>`) is the position in the kernel's
  enemy list `cs.Monsters` (turn order). Observations list live enemies only, so indices can have gaps.
  Verified rules (kernel `CreatureCmd.Kill`, `MonsterLifecycle`, `CombatState`, `MonsterSpawner`):
  - a normal enemy that dies is removed from the list: every enemy after it moves down one index;
  - an enemy whose own power vetoes removal (`ShouldCreatureBeRemovedFromCombatAfterDeath`: REATTACH,
    STEAM_ERUPTION, DIE_FOR_YOU, ILLUSION, ADAPTABLE, PAINFUL_STABS) stays in the list dead, so its index
    becomes a gap and nobody moves (it can revive in place, e.g. Decimillipede segments); an enemy that
    dies during its own move is removed right after that move;
  - an enemy that escapes is removed (indices behind it move down);
  - a summoned enemy is appended, then, when it has an encounter slot, the list is re-sorted into the
    encounter's slot order, so existing enemies can move (Fabricator goes from index 0 to 2 as its bots
    arrive); slotless encounters can also reorder through `SetEnemyIndex`.
  Track creatures by `uid` and re-read `index` from the latest observation before every targeted token.
  Legal-action descs name both: `play STRIKE_IRONCLAD [1e] (6 dmg) on enemy 1 LEAF_SLIME_M (u2)`.

### Effective numbers

`effective` on hand cards is `{"damage": {"u2": 22, "u3": 15} | null, "times": n | null, "block": n | null}`
(null when the card has no damage or block var). Values come from the kernel's preview path
(`Sts2Sim.Core/Combat/Pipeline/CardPreview.cs`, a port of the game's `DynamicVar.UpdateCardPreview`):
the card's damage var (calculated vars such as PERFECTED_STRIKE / BODY_SLAM computed first, Osty damage
dealt by Osty) through `Hook.ModifyDamage` against each live enemy (enchantment, Strength, Vigor, Weak,
per-target Vulnerable, SLOW, back attacks, ...), and the block var through `Hook.ModifyBlock`
(Dexterity, Frail, ...). It is the per-hit number the next play deals before the target's block.
`times` is the card's Repeat var or its fixed hit count (null for X cost); cards that compute their hit
count at play time report 1. The text adds `(eff 22 vs u2, 15 vs u3)`, `(eff 9 dmg x2)` or
`(eff 8 block)` only where the effective number differs from the base var (always for calculated cards
and multi-hit cards). `base_cost` is the card's own energy cost when the current cost differs (text
`0e (base 1e)`), else null. `enchantments` lists the card's enchantment, `affliction` its affliction
(BOUND by CHAINS_OF_BINDING, GALVANIZED, SMOG, ...; text `[BOUND]`), `dampened` marks a card DAMPEN
downgraded (restored when the casters die).

### Danger lines

`incoming: 18 dmg in 3 hits (u2 12x1, u3 3x2), block 5, net -13 HP -> 42/80` sums the intents' projected
attack damage (`damage` x `times`, already including Strength / Weak / Vulnerable / back attacks) of
every live enemy against your current block. It does not model hp-loss modifiers (Intangible, Buffer,
Tungsten Rod-style relics) or block you gain at end of turn. Each timer adds a `! ...` line (JSON
`danger.timers`), from powers on you and on the enemies and from status cards in hand; behaviour read
from each kernel port: POISON (start of turn), CONSTRICT / DEMISE / DISINTEGRATION (end of turn),
DOOM (end of turn, `LETHAL` when HP <= amount), TENDER, STRANGLE, OBLIVION, SLOW, GALVANIC,
CHAINS_OF_BINDING, SLOTH, SMOGGY, WITHERING_PRESENCE (per card played), SANDPIT (kills you when it
runs out, ticks at the start of each enemy turn), THE_BOMB, BATTLEWORN_DUMMY_TIME_LIMIT, HATCH,
COUNTDOWN, CRIMSON_MANTLE / INFERNO self-damage, and `end of turn in hand: TOXIC (5 dmg)` for every hand
card with an end-of-turn-in-hand effect (BURN, DECAY, DOUBT, REGRET, SHAME, TOXIC, WITHER, ...).

### Piles

`GET /games/{id}/piles` returns full card nodes (name, text, vars, enchantments, affliction) for the
master `deck` and, in combat, `hand`, `discard` and `exhaust` in pile order and `draw` sorted by id
(the draw order stays hidden, as in the game). The observation keeps only counts and sorted id lists.

### Step log

`log[0]` is `<token>: <desc>`; the rest, in this order (every line only when it applies):

| Line | Meaning |
| --- | --- |
| `phase A -> B`, `act`, `floor` | screen changes |
| `combat start: u1 NIBBIT 44/44, ...` | a combat began this step |
| `also played: X -> u2 NIBBIT (auto), Y` | cards played besides the step's own card (replays, auto-plays) |
| `potion 0 DISTILLED_CHAOS: played STRIKE_IRONCLAD -> u2 NIBBIT (auto), DEFEND_IRONCLAD (auto)` | cards a potion played |
| `played: X (auto)` | cards auto-played during an end-turn step |
| `enemy turn:` then ` u2 NIBBIT: attack 12 x1 -> player: 7 blocked, 5 dmg; +WEAK_POWER 1 -> player; +6 block; summoned u5 SPIKE_SLIME; added SLIMED x2` | one line per monster move, in order: its hits by target, powers it applied (`-> self` / target), block, creatures it summoned, status cards created; damage on others during the move (thorns) is appended as `u2 NIBBIT took 3 dmg from player`. A move without any of these shows the intent types the player saw (` u3 CORPSE_SLUG: Stun`, `Sleep`, `Heal`), or `no visible effect`. With `reveal_moves` the line leads with the move id and, when rules text is loaded, its name: ` u2 NIBBIT: BUTT_MOVE (Butt) — attack 12 x1 -> player: ...` |
| ` player took 5 dmg (TOXIC)` | end-turn-step damage outside a move (end-of-turn cards and powers, poison ticks) |
| `turn N` | a new player turn |
| `hp 80/80 -> 73/80 (7 dmg, 5 blocked)`, `block`, `energy`, `stars`, `gold` | player changes; the note sums the step's damage entries (`(healed N)` for a gain) |
| `player +VULNERABLE_POWER 2`, `player -STRENGTH_POWER`, `player STRENGTH_POWER 2 -> 4` | player power changes, in icon numbers (`player TENDER_POWER 0 -> 1`) |
| `u2 NIBBIT hp 44 -> 38 (6 dmg, 0 blocked)`, `u2 NIBBIT block 0 -> 6`, `u2 NIBBIT died`, `u2 X revived`, `u3 X escaped`, `u5 SPIKE_SLIME spawned (14/14 hp)`, `u2 +VULNERABLE_POWER 2` | per creature, by uid (never by index, so a kill produces no ghost heals or spawns) |
| `cards: drew 5; discarded X; exhausted SLIMED; generated DAZED x2` | card flow from the history |
| `deck + X`, `relic - Y`, `potion + Z` | deck, relic and potion changes |

The narration reads the kernel's own `CombatHistory` (`Sts2Sim.Core/State/CombatHistory.cs`, a port of
the game's history: card plays, potions, damage, block, powers, monster moves, draws); the per-creature
lines diff before/after snapshots keyed by uid. The kernel writes no damage entry once the combat is
ending, so the killing blow of the last enemy shows as `hp 6 -> 0` without a damage note.

## Rules text

Names and rules text come from the game's own localisation templates under
`knowledge/source/<version>/*.jsonl` (`v0.107.1`: `scripts/export_sts2_knowledge.py`; `v0.111.0`:
`scripts/export_sts2_localization.py`, localisation only, read from the installed game's PCK). Every
version directory is loaded oldest first and a newer record replaces an older one per id, so kinds a
newer export lacks fall back to the older one. Discovery: `--knowledge DIR` (a `knowledge/`,
`knowledge/source/` or single version directory), else env `STS2SIM_KNOWLEDGE`, else the nearest
`knowledge/source` above the working directory or the server binary. Loading never fails the server.

Template placeholders are filled from the live model: a card instance's dynamic vars, a relic's or
potion's vars, an event model's members. Conventions in the rendered text:

- energy renders as `[E]` (`Gain 2[E]`, `costs 0[E]`), stars as `[Star]`; markup is stripped; one line.
- a value that cannot be resolved renders as `?`; a conditional on something unknown out of
  combat (`InCombat` "(Hits N times)" suffixes, multiplayer variants) takes its default branch.
  A literal `?` in the game's own text means an Unknown (`?`) map room (Juzu Bracelet, Planisphere).
- power `text` is the power's amount-aware description with the amount written as `N` (the amount
  is on the summary line: `STRENGTH_POWER 2` / `STRENGTH_POWER — Increases attack damage by N.`).
  Temporary powers (`SETUP_STRIKE_POWER` ...) use the game's generic temporary-strength/dexterity/focus
  text and the origin card's title.

Look-ups from a shell: `sim_play.py describe <id...>` (any kind) or
`sim_play.py catalog --kind powers --grep vulnerable`.

## Token grammar (one token per step)

| Token | Meaning |
| --- | --- |
| `play <hand> [-> <enemy>]` | play a card; the target only for single-enemy cards |
| `end` | end the turn |
| `potion <slot> [-> <enemy>\|-> ally <k>]` | use a potion (in or out of combat) |
| `discard <slot>` | discard a potion |
| `pick <option>`, `confirm`, `cancel` | answer a prompt: multi-pick prompts take several `pick`s in any order (every unpicked option is listed), then `confirm` when `needs_confirm`; otherwise the last allowed pick settles it (see `prompt.desc`) |
| `travel <option>` | map: go to `map.options[option]` |
| `reward <index>`, `proceed` | rewards screen: claim one; leave |
| `card <index>`, `skip`, `reroll` | card reward: take a card; skip; reroll (when a relic allows it; other alternatives are `alt <i>`) |
| `buy <index>`, `remove`, `leave` | shop: buy entry; pay for a card removal (a prompt follows); leave |
| `rest <option>`, `leave` | rest site: choose an option; leave once done |
| `open`, `take 0`, `skip` | treasure: open the chest; take the relic; skip it |
| `event <option>`, `leave` | event: choose an option; leave a finished event |

Tokens are case- and whitespace-insensitive (`PLAY 2->0` = `play 2 -> 0`, `end turn` = `end`);
`take` = `take 0`; `next` / `proceed` / `leave` / `continue` map to the screen's single exit action.
`history.tokens` stores the canonical form. Only the tokens listed in `legal_actions` are accepted.

## Example session (combat game)

```text
$ curl -s -X POST localhost:8765/combats -d '{"seed":1,"encounter":"NIBBITS_WEAK"}'   # -> id c1, text:
== c1 Combat | turn 1 | IRONCLAD HP 80/80 block 0 energy 3/3 gold 0
incoming: 12 dmg in 1 hit (u1 12x1), block 0, net -12 HP -> 68/80
-- enemies
 [0] u1 NIBBIT 44/44 | intent: Attack 12
-- hand (draw 5, discard 0, exhaust 0)
 [0] DEFEND_IRONCLAD 1e Skill->Self (5 block)
 [2] STRIKE_IRONCLAD 1e Attack->AnyEnemy (6 dmg)
 ...
-- legal actions
 play 2 -> 0  : play STRIKE_IRONCLAD [1e] (6 dmg) on enemy 0 NIBBIT (u1)
 end  : end the turn
$ curl -s -X POST localhost:8765/games/c1/step -d '{"token":"play 2 -> 0"}'   # log:
["play 2 -> 0: play STRIKE_IRONCLAD [1e] (6 dmg) on enemy 0 NIBBIT (u1)", "energy 3 -> 2", "u1 NIBBIT hp 44 -> 38 (6 dmg, 0 blocked)"]
$ curl -s -X POST localhost:8765/games/c1/step -d '{"token":"play 0"}'        # log:
["play 0: play DEFEND_IRONCLAD [1e] (5 block)", "block 0 -> 5", "energy 2 -> 1"]
$ curl -s -X POST localhost:8765/games/c1/step -d '{"token":"end"}'           # log:
["end: end the turn", "enemy turn:", " u1 NIBBIT: attack 12 x1 -> player: 5 blocked, 7 dmg",
 "turn 2", "hp 80/80 -> 73/80 (7 dmg, 5 blocked)", "block 5 -> 0", "energy 1 -> 3", "cards: drew 5"]
$ curl -s -X POST localhost:8765/games/c1/step -d '{"token":"play 9"}'        # HTTP 400:
{"ok":false,"error":"'play 9' is not a legal token here ...","legal_actions":[{"token":"play 0 -> 0",...}]}
$ curl -s localhost:8765/games/c1/history
{"id":"c1","kind":"combat","seed":1,"config":{...,"reveal_moves":false},"tokens":["play 2 -> 0","play 0","end"],"steps":3,"crashed":false}
```

With `{"seed":1,"encounter":"NIBBITS_WEAK","reveal_moves":true}` the enemy line reads
`[0] u1 NIBBIT 44/44 | intent: [BUTT_MOVE] Attack 12` and the enemy-turn line
` u1 NIBBIT: BUTT_MOVE — attack 12 x1 -> player: 5 blocked, 7 dmg` (`BUTT_MOVE (Butt)` once rules text
is loaded).

A full run starts the same way with `POST /runs` (phase `Event`, Neow: `event 0..2`), then
`travel`, combats, `reward`/`card`/`proceed`, shops, rest sites, until `terminal` is true.

## Agent loop

```python
import json, urllib.request
def call(method, path, body=None):
    req = urllib.request.Request("http://127.0.0.1:8765" + path, method=method,
                                 data=None if body is None else json.dumps(body).encode())
    try:
        with urllib.request.urlopen(req) as r: return r.status, json.loads(r.read())
    except urllib.error.HTTPError as e: return e.code, json.loads(e.read())

_, d = call("POST", "/runs", {"seed": "ABC123", "character": "IRONCLAD"})
gid, obs = d["id"], d["observation"]
while not obs["terminal"]:
    token = choose(obs["text"], [a["token"] for a in obs["legal_actions"]])   # LLM or policy
    status, d = call("POST", f"/games/{gid}/step", {"token": token})
    if status == 500: break            # simulator crash: repro is in tmp/play-crashes.jsonl
    if status == 200: obs = d["observation"]
```

## Evaluation runner

`sim/tools/eval` (Python package `sts2eval`, uv project) plays fixed seed sets of full runs against
this server, many games in parallel, with a seeded random policy or a Claude model through the
Anthropic SDK; every step is transcribed and the run ends with `report.md` (win rate with a Wilson
interval, floors, steps, illegal actions, crashes, tokens, cost, wall time). It only uses the
endpoints above and sends `"reveal_moves"` explicitly (default false; `--reveal-moves` to ablate), so an
evaluation sees the game's view whatever the server default. A per-game loop guard hints once when the
same observation repeats on a floor and ends a game as `stuck` when a floor takes too many non-combat
steps. By default (`--obs-mode compact`) it does not send `text` but renders the same information from
the observation JSON and `/piles`, with relics, deck and potions in cached system blocks (`sts2eval parity`
checks that every id, number and text of `text` is kept); `--obs-mode full` sends `text`.
Install, flags, output layout and metric definitions: `sim/tools/eval/README.md`.

```bash
uv run --project sim/tools/eval sts2eval run --seedset a0-s10 --policy random --concurrency 10 --spawn
ANTHROPIC_API_KEY=... uv run --project sim/tools/eval sts2eval run --seedset a0-s10 --policy claude \
    --model claude-sonnet-5-5 --effort low --concurrency 10 --spawn
```
