# Raport rekomendacyjny: następna faza rozwoju przeglądarki map Fallout 2 (PoC)

## TL;DR
- **Rekomendowany kierunek: renderowanie critterów (idle/walk) + mouse picking + tryb "spaceru" (walking simulator) bez silnika skryptów**, z twardo zakodowanymi drzwiami/schodami/exit-gridami/windami. To połączenie kierunków (a) i (c) — najbardziej widoczny postęp, niskie-średnie ryzyko, najwyższy "fun factor".
- **Czego NIE robić jako celu głównego:** pełnego silnika (fallout2-ce już go zastępuje na wszystkich OS, łącznie z iOS/Androidem/PS Vita) ani silnika skryptów INT jako pierwszego kroku — to dokładnie pułapki, na których utknęły DarkFO (archiwum 2020) i Falltergeist (stagnacja ~2022).
- **Dług techniczny do spłaty najpierw:** decyzja o shaderze palety (kompilacja efektów MonoGame `mgfxc` w 2025/2026 **nadal wymaga Wine** na Linux/macOS) oraz wprowadzenie modelu pętli gry z fixed timestep, zanim dodasz animacje.

## Key Findings

### Stan ekosystemu — referencja i "cmentarz" projektów
- **fallout2-ce** (alexbatalov) to autorytatywna referencja: C++17, drop-in replacement dla `fallout2.exe`. README projektu i changelog wydań potwierdzają działanie na Windows/macOS/Linux/Android/**iOS** ("Yes, Fallout 2 can be finally run on iOS … runs smoothly both on iPhone and iPad", issue #167) oraz port **PS Vita**. Pełna gra działa — nisza "kolejnego silnika" praktycznie nie istnieje.
- **jsFO** (ajxs) — porzucony; autor w post-mortem sam wskazał trzy zabójcze problemy: (1) brak dokumentacji silnika skryptów ("even if I was able to commit the time to making a vm in Javascript… I'd still need to do large amounts of research into the actual implementation of the scripting engine itself"), (2) kosztowność animowanych palet i light-mappingu w czasie rzeczywistym, (3) wolne ładowanie zasobów w JS. Dodatkowy błąd architektoniczny: konwersja DAT przez Pythona do JSON.
- **DarkFO** (darkf) — TypeScript+Python, ~15 956 linii kodu, ~1022 commity (model COCOMO ~4 lata), repo **zarchiwizowane 3 listopada 2020**. Miało działające: lighting (wolny, z bugami), podstawową walkę, częściowo skrypty. Devblogi DarkFO obnażają, jak bolesne były hardkodowane zachowania oryginału — np. **windy są zaszyte w .exe** ("Every elevator in Fallout / Fallout 2 is hardcoded in the executable").
- **Falltergeist** — C++/SDL2, OpenGL+shadery, GPL3+, wersje ~0.3.x; dialogi częściowo, brak walki/worldmap/AI; projekt stanął ~2022. Zintegrowali i potem **usunęli skrypty Lua** ("we integrated and then removed Lua scripting. We will add it back only when engine is mature enough").
- **Wniosek:** każdy projekt celujący w "pełną grę" lub zaczynający od skryptów umarł. Przeżywają narzędzia i biblioteki formatów.

### Renderowanie critterów (kierunek a)
- FID koduje typ obiektu, kod animacji, kod animacji broni i kierunek; nazwa pliku FRM budowana jest z bazy w `art\critters\critters.lst` + 2-znakowy sufiks animacji + opcjonalny sufiks kierunku `.fr0`–`.fr5`.
- Sufiksy animacji (fodev.net): `*aa.frm` = stand, `*ab.frm` = walk, `*at.frm` = running, `*ao/*ap` = hit, `*aq` = throw_punch; knockdown/death `*ba`–`*bp`, single-frame death `*ra`–`*rp`.
- 6 kierunków (0 = NE, zgodnie z ruchem wskazówek zegara). W fallout2-ce większość animacji nie-śmierci wymuszana jest na ścieżkę `.frm`; `.fr0`–`.fr5` używane głównie dla knockdown/death z fallbackiem.
- **Pliki fallout2-ce do studiowania:** `src/art.cc` (obsługa FID, `buildFid`, rozwiązywanie nazw), `src/animation.cc` (system animacji, `reg_anim_*`), `src/proto.cc`, `src/critter.cc`.
- **Demo moment:** stojące i dychające postacie na mapie w poprawnych kierunkach; następnie chodzące.

### Lighting (kierunek b)
- To, co dawało klimat oryginału: światło per-hex, tabele intensywności, interakcja z renderowaniem obiektów. `src/light.cc`.
- jsFO i DarkFO obie zgłaszały, że light-mapping jest kosztowny/wolny w czasie rzeczywistym. W naszym przypadku (GPU + shader) to mniejszy problem niż w przeglądarce, ale **wymaga rozwiązanego shadera palety** — dlatego lighting powinien iść po decyzji o shaderze, nie przed nią.

### Mouse picking + ruch + drzwi/schody (kierunek c)
- **Mouse picking** = hit-testing FRM z uwzględnieniem przezroczystości (klikanie "przez" przezroczyste części do obiektu poniżej — DarkFO opisał to jako istotny krok: "You can now click through the transparent parts of objects"). Wymaga per-pixel alpha test.
- **Pathfinding:** hex grid, A*; w fallout2-ce w `src/path.cc` (geometria hex w `src/tile.cc`). Poza walką gra pozwala przejść wiele hexów na jeden klik (ustawienie "Path finding range").
- **Drzwi/schody/windy/exit grids:** windy są hardkodowane w .exe oryginału — w naszym PoC można je twardo zakodować **bez** silnika skryptów. Drzwi to animacja stand/odwrotna (komenda silnika `obj_open`/`obj_close`); podstawowe otwieranie da się zrobić bez VM. Pełne zachowanie (zamki, triggery skryptowe) wymaga już silnika skryptów.

### Silnik skryptów INT (kierunek d) — jak duży naprawdę (zweryfikowane na źródle)
- VM fallout2-ce: **76 nazwanych opcode'ów** `OPCODE_*` w `src/interpreter.h` (enum 0x8000–0x804B). Dispatch przez **tablicę wskaźników funkcji** `gInterpreterOpcodeHandlers[OPCODE_MAX_COUNT]`, gdzie `#define OPCODE_MAX_COUNT 768` (zapas na opcode'y zewnętrzne i sfall). Opcode'y są 16-bitowe, z górnymi bitami jako tag typu (`RAW_VALUE_TYPE_OPCODE = 0x8000`).
- Funkcje zewnętrzne (komendy SSL: `obj_is_open`, `anim`, `create_object`, `obj_open` itd.) rejestrowane w `src/interpreter_extra.cc` przez `interpreterRegisterOpcode()`.
- **Rozmiary plików (z nagłówków GitHub):** `interpreter.cc` 3353 linie (103 KB), `interpreter_extra.cc` 5073 linie (155 KB), `interpreter_lib.cc` ~2300+ linii, `interpreter.h` 247 linii. Odpalanie procedur (map_enter, spatial, use_p_proc) jest w `src/scripts.cc`.
- **Kluczowy wniosek:** brak jakiejkolwiek opublikowanej "minimalnej listy" opcode'ów do uruchomienia drzwi/triggerów. W praktyce trzeba zaimplementować ~wszystkie 76 core'owych opcode'ów + ścieżkę odpalania skryptów w `scripts.cc` + konkretne handlery (`obj_*`, zmienne lokalne/mapowe/globalne, override map-start) — czyli to bardzo duży, w dużej mierze badawczy nakład. DarkFO potwierdza ("the meat of the game logic… I needed to learn things").

### Dźwięk ACM (kierunek e)
- ACM = format Interplay, 22050 Hz, mono/stereo, bit-stream z blokami. Istnieje **libacm** (markokr): rdzeń na licencji **BSD/ISC** (README v0.9.2: "License core libacm code under minimal BSD/ISC license"), i — co kluczowe — README v1.0 stwierdza: **"libacm now decodes all Fallout 1/2 files in full length and without complaining"**.
- Dekoder został też **zmergowany do FFmpeg 3.0** (libacm README: "This decoder was merged into FFmpeg 3.0. Now ACM files can be played via ffmpeg commands or using libavcodec"; implementacja: `libavcodec/interplayacm.c`).
- W C#/.NET nie ma dojrzałego natywnego dekodera ACM — trzeba sportować libacm / `src/sound_decoder.cc` z fallout2-ce do `FalloutPoc.Formats` (czysty, samodzielny task) albo wołać FFmpeg.
- Niski "demo moment" (dźwięk niewizualny), ale port dekodera ACM idealnie pasuje jako niezależny task do warstwy parserów.

### Kierunek narzędziowy (f)
- Społeczność modderska używa przestarzałych, Windows-only, często porzuconych narzędzi: **BIS Mapper / mapper2** (notoryczne problemy z uruchomieniem, błędy CRC, hi-res patch), **Dims Mapper** (alternatywa Dimsa/Fakelsa/Radegasta), **F2wedit** (edytor proto itemów, tylko wersje PL/US), **sFall Script Editor**, FRM viewery (Frame Animator, Titanium FRM Browser).
- Proto-edytory dla scenery/critterów są w dużej mierze niedostępne/porzucone (wątki NMA o szukaniu proto editorów: "every single one has been taken down from everywhere"). fallout2-ce zaczął odbudowywać oryginalny BIS Mapper z tego samego kodu (brakuje m.in. trybu librarian/proto editing).
- **Luka:** nowoczesny, cross-platform inspektor map/assetów — a Ty masz już fundament (parsery + render). Eksport do PNG/glTF/Tiled, mod diffing, podgląd FRM/PRO. To realna, niezagospodarowana nisza z faktycznymi odbiorcami (modderzy na Linux/macOS, których stare narzędzia w ogóle nie obsługują).

### Pełny silnik (kierunek g)
- Szczerze: fallout2-ce wypełnia tę niszę i działa nawet na mobile. Wartość "kolejnego silnika" jest **edukacyjna/portfolio, nie użytkowa**. Nie rekomendowane jako cel.

### Dług techniczny
- **Shader palety / mgfxc:** w 2025/2026 kompilacja efektów MonoGame **nadal wymaga Wine na Linux/macOS** (mojoshader tłumaczy HLSL→GLSL tylko przez Wine; dokumentacja MonoGame dla Arch testowana paźdz. 2025 oraz Andrew Zah, lip. 2025: "building shaders with the MGCB … requires emulation via wine"). Deweloperzy MonoGame deklarują chęć natywnej kompilacji, ale to wciąż TODO ("they want to have native shader compilation on linux and mac as well"). **Wersje:** 3.8.4 (blog z **2025-06-09**), 3.8.4.1 (paźdz. 2025), 3.8.5 w preview (grudzień 2025 / styczeń 2026) z preview DX12/Vulkan. Obejście: `dotnet tool install dotnet-mgfxc` i ładowanie skompilowanego `.mgfx` z dysku (tracisz cache ContentManagera), albo pozostanie przy CPU palette conversion.
- **Alternatywy stosu:** **Veldrid** — autor (Eric Mellino) wycofał publiczne wsparcie ("As of February 2023, I'm no longer able to publicly share updates to Veldrid and related libraries"), de facto martwy dla nowych projektów; **FNA** (FNA3D) — dojrzałe, solidna opcja jeśli shader stanie się blokerem; **Silk.NET** — aktywny (Winter 2025 update), ale niskopoziomowy; **raylib-cs** — prostszy, mniej kontroli. **Rekomendacja: zostać przy MonoGame**, a FNA traktować jako plan B tylko jeśli shader palety zablokuje rozwój.
- **Atlasing/batching:** kluczowe dla 2000+ obiektów na mapę. Każda zmiana tekstury w `SpriteBatch` to texture swap, który zabija wydajność — rozwiązanie: atlasy + sortowanie rysowania po teksturze; cache statycznych warstw podłogi do `RenderTarget` (renderowane raz, nie co klatkę); culling obiektów poza ekranem. To best practices z dokumentacji MonoGame i forów społeczności.
- **Model pętli gry:** oryginał działa na określonych tickach. Przed dodaniem animacji warto wprowadzić **fixed timestep z akumulatorem + interpolacją renderu** (oddzielenie symulacji od FPS), żeby nie sprzęgać prędkości animacji z liczbą klatek.

## Details

### Tabela porównawcza kierunków

| Kierunek | Effort | Payoff (widoczność) | Risk | Fun |
|---|---|---|---|---|
| a) Crittery + idle/walk | Średni | Bardzo wysoki | Niski | Wysoki |
| b) Lighting | Średni | Wysoki (klimat) | Średni (zależny od shadera) | Średni |
| c) Walking sim (picking+ruch+drzwi hardcoded) | Średni-wysoki | Bardzo wysoki | Średni | Bardzo wysoki |
| d) Silnik skryptów INT | Bardzo wysoki | Średni (mało widać) | Bardzo wysoki | Niski-średni |
| e) Dźwięk ACM | Niski-średni | Niski (niewidoczny) | Niski | Niski |
| f) Narzędzie modderskie | Średni | Wysoki (realni użytkownicy) | Niski | Średni |
| g) Pełny silnik | Ekstremalny | Niski (fallout2-ce istnieje) | Ekstremalny | Niski |

### Mapowanie kierunków na tryby porażki poprzedników
- **d) skrypty jako pierwsze** → tryb porażki DarkFO/Falltergeist: ogromny, niewidoczny, badawczy nakład; Falltergeist wręcz usunął Lua, a ajxs wskazał brak dokumentacji silnika skryptów jako powód porzucenia jsFO.
- **g) pełny silnik** → tryb porażki "kolejny silnik bez niszy" (fallout2-ce istnieje i działa na mobile).
- **b) lighting bez rozwiązanego shadera** → tryb porażki jsFO/DarkFO (kosztowny light-mapping w czasie rzeczywistym).
- **a)+c)** → najbezpieczniejsze; jedyne realne ryzyko to scope creep w stronę walki/skryptów — trzeba twardo trzymać granicę "bez walki, bez VM".

## Recommendations

**Rekomendowana ścieżka: "Walking simulator" — crittery + ruch + interakcja bez skryptów.** Najpierw spłać dług techniczny (shader palety + pętla gry), potem buduj widoczne kamienie milowe. Każdy milestone jest niezależnie demonstrowalny, commitowalny i testowalny (w stylu dotychczasowych prac, z `--screenshot` do regresji wizualnej).

### Milestone 0 — Dług techniczny (fundament)
- Decyzja o shaderze palety: utrzymać CPU palette conversion **lub** skonfigurować `dotnet-mgfxc` (zaakceptować Wine na Linux) / rozważyć FNA. **Próg:** jeśli CPU-conversion daje <16 ms/klatkę przy color cyclingu na pełnej mapie — zostań na CPU i nie wprowadzaj shadera.
- Wprowadź fixed timestep (akumulator + interpolacja renderu).
- **Pliki fallout2-ce:** `src/palette.cc`, `src/color.cc`, pętla w `src/game.cc` / `src/main.cc`.
- **Demo:** ten sam render, ale stabilny timing + raport FPS.

### Milestone 1 — Statyczne crittery (idle/stand)
- Parsowanie FID, budowa nazw FRM, rozwiązywanie `critters.lst`, render stojących postaci w poprawnym kierunku, z poprawnym z-sortowaniem w hex.
- **Pliki:** `src/art.cc` (FID, `buildFid`), `src/proto.cc`, `src/critter.cc`.
- **Demo:** NPC stoją na mapie z poprawnymi sprite'ami i kierunkami.

### Milestone 2 — Animacje idle/dychanie + walk-cycle (w miejscu)
- Odtwarzanie sekwencji FRM z poprawnym FPS i offsetami klatek; animacja stand/breath i walk w miejscu (bez ruchu po mapie).
- **Pliki:** `src/animation.cc` (`reg_anim_*`, kolejka animacji), `src/art.cc`.
- **Demo:** postacie dychają/animują się; przełącznik animacji.

### Milestone 3 — Mouse picking
- Hit-testing FRM z per-pixel alpha; klikanie "przez" przezroczyste części do obiektu poniżej; podświetlenie/outline trafionego obiektu.
- **Pliki:** rendering obiektów w `src/object.cc` (rysowanie + egg/transparency), `src/tile.cc` (mapowanie ekran↔hex).
- **Demo:** najechanie/klik pokazuje nazwę/PID obiektu pod kursorem.

### Milestone 4 — Ruch dude'a z pathfindingiem
- A* na hex grid, animacja walk/run wzdłuż ścieżki, kamera podąża za postacią.
- **Pliki:** `src/path.cc` (A*), `src/tile.cc` (geometria hex, sąsiedztwo), `src/animation.cc` (move-to-tile).
- **Demo:** klik na hex → postać idzie tam animowana.

### Milestone 5 — Interakcja hardcoded (drzwi/schody/exit grids/windy)
- Otwieranie/zamykanie drzwi (animacja odwrotna, bez VM), przejścia exit-grid (zmiana mapy/elewacji), windy twardo zakodowane wg tabeli typów (jak w oryginale).
- **Pliki:** `src/map.cc` (exit grids, ładowanie map), `src/object.cc` (stany openable), referencja do tabeli wind.
- **Demo:** przejście między mapami i otwieranie drzwi — pełny "spacer" po świecie bez walki.

**Progi zmiany decyzji:**
- Jeśli na Milestone 4 pathfinding/animacje okażą się zbyt ciężkie wydajnościowo (np. <30 FPS przy wielu critterach mimo atlasingu/RenderTarget) → przełącz priorytet na kierunek narzędziowy (f), który nie wymaga symulacji real-time.
- Jeśli społeczność (NMA) zgłosi realne zapotrzebowanie na cross-platform mapper → rozważ pivot na (f) już po Milestone 3 (masz wtedy render + picking, czyli rdzeń inspektora).

**Opcjonalne równoległe taski (niezależne, niskie ryzyko):** port dekodera ACM do `FalloutPoc.Formats` (kierunek e — wykorzystaj libacm BSD/ISC jako wzorzec, nie kopiując GPL-owych pluginów) oraz lighting (kierunek b) — dopiero po rozwiązaniu shadera.

## Caveats
- **Dokładna liczba funkcji zewnętrznych** w `interpreter_extra.cc` nie została zweryfikowana ze źródła (plik ucięty po stronie serwera podczas badania); rząd wielkości to ~250–290 handlerów, a podawana liczba 213 dotyczy opcode'ów **używanych** w skryptach gry (fodev), nie liczby zarejestrowanych handlerów. Aby ustalić dokładnie — policz wywołania `interpreterRegisterOpcode()` w funkcji rejestrującej na końcu `src/interpreter_extra.cc`.
- **"Minimalny podzbiór" opcode'ów** do drzwi/triggerów **nie istnieje w żadnym źródle** — to dodatkowo potwierdza, że kierunek (d) jest trudny do oszacowania i ryzykowny.
- Status **natywnej kompilacji shaderów MonoGame** może się zmienić wraz z 3.8.5/4.0 (DesktopVK ma zastąpić DesktopGL) — zweryfikuj aktualny stan przed ostateczną decyzją o shaderze.
- **Kwestie prawne:** nie dystrybuować assetów gry, wymagać oryginalnej kopii z GOG, a przy ewentualnej publikacji nazwa projektu **nie może zawierać słowa "Fallout"**. Wzoruj się na ACM: korzystaj z licencji permisywnych (libacm BSD/ISC), a nie z GPL-owych komponentów, jeśli chcesz zachować elastyczność licencyjną.
- **Nazwy plików w fallout2-ce** (np. `path.cc` vs geometria w `tile.cc`, dokładne lokalizacje renderingu obiektów) należy potwierdzić w aktualnym drzewie `src/` repozytorium przed pracą — repo ewoluuje (projekt rozważa też rebranding na "Fallout Reimagined: Community Engine").