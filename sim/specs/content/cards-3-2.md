# W3 cards-3-2 (wave A) — port index

Files: `sim/Sts2Sim.Core/Cards/Defs/<Class>.cs`. Decompile paths are relative to `MegaCrit.Sts2.Core.`. Dropped everywhere: TriggerAnim, SFX/VFX, hit FX, waits, hover tips, `CardCmd.Preview*`, `skipCardPileVisuals`.

- EXPECT_A_FIGHT — Models.Cards/ExpectAFight.cs:18 — calculated block 15 + 5 x max(0, Strength); upgrade +1 / +3.
- EXPERTISE — Models.Cards/Expertise.cs:15 — `CardPileCmd.Draw` returning the drawn cards -> `DrawCards`; each gets single-turn Retain.
- FAN_OF_KNIVES — Models.Cards/FanOfKnives.cs:20 — power and Shiv by id (`FAN_OF_KNIVES_POWER`, `SHIV`); `Shiv.CreateInHand` inlined as a private helper (IsOverOrEnding check, CreateCard, AddGeneratedCardsToCombat Hand); OnEnqueuePlayVfx dropped.
- FLAK_CANNON — Models.Cards/FlakCannon.cs:20 — statuses snapshotted (all piles except Exhaust, AllPiles order) and hit count computed before the exhausts; random-target attack.
- FLECHETTES — Models.Cards/Flechettes.cs:18 — hits = Skills in hand at play time.
- FLICK_FLACK — Models.Cards/FlickFlack.cs:16 — Sly, all enemies.
- GRAVE_WARDEN — Models.Cards/GraveWarden.cs:16 — block then `Soul.Create` x Cards into Draw at Random.
- HAUNT — Models.Cards/Haunt.cs:16 — `HAUNT_POWER` by id, amount = HpLoss var.
- HAVOC — Models.Cards/Havoc.cs:14 — AutoPlayFromDrawPile(1, Top, forceExhaust).
- HELLRAISER — Models.Cards/Hellraiser.cs:19 — `HellraiserPower` (W1 type).
- HIDDEN_GEM — Models.Cards/HiddenGem.cs:16 — eligibility filter on own keyword mask (`HasKeyword(Unplayable)`), CombatCardSelection.NextItem; CanBeGeneratedInCombat false.
- HOWL_FROM_BEYOND — Models.Cards/HowlFromBeyond.cs:18 — card listener `AfterAutoPostPlayPhaseEntered`: autoplay self when in Exhaust and player is owner.
- INFINITE_BLADES — Models.Cards/InfiniteBlades.cs:15 — `INFINITE_BLADES_POWER` by id; upgrade adds Innate.
- I_AM_INVINCIBLE — Models.Cards/IAmInvincible.cs:17 — card listener `AfterAutoPostPlayPhaseEntered`: if top of own draw pile, AutoPlayFromDrawPile(1, Top).
- KINGLY_KICK — Models.Cards/KinglyKick.cs:16 — card listener `AfterCardDrawn` (self): cost -1 this combat.
- KINGLY_PUNCH — Models.Cards/KinglyPunch.cs:16 — card listener `AfterCardDrawn` (self): Damage += Increase; `ExtraDamage` in CardExtraState (local tag 0xCA32), re-added in AfterDowngraded.
- KNIFE_TRAP — Models.Cards/KnifeTrap.cs:19 — snapshot Shiv-tagged exhaust cards; upgrade each if KnifeTrap is upgraded, then AutoPlay(target). No OnUpgrade in the game.
- LEADING_STRIKE — Models.Cards/LeadingStrike.cs:17 — attack then 2 Shivs (helper as FAN_OF_KNIVES); Strike tag.
- MASTER_PLANNER — Models.Cards/MasterPlanner.cs:15 — `MASTER_PLANNER_POWER` by id.
- MAUL — Models.Cards/Maul.cs:21 — 2 hits, then every Maul in AllPiles (incl. itself) gets +Increase; `ExtraDamageFromMaulPlays` in CardExtraState (local tag 0xCA33), re-added in AfterDowngraded.
- MAYHEM — Models.Cards/Mayhem.cs:13 — `MAYHEM_POWER` by id.
- MELANCHOLY — Models.Cards/Melancholy.cs:17 — card listener `AfterDeath`: unless removal prevented and while in a combat pile, cost -Energy this combat.
- MIND_BLAST — Models.Cards/MindBlast.cs:17 — CalculatedDamage = draw pile size; Innate; attack via the calculated-damage AttackCommand overload.
- MIRAGE — Models.Cards/Mirage.cs:20 — calculated block = sum of POISON_POWER on alive `cs.Monsters`; upgrade removes Exhaust.
- MONOLOGUE — Models.Cards/Monologue.cs:16 — `MONOLOGUE_POWER` by id with the Apply<T> result path; gap: `monologuePower.DynamicVars.Strength.BaseValue = Power` (:95) dropped — PowerModel has no DynamicVars; value-neutral in v0.111.0 (both are 1, the card's Power var never changes).
- NO_ESCAPE — Models.Cards/NoEscape.cs:17 — DOOM_POWER by id; doom = 10 + 5 x floor(targetDoom / 10); upgrade base +5.
- OMNISLICE — Models.Cards/Omnislice.cs:19 — needs `AttackContext` (new kernel file `Combat/Pipeline/AttackContext.cs`: BeforeAttack at creation, AddHit, AfterAttack on dispose); splash on receiver's hittable teammates minus target, TotalDamage + Overkill, Unpowered|Move.
- PERFECTED_STRIKE — Models.Cards/PerfectedStrike.cs:21 — 6 + 2 x Strike-tagged cards in AllPiles; heavy-anim branch dropped.
