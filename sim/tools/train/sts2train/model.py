"""Entity-encoder actor-critic over the sts2vec observation tables.

Per table: id embeddings (sizes from the schema vocab) + numeric columns (symlog) -> a small MLP per row -> masked
mean pooling. A global head mixes the pooled tables into the state embedding. Each candidate row (kind, A, B and the
pointers hand_slot / target_slot / potion_slot / option_index / card_id) gathers the row embeddings it points at and is
scored against the state embedding; logits over cand_n with masking. Value head on the state embedding.

Size knobs: ``d`` (d_model: width of every row encoder, the trunk and the candidate scorer), ``s`` (state width),
``depth`` / ``heads`` / ``ff_mult`` (trunk). ``depth = 0`` is the original smoke model (the 17 pooled tables are
concatenated into the state MLP; 948,750 parameters at d = 96, s = 256). ``depth > 0`` adds a pre-LayerNorm transformer
trunk over 17 table tokens (the pooled tables, typed) plus one token per hand card, enemy and potion row (typed and
slot-indexed, padding masked): the state is read from the global token and the mean of all live tokens, and the
contextualised hand / enemy / potion rows feed the candidate pointers. ``PRESETS`` names the standard sizes.
"""

from __future__ import annotations

import math

import torch
import torch.nn as nn
import torch.nn.functional as F
from torch.utils.checkpoint import checkpoint

from .features import ROW_TRUNCATED, USED_BUFFERS, ModelSpec

NEG = -1e9
HASH_BUCKETS = 1021
N_TABLES = 17

# Architecture presets (d_model, state width, trunk depth / heads / FFN multiplier) -> parameter count on the
# fc2eb34c07bc8513 schema: smoke 948,750 (the 2026-11-02 smoke model), small 1,175,150, large 15,804,270.
PRESETS = {
    "smoke": {"d": 96, "s": 256, "depth": 0, "heads": 4, "ff_mult": 4},
    "small": {"d": 128, "s": 256, "depth": 2, "heads": 4, "ff_mult": 4},
    "large": {"d": 384, "s": 768, "depth": 6, "heads": 8, "ff_mult": 4},
}
ARCH_KEYS = ("d", "s", "depth", "heads", "ff_mult")
ARCH_DEFAULTS = PRESETS["smoke"]


def symlog(x: torch.Tensor) -> torch.Tensor:
    return torch.sign(x) * torch.log1p(torch.abs(x))


def masked_mean(x: torch.Tensor, mask: torch.Tensor) -> torch.Tensor:
    """x [B, R, D], mask [B, R] bool -> [B, D] (zeros where no row is present)."""
    m = mask.unsqueeze(-1).to(x.dtype)
    return (x * m).sum(1) / m.sum(1).clamp_min(1.0)


def gather_rows(table: torch.Tensor, idx: torch.Tensor) -> torch.Tensor:
    """table [B, R, D], idx [B, C] (already clamped to 0..R-1) -> [B, C, D]."""
    return torch.gather(table, 1, idx.long().unsqueeze(-1).expand(-1, -1, table.shape[-1]))


def bits(x: torch.Tensor, n: int) -> torch.Tensor:
    """Low n bits of an int tensor as floats, appended as a last axis."""
    shifts = torch.arange(n, device=x.device)
    return ((x.unsqueeze(-1) >> shifts) & 1).to(torch.float32)


class RowMLP(nn.Module):
    def __init__(self, d_in: int, d: int):
        super().__init__()
        self.l1 = nn.Linear(d_in, d)
        self.l2 = nn.Linear(d, d)

    def forward(self, x):
        return F.relu(self.l2(F.relu(self.l1(x))))


class RowLin(nn.Module):
    """One linear layer + ReLU (for the tables with many rows: map nodes, prompt options)."""

    def __init__(self, d_in: int, d: int):
        super().__init__()
        self.l1 = nn.Linear(d_in, d)

    def forward(self, x):
        return F.relu(self.l1(x))


class Block(nn.Module):
    """Pre-LayerNorm transformer block (multi-head self-attention with a key padding mask + GELU MLP), no dropout.

    Written on ``F.scaled_dot_product_attention`` rather than ``nn.TransformerEncoderLayer`` so training and
    inference run the same kernel path (the stored rollout log-probs must reproduce in the update)."""

    def __init__(self, d: int, heads: int, ff_mult: int):
        super().__init__()
        if d % heads:
            raise ValueError(f"d_model {d} is not divisible by heads {heads}")
        self.h = heads
        self.ln1 = nn.LayerNorm(d)
        self.qkv = nn.Linear(d, 3 * d)
        self.proj = nn.Linear(d, d)
        self.ln2 = nn.LayerNorm(d)
        self.ff1 = nn.Linear(d, ff_mult * d)
        self.ff2 = nn.Linear(ff_mult * d, d)

    def forward(self, x: torch.Tensor, keep: torch.Tensor) -> torch.Tensor:
        """x [B, T, d], keep [B, T] bool (True = a real token; every row must keep at least one)."""
        B, T, d = x.shape
        q, k, v = self.qkv(self.ln1(x)).view(B, T, 3, self.h, d // self.h).permute(2, 0, 3, 1, 4)
        a = F.scaled_dot_product_attention(q, k, v, attn_mask=keep[:, None, None, :])
        x = x + self.proj(a.transpose(1, 2).reshape(B, T, d))
        return x + self.ff2(F.gelu(self.ff1(self.ln2(x))))


class Policy(nn.Module):
    def __init__(self, spec: ModelSpec, d: int = 96, s: int = 256, depth: int = 0, heads: int = 4, ff_mult: int = 4):
        super().__init__()
        self.spec = spec
        self.d, self.s = d, s
        self.depth, self.heads, self.ff_mult = depth, heads, ff_mult
        self.grad_ckpt = False   # runtime switch (not architecture): recompute trunk blocks in backward
        V, E = spec.vocab, spec.enums
        self.card_emb = nn.Embedding(V["cards"], 48, padding_idx=0)
        self.relic_emb = nn.Embedding(V["relics"], 32, padding_idx=0)
        self.potion_emb = nn.Embedding(V["potions"], 32, padding_idx=0)
        self.power_emb = nn.Embedding(V["powers"], 32, padding_idx=0)
        self.monster_emb = nn.Embedding(V["monsters"], 32, padding_idx=0)
        self.encounter_emb = nn.Embedding(V["encounters"], 16, padding_idx=0)
        self.event_emb = nn.Embedding(V["events"], 16, padding_idx=0)
        self.orb_emb = nn.Embedding(V["orbs"], 8, padding_idx=0)
        self.enchant_emb = nn.Embedding(V["enchantments"], 8, padding_idx=0)
        self.affliction_emb = nn.Embedding(V["afflictions"], 8, padding_idx=0)
        self.act_emb = nn.Embedding(V["acts"], 4)
        self.char_emb = nn.Embedding(V["characters"], 8)
        self.var_emb = nn.Embedding(spec.card_vars, 8, padding_idx=0)
        self.hash_emb = nn.Embedding(HASH_BUCKETS, 32)
        self.item_emb = nn.Embedding(max(V["cards"], V["relics"], V["potions"]), 32, padding_idx=0)
        ee = lambda name, dim: nn.Embedding(max(E[name], 1), dim)  # noqa: E731
        self.phase_emb = ee("RunPhase", 8)
        self.kind_emb = ee("RunActionKind", 16)
        self.intent_emb = ee("IntentType", 8)
        self.point_emb = ee("MapPointType", 8)
        self.reward_type_emb = ee("RewardType", 8)
        self.prompt_kind_emb = ee("ChoicePromptKind", 8)
        self.ctype_emb = ee("CardType", 8)
        self.target_emb = ee("TargetType", 8)
        self.rarity_emb = ee("CardRarity", 8)
        self.rest_emb = ee("RestOption", 16)
        self.alt_emb = ee("CardRewardAlt", 16)
        self.shop_cat_emb = ee("ShopCategory", 8)
        self.pile_emb = ee("PileType", 4)
        self.relic_status_emb = ee("RelicStatus", 4)
        self.pitem_emb = ee("PromptItemKind", 8)
        self.row_emb = nn.Embedding(spec.shapes["map_nodes"][0], 8)
        self.col_emb = nn.Embedding(spec.shapes["map_nodes"][1], 4)

        C = spec.cols
        gi = C["global_i"]
        self._gi_id_cols = {k: gi[k] for k in ("phase", "decision_kind", "character", "act_id", "boss_id",
                                               "second_boss_id", "prompt_kind", "event_id", "prompt_pile",
                                               "treasure_relic_id")}
        skip = set(self._gi_id_cols.values()) | {gi["prompt_site_hash"]}
        self.register_buffer("gi_num_idx", torch.tensor([i for i in range(spec.shapes["global_i"][0]) if i not in skip]),
                             persistent=False)
        n_gnum = len(self.gi_num_idx) + spec.shapes["global_f"][0]
        self.global_mlp = RowMLP(n_gnum + 8 + 8 + 8 + 4 + 16 + 16 + 8 + 16 + 4 + 32, d)

        hand_in = 48 + 8 * 3 + 8 + 8 + 4 * 8 + 4 + 6 + 8 + 8 + spec.shapes["hand_f"][1] + spec.shapes["hand_dmg"][1]
        self.card_mlp = RowMLP(hand_in, d)
        self.deck_proj = nn.Linear(48 + 8 + 8 + 2, d)
        self.pile_proj = nn.Linear(3 * (48 + 2), d)
        self.relic_mlp = RowMLP(32 + 4 + 3, d)
        self.potion_mlp = RowMLP(32 + 8 + 1, d)
        self.power_mlp = RowMLP(32 + 2, d)
        self.orb_mlp = RowMLP(8 + 2, d)
        ni = spec.shapes["enemy_intents"][1]
        self.epower_proj = nn.Linear(32 + 1, 32)
        self.enemy_mlp = RowMLP(32 + 8 + ni * (8 + 3) + 32, d)
        self.ally_mlp = RowMLP(32 + 5, d)
        self.node_mlp = RowLin(8 + 4 + 7 + 8 + 4, 32)
        self.map_proj = nn.Linear(32, d)
        self.reward_mlp = RowMLP(8 + 32 + 2, d)
        self.alt_proj = nn.Linear(16, d)
        self.shop_mlp = RowMLP(8 + 32 + 5, d)
        self.rest_mlp = RowMLP(16 + 1, d)
        self.event_mlp = RowMLP(32 + 32 + 4 + 1, d)
        self.card32 = nn.Linear(48, 32, bias=False)
        self.prompt_mlp = RowLin(32 + 8 + 3, d)

        if depth > 0:
            self.tok_type = nn.Parameter(torch.randn(N_TABLES + 3, d) * 0.02)   # 17 tables + hand/enemy/potion rows
            self.hand_pos = nn.Embedding(spec.shapes["hand"][0], d)
            self.enemy_pos = nn.Embedding(spec.shapes["enemies"][0], d)
            self.potion_pos = nn.Embedding(spec.shapes["potions"][0], d)
            self.blocks = nn.ModuleList(Block(d, heads, ff_mult) for _ in range(depth))
            self.trunk_ln = nn.LayerNorm(d)
            self.state_mlp = nn.Sequential(nn.Linear(2 * d, s), nn.ReLU(), nn.Linear(s, s), nn.ReLU())
        else:
            self.state_mlp = nn.Sequential(nn.Linear(N_TABLES * d, s), nn.ReLU(), nn.Linear(s, s), nn.ReLU())
        self.value_head = nn.Sequential(nn.Linear(s, s // 2), nn.ReLU(), nn.Linear(s // 2, 1))
        # Candidate encoder, first layer factored per source (projected before the gather, summed after):
        # h1 = relu(Wk kind + Wn num + Wh hand[hs] + Wt enemy[ts] + Wp potion[ps] + Wo bank[oi] + Wc card + Ws state)
        self.c_kind = nn.Embedding(max(E["RunActionKind"], 1), d)
        self.c_num = nn.Linear(6, d)
        self.c_hand = nn.Linear(d, d, bias=False)
        self.c_tgt = nn.Linear(d, d, bias=False)
        self.c_pot = nn.Linear(d, d, bias=False)
        self.c_opt = nn.Linear(d, d, bias=False)
        self.c_card = nn.Linear(48, d, bias=False)
        self.c_state = nn.Linear(s, d, bias=False)
        self.c_l2 = nn.Linear(d, d)
        self.q_state = nn.Linear(s, d)
        self.c_bias = nn.Linear(d, 1)

        # option_index wiring: RunActionKind name -> option bank (see _option_bank).
        kinds = spec.enum_names["RunActionKind"]
        self.map_ranks = 8
        sizes = {"map": self.map_ranks, "rewards": spec.shapes["rewards"][0], "card_reward": spec.shapes["card_reward"][0],
                 "alts": spec.shapes["card_reward_alts"][0], "shop": spec.shapes["shop"][0],
                 "rest": spec.shapes["rest"][0], "events": spec.shapes["event_options"][0]}
        self._bank_order = ["map", "rewards", "card_reward", "alts", "shop", "rest", "events"]
        offsets, off = {}, 1
        for b in self._bank_order:
            offsets[b] = off
            off += sizes[b]
        self._prompt_offset = off
        kind_bank = {"Travel": "map", "ClaimReward": "rewards", "PickRewardCard": "card_reward",
                     "RewardAlternative": "alts", "BuyEntry": "shop", "ChooseRestOption": "rest",
                     "ChooseEventOption": "events", "PromptSelect": "prompt"}
        off_t = torch.zeros(len(kinds), dtype=torch.long)
        size_t = torch.zeros(len(kinds), dtype=torch.long)
        is_prompt = torch.zeros(len(kinds), dtype=torch.bool)
        for i, k in enumerate(kinds):
            b = kind_bank.get(k)
            if b == "prompt":
                off_t[i] = self._prompt_offset
                is_prompt[i] = True
            elif b is not None:
                off_t[i], size_t[i] = offsets[b], sizes[b]
        self.register_buffer("kind_off", off_t, persistent=False)
        self.register_buffer("kind_size", size_t, persistent=False)
        self.register_buffer("kind_prompt", is_prompt, persistent=False)
        pik = spec.enum_names["PromptItemKind"]
        self._pik_card, self._pik_relic, self._pik_potion = pik.index("card"), pik.index("relic"), pik.index("potion")

    # ------------------------------------------------------------------------------------------------ tables

    def _cards(self, rows: torch.Tensor, extra_f: torch.Tensor | None) -> torch.Tensor:
        """hand / card_reward rows [B, R, 22] (+ hand_f/hand_dmg [B, R, 8]) -> [B, R, d]."""
        c = self.spec.cols["hand"]
        g = lambda k: rows[..., c[k]]  # noqa: E731
        nvar = 4
        var_ids = torch.stack([g(f"var_id_{k}") for k in range(nvar)], -1).clamp(0, self.spec.card_vars - 1)
        var_vals = torch.stack([g(f"var_val_{k}") for k in range(nvar)], -1).float()
        num = torch.stack([g("upgrade"), g("cost_current"), g("cost_base"), g("enchant_amount"), g("playable"),
                           g("hits")], -1).float()
        parts = [self.card_emb(g("card_id")), self.ctype_emb(g("type").clamp_min(0)),
                 self.target_emb(g("target").clamp_min(0)), self.rarity_emb(g("rarity").clamp_min(0)),
                 self.enchant_emb(g("enchant_id")), self.affliction_emb(g("affliction_id")),
                 self.var_emb(var_ids).flatten(-2), symlog(var_vals), symlog(num),
                 bits(g("keyword_mask"), 8), bits(g("playable_reason_id"), 8)]
        if extra_f is None:
            extra_f = rows.new_zeros(*rows.shape[:-1], self.spec.shapes["hand_f"][1] + self.spec.shapes["hand_dmg"][1],
                                     dtype=torch.float32)
        parts.append(symlog(extra_f))
        return self.card_mlp(torch.cat(parts, -1))

    def _piles(self, x) -> torch.Tensor:
        W = self.card_emb.weight
        out = []
        for name in ("draw_counts", "discard_counts", "exhaust_counts"):
            cnt = x[name].float().view(x[name].shape[0], -1, 2)
            tot = cnt.sum((1, 2))
            bag = cnt.sum(-1) @ W / tot.clamp_min(1.0).unsqueeze(-1)
            up = cnt[..., 1].sum(1) / tot.clamp_min(1.0)
            out += [bag, symlog(tot).unsqueeze(-1), up.unsqueeze(-1)]
        return self.pile_proj(torch.cat(out, -1))

    def encode(self, x: dict[str, torch.Tensor]):
        """-> state [B, s], value [B], candidate logits [B, C] (masked), entity tables for pointer gathers."""
        C = self.spec.cols
        gi = x["global_i"]
        B = gi.shape[0]
        gc = self._gi_id_cols
        gnum = torch.cat([symlog(gi[:, self.gi_num_idx].float()), x["global_f"].float()], -1)
        glob = self.global_mlp(torch.cat([
            gnum, self.phase_emb(gi[:, gc["phase"]].clamp_min(0)), self.phase_emb(gi[:, gc["decision_kind"]].clamp_min(0)),
            self.char_emb(gi[:, gc["character"]]), self.act_emb(gi[:, gc["act_id"]]),
            self.encounter_emb(gi[:, gc["boss_id"]]), self.encounter_emb(gi[:, gc["second_boss_id"]]),
            self.prompt_kind_emb(gi[:, gc["prompt_kind"]].clamp_min(0)), self.event_emb(gi[:, gc["event_id"]]),
            self.pile_emb(gi[:, gc["prompt_pile"]].clamp_min(0)), self.relic_emb(gi[:, gc["treasure_relic_id"]]),
        ], -1))

        # hand (+ effective numbers)
        hand = x["hand"]
        hand_e = self._cards(hand, torch.cat([x["hand_f"], x["hand_dmg"]], -1))
        hand_m = hand[..., C["hand"]["card_id"]] != 0

        # deck: bag of card embeddings
        dk, dc = x["deck"], C["deck"]
        dm = dk[..., dc["card_id"]] != 0
        drow = torch.cat([self.card_emb(dk[..., dc["card_id"]]), self.enchant_emb(dk[..., dc["enchant_id"]]),
                          self.affliction_emb(dk[..., dc["affliction_id"]]),
                          symlog(dk[..., dc["upgrade"]].float()).unsqueeze(-1),
                          symlog(dk[..., dc["enchant_amount"]].float()).unsqueeze(-1)], -1)
        deck = F.relu(self.deck_proj(masked_mean(drow, dm)))
        piles = F.relu(self._piles(x))

        rl, rc = x["relics"], C["relics"]
        rel_m = rl[..., rc["relic_id"]] != 0
        rel = masked_mean(self.relic_mlp(torch.cat([
            self.relic_emb(rl[..., rc["relic_id"]]), self.relic_status_emb(rl[..., rc["status"]].clamp(0, self.spec.enums["RelicStatus"] - 1)),
            symlog(rl[..., [rc["counter"], rc["used_up"], rc["stack"]]].float())], -1)), rel_m)

        pt, pc = x["potions"], C["potions"]
        pot_m = pt[..., pc["potion_id"]] != 0
        pot_e = self.potion_mlp(torch.cat([self.potion_emb(pt[..., pc["potion_id"]]),
                                           self.target_emb(pt[..., pc["target_type"]].clamp_min(0)),
                                           pt[..., pc["usable_now"]].float().unsqueeze(-1)], -1))
        pot = masked_mean(pot_e, pot_m)

        pw, wc = x["powers_player"], C["powers_player"]
        pow_ = masked_mean(self.power_mlp(torch.cat([self.power_emb(pw[..., wc["power_id"]]),
                                                     symlog(pw[..., wc["amount"]].float()).unsqueeze(-1),
                                                     pw[..., wc["is_debuff"]].float().unsqueeze(-1)], -1)),
                           pw[..., wc["power_id"]] != 0)

        ob, oc = x["orbs"], C["orbs"]
        orbs = masked_mean(self.orb_mlp(torch.cat([self.orb_emb(ob[..., oc["orb_id"]]),
                                                   symlog(ob[..., [oc["passive"], oc["evoke"]]].float())], -1)),
                           ob[..., oc["orb_id"]] != 0)

        en, ec = x["enemies"], C["enemies"]
        en_m = en[..., ec["present"]] != 0
        it, ic = x["enemy_intents"], C["enemy_intents"]
        n_int = it.shape[2]
        imask = (torch.arange(n_int, device=it.device).view(1, 1, -1) < en[..., ec["intent_n"]].unsqueeze(-1)).float()
        intents = torch.cat([self.intent_emb(it[..., ic["type"]].clamp_min(0)),
                             symlog(it[..., [ic["damage"], ic["times"], ic["card_count"]]].float())], -1)
        intents = (intents * imask.unsqueeze(-1)).flatten(-2)
        ep, epc = x["enemy_powers"], C["enemy_powers"]
        epw = F.relu(self.epower_proj(torch.cat([self.power_emb(ep[..., epc["power_id"]]),
                                                 symlog(ep[..., epc["amount"]].float()).unsqueeze(-1)], -1)))
        epm = (ep[..., epc["power_id"]] != 0).unsqueeze(-1).float()
        epw = (epw * epm).sum(2) / epm.sum(2).clamp_min(1.0)
        hpv = en[..., [ec["hp"], ec["max_hp"], ec["block"], ec["intent_n"]]].float()
        frac = (hpv[..., 0] / hpv[..., 1].clamp_min(1.0)).unsqueeze(-1)
        en_e = self.enemy_mlp(torch.cat([self.monster_emb(en[..., ec["monster_id"]]), symlog(hpv), frac,
                                         en[..., [ec["present"], ec["flank"], ec["behind"]]].float(), intents, epw], -1))
        enemies = masked_mean(en_e, en_m)

        al, ac = x["allies"], C["allies"]
        allies = masked_mean(self.ally_mlp(torch.cat([self.monster_emb(al[..., ac["monster_id"]]),
                                                      symlog(al[..., [ac["present"], ac["hp"], ac["max_hp"], ac["block"]]].float()),
                                                      (al[..., ac["hp"]].float() / al[..., ac["max_hp"]].float().clamp_min(1.0)).unsqueeze(-1)], -1)),
                             al[..., ac["present"]] != 0)

        mp, mc = x["map_nodes"], C["map_nodes"]
        R, Cc = mp.shape[1], mp.shape[2]
        rows = self.row_emb.weight.view(1, R, 1, -1).expand(B, R, Cc, -1)
        cols = self.col_emb.weight.view(1, 1, Cc, -1).expand(B, R, Cc, -1)
        node_e = self.node_mlp(torch.cat([
            self.point_emb(mp[..., mc["point_type"]].clamp_min(0)),
            mp[..., [mc["visited"], mc["current"], mc["legal_next"]]].float(),
            symlog(mp[..., mc["quest"]].float()).unsqueeze(-1),
            bits(mp[..., mc["child_mask"]], 7), rows, cols], -1))           # [B, R, Cc, d]
        node_m = mp[..., mc["point_type"]] != 0
        map_e = F.relu(self.map_proj(masked_mean(node_e.flatten(1, 2), node_m.flatten(1, 2))))
        # Travel A = rank of the destination among legal points sorted by (col, row): column-major order.
        legal_cm = (mp[..., mc["legal_next"]] != 0).transpose(1, 2).reshape(B, R * Cc)
        node_cm = node_e.transpose(1, 2).reshape(B, R * Cc, -1)
        order = torch.argsort((~legal_cm).to(torch.int8), dim=1, stable=True)[:, : self.map_ranks]
        n_legal = legal_cm.sum(1, keepdim=True)
        rank_ok = (torch.arange(self.map_ranks, device=mp.device).view(1, -1) < n_legal).unsqueeze(-1).float()
        ranks = F.relu(self.map_proj(gather_rows(node_cm, order))) * rank_ok

        rw, wcol = x["rewards"], C["rewards"]
        rw_m = rw[..., wcol["reward_type"]] != 0
        item_max = self.item_emb.num_embeddings - 1
        rw_e = self.reward_mlp(torch.cat([self.reward_type_emb(rw[..., wcol["reward_type"]].clamp_min(0)),
                                          self.item_emb(rw[..., wcol["item_id"]].clamp(0, item_max)),
                                          symlog(rw[..., [wcol["amount"], wcol["is_card_reward"]]].float())], -1))
        rewards = masked_mean(rw_e, rw_m)

        cr = x["card_reward"]
        cr_e = self._cards(cr, None)
        cr_m = cr[..., C["hand"]["card_id"]] != 0
        card_reward = masked_mean(cr_e, cr_m)

        alts = x["card_reward_alts"]
        alt_e = F.relu(self.alt_proj(self.alt_emb(alts.clamp(0, self.spec.enums["CardRewardAlt"] - 1))))
        alt = masked_mean(alt_e, alts != 0)

        sh, sc = x["shop"], C["shop"]
        sh_m = sh[..., sc["category"]] != 0
        sh_e = self.shop_mlp(torch.cat([self.shop_cat_emb(sh[..., sc["category"]].clamp(0, self.spec.enums["ShopCategory"] - 1)),
                                        self.item_emb(sh[..., sc["item_id"]].clamp(0, item_max)),
                                        symlog(sh[..., [sc["upgrade"], sc["price"], sc["stocked"], sc["on_sale"],
                                                        sc["affordable"]]].float())], -1))
        shop = masked_mean(sh_e, sh_m)

        rs, rsc = x["rest"], C["rest"]
        rs_m = rs[..., rsc["option_id"]] != 0
        rs_e = self.rest_mlp(torch.cat([self.rest_emb(rs[..., rsc["option_id"]].clamp(0, self.spec.enums["RestOption"] - 1)),
                                        rs[..., rsc["enabled"]].float().unsqueeze(-1)], -1))
        rest = masked_mean(rs_e, rs_m)

        ev, evc = x["event_options"], C["event_options"]
        n_ev = gi[:, C["global_i"]["event_option_n"]]
        ev_m = torch.arange(ev.shape[1], device=ev.device).view(1, -1) < n_ev.unsqueeze(-1)
        ev_e = self.event_mlp(torch.cat([self.hash_emb(torch.remainder(ev[..., evc["key_hash"]], HASH_BUCKETS)),
                                         self.relic_emb(ev[..., evc["relic_id"]].clamp(0, self.relic_emb.num_embeddings - 1)),
                                         ev[..., [evc["enabled"], evc["locked"], evc["proceed"]]].float(),
                                         symlog(ev[..., evc["ordinal"]].float()).unsqueeze(-1),
                                         ev_m.float().unsqueeze(-1)], -1))
        events = masked_mean(ev_e, ev_m)

        po, poc = x["prompt_options"], C["prompt_options"]
        n_po = gi[:, C["global_i"]["prompt_option_n"]]
        po_m = torch.arange(po.shape[1], device=po.device).view(1, -1) < n_po.unsqueeze(-1)
        kind = po[..., poc["kind_of_item"]].clamp(0, self.spec.enums["PromptItemKind"] - 1)
        iid = po[..., poc["item_id"]]
        is_card, is_relic, is_potion = kind == self._pik_card, kind == self._pik_relic, kind == self._pik_potion
        other = ~(is_card | is_relic | is_potion)
        card32 = self.card32(self.card_emb.weight)                      # [V_cards, 32]
        item = torch.where(other.unsqueeze(-1), self.hash_emb(torch.remainder(iid, HASH_BUCKETS)),
                           self.item_emb(iid.clamp(0, self.item_emb.num_embeddings - 1)))
        item = torch.where(is_card.unsqueeze(-1), card32[iid.clamp(0, card32.shape[0] - 1).long()], item)
        po_e = self.prompt_mlp(torch.cat([item, self.pitem_emb(kind), symlog(po[..., [poc["upgrade"], poc["ordinal"]]].float()),
                                          (po[..., poc["picked"]] > 0).float().unsqueeze(-1)], -1))
        prompt = masked_mean(po_e, po_m)

        pooled = [glob, hand_e.mul(hand_m.unsqueeze(-1)).sum(1) / hand_m.sum(1, keepdim=True).clamp_min(1),
                  deck, piles, rel, pot, pow_, orbs, enemies, allies, map_e, rewards, card_reward, alt, shop, rest,
                  events + prompt]
        if self.depth > 0:
            state, hand_e, en_e, pot_e = self._trunk(pooled, hand_e, hand_m, en_e, en_m, pot_e, pot_m)
        else:
            state = self.state_mlp(torch.cat(pooled, -1))
        tables = {"hand": hand_e, "enemies": en_e, "potions": pot_e,
                  "bank": torch.cat([torch.zeros(B, 1, self.d, device=gi.device, dtype=hand_e.dtype), ranks, rw_e, cr_e,
                                     alt_e, sh_e, rs_e, ev_e, po_e], 1)}
        return state, tables

    def _trunk(self, pooled, hand_e, hand_m, en_e, en_m, pot_e, pot_m):
        """Transformer over [17 table tokens | hand rows | enemy rows | potion rows] -> state and contextualised rows."""
        B = hand_e.shape[0]
        H, E, P = hand_e.shape[1], en_e.shape[1], pot_e.shape[1]
        dev = hand_e.device
        tt = self.tok_type
        seq = torch.cat([torch.stack(pooled, 1) + tt[:N_TABLES],
                         hand_e + tt[N_TABLES] + self.hand_pos(torch.arange(H, device=dev)),
                         en_e + tt[N_TABLES + 1] + self.enemy_pos(torch.arange(E, device=dev)),
                         pot_e + tt[N_TABLES + 2] + self.potion_pos(torch.arange(P, device=dev))], 1)
        keep = torch.cat([torch.ones(B, N_TABLES, dtype=torch.bool, device=dev), hand_m, en_m, pot_m], 1)
        h = seq
        ckpt = self.grad_ckpt and self.training and torch.is_grad_enabled()
        for blk in self.blocks:
            h = checkpoint(blk, h, keep, use_reentrant=False) if ckpt else blk(h, keep)
        h = self.trunk_ln(h)
        o = N_TABLES
        state = self.state_mlp(torch.cat([h[:, 0], masked_mean(h, keep)], -1))
        return state, h[:, o:o + H], h[:, o + H:o + H + E], h[:, o + H + E:]

    def forward(self, x: dict[str, torch.Tensor]):
        """-> (logits [B, C] float32, masked to j < cand_n, value [B] float32)."""
        state, tables = self.encode(x)
        value = self.value_head(state).squeeze(-1)
        cand, cc = x["cand"], self.spec.cols["cand"]
        Cn = cand.shape[1]
        kind = cand[..., cc["kind"]].clamp(0, self.kind_off.shape[0] - 1).long()

        def ptr(table, col):
            i = cand[..., cc[col]].long()
            ok = (i >= 0) & (i < table.shape[1])
            return gather_rows(table, torch.where(ok, i, torch.zeros_like(i))) * ok.unsqueeze(-1).to(table.dtype), ok

        hand_p, hand_ok = ptr(self.c_hand(tables["hand"]), "hand_slot")
        tgt_p, tgt_ok = ptr(self.c_tgt(tables["enemies"]), "target_slot")
        pot_p, pot_ok = ptr(self.c_pot(tables["potions"]), "potion_slot")
        oi = cand[..., cc["option_index"]].long()
        off = self.kind_off[kind]
        bank = self.c_opt(tables["bank"])
        n_prompt = bank.shape[1] - self._prompt_offset
        size = torch.where(self.kind_prompt[kind], torch.full_like(off, n_prompt), self.kind_size[kind])
        opt_ok = (off > 0) & (oi >= 0) & (oi < size)
        opt_p = gather_rows(bank, torch.where(opt_ok, off + oi, torch.zeros_like(oi))) * opt_ok.unsqueeze(-1).to(bank.dtype)
        cid = cand[..., cc["card_id"]].clamp(0, self.card_emb.num_embeddings - 1).long()
        card_t = self.c_card(self.card_emb.weight)                       # [V_cards, d]
        num = torch.stack([symlog(cand[..., cc["A"]].float()), symlog(cand[..., cc["B"]].float()),
                           hand_ok.float(), tgt_ok.float(), pot_ok.float(), opt_ok.float()], -1)
        h1 = F.relu(self.c_kind(kind) + self.c_num(num) + hand_p + tgt_p + pot_p + opt_p + card_t[cid]
                    + self.c_state(state).unsqueeze(1))
        h2 = F.relu(self.c_l2(h1))
        logits = (h2 * self.q_state(state).unsqueeze(1)).sum(-1) / math.sqrt(self.d) + self.c_bias(h2).squeeze(-1)
        valid = torch.arange(Cn, device=cand.device).view(1, -1) < x["cand_n"].view(-1, 1)
        logits = logits.float().masked_fill(~valid, NEG)          # float32 even under autocast
        return logits, value.float()

    @property
    def arch(self) -> dict:
        return {"d": self.d, "s": self.s, "depth": self.depth, "heads": self.heads, "ff_mult": self.ff_mult}


def param_count(m: nn.Module) -> int:
    return sum(p.numel() for p in m.parameters())


FLOAT_BUFFERS = ("global_f", "hand_f", "hand_dmg")


def synthetic_batch(spec: ModelSpec, batch: int, rows: int = 64) -> dict[str, torch.Tensor]:
    """Zero-filled model inputs of the worst-case width (`rows` candidate and prompt-option rows), CPU."""
    x = {}
    for name in USED_BUFFERS:
        shape = list(spec.shapes[name])
        if name in ROW_TRUNCATED:
            shape[0] = min(shape[0], rows)
        x[name] = torch.zeros((batch, *shape), dtype=torch.float32 if name in FLOAT_BUFFERS else torch.int32)
    x["cand_n"][:] = x["cand"].shape[1]
    x["global_i"][:, spec.cols["global_i"]["prompt_option_n"]] = x["prompt_options"].shape[1]
    return x


def activation_bytes_per_sample(model: nn.Module, spec: ModelSpec, rows: int = 64, batch: int = 2) -> float:
    """Bytes autograd saves for backward per training sample (float32, worst-case candidate / prompt widths),
    measured with saved-tensor hooks on a tiny synthetic batch on the CPU (each storage counted once, parameters
    excluded). One minibatch needs about `minibatch x this`; the backward pass peaks somewhat higher."""
    x = synthetic_batch(spec, batch, rows)
    params = {p.untyped_storage().data_ptr() for p in model.parameters()}
    seen: set[int] = set()
    total = 0

    def pack(t):
        nonlocal total
        st = t.untyped_storage()
        ptr = st.data_ptr()
        if ptr not in params and ptr not in seen:
            seen.add(ptr)
            total += st.nbytes()
        return t

    dev = next(model.parameters()).device
    with torch.enable_grad(), torch.autograd.graph.saved_tensors_hooks(pack, lambda t: t):
        model({k: v.to(dev) for k, v in x.items()})
    return total / batch


def arch_of(config: dict | None) -> dict:
    """The architecture kwargs of ``Policy`` from a (checkpoint) config; missing keys = the smoke model."""
    config = config or {}
    return {k: int(config.get(k, ARCH_DEFAULTS[k])) for k in ARCH_KEYS}
