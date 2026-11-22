# Bölüm Üretimi: RNG, Üretici ve Çözücü Portu (Görev 09)

cube-hunt `src/levels/rng.ts`, `generator.ts` ve `solver.ts` (`559dfd9`, değiştirilmeden) C#'a taşındı:
`Assets/_CubeHaul/Levels/Scripts/Generation/` (namespace `CubeHaul.Levels.Generation`, UnityEngine'siz).

**Sonuç:** Golden dosyalardaki her referans seed ve seçenekte C# çıktısı TypeScript ile **birebir aynı**. Fark yok;
yalnızca §4'te listelenen, geçerli girdilerde sonucu değiştirmeyen API farkları var.

## 1. Eşleme

| TypeScript | C# | Not |
|---|---|---|
| `mulberry32(seed)` | `new Mulberry32(seed)`, `Next()`, `NextUInt32()`, `NextIndex(n)` | `NextIndex(n)` = `Math.floor(rng() * n)` |
| `Math.round` | `JsMath.Round`, `JsMath.RoundToInt` | |
| `GenOptions`, `BuildOptions`, `buildLevel(…, candidates)` | `GenerationOptions` | `GenerationOptions.For(level)` bölümün generation ayarları + kenarlar + slot sayısı |
| kasa boyutu formülleri | `CrateSizes.For(total, options)` | |
| `peelLayers(picture, edges)` | `PeelLayers.Of(picture, edges)` | |
| `splitCount`, `shuffled`, `generateLevel` | `LevelGenerator.SplitCount`, `Shuffled`, `Generate` | `Generate` bir `TrayDefinition` döndürür; slot ve kenar bölümün kendisindedir |
| `buildLevel` → `{ level, winRate }` | `LevelBuilder.Build` → `BuildResult` | `Tray`, `WinRate`, `Path`, `Verified`, `Attempts`, `Chosen` |
| `solveGreedy(level, maxTicks)` | `GreedySolver.Solve(…, maxTicks)` → `SolveResult` | |
| `chooseTap` (özel) | `GreedySolver.ChooseTap` | test için açık |
| `randomWinRate(level, games, seed)` | `RandomPlayer.WinRate(…, games, seed)`, `RandomPlayer.Play` | |
| `level-examples.ts › builtInDefinition` | `LevelBaker.Bake` | bölüm dosyasının `tray` + `difficultyMetadata` alanları |
| `worker.ts` | `LevelBaker.BakeAsync`, `EnsureTrayAsync` | §5 |

## 2. JavaScript sayıları ve C# karşılıkları

| Yer | JavaScript | Tuzak | C#'ta yapılan |
|---|---|---|---|
| `mulberry32` | `>>> 0`, `Math.imul`, `t ^= t + Math.imul(…)` (double toplam, sonra ToInt32) | `int` ile `>>` işaretli kaydırma yapar; `checked` bağlamda taşma istisnası | Tamamen `uint`, `unchecked`. Hepsi mod 2³² aritmetik olduğu için bit bit aynı. `x / 2³²` iki dilde de kesin |
| seed | `seed >>> 0` (ToUint32) | `seed + 7919k` `int.MaxValue`'yu aşar (şema `seed ≤ 2³¹−1`, `candidates ≤ 1000`) | Seed `long`; RNG'ye `unchecked((uint)seed)` ile girer = ToUint32. `seed + 101k`, `seed + k` de `long` |
| kasa boyutları | `Math.round(total / 150)` vb. | C# `Math.Round` yarımı çifte yuvarlar (8,5 → 8); `Math.Floor(x + 0.5)` 0,49999999999999994 → 1 | `JsMath.Round`: `r = Floor(x)`, `x − r ≥ 0,5` ise `r + 1` |
| rastgele indeks | `Math.floor(rng() * n)` | — | `(int)Math.Floor(rng.Next() * n)`; çarpım iki dilde aynı IEEE double |
| gecikme | `layer + rng() * noise` | `noise = 0` olsa da her kasa için bir değer çekilir | Aynı: `shuffle = 0`'da da çekilir (atlanırsa sonraki dağıtım kayar) |
| büyük kasa | `rng() < bigChance && rem >= bigMin + min` | Koşulların sırası değişirse rng çekilmeyebilir | Aynı sıra: önce rng |
| yedek karıştırma | `shuffle * (1 - k / 8)` | `k / 8` tamsayı bölmesi 0 verir | `k / 8.0` |
| çözücü puanı | `reach / (1 + same) + (force ? remainingOf * 1e-3 : 0)`, `score > best`, başlangıç `-Infinity` | `>=` eşitlikte son kolonu seçer | `double` bölme, `>` (eşitlikte ilk kolon), `double.NegativeInfinity` |
| kazanma oranı | `wins / games`, `Math.abs(winRate - target) < 0.04` | — | `(double)wins / games`; karşılaştırmalar aynı double'larla |

## 3. Sıralamalar ve eşitlik bozucular

`Array.prototype.sort` ES2019'dan beri **kararlıdır**: eşit anahtarlı öğeler önceki sıralarını korur. C#
`Array.Sort` / `List.Sort` kararlı değildir. Her sıralama C#'ta önce anahtarı, eşitlikte **sıralamadan önceki
konumu** karşılaştırır. Bu, hangi algoritma sıralarsa sıralasın tam olarak kararlı sıralamanın sonucunu verir.

| Sıralama | Anahtar | Eşitlikte | Sonuca etkisi |
|---|---|---|---|
| Bir rengin hücreleri | peel katmanı | hücre indeksi (toplanma sırası) | **Yok**: yalnızca bir kasanın ilk hücresinin *katmanı* kullanılır ve eşitler aynı katmandadır. Yine de kurulu |
| Kasalar | `zaman = katman + gürültü` | kasa id'si (oluşturulma sırası) | **Belirleyici**: `shuffle = 0`'da zamanlar tamsayıdır ve çok sayıda eşitlik vardır. Bozucu kaldırılınca testler düşüyor |
| Dağıtım sırasındaki kolonlar | kolon uzunluğu | karıştırılmış sıradaki konum | Belirleyici; en fazla 10 kolon için kararlı eklemeli sıralama |

`peelLayers` içinde halkanın yenme sırası katmanları değiştirmez (her halka anlık görüntüdür). C# `ReachableCells`
artan sırada döner, TS `Set` ekleme sırasında; katmanlar yine de aynıdır (`peel-layers.json`).

## 4. Belgelenmiş farklar

Hiçbiri geçerli bir bölümde üretilen tepsiyi, çözücü sonucunu ya da kazanma oranını değiştirmez.

| # | Durum | TypeScript | C# | Neden |
|---|---|---|---|---|
| D-1 | Kamyon sayısı resimle uyuşmayan tepsi (I-01 bozuk) ile `randomWinRate` | Sonsuz döngü (dokunuş yok, kamyon gönderilmiyor, durum değişmiyor) | O oyun kaybedilmiş sayılır | Tam olarak TS'in sonsuz döngüye girdiği koşulda durur; üretici böyle tepsi üretmez |
| D-2 | Hiçbir tepsi doğrulanamadı | `console.warn` + karıştırılmamış tepsi | Aynı tepsi; `BuildResult.Path = Unverified`, `Verified = false`; Editor menüsü uyarı yazar | Uyarı API'de görünür, gizlenmez |
| D-3 | `columns < 1`, `minBlock/maxBlock < 1`, `shuffle` NaN/∞ | `columns = 0` sonsuz döngü, diğerleri tanımsız | `ArgumentOutOfRangeException` | Şema zaten izin vermez |
| D-4 | `buildLevel`'in ara adımları | Görünmez | `BuildResult.Attempts`: denenen her tepsi, çözücü sonucu, oyunlar | Fark araştırmak için |
| D-5 | İptal | Yok (worker sonlandırılır) | `CancellationToken` | Mobilde ekrandan çıkılınca |
| D-6 | `generateLevel` dönüşü | `Level` (resim, kolonlar, `slots ?? 5`, `edges ?? "all"`) | `TrayDefinition` | Slot ve kenar `LevelDefinition`'da; varsayılanlar `GenerationOptions`'ta aynı |

Korunanlar: **F-4** (elle verilen `minBlock`/`maxBlock` sınırları korunmaz) aynen korunur
(`GeneratorRuleTests.Finding_F4_…`, `generator-cases.json › strawberry-f4-block-bounds`).

## 5. Önceden üretim ve çalışma zamanı

| Yol | Ne zaman | Nasıl |
|---|---|---|
| **Editor'de önceden üretim (önerilen)** | Bölüm içeriği hazırlanırken | **CubeHaul ▸ Levels ▸ Bake Trays From Generation Settings**: `Levels/Content` altındaki generation ayarlı her dosyanın tepsisini ve zorluğunu yeniden üretir, farklı olanların üzerine yazar. **Verify Baked Trays** hiçbir şey yazmadan güncel olmayanları listeler. Çıktı kanonik biçimdedir: güncel bir dosyaya dokunulmaz |
| Çalışma zamanında | Tepsisiz gelen bir bölüm (ör. ileride kullanıcı resmi) | `await LevelBaker.EnsureTrayAsync(level, token)`: statik tepsi varsa bölümün kendisi, yoksa arka plan iş parçacığında üretilmiş kopya |
| Kod içinden | Araçlar, testler | `LevelBaker.Bake(level)`, `LevelBaker.BakeFile(text)`, `LevelBuilder.Build(picture, options)` |

10 yerleşik bölümün hepsi önceden üretilmiş statik tepsiyle gelir; oyun açılışta üretim yapmaz.

Süreler (`Tools/dotnet › GeneratorTimingTests`, Xeon 2,3 GHz, .NET 8, tek iş parçacığı):

| Bölüm | Boyut | Denenen tepsi | Süre |
|---|---|---|---|
| strawberry | 29×29 | 1 | 14 ms |
| mushroom | 36×36 | 10 | 159 ms |
| sunset | 36×36 | 16 | 209 ms |
| ringed-planet | 46×46 | 11 | 178 ms |
| cat | 43×43 | 10 | 245 ms |
| cactus | 40×43 | 1 | 30 ms |
| rocket | 39×46 | 2 | 75 ms |
| rubber-duck | 43×43 | 4 | 107 ms |
| ice-cream | 36×46 | 16 | 327 ms |
| watermelon | 46×35 | 2 | 23 ms |
| **toplam** | | | **1,4 s** |

Telefonda IL2CPP ile birkaç kat yavaş olması beklenir (en kötü bölüm ~1–2 s). Bu yüzden yerleşik bölümler önceden
üretilir, çalışma zamanındaki üretim ana iş parçacığında yapılmaz.

## 6. Doğrulama

| Test | Ne sınar |
|---|---|
| `GeneratorGoldenTests › Mulberry32GivesTheGoldenValues` | `rng.json`: 6 seed × 256 değer, `0`, `2³¹−1` ve `2³²−1` dahil |
| `› GenerateMatchesForEveryLevelSeedAndShuffle` | `generate.json`: 10 bölüm × 4 çalıştırma; kasa sayısı, kolon boyları, öndeki kasalar, tüm kasalar (SHA-256) |
| `› BuildTriesTheSameCandidatesAndChoosesTheSameTray` (10) | `build.json`: her aday tepsi, açgözlü sonuç, 24 oyunun her biri, kazanma oranı (tam double eşitliği), seçilen aday |
| `› GeneratorCaseMatches` (12) | `generator-cases.json`: aday, yedek ve doğrulanamadı yolları, F-4, tek kolon / 10 kolon, `bigChance` 0 ve 1, `sides` kenarı, `seed = 2³¹−1`, `shuffle = 100`, yarım yuvarlama resimleri, tek küp |
| `› BakingTheGenerationSettingsGivesTheLevelFileByteForByte` (10) | Tepsi ve zorluk silinip yeniden üretilince 10 bölüm dosyası byte byte aynı |
| `› TheBakeMenuLeavesUpToDateFilesAloneAndFixesStaleOnes` | Editor menüsünün çekirdeği: güncel dosya değişmez, bozulan dosya düzelir, generation'sız dosya atlanır |
| `GeneratorRuleTests` (78) | TS `generator.test.ts`, `solver.test.ts`, `core.test.ts › levels` testleri; JS sayı tuzakları, D-1…D-3, iptal, `EnsureTrayAsync` |

Bilerek sokulan hatalar testlerde yakalanıyor: `Math.Round` (12 test düşer), RNG'de işaretli kaydırma (41),
kasa sıralamasında eşitlik bozucusuz sıralama (2), çözücüde `>=` (29), rng'nin `&&` sonrasına alınması (32),
yedekte `k / 8` tamsayı bölmesi (3), `shuffle = 0`'da gürültünün atlanması (2). Hücre sıralamasının eşitlik
bozucusunu kaldırmak hiçbir testi düşürmez, çünkü §3'te açıklandığı gibi sonucu etkilemez.
