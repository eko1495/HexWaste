
# Gecko (loc 1505) QA sweep — 1/6 landed (82, the B4 arc centerpiece)

Fifth campaign-QA town. UNLIKE Vault City (delivery-heavy), Gecko's other 5 quests are the
harder tier — surveyed but none is a clean single-interaction delivery/item-return:

- **82** Solve the Gecko powerplant problem — golden `quest-gecko-powerplant` (B4 session),
  0→2→5→6→7→9. Cross-town, 4-NPC chain: Lynette (vctycocl 17100) "become a Citizen" hub
  option → the alternate-commitment offer (msg 339) → accept the Gecko job (`2,3` then
  `2,1,1,1,2,1`, 82:=2 active). Harold (GECKSETL 16705) explains the coolant-valve near-
  meltdown + the missing Hydroelectric Magnetosphere Regulator part (`2,1,1,1,1,1,1,1,1,3`,
  82:=5, informational only). McClure (VCMClure, vctycocl 13922, greets as "Bureaucrat 1")
  confirms VC has the part and sends the dude to Randal (`1,2,3,5,1,2,1`, 82:=6). Randal
  (VCRandal, vctydwtn 23077, greets as "Trader 1") hands over the Hy-Mag part directly, no
  `--give` needed — script-granted (`1,3,1,3`, 82:=7). Festus the reactor ghoul (GECKPWPL
  24063) installs it (`1,2,1,1,1`, 82:=9 — completed per quests.txt display≥2/completed≥8;
  +4250 xp, dude reaches level 3). Back to McClure with the repair report (msg 134)
  (only visible once 82≥9, McClure's Node008 msg-134 gate) grants VC Citizenship directly:
  79 0→4, 81 0→1 (`1,5,2`). GOTCHA: Lynette's OWN citizenship-grant node cluster
  (Node076b/076c, also writes 79:=4) is dialogue-DEAD-CODE — full call-graph trace of
  vclynett.int found exactly one caller of Node076c, and it requires `gvar79==4||==5`
  already (circular); McClure's Node046 is the only *live* grant path. See vaultcity.md for
  the 79:=5/88:=5 gate (NOT reached by this golden — traced but genuinely story-gated behind
  the separate Bishop-conspiracy/quest-89 arc).
- **396** Repair the powerplant — VANILLA GAP (never written, per [[p124-quest-census]]). Skip.
- **397** Optimize the powerplant — GsTerm use_obj_on_p_proc (use an item on the reactor terminal).
- **393** Super repair kit for Skeeter — GCSkeetr Node915 checks carry item **308**; but the
  activation needs the weapon/tool-UPGRADE context (Skeeter's opt2 with items in hand = he wants
  to BUY your tools, not receive parts; the parts quest fires when you ask him to upgrade a
  weapon he lacks the part for). Skeeter = geckjunk 24893.
- **160** 3-step plasma transformer for Skeeter — GCSkeetr Node006 checks carry item **307**;
  same upgrade-context activation gate as 393.
- **616** Find Woody the ghoul for Percy — Percy (geckjunk 28127) Node007:=4; but Woody is
  CROSS-TOWN (find him in the Den via dcAnan Node012 chain), then return. Multi-town.

**VERDICT:** Gecko has no clean quick-win. To land any: 397 (find the item GsTerm wants + use it
on the reactor), 160/393 (figure out the upgrade-request activation — likely --give a plasma
weapon then ask Skeeter to upgrade it), or 616 (drive the Den Woody chain + return). Each is a
~30-60min investigation. The powerplant quests (82/396/397) tie into the big Gecko reactor
storyline. Deferred — task #60.

**Contrast:** VaultCity had 4 clean deliveries (497/493/80/459). Towns vary a LOT in how many
"easy" quests they have — VC and the early towns (Klamath/Den item-returns) are richest;
Gecko/Modoc are lean on quick wins.

Related: [[vaultcity-qa-sweep]], [[klamath-qa-sweep]], [[den-qa-sweep]], [[modoc-qa-sweep]].
