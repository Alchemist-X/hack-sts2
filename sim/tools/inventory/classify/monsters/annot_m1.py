M = {}


def add(name, **kw):
    M[name] = kw


add("Aeonglass",
    mech=["attack + block 33 (EBB)", "multi-attack 2 hits (EYE_LASERS)",
          "add Wither x WitherAmount to each target's discard (AddToCombatAndPreview)",
          "fake-upgrade every Wither already in target combat piles, WitherUpgradeCount++",
          "gain Strength = base (3, A9 4) + AdditionalStrength, then AdditionalStrength++",
          "on spawn: one WitheringPresencePower(6) per opponent with Target = that opponent; Artifact 3",
          "newly generated Wither gets FakeUpgrade x WitherUpgradeCount (AfterCardGeneratedForCombat)",
          "WitheringPresence (instanced, Target = player): every 6 cards that player plays put 1 Wither into the hand, then the counter resets"],
    kern=["CardPileCmd.AddToCombatAndPreview<T>(targets, pile, count)", "monster as hook listener: AfterCardGeneratedForCombat",
          "CardModel.FakeUpgrade (Wither)", "PowerModel.Target on a per-opponent power instance (PowerCmd.Apply(power instance))",
          "PlayerCombatState.AllCards enumeration"],
    deps={"powers": ["WitheringPresencePower", "ArtifactPower", "StrengthPower"], "cards": ["Wither"]},
    special={"role": "Glory boss (AeonglassBoss), single creature", "death": "AfterDeath is music only"},
    vnote="rule-change: EbbDamage 26/32 -> 22/26 (A9)",
    tier=2)

add("Architect",
    mech=["no-op move NOTHING (HiddenIntent)"],
    kern=["display-only creature: the TheArchitect event builds its layout CombatState (EventCombatSynchronizer.InitializeForEvent) but never enters combat"],
    special={"role": "TheArchitectEventEncounter only (event layout display); 9999 HP placeholder"},
    notes="No combat: Models.Events/TheArchitect.cs never calls EnterCombatWithoutExitingEvent and only triggers animations on the creature (:162, :322, :1115). Its only rule effect is one RunRng.Niche HP draw when the layout creature is created at event entry (Multiplayer.Game/EventCombatSynchronizer.cs:83-128).",
    tier=1)

add("AssassinRubyRaider", mech=["attack (KILLSHOT) every turn"], tier=1)

add("AxeRubyRaider", mech=["attack + block (SWING_1, SWING_2)", "attack (BIG_SWING)"], tier=1,
    notes="SWING_1 and SWING_2 share SwingMove; two state ids give the 3-cycle.")

add("Axebot",
    mech=["on spawn: StockPower(StockAmount) when StockAmount > 0 (applier null)",
          "BOOT_UP: block 10 (A9 15) + Strength BootUpStrGain x RespawnCount",
          "HAMMER_UPPERCUT: attack, then Weak 2, Frail 2 (targets)", "ONE_TWO: 2-hit attack",
          "respawn through StockPower.AfterDeath: new Axebot with StockAmount = stock-1 in the same slot"],
    kern=["CreatureCmd.Add(monster, combatState, side, slotName) mid-combat (respawn)", "StockPower.ShouldStopCombatFromEnding",
          "per-instance override field _stockOverrideAmount chooses the initial state and HP bonus"],
    deps={"powers": ["StockPower", "StrengthPower", "WeakPower", "FrailPower"]},
    special={"spawns": "respawns itself via StockPower (Axebot with StockAmount-1, ShouldPlaySpawnAnimation)",
             "hp_rule": "Min/MaxInitialHp add RespawnMaxHpBonus = RespawnCount*10, RespawnCount = 2 - StockAmount"},
    vnote="rule-change: HammerUppercut 12/14 -> 14/18, OneTwo 9/10 -> 10/11, HP gains +10 per respawn (new), BootUp Str uses RespawnCount",
    tier=3)

for _n, _hp in (("BattleFriendV1", 75), ("BattleFriendV2", 150), ("BattleFriendV3", 300)):
    add(_n,
        mech=["no-op NOTHING_MOVE (no intents)", "on spawn: BattlewornDummyTimeLimitPower(3)",
              "time limit: power decrements at its side's turn end; at 1 it sets encounter.RanOutOfTime and the dummy escapes"],
        kern=["CreatureCmd.Escape", "side-turn participants on AfterSideTurnEnd", "encounter custom state (RanOutOfTime) read by the event"],
        deps={"powers": ["BattlewornDummyTimeLimitPower"]},
        special={"role": "BattlewornDummy event combat dummy, %d HP fixed" % _hp, "death": "escape on time-out"},
        tier=2)

add("BigDummy", mech=["no-op NOTHING (HiddenIntent)"], special={"role": "test/mock encounters only, 9999 HP"},
    notes="Only Mock*Encounter use it; port only as a test seam.", tier=1)

add("BowlbugEgg", mech=["attack + block every turn (BITE)"], tier=1)

add("BowlbugNectar", mech=["attack 3 (THRASH)", "Strength 15 (A9 16) once (BUFF)", "attack 3 forever (THRASH2)"], deps={"powers": ["StrengthPower"]}, tier=1)

add("BowlbugRock",
    mech=["on spawn: ImbalancedPower", "HEADBUTT attack; if IsOffBalance after the attack, self-stun with DizzyMove",
          "POST_HEADBUTT conditional: IsOffBalance -> DIZZY_MOVE (StunIntent) else HEADBUTT", "DIZZY clears IsOffBalance"],
    kern=["CreatureCmd.Stun(creature, stunMove) (sim StunWithMove)", "power writes monster field (ImbalancedPower sets IsOffBalance on fully blocked hit)",
          "DamageResult.WasFullyBlocked"],
    deps={"powers": ["ImbalancedPower"]},
    tier=1)

add("BowlbugSilk", mech=["Weak 1 to targets (TOXIC_SPIT, initial)", "2-hit attack (THRASH)"], deps={"powers": ["WeakPower"]}, tier=1)

add("BruteRubyRaider", mech=["attack (BEAT)", "Strength 3 (ROAR)"], deps={"powers": ["StrengthPower"]}, tier=1)

add("BygoneEffigy",
    mech=["on spawn: SlowPower 1", "SLEEP_MOVE (SleepIntent, no effect) then WAKE: Strength 10", "SLASHES attack forever",
          "SLEEP_MOVE_2 registered but unreachable from the graph"],
    deps={"powers": ["SlowPower", "StrengthPower"]},
    tier=1)

add("Byrdonis", mech=["on spawn: TerritorialPower 1", "SWOOP attack (initial) <-> PECK 3-hit attack"],
    deps={"powers": ["TerritorialPower"]}, tier=1)

add("Byrdpip",
    mech=["no-op NOTHING_MOVE (no intents)", "health bar hidden"],
    kern=["pets on the player side (relic-summoned companion)"],
    special={"role": "pet creature of the Byrdpip relic; not in any encounter"},
    tier=3)

add("CalcifiedCultist", mech=["Ritual 2 (INCANTATION, once)", "attack forever (DARK_STRIKE)"], deps={"powers": ["RitualPower"]}, tier=1)

add("CeremonialBeast",
    mech=["STAMP: PlowPower 150 (A9 160) on self", "PLOW: attack + Strength 2, repeats while Plow holds",
          "Plow broken (unblocked damage leaves HP <= Plow amount): remove Strength and temporary Strength, SetStunned, Stun with StunnedMove, next BEAST_CRY",
          "STUN_MOVE (MustPerformOnceBeforeTransitioning) -> BEAST_CRY: RingingPower 1 to targets -> STOMP attack -> CRUSH attack + Strength 3 (A9 4) -> BEAST_CRY",
          "Ringing: every player card gets the Ringing affliction; once the player started a card play this turn, Ringing cards cannot be played (ShouldPlay, combat history); removed at the player's turn end"],
    kern=["CreatureCmd.Stun(creature, stunMove, nextMoveId)", "power calls a monster method (PlowPower -> CeremonialBeast.SetStunned)",
          "ShouldDisappearFromDoom = false"],
    deps={"powers": ["PlowPower", "RingingPower", "StrengthPower"], "afflictions": ["Ringing"]},
    special={"role": "Overgrowth boss (CeremonialBeastBoss)", "phases": "phase 2 after Plow breaks (IsInSecondPhase)"},
    tier=2)

add("Chomper",
    mech=["on spawn: Artifact 2", "CLAMP 2-hit attack <-> SCREECH: 3 Dazed to each target's discard",
          "initial = SCREECH when ScreamFirst (set by the encounter) else CLAMP"],
    kern=["CardPileCmd.AddToCombatAndPreview<T>"], deps={"powers": ["ArtifactPower"], "cards": ["Dazed"]},
    tier=2)

add("CrossbowRubyRaider", mech=["RELOAD: block 3 (initial)", "FIRE attack"], tier=1,
    notes="IsCrossbowReloaded only drives visuals.")

add("Crusher",
    mech=["on spawn: BackAttackLeftPower, CrabRagePower", "cycle THRASH -> ENLARGING_STRIKE -> BUG_STING (2 hits, Weak 2, Frail 2) -> ADAPT (Strength) -> GUARDED_STRIKE (attack + block 18)",
          "CrabRage: when the other crab part dies gain Strength 6 and block 99 (Unpowered)"],
    kern=["multi-part boss: two independent monsters (Crusher left, Rocket right)", "ShouldDisappearFromDoom = false"],
    deps={"powers": ["BackAttackLeftPower", "CrabRagePower", "StrengthPower", "WeakPower", "FrailPower"]},
    special={"role": "Hive boss KaiserCrabBoss, left arm", "multipart": "with Rocket; BackAttackLeft/Right mark sides for Rocket's SurroundedPower",
             "death": "AfterCurrentHpChanged and BeforeDeath overrides drive background visuals only"},
    tier=1)

add("CubexConstruct",
    mech=["on spawn: block 13, Artifact 1", "CHARGE_UP: Strength 2", "REPEATER_BLAST (x2 states): attack + Strength 2", "EXPEL 2-hit attack, then back to REPEATER_BLAST"],
    kern=["AfterAddedToRoom runs in setup window B: the 13 block is refused by the IsOverOrEnding gate (sim/specs/loop.md section 2, already modelled)"],
    deps={"powers": ["ArtifactPower", "StrengthPower"]},
    special={"death": "CurrentHpChanged subscription is visual only (BeforeRemovedFromRoom unsubscribes)"},
    tier=1)

add("DampCultist", mech=["Ritual 5 (A9 6) once (INCANTATION)", "attack forever (DARK_STRIKE 1, A9 3)"], deps={"powers": ["RitualPower"]}, tier=1)

_DECI_MECH = ["multi-attack 2 hits (WRITHE)", "attack + Strength 2 (BULK)", "attack + Weak 1 (CONSTRICT)",
              "on spawn: max HP made even and unique among segments (+2 steps, wraps to scaled MinInitialHp past MaxInitialHp), SetMaxAndCurrentHp; ReattachPower 25",
              "death with another segment alive: stays in combat untargetable, SetMoveImmediate(DEAD_MOVE) -> REATTACH (heal 25 via ReattachPower.DoReattach) -> RAND",
              "fatal only when every other segment is dead (ShouldOwnerDeathTriggerFatal)"]
_DECI_KERN = ["dead-but-present creature (ShouldCreatureBeRemovedFromCombatAfterDeath false, ShouldAllowHitting false while reviving)",
              "PowerModel internal data (isReviving) cloned with the power", "SetMoveImmediate from a power hook",
              "CreatureCmd.SetMaxAndCurrentHp at spawn", "Creature.ScaleHpForMultiplayer (identity for 1 player)",
              "StarterMoveIdx set by the encounter from its Rng"]
add("DecimillipedeSegment", mech=_DECI_MECH, kern=_DECI_KERN,
    deps={"powers": ["ReattachPower", "StrengthPower", "WeakPower"]},
    special={"role": "abstract base of the three Decimillipede segments", "multipart": "3 segments (Front/Middle/Back) revive each other",
             "death": "AfterDeath override is a phobia-texture swap (visual)"},
    notes="Move bodies live here; subclasses only implement SegmentAttack (visual). Initial state from StarterMoveIdx % 3.",
    tier=3)
for _seg in ("DecimillipedeSegmentBack", "DecimillipedeSegmentFront", "DecimillipedeSegmentMiddle"):
    add(_seg, mech=["inherits DecimillipedeSegment (all moves and spawn logic)"], kern=_DECI_KERN,
        deps={"powers": ["ReattachPower", "StrengthPower", "WeakPower"]},
        special={"role": "Hive elite DecimillipedeElite segment", "multipart": "one of 3 segments"},
        notes="Only SegmentAttack (visual) differs from the base.", tier=3)

add("DeprecatedMonster", mech=["STUB move (HiddenIntent), 0 HP"], special={"role": "deprecated placeholder, never spawned"},
    notes="Do not port.", tier=1)

add("DevotedSculptor", mech=["Ritual 9 once (FORBIDDEN_INCANTATION, applier null)", "attack forever (SAVAGE)"],
    deps={"powers": ["RitualPower"]}, tier=1)

add("Entomancer",
    mech=["on spawn: PersonalHivePower 1", "BEES multi-attack (initial)", "SPEAR attack",
          "PHEROMONE_SPIT: if PersonalHive < 3 then PersonalHive +1 and Strength 1, else Strength 2",
          "PersonalHive: every powered attack that damages it puts Amount Dazed at random positions of the attacker's draw pile (an Osty hit counts for its owner)"],
    kern=["read own power amount (Powers.OfType<PersonalHivePower>().First())"],
    deps={"powers": ["PersonalHivePower", "StrengthPower"], "cards": ["Dazed"]},
    vnote="rule-change: HP A8 155 -> 165",
    tier=1)

add("Exoskeleton",
    mech=["on spawn: HardToKillPower 9", "INIT_MOVE picks by slot name: first SKITTER, second MANDIBLES, third ENRAGE, fourth RAND",
          "SKITTER multi-attack (1 x 3, A9 x4)", "MANDIBLES attack -> ENRAGE Strength 2", "RAND: SKITTER/MANDIBLES CannotRepeat, weight 1 each"],
    kern=["ConditionalBranch on Creature.SlotName"],
    deps={"powers": ["HardToKillPower", "StrengthPower"]},
    vnote="rule-change: HP A8 25-29 -> 26-30",
    tier=1)

add("EyeWithTeeth",
    mech=["on spawn: IllusionPower (which also applies MinionPower)", "DISTRACT: 3 Dazed to each target's discard every turn",
          "Illusion: on death revives with a runtime REVIVE_MOVE (heal to full), untargetable meanwhile"],
    kern=["CardPileCmd.AddToCombatAndPreview<T>", "runtime-created MoveState via SetMoveImmediate (IllusionPower REVIVE_MOVE)",
          "dead-but-present creature until revive", "ShouldDisappearFromDoom = false"],
    deps={"powers": ["IllusionPower", "MinionPower"], "cards": ["Dazed"]},
    special={"role": "summoned by Fogmog (FogmogNormal)", "summon": "spawned mid-combat"},
    tier=2)

add("Fabricator",
    mech=["fabricateBranch: CanFabricate (alive teammates < 4) -> RAND{FABRICATE, FABRICATING_STRIKE} else DISINTEGRATE attack",
          "FABRICATE: summon a defense bot (Guardbot/Noisebot) then an aggro bot (Zapbot/Stabbot), each with MinionPower",
          "FABRICATING_STRIKE: attack then summon an aggro bot", "bot choice: RunRng.MonsterAi.NextItem over the pool minus the last spawned type"],
    kern=["CreatureCmd.Add(monster, side, slot) mid-combat", "EncounterModel.GetNextSlot", "RunRng.MonsterAi draw outside the move state machine",
          "static HashSet<MonsterModel> pools in insertion order", "per-instance _lastSpawned (canonical model id)", "CombatState.IsLiveCombat"],
    deps={"powers": ["MinionPower"], "monsters": ["Guardbot", "Noisebot", "Zapbot", "Stabbot"]},
    special={"summon": "Guardbot/Noisebot/Zapbot/Stabbot into the next free slot", "role": "Glory normal FabricatorNormal"},
    tier=3)

add("FakeMerchantMonster",
    mech=["SWIPE attack (initial)", "SPEW_COINS 2 x 8", "THROW_RELIC attack + Frail 1", "ENRAGE Strength 2 (cooldown 3)",
          "RAND_MOVE{SWIPE, SPEW_COINS, THROW_RELIC CannotRepeat; ENRAGE cooldown 3 CannotRepeat}; after THROW_RELIC: RAND_ATTACK_MOVE{SWIPE, SPEW_COINS, THROW_RELIC CannotRepeat}"],
    deps={"powers": ["FrailPower", "StrengthPower"]},
    special={"role": "FakeMerchant event combat (FakeMerchantEventEncounter)"},
    tier=1)

add("FatGremlin",
    mech=["SPAWNED_MOVE (StunIntent, no effect but IsAwake) -> FLEE (EscapeIntent): CreatureCmd.Escape, repeated",
          "carries HeistPower instances (stolen gold) handed over by GremlinMerc's SurprisePower; killing it returns the gold as an extra combat reward"],
    kern=["CreatureCmd.Escape", "HeistPower.BeforeDeath adds a GoldReward to the combat room (run layer)"],
    deps={"powers": ["HeistPower"]},
    special={"role": "spawned by GremlinMerc's SurprisePower into slot 'fat'", "death": "escapes on its 2nd turn"},
    vnote="cosmetic: HasPower<HeistPower> only picks a skin path",
    tier=2)

add("FlailKnight",
    mech=["RAM attack (initial)", "RAND{WAR_CHANT Strength 3 CannotRepeat, FLAIL 2-hit CanRepeatXTimes 2, RAM CanRepeatXTimes 2}"],
    deps={"powers": ["StrengthPower"]},
    special={"role": "Glory elite KnightsElite (with MagiKnight, SpectralKnight)"},
    tier=1)

add("Flyconid",
    mech=["INITIAL random {FRAIL_SPORES cooldown 2, SMASH} (CannotRepeat)",
          "RAND {VULNERABLE_SPORES (Vulnerable 2, cooldown 3), FRAIL_SPORES (attack + Frail 2, cooldown 2), SMASH attack}, all CannotRepeat weight 1"],
    deps={"powers": ["VulnerablePower", "FrailPower"]}, tier=1)

add("Fogmog",
    mech=["ILLUSION: summon EyeWithTeeth into slot 'illusion' (when IsLiveCombat)", "SWIPE attack + Strength 1",
          "BRANCH random {SWIPE_RANDOM (same body) w0.4, HEADBUTT w0.6} CannotRepeat; SWIPE_RANDOM -> HEADBUTT -> SWIPE"],
    kern=["CreatureCmd.Add<T>(combatState, slot) mid-combat", "CombatState.IsLiveCombat"],
    deps={"powers": ["StrengthPower"], "monsters": ["EyeWithTeeth"]},
    special={"summon": "EyeWithTeeth (slot 'illusion')"},
    tier=3)

add("FossilStalker",
    mech=["on spawn: SuckPower 3", "LATCH attack (initial)", "RAND {LATCH, TACKLE (attack + Frail 1), LASH 2-hit} each CanRepeatXTimes 2"],
    deps={"powers": ["SuckPower", "FrailPower"]}, tier=1)

add("FrogKnight",
    mech=["on spawn: PlatingPower 15 (A8 19)", "TONGUE_LASH attack + Frail 2 (initial) -> STRIKE_DOWN_EVIL attack -> FOR_THE_QUEEN Strength 5 -> HALF_HEALTH",
          "HALF_HEALTH: BEETLE_CHARGE (big attack, once) when not yet charged and CurrentHp < MaxHp / 2 (int division), else TONGUE_LASH"],
    deps={"powers": ["PlatingPower", "StrengthPower", "FrailPower"]},
    notes="PlatingAmount uses ToughEnemies (A8), not DeadlyEnemies.",
    tier=1)

add("GasBomb",
    mech=["on spawn: MinionPower", "EXPLODE (DeathBlowIntent): attack, then CreatureCmd.Kill(self)", "single state, no follow-up"],
    deps={"powers": ["MinionPower"]},
    special={"role": "summoned by LivingFog (BLOAT)", "death": "kills itself after exploding"},
    tier=1)

add("GlobeHead",
    mech=["on spawn: GalvanicPower 6 (A9 8)", "cycle SHOCKING_SLAP (attack + Frail 2, initial) -> THUNDER_STRIKE 3 hits -> GALVANIC_BURST attack + Strength 2",
          "Galvanic: at combat start and on entering combat every player Power card gets Galvanized; playing a Galvanized card deals Amount (Unpowered|Move) damage to its owner"],
    deps={"powers": ["GalvanicPower", "FrailPower", "StrengthPower"], "afflictions": ["Galvanized"]},
    vnote="rule-change: GalvanicPower amount 6 now has an A9 value 8",
    tier=1)

add("GremlinMerc",
    mech=["on spawn: SurprisePower; one ThieveryPower(20) per player with Target = that player",
          "cycle GIMME 2-hit -> DOUBLE_SMASH 2-hit + Weak 2 -> HEHE attack + Strength 2; every move then calls ThieveryPower.Steal (lose up to 20 gold)",
          "on death (SurprisePower): summon SneakyGremlin ('sneaky') and FatGremlin ('fat'); each ThieveryPower becomes a HeistPower on FatGremlin carrying the stolen gold"],
    kern=["PlayerCmd.LoseGold (GoldLossType.Stolen) inside combat (run-state effect)", "CreatureCmd.Add<T>(combatState, slot) from a power AfterDeath",
          "SurprisePower.ShouldStopCombatFromEnding", "encounter custom state (GremlinMercNormal.MarkGoldStolen)", "instanced powers with Target (ThieveryPower, HeistPower)"],
    deps={"powers": ["SurprisePower", "ThieveryPower", "HeistPower", "WeakPower", "StrengthPower"], "monsters": ["SneakyGremlin", "FatGremlin"]},
    special={"summon": "SneakyGremlin + FatGremlin on death", "role": "Underdocks normal GremlinMercNormal"},
    notes="All three damage values use ToughEnemies (A8), not DeadlyEnemies.",
    tier=3)

add("Guardbot",
    mech=["GUARD every turn: 15 block (Unpowered) on every Fabricator"],
    special={"role": "Fabricator defense spawn"},
    notes="AfterAddedToRoom only positions the node (TestMode.IsOff guard).",
    tier=1)

add("HauntedShip",
    mech=["HAUNT (initial): Weak 3 + 5 Dazed to each target's discard", "SWIPE attack <-> STOMP 3-hit attack"],
    kern=["CardPileCmd.AddToCombatAndPreview<T>"], deps={"powers": ["WeakPower"], "cards": ["Dazed"]}, tier=2)

add("HunterKiller",
    mech=["TENDERIZING_GOOP (initial): TenderPower 1", "RAND {BITE attack CannotRepeat, PUNCTURE 3-hit CanRepeatXTimes 2}"],
    deps={"powers": ["TenderPower"]}, tier=1)

add("InfestedPrism",
    mech=["on spawn: VitalSparkPower 2 (A9 3)", "cycle JAB attack -> RADIATE attack + block -> WHIRLWIND 3 hits -> PULSATE attack + block 20 (A8 22) + VitalSpark again",
          "VitalSpark: afflicts every player Skill with Tainted; playing a Tainted card applies TaintedPower (Amount) to its owner: +Amount damage taken from powered attacks until the enemy turn ends"],
    deps={"powers": ["VitalSparkPower", "TaintedPower"], "afflictions": ["Tainted"]},
    special={"role": "Hive elite InfestedPrismsElite"},
    notes="Own code is plain; the affliction work sits in VitalSparkPower (CardCmd.Afflict<Tainted>, ClearAffliction).",
    tier=1)

add("Inklet",
    mech=["on spawn: SlipperyPower 1 (HP loss capped at 1 per hit, decrements on unblocked damage)",
          "initial WHIRLWIND when MiddleInklet else JAB", "JAB -> RAND{PIERCING_GAZE, WHIRLWIND} CannotRepeat w1; WHIRLWIND/PIERCING_GAZE -> JAB",
          "INIT_RAND is built but never registered or used"],
    deps={"powers": ["SlipperyPower"]},
    vnote="cosmetic: HurtAnimationTrackOffsetForDoom override added",
    tier=1)

add("KinFollower",
    mech=["on spawn: MinionPower", "cycle QUICK_SLASH -> BOOMERANG 2 hits -> POWER_DANCE Strength 2 (A9 3)", "initial POWER_DANCE when StartsWithDance else QUICK_SLASH"],
    deps={"powers": ["MinionPower", "StrengthPower"]},
    special={"role": "TheKinBoss minion (with KinPriest)"},
    tier=1)

add("KinPriest",
    mech=["cycle ORB_OF_FRAILTY attack + Frail 1 -> ORB_OF_WEAKNESS attack + Weak 1 -> BEAM 3 hits -> RITUAL Strength 2 (A9 3)"],
    deps={"powers": ["FrailPower", "WeakPower", "StrengthPower"]},
    special={"role": "Overgrowth boss TheKinBoss (primary enemy; followers are minions)",
             "death": "AfterDeath only plays a line when the last follower dies (TalkCmd)"},
    tier=1)

REC = 'recovered as {6, 7, 8} from the macOS v0.111.0 decompile (artifacts/knowledge/decompiled-9cb4f1ad/MegaCrit.Sts2.Core.Models.Monsters/KnowledgeDemon.cs:45), whose method IL is identical to the Linux build (output/server-bootstrap/assembly-comparison.json: 51602/51602 methods, ldtoken operands included); the Linux sts2.dll holds the <PrivateImplementationDetails> field named SHA-256(int32 LE 6,7,8) = D8C9807A...E4412B and those 12 bytes occur once; an IL read of the ldtoken RVA data in the W1 critique gives the same 6, 7, 8 (docs/sim-port/w0-critique.md section 7)'

add("KnowledgeDemon",
    mech=["CURSE_OF_KNOWLEDGE (initial, at most 3 times): each target picks 1 of 2 generated cards: Disintegration (DisintegrationPower var = damage[counter]) or MindRot/Sloth/WasteAway by counter; chosen card's IChoosable.OnChosen runs",
          "SLAP attack -> KNOWLEDGE_OVERWHELMING 3 hits (IsBurnt) -> PONDER attack + heal 30 x players + Strength 2 (A9 3) -> branch: counter < 3 ? CURSE : SLAP"],
    prompts=[{"type": "ChooseCard", "api": "CardSelectCmd.FromChooseACardScreen(context, cards, player, canSkip = false)", "source": "2 generated cards: Disintegration + MindRot/Sloth/WasteAway by CurseOfKnowledgeCounter", "min": 1, "max": 1, "can_skip": False,
              "context": "BlockingPlayerChoiceContext during the enemy turn; one prompt per living target, started together and awaited with Task.WhenAll", "site": "Models.Monsters/KnowledgeDemon.cs:124 (ChooseCurse); Commands/CardSelectCmd.cs:3169"}],
    kern=["CardSelectCmd.FromChooseACardScreen prompt during the enemy turn", "CombatState.CreateCard for choice cards", "card DynamicVars override (DisintegrationPower)",
          "KnowledgeDemon.IChoosable.OnChosen on cards", "Task.WhenAll over players (serial in single player)"],
    deps={"cards": ["Disintegration", "MindRot", "Sloth", "WasteAway"], "powers": ["StrengthPower"]},
    special={"role": "Hive boss KnowledgeDemonBoss"},
    vnote="cosmetic: CurseOfKnowledge renamed CurseOfKnowledgeMove; _disintegrationDamageValues {6, 7, 8} in both versions (v0.111.0 value recovered from the macOS decompile)",
    notes="_disintegrationDamageValues (Disintegration damage by CurseOfKnowledgeCounter 0..2) is unreadable in the rule-truth decompile (LdMemberToken, Models.Monsters/KnowledgeDemon.cs:771-774); %s." % REC,
    decompile_broken=True,
    tier=3)

add("LagavulinMatriarch",
    mech=["on spawn: Sleep(): PlatingPower 12 + AsleepPower 3", "SLEEP (SleepIntent) while Asleep; SLEEP_BRANCH -> SLASH once Asleep is gone",
          "Asleep: unblocked damage removes Plating, wakes and stuns into WakeUpMove with next SLASH_MOVE; else it counts down at its turn end and wakes at 0; Plating is dropped early when Asleep <= 1",
          "cycle SLASH -> DISEMBOWEL 2 hits -> SLASH2 attack + block 12 (A8 14) -> SOUL_SIPHON (Strength -2 and Dexterity -2 to targets, Strength +2 self)"],
    kern=["CreatureCmd.Stun(creature, stunMove, nextMoveId) from a power", "side-turn participants (AsleepPower)", "BeforeSideTurnEndVeryEarly hook",
          "ShouldDisappearFromDoom default"],
    deps={"powers": ["PlatingPower", "AsleepPower", "StrengthPower", "DexterityPower"]},
    special={"role": "Underdocks boss LagavulinMatriarchBoss", "death": "AfterDamageReceived/AfterDeath overrides are visual (IsShellAwake, sleeping VFX)"},
    tier=1)

add("LivingFog",
    mech=["ADVANCED_GAS (initial): attack + SmoggyPower 1 to targets (Skills get the Smog affliction; Smog cards cannot be played)",
          "BLOAT: for i < BloatAmount (always 1, setter never called) summon GasBomb into encounter.GetNextSlot when a slot is free, then attack",
          "SUPER_GAS_BLAST attack <-> BLOAT"],
    kern=["CreatureCmd.Add<T>(combatState, slot) mid-combat", "EncounterModel.GetNextSlot"],
    deps={"powers": ["SmoggyPower"], "monsters": ["GasBomb"], "afflictions": ["Smog"]},
    special={"summon": "GasBomb per BLOAT into the next free encounter slot"},
    tier=3)

add("LivingShield",
    mech=["on spawn: RampartPower 25 (at player side turn start, every TurretOperator gains 25 block, skipped during extra turns)",
          "SHIELD_SLAM attack while any ally is alive; else SMASH attack + Strength 3 forever"],
    deps={"powers": ["RampartPower", "StrengthPower"]},
    special={"role": "Glory weak TurretOperatorWeak, protects TurretOperator"},
    tier=1)

add("LouseProgenitor",
    mech=["on spawn: CurlUpPower = CurlBlock 14 (A8 18): first powered card attack that hits it gives that much block after the card resolves, once",
          "cycle WEB_CANNON attack + Frail 2 (initial) -> CURL_AND_GROW block CurlBlock + Strength 5 (A9 7) -> POUNCE attack"],
    deps={"powers": ["CurlUpPower", "FrailPower", "StrengthPower"]},
    vnote="rule-change: CurlAndGrow Strength 5 -> GrowStrength 5 (A9 7)",
    tier=1)

add("MagiKnight",
    mech=["cycle POWER_SHIELD attack + block 5 (A8 9) -> DAMPEN (DampenPower per target, caster added; new instance only if absent) -> RAM attack -> PREP block -> MAGIC_BOMB big attack -> RAM ...",
          "Dampen: downgrades all upgraded player cards while any caster lives; restores upgrades on removal"],
    kern=["power instance internal data holding creature references (DampenPower casters) and card upgrade levels", "CardCmd.Downgrade / CardCmd.Upgrade from a power"],
    deps={"powers": ["DampenPower"]},
    special={"role": "Glory elite KnightsElite"},
    notes="PowerShieldBlock uses ToughEnemies (A8).",
    tier=1)

add("Mawler",
    mech=["CLAW 2-hit (initial)", "RAND {RIP_AND_TEAR CannotRepeat, ROAR Vulnerable 3 UseOnlyOnce, CLAW CannotRepeat}, weight 1 each"],
    deps={"powers": ["VulnerablePower"]}, tier=1)

add("MechaKnight",
    mech=["on spawn: Artifact 3", "cycle CHARGE attack (initial) -> FLAMETHROWER (v0.111.0: attack 8 (A9 12) then 4 Burn into each target's hand) -> WINDUP block 15 + Strength 5 -> HEAVY_CLEAVE attack -> FLAMETHROWER"],
    kern=["CardPileCmd.AddToCombatAndPreview<T> into the hand: per card CreateCard<T> then AddGeneratedCardToCombat (Commands/CardPileCmd.cs:1451-1456); a card added to a hand already holding MaxCardsInHand (10) goes to the owner's discard instead (Commands/CardPileCmd.cs:3405-3409, Entities.Cards/CardPile.cs:35)"],
    deps={"powers": ["ArtifactPower", "StrengthPower"], "cards": ["Burn"]},
    special={"role": "Glory elite MechaKnightElite"},
    vnote="rule-change: FLAMETHROWER gains a SingleAttackIntent and an attack (FlamethrowerDamage 8, A9 12)",
    tier=2)

add("MultiAttackMoveMonster", mech=["POKE 1 x 5 every turn"], special={"role": "test monster, 999 HP, no encounter"}, tier=1)

add("MysteriousKnight",
    mech=["FlailKnight subclass: same graph and HP", "on spawn: Strength 6 and PlatingPower 6 (after FlailKnight's base AfterAddedToRoom)"],
    deps={"powers": ["StrengthPower", "PlatingPower"]},
    special={"role": "MysteriousKnightEventEncounter (event combat)"},
    tier=1)

add("Myte",
    mech=["INIT_MOVE (unregistered conditional initial): slot 'first' -> TOXIC, 'second' -> SUCK",
          "cycle TOXIC (2 Toxic into each target's hand) -> BITE attack -> SUCK attack + Strength 2 (A9 3)"],
    kern=["CardPileCmd.AddToCombatAndPreview<T> into the hand: per card CreateCard<T> then AddGeneratedCardToCombat (Commands/CardPileCmd.cs:1451-1456); a card added to a hand already holding MaxCardsInHand (10) goes to the owner's discard instead (Commands/CardPileCmd.cs:3405-3409, Entities.Cards/CardPile.cs:35)", "ConditionalBranch on Creature.SlotName"],
    deps={"powers": ["StrengthPower"], "cards": ["Toxic"]},
    tier=2)

add("Noisebot",
    mech=["NOISE every turn: per target create 2 Dazed; first to discard, second to a random draw-pile position (RunRng.Shuffle.NextInt(count+1))"],
    kern=["CombatState.CreateCard<T>(player) + CardPileCmd.AddGeneratedCardToCombat (ported)", "CardPilePosition.Random insertion (ported)",
          "Player ?? PetOwner target resolution"],
    rng=["RunRng.Shuffle (random draw-pile position)"],
    deps={"cards": ["Dazed"]},
    special={"role": "Fabricator defense spawn"},
    tier=1)

add("OneHpMonster", mech=["NOTHING (HiddenIntent), 1 HP"], special={"role": "test monster, no encounter"}, tier=1)

add("Osty",
    mech=["Necrobinder pet: NOTHING_MOVE (no intents), 1 HP base (OstyCmd.Summon sets max HP)", "health bar visible only while alive"],
    kern=["Osty/pets subsystem (OstyCmd.Summon, PlayerCmd.AddPet, Creature.PetOwner, ally-side monster)"],
    special={"role": "player-side pet of the Necrobinder; not in any encounter"},
    tier=3)

add("Ovicopter",
    mech=["LAY_EGGS (initial): up to 3 times take the last encounter slot no enemy occupies and summon ToughEgg there with MinionPower",
          "SMASH attack -> TENDERIZER attack + Vulnerable 2 -> SUMMON_BRANCH: CanLay (alive teammates <= 3) ? LAY_EGGS : NUTRITIONAL_PASTE (Strength 3, A9 4) -> SMASH"],
    kern=["CreatureCmd.Add<T>(combatState, slot) mid-combat", "Encounter.Slots scan for the last free slot"],
    deps={"powers": ["MinionPower", "VulnerablePower", "StrengthPower"], "monsters": ["ToughEgg"]},
    special={"summon": "ToughEgg x up to 3 per LAY_EGGS"},
    notes="AfterAddedToRoom/BeforeRemovedFromRoom only start/stop an idle SFX loop. v0.111.0 state-machine source order shows MinionPower before Add; execution order (v0.107.1 async body) is Add then MinionPower.",
    tier=3)

add("OwlMagistrate",
    mech=["cycle MAGISTRATE_SCRUTINY attack -> PECK_ASSAULT 4 x 6 -> JUDICIAL_FLIGHT SoarPower 1 (IsFlying) -> VERDICT attack + Vulnerable 4, remove Soar"],
    deps={"powers": ["SoarPower", "VulnerablePower"]},
    notes="IsFlying only switches SFX and bounds; BeforeRemovedFromRoom is visual.",
    tier=1)

add("PaelsLegion",
    mech=["relic pet: NOTHING_MOVE (no intents), 9999 HP, health bar hidden"],
    kern=["pets on the player side (relic-summoned companion)"],
    special={"role": "pet creature of the PaelsLegion relic; not in any encounter"},
    tier=3)

add("Parafright",
    mech=["on spawn: IllusionPower (adds MinionPower)", "SLAM attack every turn", "revives to full HP through Illusion's runtime REVIVE_MOVE"],
    kern=["runtime-created MoveState via SetMoveImmediate (IllusionPower REVIVE_MOVE)", "dead-but-present creature until revive", "ShouldDisappearFromDoom = false"],
    deps={"powers": ["IllusionPower", "MinionPower"]},
    special={"role": "summoned by TheObscura (TheObscuraNormal)"},
    tier=2)

add("PhantasmalGardener",
    mech=["on spawn: SkittishPower 6 (A8 7): first unblocked card attack each turn gives that much block",
          "INIT_MOVE (unregistered conditional initial) by slot: first FLAIL, second BITE, third LASH, fourth ENLARGE",
          "cycle BITE -> LASH -> FLAIL 1 x 3 -> ENLARGE Strength 2 (A9 3), EnlargeTriggers++ -> BITE"],
    kern=["ConditionalBranch on Creature.SlotName"],
    deps={"powers": ["SkittishPower", "StrengthPower"]},
    special={"role": "Underdocks elite PhantasmalGardenersElite (4 gardeners)"},
    tier=1)

add("PhrogParasite",
    mech=["on spawn: InfestedPower 4", "INFECT (initial): 3 Infection to each target's discard <-> LASH 4-hit attack",
          "RAND is registered but unreachable", "on death (InfestedPower): summon 4 Wrigglers with StartStunned into PhrogParasiteElite.GetWrigglerSlotName(i); combat cannot end until then"],
    kern=["CardPileCmd.AddToCombatAndPreview<T>", "CreatureCmd.Add(monster, side, slot) from a power AfterDeath", "InfestedPower.ShouldStopCombatFromEnding"],
    deps={"powers": ["InfestedPower"], "monsters": ["Wriggler"], "cards": ["Infection"]},
    special={"role": "Overgrowth elite PhrogParasiteElite", "summon": "4 Wrigglers on death"},
    tier=3)

add("PunchConstruct",
    mech=["on spawn: Artifact 1; when StartingHpReduction > 0 set current HP to max(1, HP - reduction) directly (SetCurrentHpInternal, no hooks)",
          "initial FAST_PUNCH when StartsWithFastPunch else READY", "cycle READY block 10 -> FAST_PUNCH 2-hit + Frail 1 -> STRONG_PUNCH attack"],
    kern=["Creature.SetCurrentHpInternal (raw HP write)"],
    deps={"powers": ["ArtifactPower", "FrailPower"]},
    special={"role": "PunchConstructNormal, ConstructMenagerieNormal, PunchOffEventEncounter"},
    tier=1)

add("Queen",
    mech=["on spawn: remembers the TorchHeadAmalgam creature", "PUPPET_STRINGS (CardDebuffIntent): ChainsOfBindingPower 3 to targets",
          "YOU_ARE_MINE: Frail 99, Weak 99, Vulnerable 99 (in that order)",
          "while the Amalgam lives: BURN_BRIGHT_FOR_ME (Strength 1 to every other teammate, block 20) repeating",
          "after the Amalgam dies: OFF_WITH_YOUR_HEAD 5 hits -> EXECUTION attack -> ENRAGE Strength 2 -> OFF_WITH_YOUR_HEAD",
          "AfterDeath(Amalgam): HasAmalgamDied, and if the next move is BURN_BRIGHT_FOR_ME, SetMoveImmediate(ENRAGE)",
          "ChainsOfBinding: during the player's turn up to Amount drawn cards per turn get the Bound affliction; after one Bound card is played no other Bound card can be played; cleared before the player's turn ends"],
    kern=["monster as hook listener: AfterDeath", "SetMoveImmediate from a monster hook", "per-instance creature reference (Amalgam) stored as an id"],
    deps={"powers": ["ChainsOfBindingPower", "FrailPower", "WeakPower", "VulnerablePower", "StrengthPower"], "monsters": ["TorchHeadAmalgam"], "afflictions": ["Bound"]},
    special={"role": "Glory boss QueenBoss (with TorchHeadAmalgam)", "multipart": "Queen + TorchHeadAmalgam (minion)", "death": "BeforeRemovedFromRoom is music"},
    notes="BurnBright strength is GetValueIfAscension(DeadlyEnemies, 1, 1) = 1 at every ascension.",
    tier=2)

add("Rocket",
    mech=["on spawn: SurroundedPower 1 on every opponent, BackAttackRightPower, CrabRagePower",
          "cycle TARGETING_RETICLE -> PRECISION_BEAM -> CHARGE_UP Strength 2 (A9 3) -> LASER big attack -> RECHARGE (SleepIntent, no effect)",
          "Surrounded: the player faces one crab part; damage from the part behind (BackAttackLeft/Right) x1.5; facing follows targeted card/potion use and the survivors after a death"],
    kern=["multi-part boss: two independent monsters", "ShouldDisappearFromDoom = false"],
    deps={"powers": ["SurroundedPower", "BackAttackRightPower", "CrabRagePower", "StrengthPower"]},
    special={"role": "Hive boss KaiserCrabBoss, right arm", "multipart": "with Crusher", "death": "AfterCurrentHpChanged and BeforeDeath drive background visuals only"},
    notes="Effective tier 3 comes only from SurroundedPower (powers.classified T3: per-instance Facing plus Hook.BeforePotionUsed from the potion runtime); the monster code itself is B1, so KaiserCrabBoss can be gated only once that hook exists.",
    tier=1)

add("ScrollOfBiting",
    mech=["on spawn: PaperCutsPower 2 (each powered attack hit with unblocked damage on a player costs 2 max HP: CreatureCmd.LoseMaxHp, persists into the run)", "initial by StarterMoveIdx % 3: CHOMP / CHEW / MORE_TEETH",
          "CHOMP attack -> MORE_TEETH Strength 2 -> CHEW 2-hit -> rand {CHOMP CannotRepeat, CHEW CanRepeatXTimes 2}"],
    kern=["StarterMoveIdx set by the encounter from its Rng", "CreatureCmd.LoseMaxHp from PaperCutsPower (ported); the max-HP loss must flow back to the run state after combat"],
    deps={"powers": ["PaperCutsPower", "StrengthPower"]},
    tier=1)

add("SewerClam", mech=["on spawn: PlatingPower 8 (A8 9)", "JET attack (initial) <-> PRESSURIZE Strength 4"],
    deps={"powers": ["PlatingPower", "StrengthPower"]}, tier=1)

add("SingleAttackMoveMonster", mech=["POKE 1 damage every turn"], special={"role": "test monster, 999 HP, no encounter"}, tier=1)

add("SkulkingColony",
    mech=["on spawn: HardenedShellPower 20 (HP loss per turn capped at 20; counter resets at each side's turn start)", "cycle ZOOM -> ZOOM_2 (same body) -> INERTIA attack + Strength 2 (A9 4) -> PIERCING_STABS 2 hits -> ZOOM"],
    deps={"powers": ["HardenedShellPower", "StrengthPower"]},
    special={"role": "Underdocks elite SkulkingColonyElite"},
    tier=1)

add("SlimedBerserker",
    mech=["cycle VOMIT_ICHOR (10 Slimed to each target's discard) -> FURIOUS_PUMMELING 4 hits -> LEECHING_HUG (Weak 3 with applier null, Strength 3) -> SMOTHER attack -> VOMIT_ICHOR"],
    kern=["Slimed.AddToCombatAndPreview (already ported for the P0 slimes)"],
    deps={"powers": ["WeakPower", "StrengthPower"], "cards": ["Slimed"]},
    tier=1)

add("SlitheringStrangler",
    mech=["CONSTRICT (initial): ConstrictPower 3 to targets", "rand {THWACK attack + block 5, LASH attack} CanRepeatForever weight 1, both back to CONSTRICT"],
    deps={"powers": ["ConstrictPower"]}, tier=1)

add("SlumberingBeetle",
    mech=["on spawn: PlatingPower 15 (A8 18) and SlumberPower 3", "SNORE (SleepIntent) while Slumber holds; SNORE_NEXT -> ROLL_OUT once Slumber is gone",
          "ROLL_OUT attack + Strength 2 forever", "WakeUpMove (called by SlumberPower): IsAwake, remove Plating"],
    kern=["CreatureCmd.Stun(creature, stunMove, nextMoveId) from a power (SlumberPower)"],
    deps={"powers": ["PlatingPower", "SlumberPower", "StrengthPower"]},
    notes="Creature.Died subscription only stops the sleeping VFX.",
    tier=1)

add("SnappingJaxfruit", mech=["ENERGY_ORB every turn: attack 3 (A9 4) + Strength 2"], deps={"powers": ["StrengthPower"]},
    notes="AfterAddedToRoom/BeforeRemovedFromRoom are visual.", tier=1)

add("SneakyGremlin", mech=["SPAWNED_MOVE (StunIntent, no effect) -> TACKLE attack forever"],
    special={"role": "spawned by GremlinMerc's SurprisePower into slot 'sneaky'"}, tier=1)

add("SoulFysh",
    mech=["cycle BECKON (per target: one Beckon to a random draw-pile position, one to discard) -> DE_GAS attack -> GAZE attack + 1 Beckon to discard -> FADE (IntangiblePower 2) -> SCREAM attack + Vulnerable 3 -> BECKON"],
    kern=["CombatState.CreateCard<T> + CardPileCmd.AddGeneratedCardToCombat (ported)", "CardPilePosition.Random insertion (ported)"],
    rng=["RunRng.Shuffle (random draw-pile position)"],
    deps={"powers": ["IntangiblePower", "VulnerablePower"], "cards": ["Beckon"]},
    special={"role": "Underdocks boss SoulFyshBoss",
             "death": "AfterDeath and AfterCardChangedPilesLate overrides only drive visuals for the local player's Beckon cards"},
    vnote="rule-change: DeGasDamage A9 17 -> 18",
    tier=1)

add("SoulNexus",
    mech=["SOUL_BURN attack (initial)", "RAND {SOUL_BURN, MAELSTROM 4 hits, DRAIN_LIFE attack + Vulnerable 2 + Weak 2} CannotRepeat weight 1"],
    deps={"powers": ["VulnerablePower", "WeakPower"]},
    special={"role": "Glory elite SoulNexusElite"},
    tier=1)

add("SpectralKnight",
    mech=["HEX (initial): HexPower 2 per target (all its cards get the Hexed affliction = Ethereal; removed when the applier dies)",
          "SOUL_SLASH attack -> RAND {SOUL_SLASH CanRepeatXTimes 2, SOUL_FLAME 3 hits CannotRepeat}"],
    deps={"powers": ["HexPower"], "afflictions": ["Hexed"]},
    special={"role": "Glory elite KnightsElite"},
    tier=1)

add("SpinyToad",
    mech=["cycle PROTRUDING_SPIKES (Thorns +5) -> SPIKE_EXPLOSION attack, Thorns -5 -> TONGUE_LASH attack"],
    deps={"powers": ["ThornsPower"]},
    vnote="cosmetic: the AfterAddedToRoom override that only called base was removed",
    tier=1)

add("Stabbot", mech=["STAB every turn: attack + Frail 1"], deps={"powers": ["FrailPower"]},
    special={"role": "Fabricator aggro spawn"}, notes="AfterAddedToRoom only positions the node.", tier=1)

add("TenHpMonster", mech=["NOTHING (HiddenIntent), 10 HP"], special={"role": "test monster, no encounter"}, tier=1)

add("TerrorEel",
    mech=["on spawn: ShriekPower 70 (A8 75)", "CRASH attack <-> THRASH 3 hits + VigorPower 6",
          "Shriek: once unblocked damage leaves HP <= 70/75, Stun with next TERROR_MOVE and remove Shriek; TERROR: Vulnerable 99 -> CRASH",
          "STUN_MOVE is registered but unreachable (Stun creates its own STUNNED state)"],
    kern=["CreatureCmd.Stun(creature, nextMoveId) from a power (ported)"],
    deps={"powers": ["ShriekPower", "VigorPower", "VulnerablePower"]},
    special={"role": "Underdocks elite TerrorEelElite"},
    tier=1)

add("TestSubject",
    mech=["on spawn: AdaptablePower 1, EnragePower 2 (A9 3)", "form 1: BITE attack <-> SKULL_BASH attack + Vulnerable 1",
          "death with Adaptable: stays in combat (untargetable), RunState.ExtraFields.TestSubjectKills++, SetMoveImmediate(RESPAWN_MOVE, force)",
          "RESPAWN_MOVE (MustPerformOnce): Respawns++, Adaptable.DoRevive; 1st: Revive(SecondFormHp 200/A8 212) + PainfulStabsPower 1; 2nd: Revive(ThirdFormHp 300/A8 313) + NemesisPower 1, remove Adaptable and PainfulStabs",
          "Revive(hp): scaled hp -> SetMaxHp then Heal", "REVIVE_BRANCH: Respawns < 2 -> MULTI_CLAW (hits 3 + ExtraMultiClawCount, +1 per use) forever; else PHASE3_LACERATE 3 hits -> BIG_POUNCE 45 -> BURNING_GROWL (Burn x3 (A9 5) to discard, Strength 2 (A9 3)) -> PHASE3_LACERATE",
          "ShouldDisappearFromDoom only once Respawns >= 2"],
    kern=["dead-but-present creature + revive (AdaptablePower ShouldAllowHitting/ShouldCreatureBeRemovedFromCombatAfterDeath/ShouldStopCombatFromEnding)",
          "SetMoveImmediate(forceTransition) from a power hook", "CreatureCmd.SetMaxHp + Heal on revive", "intent hit count from a lambda (MultiAttackIntent(dmg, () => count))",
          "RunState.ExtraFields.TestSubjectKills++ (statistics counter, no rule effect)", "CardPileCmd.AddToCombatAndPreview<T>", "dynamic ShouldDisappearFromDoom"],
    deps={"powers": ["AdaptablePower", "EnragePower", "PainfulStabsPower", "NemesisPower", "IntangiblePower", "VulnerablePower", "StrengthPower"], "cards": ["Burn", "Wound"]},
    special={"role": "Glory boss TestSubjectBoss", "phases": "3 forms (100/200/300 HP at A0), revives twice",
             "death": "AfterDeath override is colour only; PowerApplied/PowerRemoved subscriptions are colour only"},
    tier=3)

add("TheAdversaryMkOne",
    mech=["on spawn: Artifact 0 (applied with amount 0)", "cycle SMASH 12 -> BEAM 15 -> BARRAGE 8 x 2 + Strength 2"],
    deps={"powers": ["ArtifactPower", "StrengthPower"]},
    special={"role": "not listed in any encounter or act (unused in v0.111.0)"}, tier=1)
add("TheAdversaryMkTwo",
    mech=["on spawn: Artifact 1", "cycle BASH 13 -> FLAME_BEAM 16 -> BARRAGE 9 x 2 + Strength 3", "FlameBeamStatusCount is declared but unused"],
    deps={"powers": ["ArtifactPower", "StrengthPower"]},
    special={"role": "not listed in any encounter or act (unused in v0.111.0)"}, tier=1)
add("TheAdversaryMkThree",
    mech=["on spawn: Artifact 2", "cycle CRASH 15 -> FLAME_BEAM 18 -> BARRAGE 10 x 2 + Strength 4"],
    deps={"powers": ["ArtifactPower", "StrengthPower"]},
    special={"role": "not listed in any encounter or act (unused in v0.111.0)"}, tier=1)

add("TheForgotten",
    mech=["on spawn: PossessSpeedPower (applier null; base.AfterAddedToRoom not called)",
          "MIASMA (initial): Dexterity -2 to targets, block 8, Dexterity +2 self <-> DREAD attack = 13 (A9 15) + own Dexterity (intent damage lambda)",
          "PossessSpeed: on death returns the stolen Dexterity to each player"],
    kern=["attack intent damage from a lambda (SingleAttackIntent(() => DreadDamage))", "read own power amount (GetPowerAmount<DexterityPower>)"],
    deps={"powers": ["PossessSpeedPower", "DexterityPower"]},
    special={"role": "Glory normal TheLostAndForgottenNormal (with TheLost)"},
    tier=1)

add("TheInsatiable",
    mech=["LIQUIFY_GROUND (initial, once): per target a SandpitPower(4) instance on itself with Target = that player; then per target 6 FranticEscape cards: 3 to random draw-pile positions, 3 to random discard positions",
          "Sandpit: decrements at each enemy turn start; reaching 0 while both sides live kills the player and Osty (force)",
          "cycle THRASH 2 hits -> LUNGING_BITE -> SALIVATE Strength 2 (A9 3) -> THRASH_2 -> THRASH"],
    kern=["instanced power per player with Target (SandpitPower)", "CreatureCmd.Kill(force) of the player from a power", "CardPilePosition.Random insertion into draw and discard (ported)",
          "AfterSideTurnStartLate hook"],
    rng=["RunRng.Shuffle (random pile positions, 6 per target)"],
    deps={"powers": ["SandpitPower", "StrengthPower"], "cards": ["FranticEscape"]},
    special={"role": "Hive boss TheInsatiableBoss", "death": "AfterDeath is music only"},
    tier=1)

add("TheLost",
    mech=["on spawn: PossessStrengthPower (applier null; base.AfterAddedToRoom not called)",
          "DEBILITATING_SMOG (initial): Strength -2 to targets, Strength +2 self <-> EYE_LASERS 2 hits",
          "PossessStrength: on death returns the stolen Strength"],
    deps={"powers": ["PossessStrengthPower", "StrengthPower"]},
    special={"role": "Glory normal TheLostAndForgottenNormal (with TheForgotten)"},
    tier=1)

add("TheObscura",
    mech=["ILLUSION (initial): summon Parafright into slot 'illusion' (when IsLiveCombat)",
          "RAND {PIERCING_GAZE attack, SAIL_MOVE (WailMove: Strength 3 to every teammate incl. itself), HARDENING_STRIKE attack + block} CannotRepeat"],
    kern=["CreatureCmd.Add<T>(combatState, slot) mid-combat", "PowerCmd.Apply to a list of teammates"],
    deps={"powers": ["StrengthPower"], "monsters": ["Parafright"]},
    special={"summon": "Parafright (slot 'illusion')"},
    tier=3)

add("ThievingHopper",
    mech=["on spawn: EscapeArtistPower 5 (counter display only)",
          "THIEVERY (initial): per living target pick one deck-backed card (DeckVersion != null) from draw+discard by priority (Uncommon non-Imbued; Common/Rare/Event non-Imbued; Basic/Quest non-Imbued; Ancient or Imbued) via RunRng.CombatCardGeneration.NextItem, RemoveFromCombat; each stolen card goes into a new SwipePower (removes the DeckVersion from the deck); then attack",
          "FLUTTER FlutterPower 5 -> HAT_TRICK attack -> NAB attack -> ESCAPE (CreatureCmd.Escape) repeated",
          "Swipe: killing the hopper puts the card back into the deck and adds a SpecialCardReward to the room"],
    kern=["CreatureCmd.Escape", "CardPileCmd.RemoveFromDeck (run deck mutation inside combat)", "card DeckVersion link", "card Enchantment/Rarity queries (Imbued)",
          "RunRng.CombatCardGeneration draw inside a monster move", "SpecialCardReward / CombatRoom.AddExtraReward (run layer)", "instanced power holding a card reference (SwipePower.StolenCard)"],
    rng=["RunRng.CombatCardGeneration (card to steal)"],
    deps={"powers": ["EscapeArtistPower", "SwipePower", "FlutterPower"], "enchantments": ["Imbued"]},
    special={"role": "Hive weak ThievingHopperWeak", "death": "escapes on its 5th move if not killed"},
    tier=3)

add("TorchHeadAmalgam",
    mech=["on spawn: MinionPower", "STRONG_TACKLE (initial, once) -> TACKLE_2 -> BEAM 8 x 3 -> TACKLE_3 -> TACKLE_4 (weak tackles) -> BEAM ..."],
    deps={"powers": ["MinionPower"]},
    special={"role": "QueenBoss companion (minion); its death switches Queen to the execution phase",
             "death": "OnDieToDoom override hides torch lights (visual)"},
    vnote="rule-change: initial TACKLE_MOVE (TackleDamage) replaced by STRONG_TACKLE_MOVE (26, A9 32); TackleDamage A9 19 -> 22; WeakTackleDamage A9 15 -> 16",
    tier=1)

add("ToughEgg",
    mech=["on spawn (not hatched): HatchPower 1, or 2 when spawned during the enemy side's turn (countdown display)",
          "HATCH_MOVE (SummonIntent, initial): IsHatched, remove every non-Minion power, Hatch(): SetMaxAndCurrentHp(RunRng.Niche.NextInt(HatchlingMinHp, HatchlingMaxHp + 1))",
          "NIBBLE attack forever", "spawned already hatched: Hatch() at once and ForceCurrentState(NIBBLE)"],
    kern=["CombatState.CurrentSide at spawn", "CreatureCmd.SetMaxAndCurrentHp", "ForceCurrentState from AfterAddedToRoom",
          "RunRng.Niche draw inside a monster move", "Creature.ScaleHpForMultiplayer (identity for 1 player)"],
    rng=["RunRng.Niche (hatchling HP roll)"],
    deps={"powers": ["HatchPower", "MinionPower"]},
    special={"role": "laid by Ovicopter (OvicopterNormal)", "summon": "spawned mid-combat by Ovicopter"},
    notes="Its private MonsterModel.Rng is read only in SetupSkins (visual).",
    vnote="rule-change: hatchling HP roll NextInt(HatchlingMinHp, HatchlingMaxHp) -> NextInt(HatchlingMinHp, HatchlingMaxHp + 1) (max now reachable)",
    tier=2)

add("TrackerRubyRaider", mech=["TRACK (initial): Frail 2", "HOUNDS 1 x 8 (A9 x 9) forever"], deps={"powers": ["FrailPower"]}, tier=1)

add("Tunneler",
    mech=["cycle BITE -> BURROW (BurrowedPower + block 32, A8 37) -> BELOW attack forever",
          "Burrowed: block is not cleared at turn start; when the block breaks, Stun with StillDizzyMove (next BITE) and remove Burrowed (losing all block)",
          "DIZZY_MOVE registered (StunIntent) -> BITE"],
    kern=["CreatureCmd.Stun(creature, stunMove, nextMoveId) from a power (ported)", "Hook.ShouldClearBlock / AfterBlockBroken listeners"],
    deps={"powers": ["BurrowedPower"]},
    vnote="cosmetic: a bestiary FromStun entry and visual node offsets",
    tier=1)

add("TurretOperator", mech=["cycle UNLOAD 5 hits -> UNLOAD_2 5 hits -> RELOAD Strength 1"], deps={"powers": ["StrengthPower"]},
    special={"role": "Glory weak TurretOperatorWeak (shielded by LivingShield's Rampart)"}, tier=1)

add("TwoTailedRat",
    mech=["initial RAND when StarterMoveIndex == -1, else SCRATCH / DISEASE_BITE / SCREECH by index % 3",
          "RAND {SCRATCH, DISEASE_BITE, SCREECH (cooldown 3)} CannotRepeat with weight 1 (1/12 while CanSummon) and CALL_FOR_BACKUP UseOnlyOnce weight 0.75 while CanSummon else 0",
          "SCRATCH/DISEASE_BITE attacks and SCREECH (Frail 1) decrement TurnsUntilSummonable (starts 2)",
          "CALL_FOR_BACKUP: summon a TwoTailedRat into the last free slot, then every rat's CallForBackupCount = max(count + 1)",
          "CanSummon: TurnsUntilSummonable <= 0, CallForBackupCount < 3, a free slot exists, no other teammate's next move is CALL_FOR_BACKUP"],
    kern=["CreatureCmd.Add<T>(combatState, slot) mid-combat", "weight lambdas reading monster state and other monsters' NextMove", "Encounter.Slots / GetNextSlot"],
    deps={"powers": ["FrailPower"]},
    special={"summon": "TwoTailedRat (itself) up to 3 calls in total"},
    tier=3)

add("Vantom",
    mech=["on spawn: SlipperyPower 8 (A8 9)", "cycle INK_BLOT attack -> INKY_LANCE 2 hits -> DISMEMBER attack + 3 Wound to discard -> PREPARE Strength 2"],
    kern=["CardPileCmd.AddToCombatAndPreview<T>", "ShouldDisappearFromDoom = false"],
    deps={"powers": ["SlipperyPower", "StrengthPower"], "cards": ["Wound"]},
    special={"role": "Overgrowth boss VantomBoss", "death": "AfterDeath is music only"},
    tier=2)

add("VineShambler",
    mech=["cycle SWIPE 2 hits (initial) -> GRASPING_VINES attack + TangledPower 1 -> CHOMP attack",
          "Tangled: every Attack card gets the Entangled affliction (+1 energy cost) until the player's turn ends"],
    deps={"powers": ["TangledPower"], "afflictions": ["Entangled"]},
    tier=1)

add("WaterfallGiant",
    mech=["on spawn: CurrentPressureGunDamage = 20 (A9 23)", "PRESSURIZE (initial): SteamEruptionPower 15 (A9 20)",
          "cycle STOMP attack + Weak 1 -> RAM attack -> SIPHON heal 10 (A8 15) x players -> PRESSURE_GUN attack (then +5) -> PRESSURE_UP attack -> STOMP; every move adds SteamEruption 3",
          "death with SteamEruption: stays in combat, TriggerAboutToBlowState: max/current HP 999999999, SetMoveImmediate(ABOUT_TO_BLOW, force)",
          "ABOUT_TO_BLOW (StunIntent, MustPerformOnce): store SteamEruption amount, remove it -> EXPLODE (DeathBlowIntent): attack for that amount, then CreatureCmd.Kill(self)"],
    kern=["dead-but-present creature (SteamEruptionPower) re-armed with 999999999 HP", "SetMoveImmediate(forceTransition) from a power hook",
          "intent damage from a lambda (PressureGun, Explode)", "dynamic ShouldDisappearFromDoom", "Creature.HpDisplay (visual)"],
    deps={"powers": ["SteamEruptionPower", "WeakPower"]},
    special={"role": "Underdocks boss WaterfallGiantBoss", "death": "explodes one turn after its first death"},
    tier=3)

add("Wriggler",
    mech=["StartStunned: SPAWNED_MOVE (StunIntent, no effect) first, then INIT_MOVE", "INIT_MOVE by slot: wriggler1/3 NASTY_BITE, wriggler2/4 WRIGGLE",
          "NASTY_BITE attack <-> WRIGGLE (1 Infection to each target's discard, Strength 2)"],
    kern=["CardPileCmd.AddToCombatAndPreview<T>", "ConditionalBranch on Creature.SlotName"],
    deps={"powers": ["StrengthPower"], "cards": ["Infection"]},
    special={"role": "PhrogParasite death spawn; DenseVegetationEventEncounter"},
    tier=2)

add("Zapbot", mech=["on spawn: HighVoltagePower 2 (Strength +2 at each of its turn ends)", "ZAP attack every turn"],
    deps={"powers": ["HighVoltagePower"]}, special={"role": "Fabricator aggro spawn"}, tier=1)

_P = "ported in P0 (sim/Sts2Sim.Core/Monsters/Defs); spec in sim/specs/"
add("CorpseSlug",
    mech=["on spawn: RavenousPower 4 (A9 5)", "cycle WHIP_SLAP 3 x 2 -> GLOMP attack -> GOOP Frail 2", "initial by StarterMoveIdx % 3 (encounter Rng staggers the slugs)"],
    deps={"powers": ["RavenousPower", "FrailPower"]}, notes=_P + "monsters-corpseslugs-seapunk.md", ported=True, tier=1)
add("FuzzyWurmCrawler",
    mech=["cycle FIRST_ACID_GOOP attack -> INHALE Strength 7 -> ACID_GOOP attack -> FIRST_ACID_GOOP"],
    deps={"powers": ["StrengthPower"]}, notes=_P + "monsters-wurm-beetle.md", ported=True, tier=1)
add("LeafSlimeM",
    mech=["STICKY_SHOT (initial): 2 Slimed to each target's discard <-> CLUMP_SHOT attack"],
    kern=["Slimed.AddToCombatAndPreview (ported)"], deps={"cards": ["Slimed"]}, notes=_P + "monsters-slimes-weak.md", ported=True, tier=1)
add("LeafSlimeS",
    mech=["initial RAND {TACKLE attack, GOOP 1 Slimed to discard} CannotRepeat; both back to RAND"],
    kern=["Slimed.AddToCombatAndPreview (ported)"], deps={"cards": ["Slimed"]}, notes=_P + "monsters-slimes-weak.md", ported=True, tier=1)
add("Nibbit",
    mech=["INIT_MOVE: alone -> BUTT; else HISS when not front, SLICE when front", "cycle BUTT attack -> SLICE attack + block -> HISS Strength 2 (A9 3)"],
    deps={"powers": ["StrengthPower"]}, notes=_P + "monsters.md", ported=True, tier=1)
add("Seapunk",
    mech=["cycle SEA_KICK attack -> SPINNING_KICK 2 x 4 -> BUBBLE_BURP block 7 (A8 8) + Strength 1 (A9 2)"],
    deps={"powers": ["StrengthPower"]}, notes=_P + "monsters-corpseslugs-seapunk.md", ported=True, tier=1)
add("ShrinkerBeetle",
    mech=["SHRINKER (initial, strong DebuffIntent): ShrinkPower -1 to targets", "CHOMP attack <-> STOMP attack"],
    deps={"powers": ["ShrinkPower"]}, notes=_P + "monsters-wurm-beetle.md", ported=True, tier=1)
add("SludgeSpinner",
    mech=["OIL_SPRAY (initial) attack + Weak 1", "RAND {OIL_SPRAY, SLAM, RAGE attack + Strength 3} CannotRepeat"],
    deps={"powers": ["WeakPower", "StrengthPower"]}, notes=_P + "monsters-underdocks-weak.md", ported=True, tier=1)
add("Toadpole",
    mech=["INIT_MOVE: front -> SPIKEN, else WHIRL", "cycle SPIKE_SPIT (Thorns -2, then 3 hits) -> WHIRL attack -> SPIKEN Thorns +2 -> SPIKE_SPIT"],
    deps={"powers": ["ThornsPower"]}, notes=_P + "monsters-underdocks-weak.md", ported=True, tier=1)
add("TwigSlimeM",
    mech=["STICKY_SHOT (initial): 1 Slimed to each target's discard", "RAND {POKEY_POUNCE attack CanRepeatXTimes 2, STICKY_SHOT CannotRepeat}"],
    kern=["Slimed.AddToCombatAndPreview (ported)"], deps={"cards": ["Slimed"]}, notes=_P + "monsters-slimes-weak.md", ported=True, tier=1)
add("TwigSlimeS", mech=["TACKLE attack every turn"], notes=_P + "monsters-slimes-weak.md", ported=True, tier=1)

_MOCK = "Mock monster (IsMock); the game's own test seam. Port only if the sim adopts the same test seams; not content."
add("MockArtifactMonster", mech=["on spawn: Artifact 1", "NOTHING (HiddenIntent), 9999 HP"], deps={"powers": ["ArtifactPower"]}, special={"role": "MockArtifactEncounter"}, notes=_MOCK, tier=1)
add("MockAttackAndSummonMinionMonster", mech=["ATTACK_AND_SUMMON_MINION: attack 1, summon BigDummy, MinionPower on it"],
    kern=["CreatureCmd.Add<T>(combatState) mid-combat"], deps={"powers": ["MinionPower"], "monsters": ["BigDummy"]}, special={"role": "MockAttackAndSummonEncounter"}, notes=_MOCK, tier=3)
add("MockAttackMonster", mech=["ATTACK 1 every turn, 9999 HP"], special={"role": "no encounter"}, notes=_MOCK, tier=1)
add("MockIntangibleMonster", mech=["INTANGIBLE (IntangiblePower 2) <-> NOTHING"], deps={"powers": ["IntangiblePower"]}, special={"role": "no encounter"}, notes=_MOCK, tier=1)
add("MockPlatingMonster", mech=["on spawn: PlatingPower PlatingAmount (set by the encounter)", "NOTHING"], deps={"powers": ["PlatingPower"]}, special={"role": "MockPlatingEncounter"}, notes=_MOCK, tier=1)
add("MockReattachMonster", mech=["on spawn: ReattachPower 1", "NOTHING; DEAD_MOVE -> REATTACH_MOVE (DoReattach) -> NOTHING, wired through object initializers"], deps={"powers": ["ReattachPower"]},
    special={"role": "no encounter"}, notes=_MOCK, tier=3)
