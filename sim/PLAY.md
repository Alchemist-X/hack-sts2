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

Flags: `--port N` (default 8765), `--crash-log PATH` (default `tmp/play-crashes.jsonl`, relative to the
working directory), `--no-crash-log`, `--knowledge DIR` (rules text, see below; applies to serve,
`--selftest` and `--replay`).

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
| `POST /runs` | `{"seed":"ABC123"\|123\|null,"character":"IRONCLAD","ascension":0,"content":"ported_only"\|"all"}` | `{"id":"r1","observation":OBS}` |
| `POST /combats` | `{"seed":1,"deck":[ids]\|null,"encounter":"NIBBITS_WEAK","ascension":0,"hp":null,"max_hp":null}` | `{"id":"c1","observation":OBS}` |
| `GET /games/{id}` | — | `{"observation":OBS}` |
| `POST /games/{id}/step` | `{"token":"play 0 -> 1"}` | `{"observation":OBS,"log":[...],"ok":true}` |
| `GET /games/{id}/history` | — | `{"id","kind","seed","config","tokens":[...],"steps":n,"crashed":false}` |
| `DELETE /games/{id}` | — | `{"ok":true}` |

Defaults: a run uses a random 10-character seed when `seed` is null, `IRONCLAD`, ascension 0 and
`ported_only` content (pulls restricted to ported encounters, events, cards, relics and potions, so
random play does not stop on unported content). A combat's `deck` defaults to the Ironclad starting
deck; a string combat seed is hashed like a run seed, a number is used as the numeric seed.

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
| `player` | `character hp max_hp block energy max_energy gold [stars] powers[{id,amount,text}] relics[{id,name,text,counter?}] potions[{slot,id,name,text}\|null] [orbs[{id,passive,evoke}] orb_slots] deck_size deck[ids, "+" = upgraded]` |
| `combat` | `hand[{index,id,name,upgraded,cost (int or "X"),star_cost?,type,target,rarity,keywords,vars{damage,block,...},text,playable,reason}] draw_count discard_count exhaust_count draw_pile (sorted) discard_pile enemies[{index,id,name,hp,max_hp,block,intents[{type,move,damage?,times?,card_count?}],powers[{id,amount,text}]}] allies?[{index,id,name,hp,max_hp,block,powers}]` |
| `map` | `{"options":[{index,type,col,row,leads_to[types]}],"nodes":[{row,col,type,edges[cols],visited,current}]}` — `options` are the rooms you can travel to next and what each leads to; `nodes` is the whole act map (start, every room, boss), sorted by row then column, `edges` = the columns of the next row it connects to |
| `rewards` | `[{index,type,detail}]` — unclaimed rewards |
| `card_reward` | `{"cards":[card as in hand, without playability],"alternatives":[{index,id}],"can_skip","skip_consumes"}` — `skip_consumes` false (the normal case): `skip` closes the screen but the card reward stays on the rewards screen (re-open it with `reward <i>`; it is lost on `proceed`); true: skipping completes the reward; null when there is no skip |
| `shop` | `{"entries":[{index,category,id,name,text,price,sold,on_sale,base_price}],"removal_price"}` (`removal_price` null once used). `on_sale` (bool, cards only) halves the price; `base_price` is the pre-sale price reconstructed as 2 x `price` (exact up to 1 gold), null when not on sale. Text: `... 26g (sale, base ~52g)` |
| `rest` | `{"options":[{index,id,enabled}]}` (HEAL, SMITH, ...) |
| `treasure` | `{chest_opened,relic,relic_name,relic_text,relic_claimed,skipped,gold_gained}` |
| `event` | `{id,name,page,finished,options[{index,key,enabled,text,relic?}]}` — option `text` is `"Title: description"` from the game's event localisation with placeholders filled from the live event model (gold, HP loss, costs, the option's relic); unresolved values (an enchantment or card chosen at runtime) show `?`. Ancient relic options (Neow ...) have no option text of their own and show `Relic title: relic text`. The EventOption model carries no structured gold/HP cost, so there is no separate cost field: the cost is in the text |
| `prompt` | `{kind,site,min,max,picked[],options[{index,id,name,upgraded,text}],can_confirm,can_cancel,source,source_name,source_text,pile,effect,needs_confirm,auto_completes_at,desc}` — a selection screen inside an effect (Neow relic choices, Discovery, Smith upgrade, potion effects...). While a prompt is pending only `pick/confirm/cancel` are legal; the combat or run screen behind it is still shown. `source` is the card/relic/potion/event that opened it (`source_text` its rules text, which says what happens to the picks), `pile` the source pile (`Hand`, `Draw`, `Discard`, `Deck`... or null), `effect` a one-line reading of the kind (`remove the picked cards from your Deck`, `Upgrade the picked cards in your Hand`, `select cards from your Discard pile`...). Settlement: `needs_confirm` true = picks are collected until `confirm`; otherwise the prompt resolves by itself on pick number `auto_completes_at` (= `max`; a single-pick prompt settles on its one `pick`), and when `can_confirm` is true `confirm` ends it early. `desc` says this in words and is rendered under the prompt header; each `pick` desc ends in `- settles the prompt` when that pick resolves it |
| `legal_actions` | `[{token,desc}]` — every legal action exactly once |
| rules text fields | `name` / `text` on cards, relics, potions, powers, enemies, shop entries, event options and prompt options, `relic_name` / `relic_text` on treasure, `move` on intents: filled from the game's localisation when rules text is loaded (see "Rules text"), null otherwise or for ids the localisation lacks. When non-null, `text` renders `ID (Name) ... — text` on the item's line; powers with text get an indented `ID — text` line under their owner |
| `text` | compact rendering: header (phase, act, floor, turn, HP, block, energy, gold), powers, relics, potions, orbs, deck, enemies with intents, hand, the phase screen, then the legal tokens one per line with their desc |

Card `text` uses the card instance's own dynamic-variable values (`vars`: upgrades and enchantments
included, but base values before strength/vulnerable etc.; the powers that modify them are listed
separately), and keywords (Exhaust, Ethereal, Sly ...) are listed in `keywords`, not repeated in `text`. Enemy indices are indices into the combat's monster list (dead
enemies are omitted, so indices can have gaps). Hand indices shift after every play: always use the
latest observation.

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
-- enemies
 [0] NIBBIT 44/44 | intent: Attack 12
-- hand (draw 5, discard 0, exhaust 0)
 [0] DEFEND_IRONCLAD 1e Skill->Self (5 block)
 [2] STRIKE_IRONCLAD 1e Attack->AnyEnemy (6 dmg)
 ...
-- legal actions
 play 2 -> 0  : play STRIKE_IRONCLAD [1e] (6 dmg) on enemy 0 NIBBIT
 end  : end the turn
$ curl -s -X POST localhost:8765/games/c1/step -d '{"token":"play 2 -> 0"}'   # log:
["play 2 -> 0: play STRIKE_IRONCLAD [1e] (6 dmg) on enemy 0 NIBBIT", "enemy 0 NIBBIT hp 44 -> 38"]
$ curl -s -X POST localhost:8765/games/c1/step -d '{"token":"play 0"}'        # log:
["play 0: play DEFEND_IRONCLAD [1e] (5 block)", "block 0 -> 5"]
$ curl -s -X POST localhost:8765/games/c1/step -d '{"token":"end"}'           # log:
["end: end the turn", "turn 2", "hp 80/80 -> 73/80", "block 5 -> 0"]
$ curl -s -X POST localhost:8765/games/c1/step -d '{"token":"play 9"}'        # HTTP 400:
{"ok":false,"error":"'play 9' is not a legal token here ...","legal_actions":[{"token":"play 0 -> 0",...}]}
$ curl -s localhost:8765/games/c1/history
{"id":"c1","kind":"combat","seed":1,"config":{...},"tokens":["play 2 -> 0","play 0","end"],"steps":3,"crashed":false}
```

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
