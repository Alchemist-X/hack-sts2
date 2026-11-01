# cards-3-1 (W3 wave A) — port index

Common drops for every entry: TriggerAnim / VFX / SFX / Cmd.Wait / PreviewCardPileAdd / hover tips / ShouldGlowRed. Cross-owner content (powers, Shiv, Debris, Fuel) is referenced by id through the registries.

- ABRASIVE — Models.Cards/Abrasive.cs:16 — Dexterity applied before Thorns (state-machine order verified).
- ACCURACY — Models.Cards/Accuracy.cs:16 — ACCURACY_POWER by id; var kept as DynamicVar "AccuracyPower".
- ADAPTIVE_STRIKE — Models.Cards/AdaptiveStrike.cs:16 — CreateClone = cs.CloneCard + ExhaustOnNextPlay=false; SetThisCombat(0); clone to Discard.
- AGGRESSION — Models.Cards/Aggression.cs:13 — NOT REGISTERED in this wave (see report: AGGRESSION_POWER owned by powers-3457; W1 gates generate it via Discovery and crash on the missing power; CardKernelTests use it as the unported placeholder sample).
- ANGER — Models.Cards/Anger.cs:16 — clone to Discard with owner as creator.
- APOTHEOSIS — Models.Cards/Apotheosis.cs:11 — AllCards walked pile by pile (Hand, Draw, Discard, Exhaust, Play) by index.
- ASHEN_STRIKE — Models.Cards/AshenStrike.cs:18 — CalculatedDamageVar multiplier = exhaust pile count; Attack(calculated) via DamageCmd.Attack(cs, func).
- BEAT_DOWN — Models.Cards/BeatDown.cs:19 — Shuffle StableShuffle then Take(N); CombatTargets NextItem per AnyEnemy card; IsOverOrEnding break.
- BLADE_DANCE — Models.Cards/BladeDance.cs:15 — Shiv.CreateInHand inlined (IsOverOrEnding guard, one AddGeneratedCardsToCombat per Shiv).
- BODY_SLAM — Models.Cards/BodySlam.cs:18 — multiplier = owner Block.
- BOMBARDMENT — Models.Cards/Bombardment.cs:22 — CardListener AfterAutoPrePlayPhaseEnteredEarly: in Exhaust pile and player==owner -> AutoPlay(null).
- BULLY — Models.Cards/Bully.cs:19 — multiplier = target Vulnerable amount.
- CALCULATED_GAMBLE — Models.Cards/CalculatedGamble.cs:14 — DiscardAndDraw(hand, hand count); upgrade adds Retain.
- CAPTURE_SPIRIT — Models.Cards/CaptureSpirit.cs:18 — Damage (unblockable/unpowered) before Souls to Draw at Random (order verified).
- CASCADE — Models.Cards/Cascade.cs:12 — X cost; ResolveEnergyXValue (+1 upgraded); AutoPlayFromDrawPile Top.
- CATASTROPHE — Models.Cards/Catastrophe.cs:16 — per iteration StableShuffle(Shuffle) of playable draw cards, fallback all draw cards.
- CLAW — Models.Cards/Claw.cs:21 — ExtraDamageFromClawPlays in CardExtraState (canonical tag 0xCA31); AfterDowngraded re-adds it.
- CLOAK_AND_DAGGER — Models.Cards/CloakAndDagger.cs:16 — block then Shivs (inlined CreateInHand).
- COLLISION_COURSE — Models.Cards/CollisionCourse.cs:17 — DEBRIS by id to Hand.
- COMPACT — Models.Cards/Compact.cs:17 — GainBlock before Transform (order verified); FUEL by id, upgraded when Compact is.
- CORRUPTION — Models.Cards/Corruption.cs:17 — CORRUPTION_POWER by id, var "Power".
- CRASH_LANDING — Models.Cards/CrashLanding.cs:18 — all opponents; Debris x (MaxCardsInHand - hand count).
- DRAIN_POWER — Models.Cards/DrainPower.cs:19 — TakeRandom(CombatCardSelection) over upgradable discard cards.
- DRUM_OF_BATTLE — Models.Cards/DrumOfBattle.cs:15 — CardListener AfterCardExhausted(self): GeneratePlayCount then GainEnergy per play; CombatState!=null check implicit.
- ECHOING_SLASH — Models.Cards/EchoingSlash.cs:19 — AttackContext inlined (AttackCommand(0).FromCard.TargetingAllOpponents, BeforeAttack/AddHit/AfterAttack); no try/finally around dispose.
- EIDOLON — Models.Cards/Eidolon.cs:15 — snapshot of Ethereal and not Unplayable exhaust cards, AutoPlay each.
- END_OF_DAYS — Models.Cards/EndOfDays.cs:23 — DOOM_POWER by id; DoomPower.DoomKill/GetDoomedCreatures inlined (Models.Powers/DoomPower.cs:506,518): Kill each then Hook.AfterDiedToDoom.
- ENTHRALLED — Models.Cards/Enthralled.cs:6 — CardListener ShouldPlay: blocks owner's other cards only for AutoPlayType.None while in Hand.
