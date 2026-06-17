# Releasing Hexwaste

## License conditions (Sustainable Use License v1.0)

Hexwaste derives from [fallout2-ce](https://github.com/alexbatalov/fallout2-ce)
and inherits its license. Every release MUST:

- ship `LICENSE.md` (the SUL text) and `NOTICE.md` (modification notice +
  attribution) inside every artifact — the release script does this;
- be **free of charge, non-commercial**: GitHub Releases only — no itch.io
  listings, no donations/tip jars attached to the project;
- contain **no game assets** (`.gitignore` guards this; verify artifacts
  before uploading);
- keep the `// ported from fallout2-ce ...` comments — they are the
  attribution trail.

The SUL is not MIT-compatible: do not relicense, do not vendor Hexwaste code
into MIT projects.

## Publishing the repository (first release)

Publish with **fresh history**: the development repo's early history briefly
tracked extracted game data (`city.txt`, `worldmap.txt`), and a history scrub
is simpler and safer as a fresh start.

```sh
git clone --depth 1 file:///path/to/FPOC hexwaste-public
cd hexwaste-public
rm -rf .git
git init && git add -A
# audit before the first commit:
git status --short          # nothing from game-data/, no *.txt game configs
grep -rl "ported from" src | head   # attribution comments present
git commit -m "Hexwaste — initial public release"
git remote add origin git@github.com:<you>/hexwaste.git
git push -u origin main
```

Also audit `CLAUDE.md` and `docs/` for machine-local paths before pushing.

## Building release artifacts

```sh
scripts/release.sh 0.5.0
```

This publishes self-contained **folder** builds (MonoGame recommends against
single-file; trimming is off because the JSON save system uses reflection)
for `linux-x64` and `win-x64`, copies `LICENSE.md`, `NOTICE.md`, `README.md`
and `CHANGELOG.md` into each build folder (`release.sh:22`), and produces
`hexwaste-<v>-linux-x64.tar.gz` (tar preserves the exec bit) and
`hexwaste-<v>-win-x64.zip` under `artifacts/`.

Before uploading, confirm the SUL files (and no game assets) made it into each
archive:

```sh
tar tzf artifacts/hexwaste-<v>-linux-x64.tar.gz | grep -E "NOTICE.md|LICENSE.md"
# expect both; then verify no game data slipped in:
tar tzf artifacts/hexwaste-<v>-linux-x64.tar.gz | grep -iE "\.(dat|map|frm|pal|pro|gam|msg|acm)$" || echo "clean"
```

`NOTICE.md` (the modification notice + fallout2-ce attribution) shipping inside
every artifact is a hard SUL requirement — the build fails the audit if it is
missing.

Upload both archives to a GitHub Release. Release notes should repeat the
one-liner: *requires an original copy of Fallout 2 (GOG/Steam); no game data
included.*
