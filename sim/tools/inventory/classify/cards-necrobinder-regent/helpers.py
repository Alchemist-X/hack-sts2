SHUF_DRAW = "RunRngType.Shuffle (conditional: CardPileCmd.Draw reshuffles the discard pile when the draw pile runs out)"
SHUF_RANDPOS = "RunRngType.Shuffle: NextInt(pileCount + 1) per card inserted at CardPilePosition.Random (Commands/CardPileCmd.cs:3431)"
TARGETS_AUTOPLAY = "RunRngType.CombatTargets: CardCmd.AutoPlay picks a random hittable enemy for each AnyEnemy card played with a null target (Commands/CardCmd.cs:407-411)"
GEN_RNG = "RunRngType.CombatCardGeneration: TakeRandom = UnstableShuffle of the filtered unlocked colorless pool, take n (Factories/CardFactory.cs:93-102)"

def C(cls, mechanics, reqs, prompts=None, powers=None, cards=None, rng=None, notes="", oq=None, hooks=None, mp=False):
    return dict(cls=cls, mechanics=list(mechanics), reqs=list(reqs), prompts=list(prompts or []), powers=list(powers or []),
                cards=list(cards or []), rng=list(rng or []), notes=notes, oq=list(oq or []), hooks=dict(hooks or {}), mp=mp)

def P(kind, entry, pile, mn, mx, filt=None, skip=False, then="", line=None, auto=None, indirect=None, prefs=None, confirm=None):
    return dict(kind=kind, entry=entry, pile=pile, min=mn, max=mx, filter=filt, can_skip=skip, then=then, line=line,
                auto_resolve=auto, indirect=indirect, prefs=prefs, confirm=confirm)

def NESTED(via):
    return dict(kind="Nested", entry=via, pile=None, min=None, max=None, filter=None, can_skip=False,
                then="prompts of the auto-played card open inside this play", line=None, auto_resolve=None, indirect=None, prefs=None, confirm=None)
