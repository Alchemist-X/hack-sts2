# W3 content index — orbs-ench-affl (wave A)

Orbs (pre-existing W1 pilot ports, not stubs; left untouched):
- DARK_ORB — Models.Orbs/DarkOrb.cs:18 — W1 pilot, unchanged
- FROST_ORB — Models.Orbs/FrostOrb.cs:19 — W1 pilot, unchanged
- GLASS_ORB — Models.Orbs/GlassOrb.cs:16 — W1 pilot, unchanged
- LIGHTNING_ORB — Models.Orbs/LightningOrb.cs:17 — W1 pilot, unchanged
- PLASMA_ORB — Models.Orbs/PlasmaOrb.cs:14 — W1 pilot, unchanged

Enchantments (sim/Sts2Sim.Core/Cards/Enchantments/Defs/):
- ADROIT — Models.Enchantments/Adroit.cs:15 — HasExtraCardText/ShowAmount UI dropped
- CLONE — Models.Enchantments/Clone.cs:3 — marker; effect lives in CloneRestSiteOption (run layer)
- CORRUPTED — Models.Enchantments/Corrupted.cs:15 — UI flags dropped
- GLAM — Models.Enchantments/Glam.cs:11 — W1 pilot, unchanged
- GOOPY — Models.Enchantments/Goopy.cs:10 — DeckVersion via CardInstance.FindDeckVersion(); hover tips dropped
- IMBUED — Models.Enchantments/Imbued.cs:13 — PlayerCombatState.TurnNumber -> PlayerState.TurnNumber
- INKY — Models.Enchantments/Inky.cs:17 — CombatState.HittableEnemies -> cs.GetHittableEnemies
- INSTINCT — Models.Enchantments/Instinct.cs:6 — none
- MOMENTUM — Models.Enchantments/Momentum.cs:8 — ExtraDamage written in canonical extra
- NIMBLE — Models.Enchantments/Nimble.cs:3 — none
- PERFECT_FIT — Models.Enchantments/PerfectFit.cs:6 — no owner check, as in the game
- ROYALLY_APPROVED — Models.Enchantments/RoyallyApproved.cs:7 — hover tips dropped
- SHARP — Models.Enchantments/Sharp.cs:6 — none
- SLITHER — Models.Enchantments/Slither.cs:10 — TestEnergyCostOverride (TestMode) and NCard anim dropped; Keywords read with cs=null in CanEnchant
- SOULS_POWER — Models.Enchantments/SoulsPower.cs:8 — local keyword sources read with cs=null
- SOWN — Models.Enchantments/Sown.cs:13 — none
- SPIRAL — Models.Enchantments/Spiral.cs:9 — replay hover tip dropped
- STEADY — Models.Enchantments/Steady.cs:7 — hover tip dropped
- SWIFT — Models.Enchantments/Swift.cs:14 — none
- TEZCATARAS_EMBER — Models.Enchantments/TezcatarasEmber.cs:9 — hover tip dropped
- VIGOROUS — Models.Enchantments/Vigorous.cs:9 — none

Afflictions (sim/Sts2Sim.Core/Cards/Afflictions/Defs/):
- BOUND — Models.Afflictions/Bound.cs:3 — marker; effect in ChainsOfBindingPower
- ENTANGLED — Models.Afflictions/Entangled.cs:3 — marker; effect in TangledPower
- GALVANIZED — Models.Afflictions/Galvanized.cs:3 — marker, IsStackable; effect in GalvanicPower
- HEXED — Models.Afflictions/Hexed.cs:10 — W1 pilot, unchanged
- RINGING — Models.Afflictions/Ringing.cs:3 — marker; effect in RingingPower
- SMOG — Models.Afflictions/Smog.cs:3 — marker; effect in SmoggyPower
- TAINTED — Models.Afflictions/Tainted.cs:8 — marker, IsStackable, Skill only; hover tips dropped; effect in VitalSparkPower
