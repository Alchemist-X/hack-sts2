import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import json, os, re, sys
from collections import Counter, OrderedDict

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
INV = common.INVENTORY
D111 = common.DECOMPILE_ROOT

import annot_common
import annot_relics_1, annot_relics_2, annot_relics_3, annot_potions  # noqa: F401
import condense

A, B = annot_common.A, annot_common.B

hooks = json.load(open(os.path.join(INV, 'hooks.json')))
HOOK = {h['name']: h for h in hooks['hooks']}
kg = json.load(open(common.stage1_path('kernel-gaps.json')))
CMD = {}
for cls, ms in kg['cmd_facades'].items():
    for m in ms:
        CMD.setdefault(cls + '.' + m['name'], set()).add(m['status'])
ported = json.load(open(os.path.join(INV, 'ported.json')))
PORTED_POWERS = set()
for e in ported['entries']['powers']:
    PORTED_POWERS.add(e['class'])
PORTED_CARDS = {e['class'] for e in ported['entries']['cards']}

REL_DELTA = {
    'BeautifulBracelet': 'v0.107.1: choose 3 deck cards (FromDeckForEnchantment) for Swift 3; v0.111.0: 4 random enchantable deck cards (RunRng.Niche TakeRandom), Swift 2, no prompt',
    'BoneTea': 'trigger moved from AfterSideTurnStart to AfterPlayerTurnStart (different point of the turn-start sequence)',
    'DiamondDiadem': 'redesigned: v0.107.1 applied DiamondDiademPower at end of a turn with <=2 cards played; v0.111.0 gives 20 block and Blur on turn 1',
    'DistinguishedCape': 'v0.107.1: lose 9 max HP + 3 Apparitions; v0.111.0: 2 random curses (RunRng.Niche) + 3 Apparitions',
    'DivineDestiny': 'turn-1 stars 6 -> 7',
    'Fiddle': 'hand-draw bonus moved from ModifyHandDrawLate to ModifyHandDraw',
    'FurCoat': 'marked combats 7 -> 8; adds a map-history flag',
    'HandDrill': 'trigger moved from AfterDamageGiven(result.WasBlockBroken) to the AfterBlockBroken hook',
    'HistoryCourse': 'replays only Attacks (v0.107.1: Attacks or Skills); owner test uses CardPlay.Player',
    'JeweledMask': 'prefers non-Innate Powers when choosing the draw-pile Power',
    'LastingCandy': 'counter now advances on combat reward screens that contain a card reward (BeforeCombatRewardOffered, odd counts trigger) instead of every combat end; requires IsCardReward|IsFromCombat flags',
    'Pendulum': 'v0.107.1 drew 1 card at AfterPlayerTurnStart every 3rd turn; v0.111.0 adds +1 to the hand draw (BeforeHandDraw counter + ModifyHandDraw)',
    'Planisphere': 'heals only on the first room of a map point (CurrentRoomCount<=1)',
    'PollinousCore': 'counter moved from BeforeSideTurnStart to BeforeHandDraw; bonus when TurnsSeen>=4 (was ==4)',
    'Regalite': 'block 2 -> 4 and once per turn (adds a per-turn flag)',
    'SealOfGold': 'gold cost per turn 5 -> 3',
    'SereTalon': 'v0.107.1: 2 random curses + 3 Wishes; v0.111.0: lose 9 max HP + 3 Wishes',
    'SignetRing': 'gold 999 -> 888',
    'ToastyMittens': 'v0.107.1: BeforeHandDraw exhaust the top draw-pile card (non-Innate on turn 1) + 1 Strength; v0.111.0: AfterPlayerTurnStart choose a hand card to exhaust (prompt) + 1 Strength',
    'ToyBox': 'wax relics offered 4 -> 5 (used-up threshold Combats x Relics follows)',
    'UnsettlingLamp': 'skips targets that have Artifact',
}
REL_COSMETIC_NOTES = {
    'DingyRug': 'API refactor (CardCreationOptions.CustomCardPool removed; Concat -> Union); same pools result',
    'PrismaticGem': 'API refactor (CustomCardPool check removed); same pools result',
    'MassiveScroll': 'API refactor (custom card pool -> pools + MultiplayerOnly filter)',
    'GlassEye': 'v0.111.0 rarity array lost in decompile; v0.107.1 values {Common, Common, Uncommon, Uncommon, Rare} are the provisional source',
    'LordsParasol': 'adds a RunManager.IsInProgress guard on the removal purchase and Sentry logging',
    'FakeStrikeDummy': 'ModifyDamageAdditive signature gained CardPlay', 'MiniatureCannon': 'ModifyDamageAdditive signature gained CardPlay',
    'MysticLighter': 'ModifyDamageAdditive signature gained CardPlay', 'StrikeDummy': 'ModifyDamageAdditive signature gained CardPlay',
    'UndyingSigil': 'ModifyDamageMultiplicative signature gained CardPlay', 'SturdyClamp': 'CreatureCmd.LoseBlock gained a choice-context parameter',
    'WhisperingEarring': 'same loop; decompiled as a state machine',
}
POT_SELF_TO_ANYPLAYER = 'TargetType Self -> AnyPlayer and the effect applies to target.Player (multiplayer throw); identical in single player'

AUTO_TOKENS = [
    (r'OrbCmd\.|OrbQueue|OrbModel', 'Defect orbs (OrbQueue, OrbCmd)'),
    (r'OstyCmd|\.Osty\b|PetOwner|AddPet|GetPet<', 'Osty/pets (OstyCmd.Summon, PlayerCmd.AddPet, Creature.PetOwner)'),
    (r'GainStars|StarsSpent|TryModifyStarCost|BaseStarCost|HasStarCostX', 'Stars (PlayerCombatState.Stars, star costs)'),
    (r'ForgeCmd', 'Forge (ForgeCmd, SovereignBlade)'),
    (r'CardFactory\.GetDistinctForCombat|CardFactory\.GetForCombat', 'CardFactory combat generation over unlock-filtered pools (CardPoolModel.GetUnlockedCards(UnlockState))'),
    (r'CardFactory\.CreateForReward|new CardReward\(|CardCreationOptions', 'CardFactory.CreateForReward / CardCreationOptions (reward card generation, CardRarityOdds)'),
    (r'CreateRandomCardForTransform|TransformToRandom|new CardTransformation\(', 'card transformation options (CardFactory.GetDefaultTransformationOptions)'),
    (r'PotionFactory\.|PotionCmd\.|HasOpenPotionSlots|MaxPotionCount|\.Potions\b', 'potion slots and procurement (PotionCmd.TryToProcure, PotionFactory)'),
    (r'RelicFactory\.|RelicCmd\.|new RelicReward\(', 'relic grab bags and RelicCmd (RelicFactory.PullNextRelicFromFront)'),
    (r'RewardsCmd\.|RewardsSet\b', 'rewards screen (RewardsCmd.OfferCustom / RewardsSet.Offer)'),
    (r'History\.(CardPlaysFinished|Entries)', 'combat history (Combat.History)'),
    (r'CardCmd\.Enchant|EnchantmentModel|\.Enchantment\b|ModelDb\.Enchantment<', 'enchantments (EnchantmentModel on cards, CardCmd.Enchant)'),
    (r'MerchantRoom|ModifyMerchantPrice|MerchantEntry|MerchantInventory', 'shop (MerchantInventory, merchant entries)'),
    (r'RestSiteOption|RestSiteRoom|RestSiteHeal', 'rest site (RestSiteOption, rest heal)'),
    (r'ActMap|MapPoint\b|MapCoord|ModifyUnknownMapPointRoomTypes|ShouldAllowFreeTravel', 'map (ActMap, MapPoint, travel)'),
    (r'GainGold|LoseGold|ModifyGoldGained|\.Gold\b', 'gold (PlayerCmd.GainGold/LoseGold, Hook.ModifyGoldGained)'),
    (r'CurrentRoom\b|RoomType\.', 'room context (RunState.CurrentRoom.RoomType)'),
    (r'CardCmd\.AutoPlay|AutoPlayFromDrawPile', 'auto-play (CardCmd.AutoPlay)'),
    (r'SetToFreeThisTurn|SetToFreeThisCombat|AddUntilPlayed|SetThisTurnOrUntilPlayed', 'temporary card cost modifiers (SetToFreeThisTurn/Combat, AddUntilPlayed, SetThisTurnOrUntilPlayed)'),
    (r'CreateClone\(|CloneCard\(|CreateDupe\(', 'card clones/dupes (CardModel.CreateClone, RunState.CloneCard, CreateDupe)'),
    (r'IsWax|RelicCmd\.Melt', 'wax/melt relics'),
    (r'new Rng\(\w+\.Owner, \w+\.Id', 'model-seeded Rng(player, ModelId) (seed = RunSeed + player slot + hash(id), Random/Rng.cs:37-40)'),
    (r'PushSelector', 'card selector stack (CardSelectCmd.PushSelector)'),
    (r'ShouldTakeExtraTurn|AfterTakingExtraTurn', 'extra turns'),
    (r'CardPilePosition\.Random', 'random pile insertion (RunRng.Shuffle NextInt(count+1), Commands/CardPileCmd.cs:3431)'),
    (r'Resources\.EnergyValue', 'CardPlay.Resources.EnergyValue (energy paid for the play)'),
]


DROP_MANUAL = {'Stars', 'Osty pet', 'OrbQueue', 'Forge (SovereignBlade)', 'Osty', 'potion slots', 'Forge'}


def normalize_kreq(items, power_names):
    out = []
    for x in items:
        if x in DROP_MANUAL:
            continue
        if re.fullmatch(r'\w+Power( \([^()]*\))?', x):
            continue
        out.append(x)
    s = set(out)
    res = []
    for x in out:
        if x == 'CardPileCmd.Add (partial)' and 'CardPileCmd.Add (partial in sim)' in s:
            continue
        if x == 'extra turns' and any(y.startswith('extra turns (') for y in s):
            continue
        if x in ('CardModel.CreateClone', 'RunState.CloneCard', 'CardModel.CreateDupe') and any(y.startswith('card clones/dupes') for y in s):
            continue
        if x in ('CardModel.SetToFreeThisTurn',) and any(y.startswith('temporary card cost modifiers') for y in s):
            continue
        if x.startswith('auto-play (') and 'CardCmd.AutoPlay' in s:
            continue
        if x.startswith('rewards screen (') and 'RewardsCmd.OfferCustom' in s:
            continue
        if x == 'combat history (Combat.History)' and any(y.startswith('combat history (') and y != x for y in s):
            continue
        if x.startswith('enchantments (EnchantmentModel on cards') and any(y.startswith('enchantments (') and y != x for y in s):
            continue
        if x.startswith('Hook.'):
            m = re.match(r'Hook\.(\w+)', x)
            base = m.group(1) if m else None
            dup = [y for y in res if y.startswith('Hook.' + base + ' (') and y != x]
            if dup and ('both scope' in x or 'run scope' in x or x.endswith('(both)') is False and 'combat room' not in x):
                continue
        if x.startswith('potion slots;'):
            continue
        if x.startswith('Hook.AfterRoomEntered (combat room') or x.startswith('Hook.AfterRoomEntered for combat rooms'):
            continue
        res.append(x)
    seen = set(); final = []
    for x in res:
        if x not in seen:
            seen.add(x); final.append(x)
    return final


def read_src(rel):
    p = os.path.join(D111, 'MegaCrit.Sts2.Core.' + rel)
    return p, open(p, encoding='utf-8', errors='replace').read()


def split_items(s, sep):
    return [x.strip() for x in s.split(sep) if x.strip()] if s else []


def behaviour(items, run=False):
    out = []
    for it in items:
        if ':' in it:
            hook, eff = it.split(':', 1)
            hook = hook.strip(); eff = eff.strip()
        else:
            hook, eff = '', it
        d = OrderedDict()
        phase = None
        m = re.match(r'^(.*?)\[(\w+)\]$', hook)
        if m:
            hook, phase = m.group(1).strip(), m.group(2)
        d['hook'] = hook
        if run:
            d['phase'] = phase or 'other'
        d['effect'] = eff
        out.append(d)
    return out


def saved_props(text):
    names = []
    for m in re.finditer(r'\[SavedProperty[^\]]*\]\s*(?:\[[^\]]*\]\s*)*(?:public|private|protected|internal)\s+[\w<>?\[\], ]+?\s(\w+)\s*\n?\s*\{', text):
        names.append(m.group(1))
    return names


def cmd_reqs(api_calls, src):
    reqs = []
    for call in api_calls:
        st = CMD.get(call)
        if not st:
            continue
        if st == {'ported'} or st <= {'ported', 'strip'} or st == {'strip'}:
            continue
        if 'ported' in st and 'missing' in st:
            if call == 'CreatureCmd.GainBlock' and re.search(r'GainBlock\([^,]+, [\w.]*DynamicVars\.Block\b', src):
                reqs.append('CreatureCmd.GainBlock(BlockVar) overload')
            if call == 'CreatureCmd.Damage' and re.search(r'CreatureCmd\.Damage\([^;]*?DynamicVars\.Damage\s*,', src):
                reqs.append('CreatureCmd.Damage(DamageVar) overload')
            continue
        if 'partial' in st:
            reqs.append(call + ' (partial in sim)')
        elif 'missing' in st:
            reqs.append(call)
    return reqs


def acquisition_relic(e):
    out = []
    ev = [x[7:] for x in e['referenced_by_models'] if x.startswith('events/')]
    rr = [x[7:] for x in e['referenced_by_models'] if x.startswith('relics/')]
    rar, pool = e['rarity'], e['pool']
    if e['starting_relic_of']:
        out.append('starter: ' + ', '.join(e['starting_relic_of']))
    if rar == 'Starter' and not e['starting_relic_of']:
        out.append('starter upgrade via ' + ', '.join(rr))
    if rar in ('Common', 'Uncommon', 'Rare', 'Shop') and pool not in ('EVENT',):
        src = 'player relic bag ({}; elite/event/relic rewards from the front, shops from the back)'.format(rar)
        out.append(src)
    if 'SHARED' in e['pools']:
        out.append('shared bag (treasure rooms; no rarity filter)')
    if 'SHARED' in e['pools'] and pool == 'EVENT':
        out.append('also listed in SharedRelicPool')
    if ev:
        out.append(('ancient: ' if rar == 'Ancient' else 'event: ') + ', '.join(sorted(set(ev))))
    if rr and rar != 'Starter':
        out.append('granted by relic: ' + ', '.join(rr))
    if rar == 'None' and pool == 'FALLBACK':
        out.append('fallback when bags are empty (RelicFactory.FallbackRelic)')
    return out


def tier_rows(entries, key):
    return Counter(e[key] for e in entries)


def build_relics():
    raw = json.load(open(os.path.join(INV, 'relics.json')))
    vd = json.load(open(common.work_path('relics-potions', 'vdiff_relics.json')))['entries']
    out_entries = []
    missing = []
    for e in raw['entries']:
        name = e['class']
        a = A.get(name)
        if a is None:
            missing.append(name)
            continue
        path, text = read_src(e['file'])
        cond = '\n'.join(condense.condense(path))
        ne = OrderedDict(e)
        combat_hooks = [h for h in e['abstract_model_hooks'] if HOOK.get(h, {}).get('scope') in ('combat', 'both')]
        run_hooks = [h for h in e['abstract_model_hooks'] if HOOK.get(h, {}).get('scope') in ('run', 'both')]
        run_hooks += [m for m in e['run_members'] if m not in run_hooks]
        for extra in ('SetupForPlayer',):
            if re.search(r'public (bool|void) ' + extra + r'\(', text):
                run_hooks.append(extra + ' (offer time)')
        ne['mechanics'] = split_items(a['m'], ';')
        ne['combat_behaviour'] = behaviour(split_items(a['c'], ' | '))
        ne['run_behaviour'] = behaviour(split_items(a['r'], ' | '), run=True)
        ne['combat_hooks'] = combat_hooks
        ne['run_layer_hooks'] = run_hooks
        ne['prompts'] = a['p']
        kreq = []
        if e['abstract_model_hooks'] or e['run_members']:
            if combat_hooks:
                kreq.append('RelicModel combat listener (after player powers; melted relics skipped, Combat/CombatState.cs:877)')
            if run_hooks:
                kreq.append('RelicModel run-scope listener (RunState.IterateHookListeners, Runs/RunState.cs:876)')
        for h in e['abstract_model_hooks']:
            hh = HOOK.get(h)
            if hh and not hh['in_sim']:
                kreq.append('Hook.{} ({})'.format(h, hh['scope']))
        if 'AfterObtained' in e['run_members'] or 'AfterRemoved' in e['run_members']:
            kreq.append('RelicCmd.Obtain pipeline (RelicModel.AfterObtained)')
        if 'participants' in text:
            kreq.append('side-turn hook participants (parameter dropped in P0)')
        if e['combat_room_entry_hooks']:
            kreq.append('combat-room entry phase: Hook.AfterRoomEntered after CombatManager.SetUpCombat, before StartCombatInternal (Rooms/CombatRoom.cs:417-420)')
        kreq += cmd_reqs(e['api_calls'], cond)
        for rx, label in AUTO_TOKENS:
            if re.search(rx, cond):
                kreq.append(label)
        for p in a['p']:
            if p['call'] not in kreq:
                kreq.append('prompt: ' + p['call'])
        kreq += split_items(a['k'], ';')
        ne['kernel_requirements'] = normalize_kreq(kreq, set(e['refs'].get('powers', [])) | {'PoisonPower', 'ConfusedPower', 'VigorPower', 'PlatingPower', 'BlurPower', 'FocusPower', 'DoomPower', 'SelfFormingClayPower'})
        applied = sorted(set(re.findall(r'PowerCmd\.Apply<(\w+)>', text)))
        ne['powers_applied'] = [{'power': p, 'ported': p in PORTED_POWERS} for p in applied]
        ne['powers_referenced'] = [{'power': p, 'ported': p in PORTED_POWERS} for p in e['refs'].get('powers', [])]
        ne['cards_referenced'] = [{'card': c, 'ported': c in PORTED_CARDS} for c in e['refs'].get('cards', [])]
        ne['rng_streams'] = split_items(a['g'], ';') if a['g'] else ([] if not e['rng_usage'] else list(e['rng_usage']))
        sp = saved_props(text)
        ne['saved_properties'] = sp
        st = a['s']
        kind = 'none'
        if st:
            low = st.lower()
            if 'savedproperty' not in low:
                low = low.replace('counter', 'count')
            if 'charges' in low or 'isusedup' in low:
                kind = 'charges'
            elif 'counter' in low or 'persists' in low:
                kind = 'persistent-counter'
            elif 'list' in low and 'savedproperty' in low:
                kind = 'persistent-list'
            elif ('modelid' in low or 'serializablecard' in low or 'set once' in low) and 'savedproperty' in low:
                kind = 'setup-data'
            elif 'bool' in low and 'savedproperty' in low:
                kind = 'persistent-flag'
            elif 'savedproperty' in low and 'cosmetic' not in low:
                kind = 'persistent-other'
            elif 'combat-scoped' in low or 'cleared' in low or 'reset at' in low:
                kind = 'combat'
            elif 'turn-scoped' in low:
                kind = 'turn'
            elif 'transient' in low:
                kind = 'transient'
            elif 'cosmetic' in low:
                kind = 'cosmetic'
            elif 'stackable' in low:
                kind = 'stack-count'
            else:
                kind = 'other'
        ne['state'] = {'description': st or None, 'kind': kind, 'shows_counter': bool(re.search(r'ShowCounter\s*=>\s*(true|[A-Z])|ShowCounter\s*\n\s*\{', text)),
                       'is_used_up_override': 'IsUsedUp' in e['hooks_overridden']}
        ne['ascension_sensitivity'] = a['a'] or 'none (no ascension reads in the file)'
        v = vd.get(name)
        if v is None:
            vdelta = {'status': 'unknown', 'note': 'no comparison data'}
        elif v['status'] == 'new':
            vdelta = {'status': 'new', 'note': 'not present in v0.107.1'}
        elif name in REL_DELTA:
            vdelta = {'status': 'rule-change', 'note': REL_DELTA[name]}
        elif v['status'] == 'same':
            vdelta = {'status': 'same', 'note': ''}
        else:
            vdelta = {'status': 'cosmetic', 'note': REL_COSMETIC_NOTES.get(name, 'decompiler style differences only (async state machine, explicit Enumerable calls, typed locals)')}
        ne['version_delta_v107'] = vdelta
        ne['tier'] = a['tier']
        ne['tier_combat'] = a['tier_combat']
        ne['run_complexity'] = a['run_kind']
        ne['port_batch'] = a['batch']
        ne['acquisition'] = acquisition_relic(e)
        nn = a['n'] or ''
        if a['tier_combat'] == 1:
            soft = [h for h in e['abstract_model_hooks'] if HOOK.get(h) and not HOOK[h]['in_sim'] and HOOK[h]['scope'] in ('combat', 'both')]
            if soft:
                extra_note = 'uses ' + ', '.join('Hook.' + h for h in soft) + ' only for display or redundant resets (not counted for tier)'
                nn = (nn + '; ' if nn else '') + extra_note
        ne['notes'] = nn
        ne['open_questions'] = split_items(a['q'], ';')
        out_entries.append(ne)
    if missing:
        raise SystemExit('missing relic annotations: ' + ', '.join(missing))
    extra = sorted(set(A) - {e['class'] for e in raw['entries']})
    if extra:
        raise SystemExit('annotations without manifest entry: ' + ', '.join(extra))
    return raw, out_entries


def build_potions():
    raw = json.load(open(os.path.join(INV, 'potions.json')))
    vd = json.load(open(common.work_path('relics-potions', 'vdiff_potions.json')))['entries']
    out_entries = []
    for e in raw['entries']:
        name = e['class']
        b = B.get(name)
        if b is None:
            raise SystemExit('missing potion annotation ' + name)
        path, text = read_src(e['file'])
        cond = '\n'.join(condense.condense(path))
        ne = OrderedDict(e)
        ne['mechanics'] = split_items(b['m'], ';')
        ne['combat_behaviour'] = behaviour(split_items(b['c'], ' | '))
        ne['run_behaviour'] = behaviour(split_items(b['r'], ' | '), run=True)
        ne['prompts'] = b['p']
        kreq = ['PotionModel runtime (slots, UsePotionAction CombatPlayPhaseOnly, OnUseWrapper: RemoveBeforeUse, BeforePotionUsed, OnUse, History.PotionUsed, AfterPotionUsed, CheckForEmptyHand; Models/PotionModel.cs:147-322)']
        if e['target_type'] in ('AnyEnemy',):
            kreq.append('potion enemy target in the decision API')
        if e['usable_out_of_combat']:
            kreq.append('out-of-combat potion use (run layer, Nodes.Potions/NPotionPopup.cs:285-345)')
        for h in e['abstract_model_hooks']:
            hh = HOOK.get(h)
            if hh and not hh['in_sim']:
                kreq.append('Hook.{} ({})'.format(h, hh['scope']))
        if e['abstract_model_hooks']:
            kreq.append('potion as hook listener (after relics)')
        kreq += cmd_reqs(e['api_calls'], cond)
        for rx, label in AUTO_TOKENS:
            if rx.startswith(r'PotionFactory') and name not in ('EntropicBrew',):
                continue
            if re.search(rx, cond):
                kreq.append(label)
        for p in b['p']:
            if p['call'] not in kreq:
                kreq.append('prompt: ' + p['call'])
        kreq += split_items(b['k'], ';')
        ne['kernel_requirements'] = normalize_kreq(kreq, set(e['refs'].get('powers', [])))
        applied = sorted(set(re.findall(r'PowerCmd\.Apply<(\w+)>', text)))
        ne['powers_applied'] = [{'power': p, 'ported': p in PORTED_POWERS} for p in applied]
        ne['powers_referenced'] = [{'power': p, 'ported': p in PORTED_POWERS} for p in e['refs'].get('powers', [])]
        ne['cards_referenced'] = [{'card': c, 'ported': c in PORTED_CARDS} for c in e['refs'].get('cards', [])]
        ne['rng_streams'] = split_items(b['g'], ';')
        ne['ascension_sensitivity'] = b['a'] or 'none (no ascension reads in the file)'
        v = vd.get(name)
        if v is None:
            vdelta = {'status': 'unknown', 'note': ''}
        elif v['status'] == 'new':
            vdelta = {'status': 'new', 'note': 'not present in v0.107.1'}
        elif v['status'] == 'same':
            vdelta = {'status': 'same', 'note': ''}
        else:
            diffs, det = v['detail']
            if 'props' in diffs and 'Self' in det.get('id_rem', []):
                note = POT_SELF_TO_ANYPLAYER
            elif name == 'MockDiscardAndAddShivsPotion':
                note = 'IsMock override added'
            elif name == 'FoulPotion':
                note = 'UI node API differences only'
            else:
                note = 'decompiler style differences only'
            vdelta = {'status': 'cosmetic', 'note': note}
        ne['version_delta_v107'] = vdelta
        ne['tier'] = b['tier']
        ne['port_batch'] = b['batch']
        acq = ['potion pool: ' + e['pool']]
        if e['pool'] not in ('EVENT', 'TOKEN', 'DEPRECATED', 'MOCK'):
            acq.append('random potions (rewards, shops, procurement effects) via PotionFactory over character + shared unlocked pools')
        if not e['can_be_generated_in_combat']:
            acq.append('excluded from in-combat generation (CanBeGeneratedInCombat=false)')
        for r in e['referenced_by_models']:
            acq.append('granted by ' + r)
        ne['acquisition'] = acq
        ne['notes'] = b['n'] or ''
        ne['open_questions'] = split_items(b['q'], ';')
        out_entries.append(ne)
    return raw, out_entries


RELIC_TIERS = {
    '1': 'combat behaviour through hooks and Cmd facades that exist in the sim (var overloads of ported methods count as existing); power content from the power wave is a dependency, not a tier raise',
    '2': 'needs hooks, Cmd facades or card/combat-state APIs that exist in the game but not in the sim (AfterCombatEnd/Victory, combat-room AfterRoomEntered, CardCmd.Upgrade, room type, gold in combat, ...)',
    '3': 'needs a player prompt or a new subsystem: orbs, Osty/pets, stars, forge, card generation, combat history, potion slots, extra turns, enchantments, or any run-layer behaviour (run layer = new subsystem)',
    '4': 'decompile loss or cross-cutting behaviour spanning several subsystems',
}
RELIC_BATCHES = {
    '1': 'combat, tier 1, only ported powers (Strength, Dexterity, Vulnerable, Weak, Thorns, Frail, Shrink) or none',
    '2': 'combat, tier 1, needs power/card content from earlier content batches (Vigor, Plating, Poison, Blur, Confused, Soul, ...)',
    '3': 'combat, tier 2 (missing hooks/facades: combat-end hooks, combat-room AfterRoomEntered, in-combat upgrade/keyword/clone, room type, gold)',
    '4': 'combat, tier 3 subsystems except character mechanics (card generation, combat history, auto-play, extra turns, potion slots, enchantment reads)',
    '5': 'character mechanics (orbs, Osty/pets, stars, forge); port with the character subsystem',
    '6': 'in-combat prompts (turn-setup prompt sites)',
    '7': 'run layer, simple effects (pickup, room entry, combat end): max HP, heal, gold, potion slots, fixed cards, curses, random upgrades/transforms/enchants',
    '8': 'run layer, modifiers and generated offers: rewards, card reward options, shop, rest site options, map, relic bags, reward screens',
    '9': 'run layer, deck prompts (remove/transform/upgrade/enchant/generic, choose card/bundle, simple grid)',
    '10': 'tier 4, cross-cutting, multiplayer-only or deprecated',
}
POTION_TIERS = RELIC_TIERS
POTION_BATCHES = {
    '1': 'OnUse through ported facades and ported powers',
    '2': 'OnUse needs power content from the power wave',
    '3': 'OnUse needs missing facades/card APIs (in-combat upgrade, pile moves, Shiv/Soul creation, cost randomization, replay count)',
    '4': 'subsystems: orbs, Osty, stars, forge, doom, auto-play, potion procurement, automatic use, card generation without prompt',
    '5': 'in-combat prompts',
    '6': 'cross-cutting (FoulPotion)',
}


def write(kind, raw, entries, tiers, batches):
    env = OrderedDict()
    env['schema'] = 'sts2sim.inventory.classified/v1'
    env['kind'] = kind
    env['game_version'] = raw['game_version']
    env['source_manifest'] = 'sim/specs/inventory/{}.json'.format(kind)
    env['source_manifest_digest'] = raw['source_digest']
    env['compared_tree'] = 'artifacts/knowledge/decompiled-v0.107.1 (v0.107.1)'
    env['classified_on'] = '2026-11-01'
    env['added_fields'] = ['mechanics', 'combat_behaviour', 'run_behaviour', 'combat_hooks', 'run_layer_hooks', 'prompts', 'kernel_requirements',
                           'powers_applied', 'powers_referenced', 'cards_referenced', 'rng_streams', 'saved_properties', 'state',
                           'ascension_sensitivity', 'version_delta_v107', 'tier', 'tier_combat', 'run_complexity', 'port_batch', 'acquisition',
                           'notes', 'open_questions'] if kind == 'relics' else [
                           'mechanics', 'combat_behaviour', 'run_behaviour', 'prompts', 'kernel_requirements', 'powers_applied',
                           'powers_referenced', 'cards_referenced', 'rng_streams', 'ascension_sensitivity', 'version_delta_v107', 'tier',
                           'port_batch', 'acquisition', 'notes', 'open_questions']
    env['tier_legend'] = tiers
    env['prompt_type_legend'] = {
        'HandSelect': 'CardSelectCmd.FromHand / FromHandForDiscard (in combat)',
        'PileSelect': 'CardSelectCmd.FromCombatPile (draw pile sorted by rarity then id, discard pile in order)',
        'ChooseCard': 'CardSelectCmd.FromChooseACardScreen (1 of n generated cards, optional skip)',
        'SimpleGrid': 'CardSelectCmd.FromSimpleGrid (in combat, generated cards)',
        'DeckRemove': 'CardSelectCmd.FromDeckForRemoval (run layer)',
        'DeckTransform': 'CardSelectCmd.FromDeckForTransformation (run layer)',
        'DeckUpgrade': 'CardSelectCmd.FromDeckForUpgrade (run layer)',
        'DeckEnchant': 'CardSelectCmd.FromDeckForEnchantment (run layer)',
        'DeckGeneric': 'CardSelectCmd.FromDeckGeneric (run layer)',
        'ChooseBundle': 'CardSelectCmd.FromChooseABundleScreen (run layer)',
        'SimpleGridRewards': 'CardSelectCmd.FromSimpleGridForRewards (run layer)',
        'RewardsScreen': 'RewardsCmd.OfferCustom / RewardsSet.Offer (reward screen decision)',
        'RestSiteOption': 'adds an option to the rest-site menu (TryModifyRestSiteOptions)',
        'CardRewardAlternative': 'adds an alternative to every card reward (TryModifyCardRewardAlternatives)',
    }
    env['run_phase_legend'] = ['pickup', 'offer', 'room_entry', 'combat_end', 'rewards', 'card_reward', 'shop', 'rest', 'map', 'treasure', 'deck_change', 'gold', 'potion', 'potion_use', 'other']
    env['port_batch_legend'] = batches
    env['count'] = len(entries)
    env['entries'] = entries
    p = common.stage1_path('{}.classified.json'.format(kind))
    with open(p, 'w', encoding='utf-8') as f:
        json.dump(env, f, indent=1, ensure_ascii=False)
        f.write('\n')
    return p


if __name__ == '__main__':
    raw, rel = build_relics()
    print(write('relics', raw, rel, RELIC_TIERS, RELIC_BATCHES))
    rawp, pot = build_potions()
    print(write('potions', rawp, pot, POTION_TIERS, POTION_BATCHES))
    print('relic tiers', Counter(e['tier'] for e in rel), 'batches', sorted(Counter(e['port_batch'] for e in rel).items()))
    print('potion tiers', Counter(e['tier'] for e in pot), 'batches', sorted(Counter(str(e['port_batch']) for e in pot).items()))
    print('relic deltas', Counter(e['version_delta_v107']['status'] for e in rel))
    print('potion deltas', Counter(e['version_delta_v107']['status'] for e in pot))
