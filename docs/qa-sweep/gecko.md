
# Gecko (loc 1505) QA sweep — surveyed, none landed (no clean quick-win)

Fifth campaign-QA town. UNLIKE Vault City (delivery-heavy), Gecko's 6 quests are ALL the
harder tier — surveyed but none is a clean single-interaction delivery/item-return:

- **82** Solve the Gecko powerplant problem — big multi-step (GsTerm terminal + VC McClure path).
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
