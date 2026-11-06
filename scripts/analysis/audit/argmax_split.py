"""Does greedy argmax hide a preference split across macro rows? Rest sites (HEAL vs the SMITH:<card> rows), shops (Remove rows), rewards (TakeCard rows vs ProceedRewards)."""
import json, sys, collections
dec = sys.argv[1]
rest = []; shop = []; rew = []
for line in open(dec):
    r = json.loads(line)
    c = r.get("cands") or []; p = r.get("pr") or []
    if len(c) != len(p): continue
    if r.get("kind") == "ChooseRestOption" or (r.get("phase") == "RestSite" and any(x.startswith("Rest:") for x in c)):
        heal = sum(pp for x, pp in zip(c, p) if x == "Rest:HEAL")
        smith = [pp for x, pp in zip(c, p) if x.startswith("Rest:SMITH:")]
        other = sum(pp for x, pp in zip(c, p) if x.startswith("Rest:") and x != "Rest:HEAL" and not x.startswith("Rest:SMITH:"))
        rest.append((heal, sum(smith), max(smith) if smith else 0.0, len(smith), r.get("chosen"), r.get("hp"), r.get("max_hp")))
    if r.get("phase") == "Shop" and any(x.startswith("Remove:") for x in c):
        rem = [pp for x, pp in zip(c, p) if x.startswith("Remove:")]
        shop.append((sum(rem), max(rem), r.get("chosen"), r.get("gold")))
    if r.get("phase") == "Rewards" and any(x.startswith("TakeCard:") for x in c) and r.get("kind") in ("TakeRewardCard","ProceedRewards","ClaimReward"):
        take = [pp for x, pp in zip(c, p) if x.startswith("TakeCard:")]
        proceed = sum(pp for x, pp in zip(c, p) if x == "ProceedRewards")
        rew.append((sum(take), proceed, r.get("chosen")))
n = len(rest); split = sum(1 for h,s,m,k,ch,hp,mh in rest if s > h); chosen_heal = sum(1 for *_, ch, hp, mh in rest if ch == "Rest:HEAL")
full = [(h,s) for h,s,m,k,ch,hp,mh in rest if hp is not None and mh and hp/mh >= 0.9]
print(f"rest decisions (with SMITH rows offered): {n}; chose HEAL {chosen_heal}; sum(SMITH)>P(HEAL): {split} ({100*split/max(1,n):.0f}%); mean P(HEAL) {sum(h for h,*_ in rest)/max(1,n):.3f}, mean sum(SMITH) {sum(s for h,s,*_ in rest)/max(1,n):.3f}, mean max(SMITH) {sum(m for h,s,m,*_ in rest)/max(1,n):.3f}, mean SMITH rows {sum(k for h,s,m,k,*_ in rest)/max(1,n):.1f}")
print(f"  at hp>=90%: n={len(full)}, sum(SMITH)>P(HEAL) in {sum(1 for h,s in full if s>h)}")
print(f"shop decisions with Remove rows: {len(shop)}; sum(Remove) mean {sum(s for s,*_ in shop)/max(1,len(shop)):.3f}; chose Remove: {sum(1 for s,m,ch,g in shop if (ch or '').startswith('Remove:'))}; sum(Remove)>0.5: {sum(1 for s,*_ in shop if s>0.5)}")
print(f"reward decisions with TakeCard rows: {len(rew)}; chose Proceed(skip): {sum(1 for s,pr,ch in rew if ch=='ProceedRewards')}; mean P(proceed) {sum(pr for s,pr,ch in rew)/max(1,len(rew)):.3f}, mean sum(TakeCard) {sum(s for s,pr,ch in rew)/max(1,len(rew)):.3f}")
