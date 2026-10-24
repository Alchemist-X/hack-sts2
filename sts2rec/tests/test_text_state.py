"""Tests for scripts/bench/text_state.py (compact text encoding of bench observations)."""
import json
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
sys.path.insert(0, str(ROOT / 'scripts' / 'bench'))
import sts2_live as live  # noqa: E402
import text_state  # noqa: E402

FRICTIONS = Path(__file__).parent / 'fixtures' / 'live_frictions'
TEXT = Path(__file__).parent / 'fixtures' / 'text_state'


def observations():
    for path in sorted(FRICTIONS.glob('*.json')):
        yield path.stem, live.public_display(json.loads(path.read_text()))
    for path in sorted(TEXT.glob('*.json')):
        yield path.stem, json.loads(path.read_text())


ALL = list(observations())


def test_fixture_coverage():
    states = {obs['state']['state_type'] for _, obs in ALL}
    assert {'monster', 'elite', 'map', 'shop', 'rewards', 'card_reward', 'card_select', 'hand_select',
            'treasure', 'event', 'rest_site', 'game_over'} <= states | {'elite' if 'boss' in states else ''}


@pytest.mark.parametrize('name,obs', ALL, ids=[n for n, _ in ALL])
def test_encode_is_deterministic_and_lists_every_legal_action(name, obs):
    text = text_state.encode(obs)
    assert text == text_state.encode(json.loads(json.dumps(obs)))
    refs = text_state.legal_refs(obs)
    assert f'## Legal actions ({len(refs)})' in text
    lines = text.splitlines()
    for i, ref in enumerate(refs):
        assert f'{i}: {text_state.action_label(ref)}' in lines
    # no engine paths, raw icon file names or python reprs leak into the prompt
    assert '/workspace' not in text and '.png]' not in text and "{'" not in text


@pytest.mark.parametrize('name,obs', ALL, ids=[n for n, _ in ALL])
def test_encode_keeps_core_facts(name, obs):
    text = text_state.encode(obs)
    state = obs['state']
    player = state['player']
    assert f'HP {player["hp"]}/{player["max_hp"]}' in text
    assert f'Gold {player["gold"]}' in text
    for relic in player.get('relics') or []:
        assert relic['name'] in text
    for potion in player.get('potions') or []:
        assert f'slot{potion["slot"]} {potion["name"]}' in text
    for card in player.get('hand') or []:
        assert card['name'] in text
    for enemy in (state.get('battle') or {}).get('enemies') or []:
        assert f'{enemy["name"]}@{enemy["combat_id"]} HP {enemy["hp"]}/{enemy["max_hp"]}' in text
    for power in player.get('status') or []:
        assert f'{power["name"]} {power["amount"]}' in text


def test_combat_details():
    obs = dict(ALL)['combat_three_inklets']
    text = text_state.encode(obs)
    assert 'Energy 3/3' in text and 'Powers: Thorns 3' in text
    assert 'H3 Twin Strike+ [1] Attack/->AnyEnemy: Deal 7 damage twice.' in text
    assert 'Inklet@2 HP 11/11 Block 0 | intent: Attack 1x3=3 | Slippery 1, Weak 1' in text
    assert 'Slippery (Buff): The next time Inklet loses HP' in text  # glossary, once
    assert text.count('Slippery (Buff):') == 1
    assert 'Draw (12)' in text


def test_piles_grouped_and_engine_preview():
    text = text_state.encode(dict(ALL)['combat_engine_preview'])
    draw = next(line for line in text.splitlines() if line.startswith('Draw ('))
    assert ' x' in draw  # duplicates grouped as "Strike x3"
    assert '(engine: dmg @' in text
    assert 'rule_preview' not in text


def test_map_shop_select_blocks():
    named = dict(ALL)
    m = text_state.encode(named['map'])
    assert 'Next: 1,1 Monster -> 0,2M 2,2M' in m and 'r16: 3B>' in m and 'Ceremonial Beast' in m
    shop = text_state.encode(named['shop'])
    assert '- card 38g SALE Hemokinesis [1] Attack/Uncommon: Lose 2 HP. Deal 15 damage.' in shop
    assert 'card_removal 75g remove one card from your deck' in shop
    assert '[E]' in shop and 'can_afford' not in shop
    up = text_state.encode(named['card_select_upgrade'])
    assert 'min_select=4' in up and 'Multi-select: answer with a list of 4 numbers' in up
    assert text_state.selection_size(named['card_select_upgrade']) == (4, 4)
    assert text_state.selection_size(named['combat']) is None


def test_unknown_fields_are_not_dropped():
    obs = json.loads(json.dumps(dict(ALL)['rewards']))
    obs['state']['rewards']['items'][0]['new_field'] = 7
    obs['state']['mystery_block'] = {'x': 1}
    text = text_state.encode(obs)
    assert '"new_field":7' in text and 'Other state: {"mystery_block":{"x":1}}' in text


def test_wiki_facts_optional():
    obs = dict(dict(ALL)['combat'])
    obs['wiki_facts'] = 'Twig Slime: splits.'
    assert 'Twig Slime: splits.' not in text_state.encode(obs)
    assert '## Wiki facts' in text_state.encode(obs, facts=True)


def test_measure_reports_savings(tmp_path):
    path = tmp_path / 'obs.json'
    path.write_text(json.dumps(dict(ALL)['combat_three_inklets']))
    summary = text_state.measure([path])
    assert summary['decisions'] == 1
    assert summary['all']['text_tokens_mean'] < summary['all']['json_tokens_mean']


def test_text_fixtures_have_no_seeds_or_paths():
    for path in TEXT.glob('*.json'):
        raw = path.read_text()
        assert '"seed"' not in raw and '/workspace' not in raw and '/home/' not in raw and 'BENCH' not in raw
