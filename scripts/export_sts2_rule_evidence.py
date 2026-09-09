#!/usr/bin/env python3
"""Add missing rule models and reviewed cross-model checks from the local game.

No network, active save, RNG, or future state is read. Re-run after exporting a
versioned source corpus; the generated evidence is tied to its assembly hash.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re

from export_sts2_knowledge import pascal_to_model_id

ROOT = Path(__file__).resolve().parents[1]
CORE = "MegaCrit/sts2/Core/"

# These summaries are reviewed interpretations. Required fragments make a
# changed rule fail export, rather than silently carrying an old interpretation.
CHECKS = [
    ("DAMAGE_ORDER", [],
     "伤害变量：附魔加法→附魔倍率→全局加法（力量、污染等）→全局倍率→最低上限。保留小数到最终预览取整，不能对已取整卡面再套倍率。MCP 的 engine_ui_hooks 数值仅为当前状态、单次伤害变量，不包含打出过程、触发条件、多段期间的状态变化或完整牌序。",
     [("Hooks/Hook.cs", ["num += cardSource.Enchantment.EnchantDamageAdditive", "num *= cardSource.Enchantment.EnchantDamageMultiplicative", "ModifyDamageInternal", "ModifyDamageAdditive", "ModifyDamageMultiplicative"]),
      ("MonsterMoves/Intents/AttackIntent.cs", ["return Math.Max(0, (int)num);"])]),
    ("DRAW_AND_SHUFFLE", [],
     "抽牌逐张执行：每次抽取前，抽牌堆为空才把当前弃牌堆洗入；每张入手后先执行抽牌触发，再继续下一张，手牌上限、禁止抽牌或战斗结束可提前停止。正在结算的牌不自动属于弃牌堆；先检查牌所在区域。只使用公开的无序剩余集合与牌效明确放到顶部的已知牌，不读取内部牌序。",
     [("Commands/CardPileCmd.cs", ["for (int i = 0; i < drawsRequested; i++)", "await ShuffleIfNecessary(choiceContext, player);", "await Hook.AfterCardDrawn", "if (!pile.Cards.Any() && pile2.Cards.Any())"])]),
    ("TAINTED_PER_HIT", ["POWER.TAINTED_POWER"],
     "污染在每次符合 PoweredAttack 的伤害加法钩子中增加受击目标的 Amount；不是每回合只扣一次。它在敌方回合结束时移除。多段攻击应按每段受当前污染修正计算，再处理格挡。优先读取引擎意图数值，不能把污染重复加一遍。",
     [("Models/Powers/TaintedPower.cs", ["target != base.Owner", "!props.IsPoweredAttack()", "return base.Amount;", "side == CombatSide.Enemy"])]),
    ("INSTINCT_BEFORE_STRENGTH", ["ENCHANTMENT.INSTINCT"],
     "本能对符合 PoweredAttack 的基础伤害施加 ×2，发生在力量等全局加法之前；有力量时不是把整张已显示伤害再翻倍。例：基础6、力量3且无其他修正→6×2+3=15。",
     [("Models/Enchantments/Instinct.cs", ["EnchantDamageMultiplicative", "props.IsPoweredAttack()", "2m"]),
      ("Hooks/Hook.cs", ["cardSource.Enchantment.EnchantDamageMultiplicative", "ModifyDamageInternal"])]),
    ("PHROG_PARASITE_CYCLE", ["MONSTER.PHROG_PARASITE", "POWER.INFESTED_POWER"],
     "异蛙寄生虫从 INFECT_MOVE 开始，随后与 LASH_MOVE 交替；源码里未接入初始可达链的 RAND 节点不代表实际随机。A10 的 Lash 基础5×4；HP区间66–68。Infested 死亡触发生成4只 Wriggler，并令它们以眩晕状态开始；死亡不等于战斗立即结束。各类死亡发生时点仍应核对新怪当前意图。",
     [("Models/Monsters/PhrogParasite.cs", ["INFECT_MOVE", "LASH_MOVE", "FollowUpState", "return new MonsterMoveStateMachine"]),
      ("Models/Powers/InfestedPower.cs", ["StartStunned = true", "ShouldStopCombatFromEnding"])]),
    ("AEONGLASS_CYCLE", ["MONSTER.AEONGLASS", "POWER.WITHERING_PRESENCE_POWER", "CARD.WITHER"],
     "沙漏从 EBB_MOVE→EYE_LASERS_MOVE→INCREASING_INTENSITY_MOVE 循环。A10 起始基础攻击32与12×2（后续力量会改变），EBB另获33格挡。开场3人工制品；每打6张牌向手牌加入一张 Wither。强化回合升级现存 Wither，A10向弃牌堆加入2张，并增加力量；新生成 Wither 也匹配已累计的升级次数。规划时检查公开计数、当前Wither文本和下一次洗牌，不能只看本回合面板伤害。",
     [("Models/Monsters/Aeonglass.cs", ["EyeLasersRepeat => 2", "EbbBlock => 33", "wither.FakeUpgrade();", "WitherUpgradeCount++;", "AdditionalStrength++;"]),
      ("Models/Powers/WitheringPresencePower.cs", ["CardsLeft", "PileType.Hand, 1", "= 6m;"])]),
]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def export(decompiled, output):
    manifest = json.loads((output / 'manifest.json').read_text())
    if manifest['game_version'].lstrip('v') != '0.107.1':
        raise ValueError('Reviewed rule checks require v0.107.1; review before exporting another version')
    entities = []
    for kind, folder, prefix in [('powers', 'Powers', 'POWER'), ('enchantments', 'Enchantments', 'ENCHANTMENT'), ('afflictions', 'Afflictions', 'AFFLICTION')]:
        for path in sorted((decompiled / CORE / 'Models' / folder).glob('*.cs')):
            code = path.read_text()
            if not re.search(r'public (?:sealed )?class ' + path.stem + r'\s*:', code):
                continue
            identity = pascal_to_model_id(path.stem)
            entities.append({'kind': kind, 'id': identity, 'model_id': prefix + '.' + identity,
                'game_version': manifest['game_version'], 'source': {
                    'decompiled_path': str(path.relative_to(decompiled)), 'sha256': sha(path),
                    'rule_source': code,
                    'related_types': sorted(set(re.findall(r'\b(?:ModelDb\.(?:Card|Power|Monster|Enchantment|Affliction)<)(\w+)', code)))}})
    rules = []
    for identity, models, summary, sources in CHECKS:
        refs = []
        for relative, fragments in sources:
            path = decompiled / CORE / relative
            code = path.read_text()
            for fragment in fragments:
                if fragment not in code:
                    raise ValueError(f'{identity}: missing required source fragment in {relative}: {fragment}')
            refs.append({'decompiled_path': CORE + relative, 'sha256': sha(path),
                         'line': next(i for i, line in enumerate(code.splitlines(), 1) if fragments[0] in line),
                         'required_fragments': fragments})
        rules.append({'id': identity, 'related_models': models, 'summary': summary,
                      'evidence': 'reviewed_interpretation_of_versioned_game_source', 'sources': refs})
    result = {'schema_version': 1, 'game_version': manifest['game_version'],
              'assembly_sha256': manifest['sources']['assembly']['sha256'],
              'decompiled_root': os.path.relpath(decompiled.resolve(), output.resolve()), 'entities': entities, 'checks': rules}
    target = output / 'rule-evidence.json'
    target.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps({'path': str(target), 'entities': len(entities), 'reviewed_checks': len(rules)}))


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--decompiled-root', type=Path, default=ROOT/'artifacts/knowledge/decompiled-v0.107.1')
    p.add_argument('--output', type=Path, default=ROOT/'knowledge/source/v0.107.1')
    args = p.parse_args()
    export(args.decompiled_root, args.output)
