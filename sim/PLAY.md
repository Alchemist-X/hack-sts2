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
working directory), `--no-crash-log`.

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
| `map` | `{"options":[{index,type,col,row,leads_to[types]}]}` — the rooms you can travel to next and what each leads to |
| `rewards` | `[{index,type,detail}]` — unclaimed rewards |
| `card_reward` | `{"cards":[card as in hand, without playability],"alternatives":[{index,id}],"can_skip"}` |
| `shop` | `{"entries":[{index,category,id,name,text,price,sold,on_sale?}],"removal_price"}` (`removal_price` null once used) |
| `rest` | `{"options":[{index,id,enabled}]}` (HEAL, SMITH, ...) |
| `treasure` | `{chest_opened,relic,relic_name,relic_text,relic_claimed,skipped,gold_gained}` |
| `event` | `{id,name,page,finished,options[{index,key,enabled,text,relic?}]}` |
| `prompt` | `{kind,site,min,max,picked[],options[{index,id,name,upgraded,text}],can_confirm,can_cancel}` — a selection screen inside an effect (Neow relic choices, Discovery, Smith upgrade, potion effects...). While a prompt is pending only `pick/confirm/cancel` are legal; the combat or run screen behind it is still shown |
| `legal_actions` | `[{token,desc}]` — every legal action exactly once |
| rules text fields | `name` / `text` on cards, relics, potions, powers, enemies, shop entries, event options and prompt options, `relic_name` / `relic_text` on treasure, `move` on intents: **null until rules text is loaded** (scaffold stub `RulesText`). When non-null, `text` renders `ID (Name) ... — text` on the item's line; powers with text get an indented `ID — text` line under their owner |
| `text` | compact rendering: header (phase, act, floor, turn, HP, block, energy, gold), powers, relics, potions, orbs, deck, enemies with intents, hand, the phase screen, then the legal tokens one per line with their desc |

Card text is not ported: cards are described by id, type, cost, target, keywords and their
dynamic-variable values (`vars`, base values before strength/vulnerable etc.; the powers that
modify them are listed separately). Enemy indices are indices into the combat's monster list (dead
enemies are omitted, so indices can have gaps). Hand indices shift after every play: always use the
latest observation.

## Token grammar (one token per step)

| Token | Meaning |
| --- | --- |
| `play <hand> [-> <enemy>]` | play a card; the target only for single-enemy cards |
| `end` | end the turn |
| `potion <slot> [-> <enemy>\|-> ally <k>]` | use a potion (in or out of combat) |
| `discard <slot>` | discard a potion |
| `pick <option>`, `confirm`, `cancel` | answer a prompt (multi-pick prompts take several `pick`s, then `confirm`) |
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
