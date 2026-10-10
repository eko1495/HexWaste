# fo2ce RNG call-site audit (2026-10)

Base commit: fallout2-ce `e97087b` (line numbers are from that commit). This table drives which
call sites the oracle patch switches to `randomBetweenCosmetic()` (separate Park-Miller stream, never
traced); every other site stays on the traced logic stream via the `randomBetween` macro.

Classification rule: **cosmetic** = the draw's timing is driven by wall-clock timers or rendering AND its
result changes nothing but visuals/audio. **logic** = everything else; ambiguous => logic.
`randomRoll()` (random.cc) calls `randomBetween` internally and is logic; its callers (combat, skill) are
therefore not listed separately.

Totals: 79 sites, 7 cosmetic, 72 logic.

| file:line | function | class | reason |
|---|---|---|---|
| actions.cc:363 | _show_damage_to_object | logic | knockback/explosion/damage in action resolution |
| actions.cc:364 | _show_damage_to_object | logic | knockback/explosion/damage in action resolution |
| actions.cc:1813 | _compute_explosion_damage | logic | knockback/explosion/damage in action resolution |
| actions.cc:1977 | _compute_dmg_damage | logic | knockback/explosion/damage in action resolution |
| animation.cc:3071 | _dude_fidget | cosmetic | idle-animation picker, fires off the `_get_bk_time()` wall-clock timer; picks which on-screen critter plays an ANIM_STAND flourish (visual + sfx only) |
| animation.cc:3109 | _dude_fidget | cosmetic | sets the wall-clock delay (`nextTime`, compared with `_get_bk_time()`) until the next idle animation; timing only |
| combat.cc:2882 | _combat_give_exps | logic | combat resolution (hit, crit, damage, knockback, flavour messages tied to attacks) |
| combat.cc:2886 | _combat_give_exps | logic | combat resolution (hit, crit, damage, knockback, flavour messages tied to attacks) |
| combat.cc:3491 | _combat_attack | logic | combat resolution (hit, crit, damage, knockback, flavour messages tied to attacks) |
| combat.cc:3598 | _check_ranged_miss | logic | combat resolution (hit, crit, damage, knockback, flavour messages tied to attacks) |
| combat.cc:3654 | _shoot_along_path | logic | combat resolution (hit, crit, damage, knockback, flavour messages tied to attacks) |
| combat.cc:3802 | attackComputeEnhancedKnockout | logic | combat resolution (hit, crit, damage, knockback, flavour messages tied to attacks) |
| combat.cc:3858 | attackCompute | logic | combat resolution (hit, crit, damage, knockback, flavour messages tied to attacks) |
| combat.cc:3880 | attackCompute | logic | combat resolution (hit, crit, damage, knockback, flavour messages tied to attacks) |
| combat.cc:3892 | attackCompute | logic | combat resolution (hit, crit, damage, knockback, flavour messages tied to attacks) |
| combat.cc:3942 | attackCompute | logic | combat resolution (hit, crit, damage, knockback, flavour messages tied to attacks) |
| combat.cc:3947 | attackCompute | logic | combat resolution (hit, crit, damage, knockback, flavour messages tied to attacks) |
| combat.cc:4102 | attackComputeCriticalHit | logic | combat resolution (hit, crit, damage, knockback, flavour messages tied to attacks) |
| combat.cc:4203 | attackComputeCriticalFailure | logic | combat resolution (hit, crit, damage, knockback, flavour messages tied to attacks) |
| combat.cc:4278 | _do_random_cripple | logic | combat resolution (hit, crit, damage, knockback, flavour messages tied to attacks) |
| combat.cc:4642 | attackComputeDamage | logic | combat resolution (hit, crit, damage, knockback, flavour messages tied to attacks) |
| combat_ai.cc:1030 | _ai_check_drugs | logic | AI decision / AI taunt selection in combat (game-time driven; message choice kept on logic stream as ambiguous) |
| combat_ai.cc:1100 | _ai_check_drugs | logic | AI decision / AI taunt selection in combat (game-time driven; message choice kept on logic stream as ambiguous) |
| combat_ai.cc:1825 | _ai_best_weapon | logic | AI decision / AI taunt selection in combat (game-time driven; message choice kept on logic stream as ambiguous) |
| combat_ai.cc:2291 | _ai_pick_hit_mode | logic | AI decision / AI taunt selection in combat (game-time driven; message choice kept on logic stream as ambiguous) |
| combat_ai.cc:2316 | _ai_pick_hit_mode | logic | AI decision / AI taunt selection in combat (game-time driven; message choice kept on logic stream as ambiguous) |
| combat_ai.cc:2641 | _ai_called_shot | logic | AI decision / AI taunt selection in combat (game-time driven; message choice kept on logic stream as ambiguous) |
| combat_ai.cc:2656 | _ai_called_shot | logic | AI decision / AI taunt selection in combat (game-time driven; message choice kept on logic stream as ambiguous) |
| combat_ai.cc:3324 | _combatai_msg | logic | AI decision / AI taunt selection in combat (game-time driven; message choice kept on logic stream as ambiguous) |
| combat_ai.cc:3367 | _combatai_msg | logic | AI decision / AI taunt selection in combat (game-time driven; message choice kept on logic stream as ambiguous) |
| combat_ai.cc:3422 | _combat_ai_random_target | logic | AI decision / AI taunt selection in combat (game-time driven; message choice kept on logic stream as ambiguous) |
| critter.cc:533 | _critter_check_rads | logic | queues a radiation game event (game state) |
| endgame.cc:1147 | endgameSetupDeathEnding | logic | selects the death ending (game outcome) |
| game_dialog.cc:2505 | _gdSetupFidget | cosmetic | talking-head fidget choice; runs from the dialog render loop on real-time timers; changes only the head animation |
| game_dialog.cc:2865 | gameDialogTicker | cosmetic | talking-head fidget wait (`_tocksWaiting`, compared with `getTicksSince`); wall-clock timing only |
| game_dialog.cc:3654 | _gdPickAIUpdateMsg | logic | AI update message choice during dialog/combat control; not wall-clock timed => logic |
| game_dialog.cc:3658 | _gdPickAIUpdateMsg | logic | AI update message choice during dialog/combat control; not wall-clock timed => logic |
| game_sound.cc:2125 | ambientSoundEffectEventProcess | logic | ambient sfx event delay; queued on the game-time event queue (queue contents are game state), so ambiguous => logic |
| interpreter_extra.cc:782 | opRandom | logic | script `random()` opcode |
| item.cc:1290 | weaponGetDamage | logic | item damage / drug effect / addiction |
| item.cc:2674 | _perform_drug_effect | logic | item damage / drug effect / addiction |
| item.cc:2838 | _item_d_take_drug | logic | item damage / drug effect / addiction |
| map.cc:1180 | _map_age_dead_critters | logic | blood-splat frame on critter death; spawns an object (game state) |
| message.cc:557 | messageListFilterBadwords | logic | badword replacement index; ambiguous => logic |
| party_member.cc:1528 | _partyMemberIncLevels | logic | party level-up roll |
| pipboy.cc:2274 | pipboyRenderScreensaver | cosmetic | Pipboy 'alpha mission' screensaver loop, paced by `getTicks()`; drives only the bomb sprite animation |
| pipboy.cc:2289 | pipboyRenderScreensaver | cosmetic | same screensaver loop; bomb sprite position (visual only) |
| pipboy.cc:2300 | pipboyRenderScreensaver | cosmetic | same screensaver loop; bomb sprite velocity (visual only) |
| proto_instance.cc:212 | _obj_look_at_func | logic | item use / trap outcome |
| proto_instance.cc:1186 | _protinst_default_use_item | logic | item use / trap outcome |
| proto_instance.cc:1342 | _protinst_use_item_on | logic | item use / trap outcome |
| random.cc:87 | randomRoll | logic | inside random.cc (roll helpers / self-test); logic stream |
| random.cc:115 | randomTranslateRoll | logic | inside random.cc (roll helpers / self-test); logic stream |
| random.cc:124 | randomTranslateRoll | logic | inside random.cc (roll helpers / self-test); logic stream |
| random.cc:233 | randomValidatePrerandom | logic | inside random.cc (roll helpers / self-test); logic stream |
| skill.cc:579 | skillUse | logic | skill outcome / heal amount / message selection tied to skill use |
| skill.cc:591 | skillUse | logic | skill outcome / heal amount / message selection tied to skill use |
| skill.cc:610 | skillUse | logic | skill outcome / heal amount / message selection tied to skill use |
| skill.cc:680 | skillUse | logic | skill outcome / heal amount / message selection tied to skill use |
| skill.cc:692 | skillUse | logic | skill outcome / heal amount / message selection tied to skill use |
| skill.cc:764 | skillUse | logic | skill outcome / heal amount / message selection tied to skill use |
| skill.cc:870 | skillUse | logic | skill outcome / heal amount / message selection tied to skill use |
| skill.cc:943 | skillUse | logic | skill outcome / heal amount / message selection tied to skill use |
| skill.cc:1215 | skillsGetGenericResponse | logic | skill outcome / heal amount / message selection tied to skill use |
| stat.cc:711 | statRoll | logic | stat roll |
| worldmap.cc:3416 | wmRndEncounterOccurred | logic | worldmap encounters / travel / sfx index rolls |
| worldmap.cc:3475 | wmRndEncounterOccurred | logic | worldmap encounters / travel / sfx index rolls |
| worldmap.cc:3590 | wmRndEncounterPick | logic | worldmap encounters / travel / sfx index rolls |
| worldmap.cc:3643 | wmRndEncounterPick | logic | worldmap encounters / travel / sfx index rolls |
| worldmap.cc:3646 | wmRndEncounterPick | logic | worldmap encounters / travel / sfx index rolls |
| worldmap.cc:3693 | wmSetupRandomEncounter | logic | worldmap encounters / travel / sfx index rolls |
| worldmap.cc:3867 | wmSetupCritterObjs | logic | worldmap encounters / travel / sfx index rolls |
| worldmap.cc:3923 | wmSetupRndNextTileNumInit | logic | worldmap encounters / travel / sfx index rolls |
| worldmap.cc:3935 | wmSetupRndNextTileNumInit | logic | worldmap encounters / travel / sfx index rolls |
| worldmap.cc:3981 | wmSetupRndNextTileNum | logic | worldmap encounters / travel / sfx index rolls |
| worldmap.cc:4003 | wmSetupRndNextTileNum | logic | worldmap encounters / travel / sfx index rolls |
| worldmap.cc:4004 | wmSetupRndNextTileNum | logic | worldmap encounters / travel / sfx index rolls |
| worldmap.cc:4118 | wmEvalConditional | logic | worldmap encounters / travel / sfx index rolls |
| worldmap.cc:6098 | wmSfxRollNextIdx | logic | worldmap encounters / travel / sfx index rolls |
