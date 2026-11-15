# CubeHaul3D — Taşıma Planı (TypeScript → Unity 6.3 LTS / C#)

| | |
|---|---|
| **Kaynak** | [yunusemreerinc/cube-hunt](https://github.com/yunusemreerinc/cube-hunt), `main` @ `559dfd90977b1e15367bd7de25aec9fbae4b52f6` (15.11.2026) |
| **Hedef** | CubeHaul3D — Unity 6.3 LTS (6000.3.x), Universal 3D (URP) şablonu, C# |
| **Platformlar** | Android, iOS (dikey ekran, dokunmatik) |
| **Bu belgenin kapsamı** | Görev 01 — kaynak kod analizi ve taşıma haritası: referans TS sınıfları, claim/eat zamanlaması, çağrı sırası, animasyon/UI envanteri, katman ayrımı, TS → C# dosya/sınıf eşlemesi, bağımlılık diyagramları, 25 invariant, test planı, JSON bölüm formatı, riskler |
| **Kapsam dışı (bu aşamada)** | C# kodu, Unity projesi, 3D sahne. TypeScript projesine dokunulmaz. |

Planın tamamı, kaynak koddaki davranışın **bire bir korunması** ilkesine dayanır: C# çekirdeği aynı tohumla (seed)
aynı tepsiyi üretmeli, aynı hamle dizisinde aynı kamyonları aynı küplere göndermeli ve aynı anda kazanmalı ya da
tıkanmalıdır. 3D görselleştirme bu çekirdeğin üzerine, kuralları etkilemeyecek şekilde oturur.

### Alınan kararlar

| Konu | Karar | Etkisi |
|---|---|---|
| Proje adı | **CubeHaul3D** (depo adıyla aynı) | Unity projesi depo kökünde; asmdef/namespace önekleri `CubeHaul.*`; JSON `format` değerleri `cubehaul.*` |
| Kamera | **Ortografik** | Tüm küpler ekranda aynı boyda; okunabilirlik riski (R-17) azalır; derinlik hissi ışık ve eğik açıyla verilir (§3.5) |
| TS bulguları F-2, F-3, F-4 ([TS_BASELINE §4](TS_BASELINE.md#4-bulgular)) | **Aynen korunur** | Boş bölümün `playing` başlaması, `Collect`'in claim aramaması ve `minBlock`/`maxBlock` sınırları C#'ta TS ile aynı; düzeltmeler golden eşitlikten sonra, ayrı değişiklikte |
| Bölüm formatı ([LEVEL_FORMAT](LEVEL_FORMAT.md)) | **LevelDefinition v1**: düz `cells`, `Crate[][]` kolonlar, statik `tray` ile `generation` ayrı | Unity'den bağımsız C# okuyucu; `JsonUtility` yok; TS ve C# aynı kurallar |
| "Your image" | **İlk sürümde yok** | Galeri eklentisi, izinler ve çalışma zamanında bölüm üretimi v1 kapsamından çıktı (§2.3, §8) |

---

## İçindekiler

1. [Kaynak projenin analizi](#1-kaynak-projenin-analizi) — bağımlılıklar §1.4, referans sınıflar §1.5, claim/eat zamanlaması §1.6, yaşam döngüleri §1.7, çağrı sırası §1.8, animasyon/UI §1.9
2. [Katmanların ayrılması: mantık, bölüm üretimi, görselleştirme](#2-katmanların-ayrılması)
3. [Unity C# sınıf mimarisi](#3-unity-c-sınıf-mimarisi) — eşleme tablosu §3.7, C# bağımlılık diyagramı §3.8
4. [Korunması gereken invariantlar](#4-korunması-gereken-invariantlar)
5. [Davranışı korumak için test planı](#5-test-planı)
6. [Bölüm verisi için JSON formatı](#6-bölüm-verisi-için-json-formatı)
7. [Riskler ve önlemler](#7-riskler-ve-önlemler)
8. [Önerilen iş sırası](#8-önerilen-iş-sırası)
9. [Açık sorular](#9-açık-sorular)

Kararlar için başlıktaki [Alınan kararlar](#alınan-kararlar) tablosuna bakın.

---

## 1. Kaynak projenin analizi

### 1.1 Teknoloji ve yapı

- TypeScript 5.9, Vite 8 (tek HTML dosyasına gömülü derleme), Vitest 5. Çalışma zamanı bağımlılığı yok.
- Çizim: HTML Canvas 2D, sabit tasarım çözünürlüğü **420 × 900** (dikey), pencereye ölçeklenir.
- Arka plan iş parçacığı: Web Worker (bölüm üretimi).
- CI: GitHub Actions → `npm test` + `npm run build`.
- Taban çizgisi doğrulandı: `npx vitest run` → **2 dosya, 43 test, hepsi geçti** (Node 22).

| Dosya | Satır | Sorumluluk | Katman |
|---|---:|---|---|
| `src/core/types.ts` | 44 | `ColorId`, `OpenEdges`, `EMPTY`, `Picture`, `Block`, `Level`, `cloneLevel` | Oyun mantığı |
| `src/core/board.ts` | 342 | Izgara, açık (ulaşılabilir) boş hücreler, renk başına ulaşılabilir küpler, hedef seçimi, claim/eat | Oyun mantığı |
| `src/core/game.ts` | 126 | Tepsi kolonları, slotlar, `tap/tick/collect/step`, kazanma/tıkanma | Oyun mantığı |
| `src/levels/rng.ts` | 11 | `mulberry32` deterministik PRNG | Bölüm üretimi |
| `src/levels/generator.ts` | 173 | `peelLayers`, `shuffled`, `splitCount`, `generateLevel`, `buildLevel` | Bölüm üretimi |
| `src/levels/solver.ts` | 83 | `solveGreedy`, `randomWinRate` | Bölüm üretimi |
| `src/levels/art.ts` | 360 | `Sketch` çizim DSL'i, 10 yerleşik resim, bölüm ayarları (`builtInLevels`) | Bölüm içeriği |
| `src/levels/image.ts` | 95 | `quantize` (k-means, saf), `pictureFromFile` (DOM/canvas) | Bölüm üretimi + platform |
| `src/levels/worker.ts` | 38 | Bölümleri Web Worker'da üretme | Platform |
| `src/view/roads.ts` | 162 | `RoadMap`: kamyon rotası (yol halkası + temizlenmiş hücrelerde BFS) | Simülasyon (saf) |
| `src/view/draw.ts` | 952 | Canvas çizim yardımcıları (kamyon, küp, buton, ikon, tuğla…) | Görselleştirme |
| `src/main.ts` | 1750 | `App`: yerleşim, giriş, zamanlama, kamyon hareketi, animasyon, katman önbelleği, paneller | **Karışık** (bkz. §2.2) |
| `tests/core.test.ts` | 300 | Kurallar, üretici, çözücü, quantize | Test |
| `tests/roads.test.ts` | 108 | Rota testleri, tüm yerleşik bölümlerde rota doğrulaması | Test |

README zaten şu ayrımı hedeflediğini söylüyor: *"src/core: game rules only, no drawing (easy to test or port to Unity/Godot)"*.
Bu doğru; ancak `main.ts` içinde **kuralları etkileyen zamanlama mantığı** da bulunuyor (§2.2). Taşımanın en kritik
tasarım kararı bu parçayı görünümden ayırmaktır.

### 1.2 Oyunun kuralları (korunacak davranışlar kataloğu)

Testlerde bu kimliklere (B-xx) atıf yapılır.

**Tahta (Board)**

- **B-01** Hücre indeksi `i = y * width + x`, `y = 0` en üst satır. `cells[i] = -1` (EMPTY) boş, aksi hâlde paletteki renk indeksi.
- **B-02** Açık kenarlar (`OpenEdges`): `"bottom"` yalnızca alt satır; `"sides"` alt + sol + sağ; `"all"` dört kenar. Alt kenar her zaman açıktır (rampa altta, ortada).
- **B-03** Bir küp **ulaşılabilir**: açık bir kenarda duruyorsa ya da *açık* bir boş hücreye komşuysa. *Açık* boş hücre = boş hücreler üzerinden açık bir kenara bağlı. Resmin içine hapsolmuş saydam boşluklar, kamyon oraya varana kadar açık sayılmaz.
- **B-04** Hedef seçimi (`pickTarget`): (1) rampaya en yakın satır (en alttaki), (2) en kısa sürüş = `içerideki hücre sayısı + yol mesafesi`, yol içerideki hücre sayısında eşitlik bozucudur (`better`: önce derinlik, sonra yol), (3) eşitlikte küçük indeks. Sonuç: her satır ortadan dışa doğru temizlenir; dört tarafı açık bölümlerde uzak sol/sağ kenar küpleri alttaki satırları bekler.
- **B-05** Yol mesafesi (`roadTo`): rampa alt kenarın ortasında; alt kenar `|x+0.5 − w/2|`, yan kenarlar `w/2 + (h − (y+0.5))`, üst kenar `w/2 + h + min(x+0.5, w − x − 0.5)`.
- **B-06** `claim`: küp başka kamyona verilmez ama tahtada kalır ve arkasını kapatmaya devam eder. `eat`: küp kalkar, hücre ve dokunduğu kapalı boşluklar açılır (flood), derinlikler artımlı güncellenir.
- **B-07** Claim edilmiş bir küp, yanı açılsa bile yeniden ulaşılabilir listesine girmez.
- **B-08** `opensUp(color)`: yoldaki kamyonlar küplerini aldığında bu renkten bir küp açılacak mı? (Claim edilmiş hücrelerden ve kapalı boşluklardan flood.)

**Oyun (Game)**

- **B-10** Tepsi = 5 kolon (varsayılan); yalnızca her kolonun **öndeki** kasası dokunulabilir. Dokunulan kasa **ilk boş** slota gider. Slot yoksa `tap` −1 döner.
- **B-11** `tick(ready)`: her slot (indeks sırasıyla) en fazla bir kamyon gönderir; `ready(slot)` false ise atlanır. Kasanın sayısı 0'a inince slot boşalır.
- **B-12** `collect(cell)`: kamyon küpü aldığında çağrılır; o ana kadar küp yerinde durur. `step()` = `tick()` + tüm küplerin anında toplanması (çözücü ve testler için).
- **B-13** Kazanma: `remaining == 0`. Tıkanma: boş slot yok **ve** yolda kamyon yok (`pending == 0`) **ve** hiçbir slot yiyemiyor. Durum `playing`'den çıkınca `tick` ve `tap` hiçbir şey yapmaz.
- **B-14** `slotBlocked`: rengin ulaşılabilir küpü yok **ve** yoldaki kamyonlar da açmayacak. (UI'da çizgili "bekliyor" görünümü.)
- **B-15** `addSlot`: bir slot ekler, `stuck` ise `playing`'e döndürür.
- **B-16** `Game` kurucusu bölümü klonlar; aynı `Level` nesnesi defalarca oynanabilir (yeniden başlatma).

**Bölüm üretimi**

- **B-20** `mulberry32(seed)` — tüm rastgelelik buradan; aynı tohum = her ortamda aynı bölüm.
- **B-21** `peelLayers`: resmi halka halka soyarak her küpün açıldığı katman.
- **B-22** Kasa boyutları resim büyüklüğüne göre: `min = max(6, round(total/150))`, `smallMax = max(min+8, round(total/55))`, `bigMin = max(smallMax+10, round(total/32))`, `max = max(bigMin+6, min(80, round(total/16)))`, `bigChance = 0.15`. Bir rengin son kasası `smallMax`'tan küçükse kalan ne ise o olur (ör. Cactus'ta 4'lük kasa — `min`'in altında; bu **mevcut davranıştır**, korunmalı).
- **B-23** Kasa "zamanı" = ilk hücresinin katmanı + `rng() * shuffle * (maxLayer+1)` gürültü; zamana göre **kararlı** sıralama; 5'erli satırlar halinde, karıştırılmış kolon sırasıyla ve kısa kolonlar önce olacak şekilde dağıtım.
- **B-24** `buildLevel`: 16 aday (`seed + k*7919`); açgözlü çözücünün kazanamadığı elenir; rastgele oyuncu kazanma oranı (`24` oyun, tohum `seed + k*101`) hedefe en yakın olan seçilir; fark < 0.04 ise erken durur. Hiçbiri geçmezse `shuffle` kademeli azaltılır; o da olmazsa `shuffle = 0`.
- **B-25** Zorluk etiketi: `winRate ≥ 0.9` Easy, `≥ 0.65` Medium, `≥ 0.45` Hard, aksi hâlde Expert.
- **B-26** Kamyon toplamları her renk için resimdeki küp sayısına **tam** eşittir.

**Oturum / sunum kuralları (`main.ts`'de gömülü)**

- **B-30** "+1 Slot" güçlendirmesi bölüm başına **bir kez**, en fazla **6** slot; oyun kazanıldıysa kullanılamaz.
- **B-31** Hız düğmesi 1x → 2x → 3x → 1x. Hız sürüş, yükleme ve kamyon arası süreyi çarpar; kasanın uçuşu, parçacıklar ve UI animasyonları **çarpılmaz**.
- **B-32** Bir slot, kasa uçuşu bitip yerine oturana kadar kamyon göndermez (uçuş `1/3.8 s`, gerçek zaman).
- **B-33** Aynı slot ardışık kamyonları `POP_INTERVAL = 30/200 = 0.15 s` (simülasyon zamanı) arayla gönderir.
- **B-34** Kamyon rampanın 3 şeridinden (`-34, 0, +34`) rotası en kısa olanı seçer; her şerit kamyonları `30 * carScale / 200 s` arayla çıkarır. Kamyonlar birbirini beklemez, kesişimlerde içinden geçer.
- **B-35** Kamyon hedefe vardığında küp **o anda** `collect` edilir (tahtadan kalkar), sonra `LOAD_TIME = 0.42 s` yükler, aynı yoldan geri dönüp rampaya iner.
- **B-36** Sonuç paneli: durum `playing` değilse **ve** sahada/rampada kamyon kalmadıysa 0.4 s sonra açılır. Kazanınca "Next level" (sonuncuda "Back to level 1"), tıkanınca "+1 Slot" (kullanılabiliyorsa) ve "Retry".
- **B-37** Bölüm gezintisi: önceki/sonraki döngüseldir (10 → 1). İlerleme kaydı yok, uygulama 1. bölümden başlar (web'de `?level=N`).
- **B-38** Dolu slot varken kasaya dokunmak tepsiyi sallar (0.35 s), hamle yapmaz.
- **B-39** *(v1 kapsamı dışında, ileride eklenecek)* "Your image": resim uzun kenarı 40 küp olacak şekilde küçültülür (en az 4), en fazla 6 renk, alfa < 128 saydam; bölüm ayarı `seed 1234, shuffle 0.7, edges bottom, targetWinRate 0.6`.

### 1.3 Yerleşik bölümler (ölçülen değerler)

Kaynak commit'te Node 22 üzerinde ölçüldü (`buildLevel` çıktısı). Bunlar C# portu için **golden** referanslardır.

| # | Ad | Boyut | Renk | Küp | Kenar | shuffle | hedef | winRate | Kasa | Kasa boyu | Kolon uzunlukları | Greedy tick/tap | Üretim (ms) |
|--:|---|---|--:|--:|---|--:|--:|--:|--:|---|---|---|--:|
| 1 | Strawberry | 29×29 | 4 | 527 | all | 0.2 | 1.00 | 1.000 | 45 | 6–32 | 9/9/9/9/9 | 114/45 | 61 |
| 2 | Mushroom | 36×36 | 6 | 1296 | bottom | 0.2 | 0.85 | 0.833 | 61 | 9–78 | 12/12/12/13/12 | 311/61 | 354 |
| 3 | Sunset | 36×36 | 6 | 1296 | bottom | 0.2 | 0.60 | 0.542 | 49 | 9–75 | 10/10/10/9/10 | 329/49 | 434 |
| 4 | Ringed Planet | 46×46 | 6 | 2116 | bottom | 2 | 0.48 | 0.500 | 66 | 12–79 | 13/13/13/14/13 | 492/66 | 311 |
| 5 | Cat | 43×43 | 6 | 1849 | bottom | 1.5 | 0.40 | 0.417 | 64 | 12–77 | 13/12/13/13/13 | 409/64 | 456 |
| 6 | Cactus | 40×43 | 6 | 1720 | bottom | 1 | 0.80 | 0.792 | 72 | 4–65 | 15/14/14/14/15 | 357/72 | 47 |
| 7 | Rocket | 39×46 | 6 | 1794 | bottom | 1 | 0.72 | 0.708 | 65 | 12–80 | 13/13/13/13/13 | 567/65 | 89 |
| 8 | Rubber Duck | 43×43 | 6 | 1849 | bottom | 0.2 | 0.70 | 0.708 | 60 | 12–77 | 12/12/12/12/12 | 453/60 | 138 |
| 9 | Ice Cream | 36×46 | 7 | 1656 | bottom | 1.5 | 0.50 | 0.500 | 62 | 7–80 | 13/12/12/13/12 | 385/62 | 482 |
| 10 | Watermelon | 46×35 | 5 | 1610 | bottom | 1.5 | 0.35 | 0.333 | 62 | 11–80 | 12/13/13/12/12 | 360/62 | 36 |

`mulberry32` referans çıktıları (ilk 4 değer, 10 hane):

| seed | 1. | 2. | 3. | 4. |
|--:|---|---|---|---|
| 1 | 0.6270739406 | 0.0027357212 | 0.5274470400 | 0.9810509675 |
| 11 | 0.5115870486 | 0.5299464082 | 0.6081185641 | 0.5901576360 |
| 1234 | 0.0732949781 | 0.7034119898 | 0.9028560191 | 0.9705493662 |

Gözlemler:

- En büyük ızgara 46×46 = 2116 küp. 3D'de bu kadar küpün mobilde tek tek `GameObject` olarak çizilmesi pahalıdır (§7, R-11).
- Masaüstü V8'de bir bölümün üretimi 36–482 ms. IL2CPP ile orta seviye bir telefonda bunun birkaç katı beklenmeli. Yerleşik bölümler önceden üretilip JSON'a gömülecek (§6). "Your image" v1'de olmadığından ilk sürümde çalışma zamanında hiç bölüm üretilmez.

### 1.4 TypeScript modül bağımlılıkları

`import` satırlarından çıkarıldı. Kesikli oklar yalnızca tür (`import type`) bağımlılığıdır.

```mermaid
flowchart LR
  subgraph core["src/core — kurallar"]
    types["types.ts"]
    board["board.ts"]
    game["game.ts"]
  end
  subgraph levels["src/levels — üretim"]
    rng["rng.ts"]
    solver["solver.ts"]
    generator["generator.ts"]
    art["art.ts"]
    image["image.ts"]
    worker["worker.ts"]
  end
  subgraph view["src/view — çizim"]
    roads["roads.ts"]
    draw["draw.ts"]
  end
  main["main.ts (App)"]
  board --> types
  game --> board
  game --> types
  solver --> game
  solver --> rng
  solver -.-> types
  generator --> board
  generator --> types
  generator --> rng
  generator --> solver
  art --> types
  art -.-> generator
  image --> types
  worker --> art
  worker --> generator
  worker -.-> types
  roads -.-> board
  main --> game
  main --> types
  main --> art
  main --> generator
  main --> image
  main --> draw
  main --> roads
  main ==>|"Web Worker (inline)"| worker
```

Gözlemler: `core` hiçbir şeye bağlı değil (yalnızca kendi içinde); `draw.ts` hiçbir modülü içe aktarmıyor (saf Canvas
yardımcıları); `roads.ts` `Board`'u yalnızca tür olarak kullanıyor (`width`, `height`, `openSides`, `neighbors`,
`isOpen`). Döngüsel bağımlılık yok. Taşımada bu yön korunur (§3.8).

### 1.5 Referans sınıflar: iç durum ve algoritmalar

#### `Board` (`src/core/board.ts`)

| Alan | Tür | Başlangıç | Kim değiştirir | Anlam |
|---|---|---|---|---|
| `cells` | `Int16Array` | resmin kopyası | `eat`: renk → `EMPTY` | Tahtadaki küpler |
| `open` | `Uint8Array` (özel) | 0 | `flood`: 0 → 1 | Açık kenara bağlı boş hücre (yalnızca artar) |
| `claimed` | `Uint8Array` (özel) | 0 | `claim` 0→1, `eat` 1→0 | Kamyon gönderilmiş ama alınmamış küp |
| `pending` | `number` | 0 | `claim` ++, `eat` (claimed) −− | Yoldaki kamyon sayısı |
| `reachable` | `Set<number>[]` (özel, renk başına) | kurucuda açıktakiler | `flood` ekler; `claim`/`eat` siler | Şu an kamyon gönderilebilecek küpler |
| `remainingByColor`, `remaining` | `number[]`, `number` | küp sayıları | `eat` −− | Kalan küpler (claimed dahil) |
| `depth` | `Int32Array` (özel) | `1 << 30` | `updateDepths` (yalnızca azalır) | Açık hücreye yoldan girişten sonra kaç hücre içeride |
| `road` | `Float32Array` (özel) | `+∞` | `updateDepths` | O girişe kadar rampadan yol mesafesi (hücre) |
| `wayDepth`, `wayRoad` | `number` (özel) | `+∞` | `wayIn` | Sıcak yolda tahsis yapmamak için geçici sonuç |

Algoritmalar (birebir korunacak):

1. **Kurucu.** (a) Her boş, henüz açık olmayan ve açık kenarda duran hücreden `flood`; (b) her küp için
   `remaining++`, `remainingByColor[c]++`, açıkta (`isExposed`) ise `reachable[c]`'ye ekle.
2. **`flood(i)`** (yığınlı DFS): `i`'yi açar; komşu küp ise ve claimed değilse `reachable`'a ekler; komşu boş ve
   kapalıysa açar ve yığına iter. Sonunda açılan tüm hücrelerle `updateDepths(opened)`.
3. **`updateDepths(opened)`**: her yeni açık hücre için önce açık kenarlarından `(0, roadTo)`, sonra açık
   komşularından `(depth+1, road)` adaylarının en iyisi (`better`: önce derinlik, eşitlikte yol, **katı** `<`);
   ardından bu hücrelerden kuyruklu gevşetme (BFS) — yalnızca iyileşen komşular yeniden kuyruğa girer.
4. **`wayIn(i)`** (küp için): adaylar sırasıyla alt kenar, yan kenarlar (`edges ≠ bottom`), üst kenar
   (`edges = all`), sonra sol, sağ, üst, alt açık komşular; seçim `better` ile (sözlük sırası, ilk bulunan kazanır).
5. **`pickTarget(c)`**: `reachable[c]` üzerinde; satır = `h−1−y` (0 = alt). Önce en küçük satır, sonra en küçük
   **sürüş = wayDepth + wayRoad (toplam)**, eşitlikte en küçük indeks. Dikkat: `wayIn` yolu *sözlük sırasıyla*
   seçer ama `pickTarget` küpleri *toplamla* karşılaştırır; bu iki ölçüt kasıtlıdır ve aynen taşınmalıdır (I-08).
6. **`claim(i)`**: `reachable`'da değilse istisna; `reachable`'dan sil, `claimed = 1`, `pending++`.
7. **`eat(i)`**: boşsa istisna; claimed ise bayrağı sil ve `pending−−`, değilse `reachable`'da olmalı (yoksa istisna);
   hücreyi boşalt, sayaçları düşür, `flood(i)`.
8. **`opensUp(c)`**: `pending = 0` ise false. Tüm claimed hücrelerden başlayarak kapalı boş hücreler üzerinden yayılır;
   claimed olmayan `c` renkli bir komşu bulursa true. Her çağrıda `cells.length` boyunda dizi ayırır (R-11).

#### `Game` (`src/core/game.ts`)

| Alan | Anlam |
|---|---|
| `level` | Kurucuda `cloneLevel` ile alınan derin kopya (kasalar kopyalanır, resim paylaşılır) |
| `board` | `new Board(level.picture, level.edges)` |
| `columns` | `level.columns` (kopya üzerinde); `[c][0]` öndeki kasa; `tap` `shift()` ile çıkarır |
| `slots` | `(Block \| null)[]`, uzunluk `slotCount`; `addSlot` `push(null)` |
| `status` | `"playing" \| "won" \| "stuck"`; her `tap`/`tick`/`collect`/`step` sonunda `updateStatus` |

`updateStatus`: `remaining = 0` → `won`; değilse `isStuck()` → `stuck`. `stuck`'tan çıkış yalnızca `addSlot`.

#### `RoadMap` (`src/view/roads.ts`)

Ekran (tasarım pikseli) koordinatlarında çalışır. `routeFrom(t, entry)`: hedef küpten başlayıp **açık** hücreler
üzerinde BFS; ziyaret edilen her hücrenin açık kenarları için maliyet = `dist·cs + kenardan yola uzaklık +
roadDistance(entry, yol noktası)`; en ucuz (katı `<`, ilk bulunan) seçilir. Yol: `entry` → `roadPath` (kısa yöndeki
köşeler, 0.5 px eşikli) → yol noktası → BFS ağacı boyunca hücre merkezleri; küp kenardaysa `beside` (kenarın bir
hücre dışı). `along` yol halkası üzerindeki konumu sol üst köşeden saat yönünde ölçer (0.5 px eşik).

#### Üretim hattı (`generator.ts`, `solver.ts`, `rng.ts`)

- `mulberry32(seed)`: 32-bit durum; `a += 0x6D2B79F5`; iki `Math.imul` karıştırma; çıktı `uint32 / 2^32`.
- `peelLayers(picture, edges)`: geçici `Board`; her turda tüm renklerin `reachableCells` anlık görüntüsünü katman `l`
  ile işaretleyip yer; `remaining = 0` olana kadar.
- `generateLevel`: §1.2 B-22/B-23. RNG çağrı **sırası**: renk sırasıyla `splitCount` içindeki çağrılar → her kasa
  için bir gürültü çağrısı → her dağıtım satırı için `shuffled` (5 sütunda 4 çağrı). Sıra değişirse tüm tepsi değişir.
- `buildLevel`: aday döngüsü → `solveGreedy` → `randomWinRate`; seçim katı `<` (eşitlikte önceki aday kalır).
- `solveGreedy`: döngü {boş slotları `chooseTap(force=false)` ile doldur → `step()` → hiçbir şey gönderilmediyse
  `chooseTap(force=true)` ile bir dokunuş}; puan = `reach / (1 + aynı renkli slot) + (force ? kalan·1e-3 : 0)`,
  katı `>` (eşitlikte küçük kolon). `maxTicks = 200 000`.
- `randomWinRate(level, 24, seed)`: oyun `k` için `mulberry32(seed + k·101)`; boş slot oldukça dolu kolonlardan
  `floor(rng·n)` ile seç, sonra `step()`.

### 1.6 Claim / eat zamanlaması

İki çalışma kipi vardır ve ikisi de korunmalıdır:

| Kip | Kim kullanır | Sıra |
|---|---|---|
| **Anında** (`Game.step`) | Çözücü, rastgele oyuncu, birim testleri | `tick()` claim eder → aynı çağrıda tüm hücreler `eat` |
| **Gecikmeli** (`tick` + `collect`) | Tarayıcıdaki oyun | `tick(ready)` claim eder; küp kamyon varana kadar tahtada kalır; varışta `collect` → `eat` |

Gecikmeli kipte bir küpün zaman çizelgesi (hız = 1x, birimler tasarım pikseli):

```
t₀            Game.tick(ready)  → claim: count−−, pending++, küp reachable'dan çıkar ama yolu kapatmaya devam eder
              App.sendTruck     → 3 şerit için RoadMap.routeFrom, en ucuz şerit; kamyon şerit kuyruğuna
t₀ + q        şerit serbest     → q: şerit kuyruğu bekleme (her şerit 30·carScale/200 s ≈ 0.075–0.15 s aralıkla)
t₀ + q + D/v  varış (pickUp)    → shown[cell] = EMPTY, Game.collect(cell) → Board.eat → flood → yeni reachable
              + 0.42 s          → yükleme (küp kasaya uçar; LAND_AT = 0.78'de kasaya iner)
              + D/v             → aynı yoldan geri, rampaya iner, kaybolur
```

`v = 200·speed` px/s; `D = |TUNNEL.y + 0.2·TUNNEL.h − ring.y1| + rota uzunluğu`. Aynı slotun sonraki kamyonu en erken
`0.15 s / speed` sonra çıkar ve yalnızca o anda ulaşılabilir bir küp varsa. Sonuçları:

- Claim ile collect arasında küp hâlâ engeldir; bu yüzden `slotBlocked` `opensUp`'a bakar (yanlış "bloke" göstermemek için).
- `pending > 0` iken oyun tıkanmış sayılmaz; yoldaki kamyon sayısının üst sınırı yoktur (15 kamyon testi).
- Kazanma son `collect`'te ilan edilir; panel kamyonlar dönüp 0.4 s geçince açılır.
- Kare içinde `tick` sürüşten **önce** çalışır: bir karede açılan küp en erken bir sonraki karenin `tick`'inde claim
  edilir (I-18). C# sabit adım döngüsü bu sırayı korur.

### 1.7 Kasa, slot ve kamyon yaşam döngüleri

```mermaid
stateDiagram-v2
  direction LR
  state "Tepside" as Tray
  state "Uçuyor (slot dolu, kamyon yok)" as Flying
  state "Aktif" as Active
  state "Bloke (yalnızca görünüm)" as Blocked
  state "Ayrılıyor (0.5 s)" as Leaving
  [*] --> Tray
  Tray --> Flying : Game.tap — ön kasa, ilk boş slot
  Flying --> Active : 1/3.8 s (gerçek zaman)
  Active --> Active : tick → kamyon, count−−, 0.15 s bekle
  Active --> Blocked : reachable = 0 ve opensUp = false
  Blocked --> Active : renk yeniden ulaşılabilir
  Active --> Leaving : count = 0 → slot null
  Leaving --> [*]
```

```mermaid
stateDiagram-v2
  direction LR
  state "Rampa kuyruğunda" as Queued
  [*] --> Queued : sendTruck
  Queued --> out : şerit aralığı doldu
  out --> load : rota sonu → pickUp / Game.collect
  load --> home : 0.42 s (sim)
  home --> gone : rampaya ulaştı
  gone --> [*]
```

Tepsi: her kolon bir liste, yalnızca `[0]` dokunulabilir; ekranda 3 sıra görünür (önde tam, arkada solgun ve dar).
Bir kolon boşalınca kalan kolonlar ortada toplanır (yalnızca görünüm).

### 1.8 Çağrı sırası

**Açılış**

```
App.constructor
├─ builtInLevels()                              10 × Sketch → Picture (+ ayarlar)
├─ resize()
├─ startLevel()
│  ├─ builtLevel(i) → buildLevel(picture, spec)          (önbellekte yoksa, ana iş parçacığında)
│  │  └─ k = 0..15
│  │     ├─ generateLevel(seed + k·7919)
│  │     │  ├─ mulberry32(seed)
│  │     │  ├─ peelLayers → new Board (flood → updateDepths) → [reachableCells → eat]*
│  │     │  ├─ renk başına: kararlı sort(katman) → splitCount
│  │     │  ├─ gürültü → kararlı sort(zaman)
│  │     │  └─ 5'li satırlar: shuffled → kararlı sort(kolon uzunluğu) → dağıt
│  │     ├─ solveGreedy → new Game → [chooseTap → tap]* → step()* …
│  │     └─ randomWinRate(level, 24, seed) → 24 × (new Game, tap/step)
│  ├─ new Game(level) → cloneLevel → new Board
│  ├─ cellSize / alan orijini / ring → new RoadMap(board, fx, fy, cs, ring, entry)
│  └─ görünüm durumu sıfırla (cars, ramp, flyers, timers, colX, levelSeq++, slotBoostUsed = false)
├─ prebuildLevels() → Worker.postMessage(i)              (komşu bölümlerden başlayarak)
└─ requestAnimationFrame(frame)
```

**Kare** — `frame(now)`: `dt = min(0.05, Δt)` → `update(dt)` → `draw()`.

`update(dt)` sırası (C#'ta `GameSession.Advance` + görünüm `Update` bu sırayı korur):

1. `time += dt`, `shake` azalt
2. `sdt = dt·speed`; her slot için `slotTimer −= sdt`; `slotBump` azalt
3. `status = playing` ise `game.tick(ready)` (`ready = slotTimer ≤ 0 ∧ slot uçmuyor`) → her `Dispatch` için
   `sendTruck` → `slotTimer = 0.15`, `slotBump = 1`, slot boşaldıysa `leave()`
4. Rampa şeritleri: `rampTimer −= sdt`; süresi dolan şerit kuyruğundan bir kamyon `cars`'a
5. Kamyonlar: `load` → yöne dön, `wait −= sdt`, `LAND_AT`'ta toz, bitince `home` yolu; diğerleri →
   `drive(c, 200·sdt)` (varışta `pickUp` → `game.collect` → `board.eat`) → yöne dön
6. `gone` kamyonları sil
7. Parçacıklar, buton basma, uçan kasalar (`t += dt·3.8`, bitince `land`), ayrılan kasalar, `slotLand`, `trayRise`,
   kolon kayması — hepsi gerçek `dt`
8. Durum `playing` değil ve kamyon/kuyruk yoksa `overlayDelay += dt`

`draw()` sırası: buton listesini sıfırla → statik katman (anahtar değiştiyse) → küp katmanı (kirli hücreler) →
ekrana bas → canlı geçiş: üst çubuk, slotlar (her slot için `slotBlocked` → `opensUp`), tepsi, uçan kasalar,
alt çubuk → kamyonlar → havadaki küpler → parçacıklar → panel. Butonlar çizim sırasında kaydedilir; dokunuşta son
kaydedilen (en üstteki) kazanır.

**Dokunuş** — `pointerdown` → `onPointer` → butonları sondan başa tara → `onClick` → `tapColumn(col)` →
(boş slot yoksa `shake = 0.35`, çık) → `startCounts.set(id, count)` → `game.tap(col)` → `updateStatus` →
`flyers.push`, `trayRise[col] = 0`.

**Kamyon gönderme** — `Game.tick` → slot `s` için `ready(s)` → `board.pickTarget(color)` (→ her aday için `wayIn`)
→ `board.claim(cell)` → `count−−` → `count = 0` ise `slots[s] = null` → döngü sonunda `updateStatus`.

**Toplama** — `App.drive` → rota bitti → `pickUp` → `shown[cell] = EMPTY`, `cubeDirty.push` → `game.collect(cell)`
→ `board.eat` → `flood` → `updateDepths` → `updateStatus`.

**Güçlendirme** — `+1 Slot` → `App.addSlot` (kullanılmadıysa ve < 6) → `game.addSlot` (`stuck → playing`) →
`overlayDelay = 0`.

### 1.9 Animasyon ve UI envanteri

**Animasyonlar** (hepsi görünüm katmanına gider; "sim" zaman tabanlı olanlar hız düğmesiyle hızlanır):

| Animasyon | Tetikleyici | Süre / parametre | Zaman | TS | Unity karşılığı |
|---|---|---|---|---|---|
| Kasa uçuşu (tepsi → slot, 26 px yay, tepede şişme) | `tap` | `1/3.8 s` | gerçek | `Flyer`, `drawFlyers` | `CrateFlight` |
| Slota iniş (zıplama, parıltı, 8 kıvılcım) | uçuş biter | 0.45 s | gerçek | `land`, `slotLand` | `SlotView` + `FxService` |
| Sayı "pop"u | kamyon çıkar | `slotBump` 1→0, 6/s | gerçek | `slotBump` | `SlotView` |
| İlerleme çubuğu | kamyon çıkar | kalan / başlangıç | — | `startCounts` | `SlotView` |
| Bloke çizgileri + etiket | `slotBlocked` | sürekli | — | `holdStripes`, `pill` | `SlotView` (şerit shader'ı) |
| Kasanın ayrılması (halka, şişme, dönerek küçülme, 14 konfeti) | son kamyon | 0.5 s | gerçek | `leave`, `drawLeaving` | `SlotView` + `FxService` |
| Tepsi yükselme dalgası + parlama | `tap` | 0.32 s + 0.06 s·sıra, `easeOutBack`; parlama 0.4 s | gerçek | `trayRise`, `sheen` | `TrayView` |
| Kolonların kayması | kolon boşalır | `1 − e^(−12·dt)` | gerçek | `colX` | `TrayView` |
| Tepsi sallanması | dolu slotta dokunuş | 0.35 s | gerçek | `shake` | `TrayView` |
| Rampadan çıkış/iniş (büyüme, solma) | kamyon | 28 px boyunca | sim | `RAMP_FADE` | `TruckView` |
| Dönüş yumuşatma | sürüş / yükleme | 14 rad/s (yükte 10) | sim | `turnToward` | `TruckView` |
| Sağ şeritten gitme | yol üstünde | `5.8·carScale·laneShare` | — | `carPose`, `laneShare` | `TruckView` |
| Küp yükleme (sallanma, yay, kasaya iniş, yay sıkışması, toz) | varış | 0.42 s; `LIFT_END 0.22`, `LAND_AT 0.78` | sim | `drawLoading`, `landPuff` | `TruckCargoAnimator` |
| Küp kalkış tozu | varış | 0.25–0.4 s | gerçek | `pickUp` | `FxService` |
| Buton basma (çökme, ışık, dalga) + 8 ms titreşim | dokunuş | 0.28 s | gerçek | `press`, `pressGlow` | `PressFeedback`, `HapticsService` |
| Sonuç paneli | durum + kamyonlar bitti | 0.4 s gecikme | gerçek | `drawOverlay`, `panel` | `EndPanelView` |

Parçacıklar `Math.random` kullanır; oyun mantığına etkisi yoktur ve C#'ta deterministik olması gerekmez.

**UI öğeleri** (420×900 tasarım koordinatları):

| Öğe | Konum / boyut | İçerik | Unity karşılığı |
|---|---|---|---|
| Üst çubuk | y ≈ 14–55 | Yeniden başlat (30,32 r17), önceki (70,32 r15), sonraki (300,32 r15), başlık `i/10 · Ad` (185,24), zorluk çipi (185,38), hız düğmesi (326,14 82×38) | `HudView`, `SpeedButton` (uGUI) |
| Tahta kartı | (16,64) 388×388 | Kart, yol halkası (asfalt 23 px, kenar çizgileri, kesikli orta çizgi), tuğla duvar (9.5), küpler (`cs − max(1, 0.1·cs)`), alınan küp yerinde soket | `BoardView`, `RoadView`, `WallView` (3D) |
| Rampa | merkez (210,496) 112×36 | Giriş yolu, 3 şerit (−34/0/+34), portal ışıkları | `RampView` (3D) |
| Slot dok | y 540, h 66, genişlik ≤ 68, aralık 10, en fazla 6 | Boş yuva, kasa, sayı, ilerleme çubuğu, bloke çizgileri | `SlotView` (3D veya UI) |
| Tepsi | y 636, 5 kolon 68×52, aralık 10×8, görünür 3 sıra | Alfa 1 / 0.55 / 0.32, içe çekme 0 / 5 / 9, yazı 25 / 19 / 19 | `TrayView`, `CrateView` |
| Alt çubuk | y 840, 176×54 düğmeler | "+1 Slot" (rozet 1/0); "Your image" (v1'de yok) | `BottomBarView` |
| Panel | 320×220 | "Picture complete!" / "Out of moves", düğmeler | `EndPanelView` |

**`draw.ts` yardımcıları → Unity varlıkları:** `car` → kamyon modeli/prefab'ı (renk materyal özelliğiyle);
`cubeSprite` → yuvarlatılmış küp mesh'i + tek materyal; `socketSprite` → soket mesh'i/decal; `pod` → kasa prefab'ı;
`countLabel`, `labelWidth`, `BODY_FONT` → TextMeshPro yazı tipi + konturlu materyal; `roundIconButton`, `roundButton`,
`icon`, `iconChip`, `badge`, `speedArrows(Width)` → UI sprite'ları ve prefab'lar; `shelf`, `lane`, `dock`, `well`,
`pill`, `holdStripes` → UI/3D dekorlar ve şerit shader'ı; `brickWall` → duvar mesh'i + doku; `shade`, `luminance` →
`ColorUtil`; `stamp` ve katman önbellekleri → gerek yok (URP batching/instancing).

---

## 2. Katmanların ayrılması

### 2.1 Hedef katmanlar

```mermaid
flowchart TB
  subgraph Pure["Saf C# (UnityEngine referansı yok, EditMode'da test edilir)"]
    Core["Core — oyun kuralları<br/>Board, Game"]
    Levels["Levels — bölüm üretimi<br/>Mulberry32, LevelGenerator, Solver, ImageQuantizer"]
    Data["Data — JSON DTO + eşleyici + doğrulayıcı"]
    Sim["Simulation — kurala etki eden zamanlama<br/>RoadMap, TruckFleet, DispatchScheduler, GameSession"]
  end
  subgraph Unity["Unity katmanı (MonoBehaviour, URP)"]
    App["App — önyükleme, bölüm deposu, arka plan üretimi"]
    View["View — 3D sahne, UI, efekt, kamera, giriş"]
  end
  Levels --> Core
  Data --> Core
  Sim --> Core
  App --> Data
  App --> Levels
  App --> Sim
  View --> Sim
  View -.olaylar.-> App
```

Bağımlılıklar yalnızca yukarıdan aşağıya akar. Görünüm (View), kuralları **hiçbir zaman** doğrudan değiştirmez; yalnızca
`GameSession`'a komut gönderir (`TapColumn`, `UseSlotBooster`, `SetSpeed`, `Restart`) ve onun olaylarını dinler.

### 2.2 `main.ts`'nin parçalanması

`App` sınıfı her şeyi bir arada tutuyor. Sorumluluklar şöyle dağıtılacak:

| `main.ts` parçası | Ne yapıyor | Kurala etkisi | Hedef |
|---|---|---|---|
| `startLevel`, `goToLevel`, `builtLevel`, `levelIndex`, `custom*` | Bölüm yaşam döngüsü, gezinti | Var (B-37) | `Simulation.GameSession` + `LevelProgression` |
| `addSlot`, `slotBoostUsed`, `MAX_SLOTS` | Güçlendirme kuralı | **Var** (B-30) | `Simulation.BoosterRules` |
| `speed` | Hız çarpanı | Var (B-31) | `GameSession.Speed` |
| `update()`: `slotTimer`, `ready`, `POP_INTERVAL`, uçuş kapısı | Kamyon gönderme zamanlaması | **Var** (B-32, B-33): hangi küpün hangi anda claim edildiğini belirler | `Simulation.DispatchScheduler` |
| `sendTruck`, `ramp`, `rampTimer`, `carScale` | Şerit seçimi, rampa kuyruğu | Var (B-34) | `Simulation.TruckFleet` |
| `drive`, `pickUp`, `Car.state/wait` | Kamyonun yolda ilerlemesi, varışta `collect` | **Var** (B-35): `collect` anı yeni küpleri açar | `Simulation.TruckFleet` |
| `RoadMap` (`view/roads.ts`) | Rota planlama | Var (rota uzunluğu = varış zamanı) | `Simulation.RoadMap` |
| `cellSize`, `BOARD`, `FIELD_INSET`, `ROAD_GAP`, `TUNNEL`, `RAMP_LANES` | Tarla/yol yerleşimi | Var (mesafeler zamanı belirler) | `Simulation.FieldLayout` |
| `overlayDelay`, panel koşulları | Sonuç paneli zamanlaması | Dolaylı (B-36) | `GameSession.EndPanelReady` |
| `winRate` → Easy/Medium/... | Zorluk etiketi | Yok (gösterim) | `Levels.DifficultyLabel` |
| `Car.angle`, `turnToward`, `carPose`, `laneShare`, `bedPoint` | Kamyonun yönü, şeritte sağdan gitme, kasadaki küp | Yok | View: `TruckView` |
| `shown`, `cubeDirty`, `drawCubes` | Küp görünürlüğü | Yok (`shown` = `collect` olayıyla güncellenir) | View: `BoardView` |
| `flyers`, `leavers`, `slotLand`, `slotBump`, `trayRise`, `colX`, `shake`, `press` | UI animasyonları | Yok (uçuş süresi hariç → B-32) | View: `TrayView`, `SlotView`, `CrateFlight` |
| `particles`, `landPuff`, `land`, `leave` | Efektler (`Math.random`) | Yok | View: `FxService` |
| `staticLayer`, `cubeLayer`, `trayLayer`, `stamp`, `sprites` | Canvas önbellekleme | Yok | Kaldırılır (URP batching / instancing) |
| `onPointer`, `buttons`, `tapColumn` | Giriş | Yok | View: `InputRouter` |
| `resize`, `dpr`, `scale` | Ekran ölçekleme | Yok | View: `CameraRig`, Canvas Scaler, Safe Area |
| `prebuildLevels`, `buildCustom`, Worker | Arka plan üretim | Yok | v1: gerek yok (bölümler pişirilmiş). İleride: App `LevelBuildService` (thread) |
| `onFile`, `pictureFromFile` | Dosya seçme, küçültme | Yok (sonucu bölümü belirler) | **v1'de yok.** İleride: App `ImagePickerService` + `Levels.ImageQuantizer` |
| `navigator.vibrate(8)` | Dokunma geri bildirimi | Yok | View: `HapticsService` |

> **Neden zamanlama simülasyon katmanında?** Tarayıcıda oyunun gidişatı yalnızca `tap` sırasına değil, kamyonların
> ne zaman küpe vardığına da bağlıdır (B-12, B-35): bir küp ancak kamyon onu aldığında arkasındaki küpleri açar.
> 3D sahnede yol uzunlukları ya da hızlar değişirse, aynı dokunuşlar farklı bir oyuna dönüşebilir ve ayarlanmış
> zorluk kayar. Bu yüzden mesafe/hız/zaman hesapları görünümden bağımsız, saf ve test edilebilir bir katmanda,
> TypeScript'teki **tasarım birimleriyle** (420×900 piksel uzayı) birebir tutulur. 3D sahne bu koordinatları yalnızca
> dünyaya eşler (§3.5).

### 2.3 `levels/` içindeki ayrım

| Parça | Hedef | Not |
|---|---|---|
| `rng.ts`, `generator.ts`, `solver.ts` | `Levels` (saf) | Bire bir port, bit düzeyinde aynı sonuç |
| `image.ts › quantize` | `Levels.ImageQuantizer` (saf) | Girdi: `byte[] rgba, width, height`. Saf ve küçük olduğu için v1'de de taşınır ve test edilir (mevcut testi korur, özelliği sonra eklemeyi kolaylaştırır); oyunda kullanılmaz |
| `image.ts › pictureFromFile` | **v1'de yok.** İleride: App `ImagePickerService` | Unity `Texture2D` + GPU küçültme; sonuç tarayıcıdan birebir olmayabilir (R-20) |
| `art.ts › Sketch` + resimler | **Taşınmaz** — bir kerelik dışa aktarılır | Resimler JSON'a "pişirilir" (§6). `Math.sin/sqrt/pow` eşik farkları C#'ta pikselleri değiştirebilir (R-04) |
| `art.ts › builtInLevels` ayarları | JSON `generation` bölümü | |
| `worker.ts` | v1: yok. İleride: App `LevelBuildService` | `Task.Run` + ana iş parçacığına dönüş |

---

## 3. Unity C# sınıf mimarisi

### 3.1 Depo ve klasör düzeni

Unity projesi doğrudan depo kökünde durur (Unity yalnızca `Assets/`, `Packages/`, `ProjectSettings/` klasörlerini
okur; `Tools/` ve belgeler etkilenmez). `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `Build/` ve `Tools/**/node_modules`
`.gitignore`'a girer.

```
CubeHaul3D/                         (bu depo = Unity 6.3 LTS projesi, URP)
├─ docs/
│  ├─ PORTING_PLAN.md               Bu belge
│  ├─ TS_BASELINE.md                TS referans test raporu ve golden değerler
│  └─ LEVEL_FORMAT.md               LevelDefinition v1 formatı
├─ LevelFormat/                     Bölüm formatı: JSON Schema, örnekler, iki dilin ortak test verileri (Görev 03)
├─ Tools/
│  ├─ level-format/                 TS okuyucu/doğrulayıcı ve kanonik yazıcı (Görev 03)
│  ├─ dotnet/                       CubeHaul.Data'yı Unity dışında derleyen ve test eden .NET projeleri
│  ├─ ts-reference/                 TS referans testleri (Görev 02, docs/TS_BASELINE.md); kaynağı pinli commit'ten okur
│  └─ (ts-export)                   Görev 04'te ts-reference'a katıldı: npm run export:golden / check:golden
│                                   (TS projesini değiştirmez; ayrı klon/alt modül üzerinden içe aktarır)
├─ Packages/manifest.json           URP, Input System, Test Framework, uGUI/TextMeshPro, sabit sürümler
├─ ProjectSettings/
└─ Assets/
   ├─ CubeHaul/
   │  ├─ Scripts/
   │  │  ├─ Core/          CubeHaul.Core.asmdef         noEngineReferences: true
   │  │  ├─ Levels/        CubeHaul.Levels.asmdef       noEngineReferences: true  → Core
   │  │  ├─ Data/          CubeHaul.Data.asmdef         noEngineReferences: true  → Core
   │  │  ├─ Simulation/    CubeHaul.Simulation.asmdef   noEngineReferences: true  → Core, Levels
   │  │  ├─ App/           CubeHaul.App.asmdef          → Core, Levels, Data, Simulation, UnityEngine
   │  │  └─ View/          CubeHaul.View.asmdef         → Simulation, App, UnityEngine, URP, Input System
   │  ├─ Content/Levels/   level-01-strawberry.level.json … level-10-watermelon.level.json (Görev 04)
   │  ├─ Art/              Modeller, materyaller, shader'lar
   │  └─ Scenes/           Boot.unity, Game.unity
   └─ Tests/
      ├─ EditMode/         CubeHaul.Tests.EditMode.asmdef  (NUnit; saf katmanlar + JSON)
      ├─ PlayMode/         CubeHaul.Tests.PlayMode.asmdef  (sahne, giriş, akış)
      └─ Fixtures/Golden/  TS'den dışa aktarılan referans çıktılar
```

`noEngineReferences: true` saf katmanların yanlışlıkla `UnityEngine` kullanmasını derleyici düzeyinde engeller.
Böylece bu katmanlar istenirse Unity dışında, `dotnet test` ile de çalıştırılabilir.

### 3.2 Core — oyun kuralları (`CubeHaul.Core`)

| C# türü | TS karşılığı | Tasarım notu |
|---|---|---|
| `static class Cells { const int Empty = -1; }` | `EMPTY` | |
| `enum OpenEdges { All, Sides, Bottom }` | `OpenEdges` | JSON'da metin; ayrıştırma ordinal (R-14) |
| `enum Side { Top, Right, Bottom, Left }` | `Side` | |
| `sealed class Picture` (`Name`, `Width`, `Height`, `short[] Cells`, `string[] PaletteHex`) | `Picture` | Değişmez kabul edilir; `Board` kopyalar |
| `sealed class Crate` (`Id`, `Color`, `Count`) | `Block` | Oyun içinde `Count` azalır; bu yüzden sınıf (struct değil) |
| `sealed class Level` (`Picture`, `List<List<Crate>> Columns`, `SlotCount`, `Edges`) + `Clone()` | `Level`, `cloneLevel` | Derin kopya yalnızca kasalar için, resim paylaşılır |
| `sealed class Board` | `Board` | Aşağıda |
| `enum GameStatus { Playing, Won, Stuck }` | `Status` | |
| `readonly struct Dispatch` (`Slot`, `Cell`, `Color`) | `Dispatch` | |
| `sealed class Game` | `Game` | Aşağıda |

**`Board` genel arayüzü** (TS ile aynı adlar, PascalCase):

```text
Board(Picture picture, OpenEdges edges = All)
int Width, Height; short[] Cells; OpenEdges Edges
int Remaining; int Pending
IReadOnlyList<Side> OpenSides(int i)        // sıcak yolda yerine bit maskesi kullanan iç sürüm
void Neighbors(int i, Span<int> buffer, out int count)   // tahsis yapmayan sürüm
bool IsOpen(int i); bool IsClaimed(int i)
int ReachableCount(int color); int RemainingOf(int color)
void CopyReachableCells(int color, List<int> into)        // TS: reachableCells (anlık görüntü)
bool OpensUp(int color)                     // tamponlar yeniden kullanılır (R-11)
int PickTarget(int color)
void Claim(int i); void Eat(int i)
```

İç veri: `short[] cells`, `byte[] open`, `byte[] claimed`, `int[] depth` (başlangıç `1 << 30`),
`double[] road` (başlangıç `+∞`; TS'de `Float32Array`, bkz. R-05), renk başına `HashSet<int> reachable`.
`PickTarget` sonucu kümenin gezinme sırasından bağımsızdır (eşitlikte küçük indeks), bu yüzden `HashSet` güvenlidir;
yine de sıraya bağlı hiçbir dış davranış `HashSet` sırasına dayanmamalıdır (R-06).

**`Game` genel arayüzü:**

```text
Game(Level level)
Level Level; Board Board; List<List<Crate>> Columns; List<Crate?> Slots; GameStatus Status
Crate? Front(int col); bool HasFreeSlot(); bool CanTap(int col)
int Tap(int col)                            // slot indeksi ya da -1
bool SlotCanEat(int slot); bool SlotBlocked(int slot)
void AddSlot()
int Tick(Func<int,bool> ready, List<Dispatch> output)    // tahsis yapmayan; null ready = hepsi hazır
void Collect(int cell)
int Step(List<Dispatch> output)
bool IsStuck(); int TrayCount()
```

Kolonlar `List<Crate>` olarak kalır (önden çıkarma `RemoveAt(0)`; kolonlar en fazla ~15 kasa), çünkü görünüm
arka sıradaki kasaları da çizer. `Tick` çıktıyı çağıranın listesine yazar, kare başına çöp üretmez.

### 3.3 Levels — bölüm üretimi (`CubeHaul.Levels`)

| C# türü | TS karşılığı | Not |
|---|---|---|
| `sealed class Mulberry32` (`double Next()`) | `mulberry32` | `uint` aritmetiği, `unchecked` (R-03) |
| `static class StableSort` | `Array.prototype.sort` | Kararlı sıralama; `List.Sort` **kullanılmaz** (R-01) |
| `static class JsMath` (`Round`, `Imul`) | `Math.round`, `Math.imul` | `Round(x) = Math.Floor(x + 0.5)` (R-02) |
| `struct GenOptions`, `struct BuildOptions` | `GenOptions`, `BuildOptions` | Opsiyonel alanlar `int?/double?` |
| `static class PeelLayers` | `peelLayers` | |
| `static class LevelGenerator` (`Generate`, `Build`, `Shuffled`, `SplitCount`) | `generateLevel`, `buildLevel`, `shuffled`, `splitCount` | `Build` iptal edilebilir (`CancellationToken`) |
| `readonly struct BuiltLevel` (`Level`, `WinRate`) | `BuiltLevel` | |
| `static class GreedySolver` → `SolveResult` | `solveGreedy` | |
| `static class RandomPlayer` (`WinRate`) | `randomWinRate` | |
| `static class ImageQuantizer` (`Quantize(byte[] rgba, int w, int h, int k, string name)`) | `quantize` | |
| `static class DifficultyLabel` (`From(double winRate)`) | `main.ts` eşikleri | B-25 |
| `sealed class LevelSpec` | `LevelSpec` | JSON'dan gelir |

### 3.4 Simulation — kurala etki eden zamanlama (`CubeHaul.Simulation`)

```mermaid
classDiagram
  class GameSession {
    +Game Game
    +TruckFleet Fleet
    +DispatchScheduler Scheduler
    +BoosterRules Boosters
    +int Speed
    +LevelInfo Info
    +Advance(double realDt)
    +TapColumn(int col) TapResult
    +UseSlotBooster() bool
    +CycleSpeed()
    +bool EndPanelReady
    +event Action~SessionEvent~ Events
  }
  class FieldLayout {
    +double CellSize
    +Pt FieldOrigin
    +Ring Ring
    +Pt TunnelCenter
    +Pt LaneEntry(int lane)
    +Pt CellCenter(int i)
    +double CarScale
  }
  class RoadMap {
    +Route(int cell) Pt[]
    +RouteFrom(int cell, Pt entry) RoutePlan
    +RoadDistance(Pt a, Pt b) double
  }
  class DispatchScheduler {
    -double[] slotTimer
    -double[] landingGate
    +OnCratePlaced(int slot)
    +IsReady(int slot) bool
    +OnDispatched(int slot)
    +Advance(double simDt, double realDt)
  }
  class TruckFleet {
    +IReadOnlyList~Truck~ Trucks
    +Send(Dispatch d)
    +Advance(double simDt)
    +bool IsIdle
  }
  class Truck {
    +int Id
    +int Cell
    +int Color
    +TruckState State
    +Pt Position
    +Pt[] Path
    +int Step
    +double LoadWait
  }
  class BoosterRules {
    +int MaxSlots
    +bool SlotBoostUsed
    +CanAddSlot(Game) bool
  }
  GameSession --> Game
  GameSession --> DispatchScheduler
  GameSession --> TruckFleet
  GameSession --> BoosterRules
  TruckFleet --> RoadMap
  TruckFleet --> FieldLayout
  TruckFleet --> Truck
  TruckFleet ..> Game : Collect(cell)
```

- **`SimConstants`**: `CarSpeed = 200`, `ConvoyGap = 30`, `PopInterval = 0.15`, `LoadTime = 0.42`,
  `CrateFlightTime = 1/3.8`, `EndPanelDelay = 0.4`, `RampLanes = {-34, 0, 34}`, `MaxDt = 0.05`; yerleşim sabitleri
  (`Board = {16, 64, 388, 388}`, `FieldInset = 38`, `RoadGap = 22`, `Tunnel = {210, 496, 112, 36}`). Tümü `main.ts`'den aynen.
- **`FieldLayout`**: `cellSize = min((388−76)/w, (388−76)/h)`, tarla orijini, yol halkası, şerit giriş noktaları,
  `carScale = clamp(cellSize/13, 0.5, 1)`. Birim: TS tasarım pikseli.
- **`RoadMap`**: `roads.ts`'nin bire bir portu. `Pt` = `readonly struct (double X, double Y)`, **y aşağı** (TS ile aynı).
  Epsilon değerleri (`0.5`) tasarım pikselinde kalır (R-10).
- **`DispatchScheduler`**: slot zamanlayıcıları (simülasyon zamanı) ve kasa iniş kapısı (gerçek zaman, B-32). `Game.Tick`'e
  verilen `ready` önbelleğe alınmış tek bir delege olur.
- **`TruckFleet`**: `sendTruck` (3 şerit içinde en kısa rota), şerit kuyrukları ve aralıkları, `drive` (bir karede birden
  fazla ara noktayı geçebilir), varışta `game.Collect(cell)` (B-35), yükleme bekleme, dönüş yolu (`route` ters + rampa).
  Kamyon yönü (`angle`) ve şerit kaydırması **burada değil**, görünümde hesaplanır (kurala etkisi yok).
- **`GameSession.Advance(realDt)`**: `realDt` `MaxDt` ile kırpılır; içeride **sabit adımlı** döngü (öneri 1/60 s,
  akümülatör). Her adımda sırasıyla: zamanlayıcılar → `Game.Tick(ready)` → filo → bitiş paneli sayacı. Bu sıra
  `main.ts › update()` ile aynıdır.
- **Olaylar** (`SessionEvent`, görünüm bunları dinler): `CratePlaced(col, slot, crateId, count)`,
  `TapRejected(col)` (tepsi sallanır), `TruckQueued(truckId, lane)`, `TruckLaunched(truckId)`,
  `CubeCollected(cell, color)`, `TruckLoaded(truckId)`, `TruckHome(truckId)`, `CrateEmptied(slot, color)`,
  `SlotAdded(slotCount)`, `StatusChanged(status)`, `SpeedChanged(speed)`, `LevelStarted(info)`.

### 3.5 App ve View — Unity katmanı

| Sınıf | Tür | Görev |
|---|---|---|
| `GameBootstrap` | MonoBehaviour | Servisleri kurar, ilk bölümü yükler (geliştirme için komut satırı/Inspector'dan bölüm seçimi, `?level=` karşılığı) |
| `LevelRepository` | C# servis | Katalog + seviye `TextAsset`'leri → `LevelDefinitionReader.Read` → `LevelDefinition` → `Core.Level` |
| `LevelLibrary` | ScriptableObject | Katalog ve seviye `TextAsset` referansları (Android'de `StreamingAssets` okuma karmaşasından kaçınmak için) |
| ~~`LevelBuildService`~~ | — | *v1 sonrası:* özel resim için `Task.Run(LevelGenerator.Build)`, iptal, ana iş parçacığına aktarma |
| ~~`ImagePickerService`~~ | — | *v1 sonrası:* galeriden resim, `Texture2D` → GPU ile küçültme → `byte[] rgba` |
| `GameController` | MonoBehaviour | `Update()` → `session.Advance(Time.unscaledDeltaTime)`; olayları görünümlere dağıtır |
| `WorldMapper` | C# | Tasarım pikseli (x, y-aşağı) → dünya `(x·s, 0, −y·s)`; tek yerde, test edilebilir |
| `BoardView` | MonoBehaviour | Küpleri renk başına **GPU instancing** ile çizer (`Graphics.RenderMeshInstanced` ya da BRG); alınan küp yerine "soket" |
| `RoadView`, `WallView`, `RampView` | MonoBehaviour | Halkaya göre yol mesh'i, kapalı kenarlarda tuğla duvar (bottom: üst+yanlar; sides: yalnızca üst), rampa |
| `TruckView` + `TruckPool` | MonoBehaviour | Nesne havuzu; pozisyon simülasyondan, yön yumuşatma/şerit kaydırma/yük animasyonu burada |
| `TrayView`, `CrateView`, `SlotView`, `CrateFlight` | MonoBehaviour | Tepsi kolonları (ön + 2 arka kademe, solma), slotlar, ilerleme çubuğu, "bloke" çizgileri (B-14), uçuş/iniş/ayrılma |
| `HudView`, `BottomBarView`, `EndPanelView` | uGUI veya UI Toolkit | Üst çubuk (yeniden başlat, önceki/sonraki, başlık, zorluk çipi, hız), "+1 Slot" (v1'de alt çubuktaki tek düğme; "Your image" yeri boş kalmaz, yerleşim yeniden düzenlenir), sonuç paneli |
| `InputRouter` | MonoBehaviour | Input System; UI üstünde mi kontrolü, 3D kasa seçimi (fizik raycast ya da ekran dikdörtgeni) |
| `CameraRig` | MonoBehaviour | **Ortografik** kamera; `orthographicSize` tarlayı + yolu + rampayı her en-boy oranında sığdıracak şekilde hesaplanır, `Screen.safeArea` dikkate alınır |
| `FxService`, `HapticsService`, `AudioService` | C#/MonoBehaviour | Parçacıklar (havuzlu), titreşim (iOS `UIImpactFeedbackGenerator`, Android kısa titreşim), ses (ileride) |

**Kamera (karar: ortografik).** Projeksiyon ortografik olduğundan tüm küpler ekranda aynı boyda görünür; uzak satırlar
küçülmez. Kamera rampa tarafından, eğik bir açıyla (başlangıç önerisi X ekseninde ~55°, gri kutu aşamasında
ayarlanır) bakar; 90° (tam tepeden) de aynı kodla desteklenir. Derinlik hissi perspektiften değil, küplerin yan
yüzleri, yumuşak gölge ve ambient occlusion ile verilir. Ortografik kameranın yan faydaları: ekrandan dünyaya
dönüşüm doğrusaldır (dokunma testleri ve UI hizalaması basit), `orthographicSize` tek bir sayıyla bölüm boyutuna
göre ayarlanır, kare-başı kamera hareketi gerekmez. Eğik açıda tarla dikey olarak `cos(açı)` kadar kısalır; bu
`CameraRig`'in sığdırma hesabına dahil edilir.

**Koordinat eşlemesi.** Izgara satırı `y = 0` en üstte, `y = height−1` rampaya en yakın satır. 3D'de kamera rampa
tarafından bakar; tasarım düzlemi XZ düzlemine `X = x·s`, `Z = −y·s` ile yatırılır. Böylece alt satır kameraya en
yakın olur ve resim aynalanmaz. Bu dönüşüm yalnızca `WorldMapper`'da yapılır (R-09).

**Akış (bir dokunuş):**

```mermaid
sequenceDiagram
  participant P as Oyuncu
  participant IR as InputRouter
  participant GS as GameSession
  participant G as Game
  participant TF as TruckFleet
  participant V as Görünümler
  P->>IR: Kolon 2'ye dokunur
  IR->>GS: TapColumn(2)
  GS->>G: Tap(2) → slot 0
  GS-->>V: CratePlaced(2, 0, id, count)
  Note over GS: iniş kapısı 1/3.8 s
  loop sabit adım
    GS->>G: Tick(ready)
    G-->>GS: Dispatch(slot 0, cell, color)
    GS->>TF: Send(dispatch)
    GS-->>V: TruckQueued / TruckLaunched
    TF->>TF: rota boyunca ilerle
    TF->>G: Collect(cell) (varışta)
    GS-->>V: CubeCollected(cell)
  end
  GS-->>V: StatusChanged(Won)
  Note over GS: kamyonlar dönünce + 0.4 s
  GS-->>V: EndPanelReady
```

### 3.6 İş parçacıkları ve performans ilkeleri

- Saf katmanlar Unity API'si çağırmaz, bu yüzden `LevelGenerator.Build` arka planda güvenle çalışır.
  Sonuç `SynchronizationContext`/ana iş parçacığı kuyruğu ile sahneye döner.
- Kare başına tahsis yok: `Tick` çıktı listesi, `OpensUp` tamponu, `Neighbors` için `Span<int>` yeniden kullanılır.
  `SlotBlocked` (her karede her slot için çağrılıyor) `OpensUp` sonucunu yalnızca tahta değiştiğinde yeniden hesaplar
  (sürüm sayacı ile önbellek).
- Küp çizimi tek materyal + instancing; 2116 küp ve ~100 kamyonla hedef: orta seviye Android'de 60 fps.

### 3.7 TypeScript → C# dosya/sınıf eşleme tablosu

C# yolları `Assets/CubeHaul/Scripts/` altına görelidir. "—" = taşınmaz.

**Kurallar (`src/core`) → `CubeHaul.Core`**

| TS dosyası | TS sembolü | C# dosyası | C# türü / üyesi | Not |
|---|---|---|---|---|
| `core/types.ts` | `ColorId` | — | `int` | |
| | `OpenEdges` | `Core/OpenEdges.cs` | `enum OpenEdges { All, Sides, Bottom }` | JSON'da metin |
| | `EMPTY` | `Core/Cells.cs` | `Cells.Empty = -1` | |
| | `Picture` | `Core/Picture.cs` | `sealed class Picture` | `short[] Cells`, `string[] PaletteHex` |
| | `Block` | `Core/Crate.cs` | `sealed class Crate` | `Count` değişken |
| | `Level`, `cloneLevel` | `Core/Level.cs` | `sealed class Level`, `Level.Clone()` | |
| `core/board.ts` | `Side` | `Core/OpenEdges.cs` | `enum Side` | |
| | `Board` | `Core/Board.cs` | `sealed class Board` | Tüm genel ve özel üyeler aynı adla (§3.2); `better` → `private static bool Better(...)` |
| `core/game.ts` | `Status` | `Core/GameStatus.cs` | `enum GameStatus` | |
| | `Dispatch` | `Core/Dispatch.cs` | `readonly struct Dispatch` | |
| | `Game` | `Core/Game.cs` | `sealed class Game` | `Tick` çıktı listesi alır |

**Üretim (`src/levels`) → `CubeHaul.Levels`, `CubeHaul.Data`, araçlar**

| TS dosyası | TS sembolü | C# dosyası | C# türü / üyesi | Not |
|---|---|---|---|---|
| `levels/rng.ts` | `mulberry32` | `Levels/Mulberry32.cs` | `sealed class Mulberry32 { double Next(); }` | `uint`, `unchecked` |
| (JS çalışma zamanı) | `Array.sort`, `Math.round`, `Math.imul` | `Levels/StableSort.cs`, `Levels/JsMath.cs` | `StableSort.Sort`, `JsMath.Round`, `JsMath.Imul` | R-01, R-02, R-03 |
| `levels/generator.ts` | `GenOptions`, `BuildOptions`, `CrateSizes` | `Levels/GenOptions.cs` | `struct GenOptions`, `struct BuildOptions`, `struct CrateSizes` | |
| | `peelLayers` | `Levels/PeelLayers.cs` | `PeelLayers.Compute` | |
| | `shuffled`, `splitCount`, `generateLevel`, `buildLevel` | `Levels/LevelGenerator.cs` | `Shuffled`, `SplitCount`, `Generate`, `Build` | |
| | `BuiltLevel` | `Levels/BuiltLevel.cs` | `readonly struct BuiltLevel` | |
| `levels/solver.ts` | `SolveResult`, `solveGreedy`, `chooseTap` | `Levels/GreedySolver.cs` | `GreedySolver.Solve`, `ChooseTap` | |
| | `randomWinRate` | `Levels/RandomPlayer.cs` | `RandomPlayer.WinRate` | |
| `levels/image.ts` | `quantize` | `Levels/ImageQuantizer.cs` | `ImageQuantizer.Quantize` | v1'de oyunda kullanılmaz |
| | `pictureFromFile` | — | — | v1'de yok |
| `levels/art.ts` | `Sketch`, 10 resim fonksiyonu | — → `Tools/ts-reference` (`npm run export:golden`) | — | Çıktı: `Content/Levels/*.level.json` (R-04, Görev 04) |
| | `LevelSpec` | `Levels/LevelSpec.cs` | `sealed class LevelSpec` | JSON'dan |
| | `builtInLevels` | `App/LevelRepository.cs` | `LevelRepository.LoadBuiltIn()` | JSON → `LevelSpec` + `Level` |
| (yeni) | JSON formatı | `Data/LevelDefinition.cs`, `Data/LevelDefinitionReader.cs`, `Data/LevelValidator.cs`, `Data/LevelIssue.cs`, `Data/Json/JsonParser.cs` | Model, okuyucu, doğrulayıcı, JSON ayrıştırıcı (**yazıldı**, Görev 03) | [LEVEL_FORMAT](LEVEL_FORMAT.md) |
| `levels/worker.ts` | `BuildRequest`, `BuiltMessage`, `onmessage` | — | — | v1'de çalışma zamanı üretimi yok |
| `main.ts` | zorluk eşikleri | `Levels/DifficultyLabel.cs` | `DifficultyLabel.From(double)` | B-25 |

**Rota ve zamanlama (`src/view/roads.ts`, `main.ts`'nin kural kısmı) → `CubeHaul.Simulation`**

| TS kaynağı | TS sembolü | C# dosyası | C# türü / üyesi |
|---|---|---|---|
| `view/roads.ts` | `Pt`, `Ring` | `Simulation/Pt.cs`, `Simulation/Ring.cs` | `readonly struct Pt`, `readonly struct Ring` |
| | `RoadMap` (`center`, `beside`, `roadPoint`, `along`, `roadDistance`, `roadPath`, `route`, `routeFrom`) | `Simulation/RoadMap.cs` | `sealed class RoadMap` (aynı adlar) |
| `main.ts` | `CAR_SPEED`, `CONVOY_GAP`, `POP_INTERVAL`, `LOAD_TIME`, `FLY_SPEED`, `RAMP_LANES`, `BOARD`, `FIELD_INSET`, `ROAD_GAP`, `TUNNEL`, `MAX_SLOTS` | `Simulation/SimConstants.cs` | `static class SimConstants` |
| | `cellSize`, `cellCenter`, `ring` hesabı, `carScale` | `Simulation/FieldLayout.cs` | `sealed class FieldLayout` |
| | `slotTimer`, `ready`, uçuş kapısı | `Simulation/DispatchScheduler.cs` | `sealed class DispatchScheduler` |
| | `Car` (`x, y, sx, sy, path, step, route, cube, cell, state, cargo, wait`) | `Simulation/Truck.cs` | `sealed class Truck`, `enum TruckState` |
| | `sendTruck`, `ramp`, `rampTimer`, `drive`, `pickUp` (kural kısmı) | `Simulation/TruckFleet.cs` | `sealed class TruckFleet` |
| | `addSlot`, `slotBoostUsed` | `Simulation/BoosterRules.cs` | `sealed class BoosterRules` |
| | `levelIndex`, `goToLevel`, `startLevel` | `Simulation/LevelProgression.cs`, `Simulation/GameSession.cs` | `LevelProgression`, `GameSession.Start/Restart` |
| | `update()` (kural kısmı), `speed`, `overlayDelay` | `Simulation/GameSession.cs` | `GameSession.Advance`, `Speed`, `EndPanelReady` |
| | (yeni) olaylar | `Simulation/SessionEvents.cs` | `SessionEvent` türleri (§3.4) |

**Görünüm ve uygulama (`main.ts`'nin geri kalanı, `view/draw.ts`) → `CubeHaul.App`, `CubeHaul.View`**

| TS sembolü | C# dosyası | C# türü |
|---|---|---|
| `App.constructor`, `requestAnimationFrame` döngüsü | `App/GameBootstrap.cs`, `App/GameController.cs` | `MonoBehaviour` |
| — (bölüm dosyaları) | `App/LevelLibrary.cs` | `ScriptableObject` |
| `resize`, `scale`, `dpr` | `View/CameraRig.cs` | ortografik kamera + safe area |
| tasarım px → dünya | `View/WorldMapper.cs` | `static class WorldMapper` |
| `drawBoard`, `drawCubes`, `shown`, `sprite`, `cubeSprite`, `socketSprite` | `View/BoardView.cs`, `View/CubeInstancer.cs` | instanced küpler + soketler |
| `drawBoard` (yol), `drawWalls`, `brickWall` | `View/RoadView.cs`, `View/WallView.cs` | mesh üretimi |
| `drawTunnel` | `View/RampView.cs` | |
| `car`, `carPose`, `laneShare`, `turnToward`, rampa solması | `View/TruckView.cs`, `View/TruckPool.cs` | |
| `drawLoading`, `bedPoint`, `landPuff` | `View/TruckCargoAnimator.cs` | |
| `drawSlots`, `land`, `leave`, `drawLeaving`, `holdStripes`, `pill`, `dock`, `well` | `View/SlotView.cs` | |
| `drawFlyers`, `Flyer` | `View/CrateFlight.cs` | |
| `drawTray`, `drawTrayStacks`, `trayRise`, `colX`, `shake`, `slotRect`, `trayLeft`, `columnTargetX`, `blockRect`, `shelf`, `lane`, `pod` | `View/TrayView.cs`, `View/CrateView.cs`, `View/TrayLayout.cs` | |
| `drawTopBar`, `drawSpeedButton`, `roundIconButton`, `icon`, `speedArrows`, `countLabel` | `View/HudView.cs`, `View/SpeedButton.cs` | uGUI + TextMeshPro |
| `drawBottomBar`, `actionButton`, `badge`, `iconChip` | `View/BottomBarView.cs` | |
| `drawOverlay`, `panel`, `wrap` | `View/EndPanelView.cs` | |
| `onPointer`, `buttons`, `tapColumn` (giriş kısmı) | `View/InputRouter.cs` | Input System |
| `press`, `pressOf`, `pressGlow`, `navigator.vibrate` | `View/PressFeedback.cs`, `View/HapticsService.cs` | |
| `Particle`, `particles` | `View/FxService.cs` | havuzlu `ParticleSystem` |
| `easeOutBack`, `sheen`, `atDepth`, `shade`, `luminance`, `luminanceFill` | `View/Easing.cs`, `View/ColorUtil.cs` | |
| `paintLayer`, `stamp`, `staticLayer`, `cubeLayer`, `trayLayer` | — | Canvas önbelleği; URP'de gerekmez |
| `prebuildLevels`, `buildCustom`, `onFile`, `message` | — | v1'de yok |

**Testler**

| TS testi | C# test dosyası (`Assets/Tests/EditMode/`) |
|---|---|
| `tests/core.test.ts › Board`, `sealed gaps` | `BoardTests.cs` |
| `› Board edges` | `BoardEdgeTests.cs` |
| `› Game` | `GameTests.cs` |
| `› claimed cubes` | `ClaimTests.cs` |
| `› cubes next to the road first` | `TargetingTests.cs` |
| `› levels` | `BuiltInLevelTests.cs`, `GeneratorTests.cs` |
| `› quantize` | `ImageQuantizerTests.cs` |
| `tests/roads.test.ts` | `RoadMapTests.cs`, `RoadMapLevelTests.cs` |
| (yeni) §4 invariantları | `InvariantChecker.cs` (test yardımcısı), `InvariantFuzzTests.cs` |
| (yeni) golden | `GoldenRngTests.cs`, `GoldenGeneratorTests.cs`, `GoldenReplayTests.cs`, `GoldenRouteTests.cs` |

### 3.8 C# bağımlılık diyagramı

Oklar "bağlıdır" anlamındadır. Saf derlemeler (üst sıra) `UnityEngine`'e bağlanamaz (`noEngineReferences`).
`App`, `View`'ı tanımaz: görünümler `GameController`'dan `GameSession`'ı alıp olaylarına abone olur.

```mermaid
flowchart BT
  Core["CubeHaul.Core<br/>Board · Game · Level · Picture · Crate"]
  Levels["CubeHaul.Levels<br/>Mulberry32 · LevelGenerator · GreedySolver · RandomPlayer · ImageQuantizer"]
  Data["CubeHaul.Data<br/>LevelDefinition · LevelDefinitionReader · LevelValidator"]
  Sim["CubeHaul.Simulation<br/>FieldLayout · RoadMap · DispatchScheduler · TruckFleet · GameSession"]
  App["CubeHaul.App<br/>GameBootstrap · LevelRepository · LevelLibrary · GameController"]
  View["CubeHaul.View<br/>BoardView · TruckView · TrayView · SlotView · HudView · CameraRig · InputRouter"]
  Unity[["UnityEngine · URP · Input System · TextMeshPro"]]
  TE["CubeHaul.Tests.EditMode"]
  TP["CubeHaul.Tests.PlayMode"]
  Levels --> Core
  Data --> Core
  Sim --> Core
  Sim --> Levels
  App --> Data
  App --> Levels
  App --> Sim
  App --> Unity
  View --> Sim
  View --> App
  View --> Unity
  TE --> Sim
  TE --> Data
  TE --> App
  TP --> View
```

TS ile karşılaştırma: TS'de `main.ts` her şeye bağlı tek bir düğümdü; C#'ta bu düğüm `Simulation` (kural etkili kısım),
`App` (yaşam döngüsü) ve `View` (çizim) olarak üçe bölünür. `core → levels` yönü (TS'de `generator → board`, `solver → game`)
aynen `Levels → Core` olarak kalır.

---

## 4. Korunması gereken invariantlar

Aşağıdaki koşullar oyunun **her** durumunda doğru olmalıdır. C# portunda bunları denetleyen bir test yardımcısı
(`InvariantChecker`) yazılacak ve hem birim testlerinde hem rastgele işlem taramalarında her adımdan sonra çağrılacak.

**Doğrulama sütunu:** "✔ tarama" = kaynak TS koduna karşı ampirik olarak doğrulandı: 10 yerleşik bölüm × 6 oyun,
rastgele `tap` / rastgele `ready` maskeli `tick` / rastgele sırada gecikmeli `collect` / tıkanınca `addSlot`;
yaklaşık **1 017 000 işlem**, her işlemden sonra tüm koşullar kontrol edildi, **0 ihlal**. "Test" = mevcut TS testi
bunu kapsıyor. "Kod" = kaynak kod okunarak çıkarıldı, C#'ta yeni test yazılacak.

| # | Invariant | Kaynak | Doğrulama |
|---|---|---|---|
| I-01 | **Renk korunumu:** her renk için Σ(tepsi + slot kasa sayıları) + (claim edilmiş, alınmamış küpler) = tahtada kalan küpler. Başlangıçta tepsi toplamı resimdeki küp sayısına eşittir. | `Game.tick`, `Board.claim/eat`, `generateLevel` | ✔ tarama, test |
| I-02 | **Sayaçlar:** `remaining` = boş olmayan hücre sayısı; `remainingOf(c)` = `c` renkli hücre sayısı; `pending` = claimed bayrağı sayısı. | `Board` | ✔ tarama |
| I-03 | Claimed hücre boş değildir ve hiçbir `reachable` kümesinde değildir. | `Board.claim`, `flood` | ✔ tarama |
| I-04 | **Açık hücreler:** `open[i] = 1` ⇔ hücre boş **ve** boş hücreler üzerinden açık bir kenara bağlı. Kapalı boşluklar ancak bu bağlantı oluşunca açılır. | `Board.flood` | ✔ tarama, test |
| I-05 | **Monotonluk:** hücreler yalnızca renk → `EMPTY` değişir; `open` yalnızca 0 → 1; `depth`/`road` yalnızca azalır. Hiçbir işlem bir küpü geri koymaz ya da bir yolu kapatmaz. | `Board` | Kod (I-04 taramasıyla dolaylı) |
| I-06 | **Ulaşılabilirlik kesinliği:** `reachable[c]` = { `c` renkli, claimed olmayan, açık kenarda duran ya da açık komşusu olan küpler } — ne eksik ne fazla. | `Board.flood`, kurucu | ✔ tarama |
| I-07 | **Hedef seçimi:** `pickTarget(c) = −1` ⇔ `reachable[c]` boş; değilse sonuç `reachable[c]`'nin elemanıdır ve (satır, sürüş, indeks) sırasında en küçüğüdür; sonuç kümenin gezinme sırasından bağımsızdır. | `Board.pickTarget` | ✔ tarama (üyelik), test (sıra) |
| I-08 | **İki ölçüt:** `wayIn` giriş yolunu (derinlik, yol) **sözlük sırasıyla** seçer; `pickTarget` küpleri **toplam** `derinlik + yol` ile karşılaştırır. İkisi birleştirilmemeli ya da "düzeltilmemeli". | `Board.wayIn`, `pickTarget` | Kod, test (TargetingTests) |
| I-09 | Her açık hücrenin `depth`/`road` değeri, açık kenarlardan açık hücreler üzerinden sıfırdan hesaplanan en iyi (derinlik, yol) çiftine eşittir (artımlı güncelleme = tam hesaplama). | `Board.updateDepths` | Kod → yeni test |
| I-10 | `claim` yalnızca ulaşılabilir küpte, `eat` yalnızca claimed ya da ulaşılabilir küpte geçerlidir; aksi hâlde istisna. | `Board.claim/eat` | Test |
| I-11 | **Slotlar:** dolu slotta `count ≥ 1`; `count` 0'a indiği `tick`'te slot boşalır; slot sayısı yalnızca `addSlot` ile artar, hiç azalmaz. | `Game.tick/addSlot` | ✔ tarama |
| I-12 | **Tepsi:** yalnızca kolonun öndeki kasası alınır ve **en küçük indeksli** boş slota gider; diğer kasaların sırası değişmez; kasa `id`'leri benzersizdir ve oyun boyunca korunur. | `Game.tap` | ✔ tarama |
| I-13 | **Tick:** slot başına en fazla bir dispatch; dispatch'ler artan slot sırasında; `ready(s) = false` olan slot atlanır; aynı tick'teki hücreler birbirinden farklıdır. | `Game.tick` | ✔ tarama, test |
| I-14 | **Durum:** `won` ⇔ `remaining = 0`; `won` son durumdur; `stuck` ⇒ boş slot yok ∧ `pending = 0` ∧ hiçbir slot yiyemiyor; `stuck → playing` yalnızca `addSlot` ile; `playing` dışında `tap`/`tick` etkisizdir. | `Game.updateStatus/isStuck` | ✔ tarama, test |
| I-15 | Yolda kamyon varken (`pending > 0`) oyun tıkanmış sayılmaz. | `Game.isStuck` | ✔ tarama, test |
| I-16 | `slotBlocked(s)` ⇔ slot dolu ∧ rengin ulaşılabilir küpü yok ∧ `opensUp = false`; `pending = 0` ise `opensUp = false`. | `Game.slotBlocked`, `Board.opensUp` | Test |
| I-17 | Her dispatch **tam bir kez** `collect` edilir; küp `collect` anına kadar tahtada görünür kalır, `collect` anında kalkar. | `App.pickUp` | Kod → G-08, G-11 |
| I-18 | **Kare içi sıra:** zamanlayıcılar → `tick` → rampa → sürüş/`collect`. Bir adımda açılan küp en erken bir sonraki adımın `tick`'inde claim edilir. | `App.update` | Kod → G-11 |
| I-19 | **Zaman tabanları:** slot aralığı, rampa aralığı, sürüş ve yükleme simülasyon zamanında (hızla çarpılır); kasa uçuşu, UI animasyonları, parçacıklar ve panel gecikmesi gerçek zamanda. | `App.update` | Kod → yeni test |
| I-20 | **Determinizm:** aynı (resim, seçenekler) → aynı tepsi; aynı işlem dizisi → aynı dispatch dizisi. Kurallarda tek rastgelelik kaynağı `mulberry32`; `Math.random` yalnızca efektlerde. | `generator.ts`, `solver.ts` | ✔ tarama, test |
| I-21 | `mulberry32` çıktısı `[0, 1)` aralığında ve `2⁻³²`'nin katıdır; RNG çağrı sırası (bölüm 1.5) değişmez. | `rng.ts` | ✔ tarama (aralık, 10⁶ değer); kat ve sıra: kod |
| I-22 | **Tepsi biçimi:** kolon sayısı = `columns`; kolon uzunlukları en fazla 1 farklı; her kasada `count ≥ 1`. | `generateLevel` | ✔ tarama |
| I-23 | `buildLevel`'in seçtiği her yerleşik bölüm açgözlü çözücüyle güçlendirme olmadan kazanılır. | `buildLevel` | Test |
| I-24 | **Rotalar:** ilk nokta şerit girişi; tarla içindeki her ara nokta açık hücre; son nokta küp merkezinden tam bir hücre uzaklıkta. | `RoadMap.routeFrom` | Test |
| I-25 | **Güçlendirme:** "+1 Slot" bölüm başına bir kez, toplam ≤ 6 slot, kazanılmış oyunda kullanılamaz; bölüm (yeniden) başlayınca sıfırlanır. | `App.addSlot`, `startLevel` | Kod → yeni test |

---

## 5. Test planı

Unity Test Framework (NUnit). Saf katmanlar **EditMode**, sahne ve akış **PlayMode** testleriyle sınanır. CI'da
Unity batch mode (`-runTests -testPlatform EditMode/PlayMode`) çalıştırılır.

### 5.1 Mevcut 43 testin birebir taşınması (EditMode)

| TS testi | C# test sınıfı | Davranış |
|---|---|---|
| Board: only exposes cells on the edge or next to eaten cells | `BoardTests` | B-03, B-04 |
| Board: refuses to eat an unreachable cell | `BoardTests` | B-06 |
| Game: wins when the picture is eaten | `GameTests` | B-13 |
| Game: is stuck when every slot holds a color that cannot eat (+ addSlot) | `GameTests` | B-13, B-15 |
| levels: ×10 truck counts match the picture and greedy play wins | `BuiltInLevelTests` (TestCaseSource) | B-24, B-26 |
| levels: later levels are harder for a careless player | `BuiltInLevelTests` | B-24 |
| levels: generation is deterministic for a seed | `GeneratorTests` | B-20 |
| quantize: reduces colors and keeps transparency | `ImageQuantizerTests` | B-39 |
| Board edges: bottom/sides/all reachable counts 3/7/8 | `BoardEdgeTests` | B-02 |
| sealed gaps: transparent gap opens only once reachable | `BoardTests` | B-03 |
| claimed cubes ×3 (block until pickup; never offered twice; not stuck while pending) | `ClaimTests` | B-06, B-07, B-13 |
| cubes next to the road first ×8 (road-side first, walls, shortest drive, rows near ramp first, not blocked while truck out, hold slot back, middle-out, 15 trucks) | `TargetingTests` | B-04, B-05, B-08, B-11, B-14 |
| RoadMap ×3 (park below bottom cube, through cleared cells only, around the road) | `RoadMapTests` | B-34 |
| RoadMap ×10 (every route avoids cubes and ends beside its cube) | `RoadMapLevelTests` | B-34, B-35 |

Kural: test adları ve beklenen değerler TS ile aynı kalır; bir C# testi TS testinden farklı bir değer bekliyorsa bu
bir **hata** sayılır, testin "düzeltilmesi" değil.

### 5.2 Diferansiyel (golden) testler — TS ↔ C# eşdeğerliği

`Tools/ts-reference` (`npm run export:golden`), kaynak TS kodunu pinli commit'ten içe aktarıp `Assets/Tests/Fixtures/Golden/` altına JSON yazar.
Görev 04'te üretilenler ve biçimleri: [GOLDEN_FIXTURES.md](GOLDEN_FIXTURES.md). Aşağıdaki tablo ilk plandır; durum sütunu güncel hâlidir.
C# testleri aynı girdileri çalıştırıp çıktıyı **tam eşitlikle** karşılaştırır.

| Kimlik | Fixture | Karşılaştırılan | Durum (Görev 04) |
|---|---|---|---|
| G-01 | `rng.json` | Tohum 0, 1, 11, 1234, 2³¹−1 için ilk 1000 değer (`double` bit eşitliği) | ✔ `rng.json` (6 tohum × 256 değer, uint32) |
| G-02 | `pictures.json` | 10 resmin hücreleri ve paleti = JSON bölüm dosyalarındaki `rows` | ✔ Bölüm dosyalarında (`cells`, `palette`) |
| G-03 | `peel_layers.json` | Her resim × her kenar türü için `peelLayers` dizisi | ✔ `peel-layers.json` (her bölüm kendi kenar türüyle) |
| G-04 | `split_count.json` | Rastgele (n, boyutlar, tohum) üçlüleri için `splitCount` | — Henüz yok (C# üretici portunda eklenecek) |
| G-05 | `generate.json` | Her resim × tohum {1, 5, 11, 1234} × shuffle {0, 0.2, 0.5, 1.5, 2} için `generateLevel` kolonları (shuffle 0 kararlı sıralamayı sınar) | ✔ `generate.json` (tohum {1, 77} × shuffle {0, 1.5}, özet + öndeki kasalar) |
| G-06 | `build.json` | 10 yerleşik bölüm için `buildLevel` → kolonlar + `winRate` (= §1.3 tablosu) | ✔ `build.json` + bölüm dosyalarındaki `tray` |
| G-07 | `solver.json` | Her yerleşik bölüm için `solveGreedy` (solved, ticks, taps) ve `randomWinRate` | ✔ `build.json` (aday başına çözücü ve 24 oyunun tek tek sonucu) |
| G-08 | `replays/*.json` | Hamle izleri: `tap/tick(ready maskesi)/collect/addSlot` dizisi ve her adımdan sonra dispatch'ler, `status`, `pending`, `remaining`, her slot için `SlotBlocked` | ✔ `scenarios/*.json` (10 senaryo, her adımda tam durum) |
| G-09 | `routes.json` | Örnek dispatch'ler için `RoadMap.RouteFrom` ara noktaları ve maliyet (tolerans 1e-9) | — Henüz yok |
| G-10 | `quantize.json` | Sabit RGBA girdileri (düz renk, gradyan, saydam) için `quantize` sonucu | — Henüz yok |

G-08 izleri, TS tarafında tohumlu rastgele bir oyuncu ile her bölümde birkaç yüz adım oynatılarak üretilir
(gecikmeli `collect` dahil); böylece `tick`/`collect` ayrımı ve `opensUp` gibi nadir durumlar da kapsanır.

### 5.3 Yeni birim testleri (EditMode)

- **Invariant taraması:** §4'teki koşulları denetleyen `InvariantChecker`; her yerleşik bölümde tohumlu rastgele
  `tap`/`tick(ready maskesi)`/gecikmeli `collect`/`addSlot` dizileri, her işlemden sonra kontrol (TS'de yapılan
  1 milyon işlemlik taramanın C# eşi; CI'da daha kısa sürümü).
- **Artımlı = tam hesaplama:** her `eat` sonrası `depth`/`road` dizileri sıfırdan BFS ile hesaplananla aynı (I-09).
- **Oturum kuralları:** "+1 Slot" bölüm başına bir kez, en fazla 6, kazanınca kapalı (B-30); hız döngüsü (B-31);
  bölüm gezintisi döngüsel (B-37); dolu slotta dokunuş reddedilir ve olay yayılır (B-38).
- **Zamanlama:** `DispatchScheduler` — iniş kapısı hızdan bağımsız (B-32), slot aralığı 0.15 s × hız (B-33);
  `TruckFleet` — şerit seçimi en kısa rota, şerit aralığı, varış anında `Collect` (B-34, B-35); bitiş paneli gecikmesi (B-36).
- **Kare hızından bağımsızlık:** aynı hamle dizisi 30, 60 ve 120 fps `realDt` dizileriyle oynatılınca aynı dispatch
  dizisi ve aynı sonuç (sabit adım doğrulaması).
- **Tarayıcı simülasyonuyla eşdeğerlik:** TS `App.update` mantığını saf bir betikle (görünümsüz) çalıştırıp sabit
  `dt` ile `CubeCollected` sırasını dışa aktar; C# `GameSession` aynı sırayı üretmeli (G-11).
- **`WorldMapper` / `CameraRig`:** alt satır kameraya en yakın, x ekseni aynalanmamış; en büyük bölüm (46×46) ve en uç en-boy oranlarında (16:9 – 21:9) tarla + yol + rampa ortografik görüş alanına sığıyor.
- **Zorluk etiketi:** sınır değerleri 0.9 / 0.65 / 0.45.
- **Performans (EditMode, gösterge niteliğinde):** `LevelGenerator.Build` en büyük bölümde < 2 s (editör), `Board.PickTarget` döngüsü tahsis yapmıyor (`GC.GetAllocatedBytesForCurrentThread`).

### 5.4 JSON veri testleri (EditMode)

- Her katalog girdisinin dosyası var, `id` benzersiz, sıra §1.3 ile aynı.
- Şema doğrulaması ([LEVEL_FORMAT §5](LEVEL_FORMAT.md#5-doğrulama-kuralları); Görev 03'te TS ve C# için yazıldı). Eski taslak: `rows` sayısı = `height`, her satır uzunluğu = `width`, palet anahtarları benzersiz ve
  tek karakter, `.` palet anahtarı değil, renkler `#rrggbb`, `edges` geçerli, kolon sayısı = `generation.columns`.
- **Toplam eşitliği:** her renk için kasa sayıları toplamı = resimdeki küp sayısı (B-26); kasa `id`'leri benzersiz.
- **Çözülebilirlik:** her yerleşik bölüm `GreedySolver` ile kazanılıyor; `difficulty.winRate` yeniden hesaplanınca aynı.
- **Gidiş-dönüş:** `LevelMapper` ile JSON → `Level` → JSON aynı içerik.
- **Kültür:** tüm ayrıştırma testleri `CultureInfo("tr-TR")` altında da çalıştırılır (R-14).
- **Yeniden üretim:** C# `LevelGenerator.Build(generation)` sonucu JSON'daki `tray` ile aynı (bu test geçtiği sürece
  bölüm dosyaları C# ile de güvenle yeniden pişirilebilir).

### 5.5 PlayMode ve cihaz testleri

- **PlayMode:** sahne yüklenir; kamera ortografik; alt çubukta "Your image" düğmesi yok; bölüm 1 açılır; kolona dokunma → kasa slota uçar → kamyon çıkar; tüm küpler alınınca
  panel açılır ve "Next level" 2. bölüme geçer; tıkanma senaryosunda "+1 Slot" oyunu sürdürür; "Retry" bölümü sıfırlar;
  hız düğmesi kamyon hızını ölçülebilir şekilde katlar.
- **Görsel regresyon (opsiyonel):** sabit kamera ve sabit simülasyon zamanında ekran görüntüsü karşılaştırması.
- **Cihaz matrisi (manuel + Unity Profiler):** düşük/orta/üst segment Android (ARM64, Vulkan ve GLES3), en az iki iPhone
  sürümü; 46×46 bölümde 3x hızda kare süresi, bellek, ısınma; çentikli ekran ve farklı en-boy oranları; arka plana
  alıp geri dönme; Türkçe sistem dili.

---

## 6. Bölüm verisi için JSON formatı

> **Görev 03 ile kesinleşti:** format, şema, örnekler, doğrulama kuralları ve sürümleme
> **[`LEVEL_FORMAT.md`](LEVEL_FORMAT.md)** belgesindedir. Bu bölümdeki ilk taslağın (karakter satırları, `JsonUtility`
> uyumlu `[{ "crates": [...] }]` kolonları, `format` alanı) yerini almıştır.

Özet:

- **LevelDefinition v1:** `schemaVersion`, `levelId`, `themeId`, `name`, `picture` (`width`, `height`, düz `cells`
  dizisi — `-1` boş, aksi hâlde palet indeksi —, `palette`), `edges`, `slotCount`, statik `tray.columns`
  (`Crate[][]`), ayrı `generation` ayarları (`seed`, `shuffle`, …) ve `difficultyMetadata`.
- **Statik ve üretim ayrı:** `tray` varsa oyun onu olduğu gibi oynar; yalnızca `generation` varsa tepsi üretilir.
  En az biri zorunlu.
- **Unity'den bağımsız:** C# okuyucu (`CubeHaul.Data`) kendi JSON ayrıştırıcısıyla çalışır; `JsonUtility` ve
  Newtonsoft kullanılmaz (R-13, R-15). Kaynak Unity dışında `netstandard2.1` + C# 9 ile derlenip test edilir.
- **İki dilde aynı doğrulama:** TS (`Tools/level-format`) ve C# (`Assets/CubeHaul/Scripts/Data`) okuyucuları aynı
  kuralları aynı sırayla uygular ve aynı kodları aynı JSON Pointer yollarında bildirir; `LevelFormat/fixtures`
  altındaki ortak test verileri ikisini birlikte bağlar.
- **Katalog** (bölüm sırası, `levelId` benzersizliği) ve Unity'ye yükleme (`LevelRepository`, `LevelLibrary`) ayrı
  bir görevin konusudur.

---

## 7. Riskler ve önlemler

Olasılık/Etki: **Y**üksek, **O**rta, **D**üşük.

**Kritik riskler (olasılık ve etki yüksek):** R-01 kararsız sıralama, R-02 `Math.round` farkı, R-07 zamanlamaya
bağlı oynanış, R-14 Türkçe yerel ayar. Ayrıca sessizce davranış değiştirebilecek iki tuzak: R-27 iki farklı mesafe
ölçütü ve R-28 kare içi çağrı sırası. Bunların her biri için golden ya da invariant testi bulunur ve ilgili kod
portlanmadan önce yazılır.

| # | Risk | Ol. | Etki | Önlem |
|---|---|:-:|:-:|---|
| R-01 | **Kararlı olmayan sıralama.** JS `sort` kararlıdır (ES2019); C# `List.Sort`/`Array.Sort` değildir. `generateLevel` üç yerde eşit anahtarlı sıralamaya dayanıyor (hücreleri katmana göre, kasaları zamana göre — `shuffle = 0` geri dönüşünde eşitlik çok —, kolonları uzunluğa göre). Farklı tepsi = farklı bölüm. | Y | Y | `StableSort` yardımcı sınıfı (eklemeli sıralama ya da indeks eşitlik bozuculu karşılaştırma) veya LINQ `OrderBy`; G-05 shuffle 0 durumu |
| R-02 | **`Math.round` farkı.** JS yarımları yukarı yuvarlar (`2.5 → 3`, `−2.5 → −2`), C# `Math.Round` bankacı yuvarlaması yapar (`2.5 → 2`). Kasa boyutu formülleri (B-22) etkilenir. | Y | Y | `JsMath.Round(x) = Math.Floor(x + 0.5)`; `Math.Round` kullanımını analizörle yasaklama; G-04 |
| R-03 | **`mulberry32` 32-bit aritmetiği.** `>>>`, `Math.imul`, işaretli/işaretsiz karışımı. `checked` bağlamda taşma istisnası. | O | Y | Tamamen `uint` ile `unchecked`; G-01 ile 1000 değer bit eşitliği |
| R-04 | **`art.ts` kayan nokta eşikleri.** `Math.sin/sqrt/**` sonuçları platformlar arasında son bitte farklı olabilir; eşik üzerindeki bir piksel değişirse resim değişir. | O | O | `Sketch` taşınmaz; resimler JSON'a pişirilir (G-02) |
| R-05 | **`Float32Array` yol mesafeleri.** TS `road`'u float32 saklayıp double ile karşılaştırıyor. Değerler 0.5'in katı ve < 2²⁴ olduğu için kesin; yine de C#'ta farklı tür seçimi sapma riski taşır. | D | O | C#'ta `double[]` (değerler kesin olduğundan sonuç aynı); G-03/G-08 ile doğrulama |
| R-06 | **Küme gezinme sırası.** JS `Set` ekleme sırasını korur, `HashSet` garanti etmez. `PickTarget` eşitlik bozucusu sayesinde etkilenmez; `peelLayers` sırası katman sonucunu değiştirmez. Yeni kod sıraya güvenirse ince hatalar olur. | D | O | Sıraya ihtiyaç olan her yerde açık sıralama; G-03, G-08 |
| R-07 | **Zamanlamaya bağlı oynanış.** Küpler kamyon varınca açılır; 3D'de yol uzunluğu/hız değişirse aynı hamleler farklı sonuç verir, ayarlı zorluk (winRate) oyuncu deneyiminde kayar. | Y | Y | Simülasyon TS tasarım birimlerinde ve sabitleriyle (§3.4); 3D yalnızca eşler; G-11. Görsel nedenlerle değişiklik gerekirse önce simülasyonda, ölçülerek |
| R-08 | **Kare hızı bağımlılığı.** TS değişken `dt` (≤ 0.05 s) kullanıyor; sonuç cihaz fps'ine bağlı. | O | O | Sabit adımlı simülasyon + akümülatör; 30/60/120 fps testi |
| R-09 | **Koordinat dönüşümü.** Canvas y aşağı, Unity Y yukarı / Z ileri; yanlış eşleme resmi aynalar ya da rampayı ters tarafa koyar. | O | O | Tek nokta `WorldMapper`; birim testi; satır 0 = en uzak |
| R-10 | **Piksel cinsinden epsilon'lar.** `RoadMap.along` ve `roadPath` 0.5 px eşik kullanıyor; birimler dünya metresine çevrilirse rota yanlış kenarı seçer. | O | O | `RoadMap` tasarım pikselinde kalır; dünyaya yalnızca `WorldMapper` çevirir |
| R-11 | **Mobil performans/GC.** `opensUp` her çağrıda tahta boyunda dizi ayırıyor ve her karede her slot için çağrılıyor; `Tick` dizi döndürüyor; 2116 ayrı `GameObject`/materyal çizim çağrısı. | Y | O | Tampon yeniden kullanımı, sürüm sayaçlı önbellek; instancing tek materyal; nesne havuzları; Profiler bütçesi |
| R-12 | **Üretim süresi.** Masaüstünde 36–482 ms; IL2CPP + düşük segment telefonda saniyeler sürebilir; ana iş parçacığında donma. | O | O | Yerleşik bölümler pişirilmiş; özel resim `Task.Run` + "Building level…" göstergesi + iptal |
| R-13 | **`JsonUtility` kısıtları.** İç içe dizi, `null`, sözlük, metin enum desteklenmez; opsiyonel alanlar varsayılana düşer. | Y | O | **Giderildi (Görev 03):** `JsonUtility` kullanılmıyor; `CubeHaul.Data` kendi katı JSON ayrıştırıcısıyla okuyor ([LEVEL_FORMAT §4](LEVEL_FORMAT.md#4-okuma-sırası-ve-iki-dilde-aynı-davranış)) |
| R-14 | **Türkçe yerel ayar (tr-TR).** `"SIDES".ToLower()` → `"sıdes"`; `double.Parse("0.2")` ondalık virgül bekler; `ToString()` virgüllü yazar. Hedef kitlede yaygın. | Y | Y | Her yerde `ToLowerInvariant`, `StringComparison.Ordinal`, `CultureInfo.InvariantCulture`; enum'lar için `switch` ile ordinal ayrıştırma; testler tr-TR altında da |
| R-15 | **IL2CPP kod ayıklama / AOT.** Yansımaya dayalı JSON (Newtonsoft) ve jenerikler iOS'ta çalışma zamanında kırılabilir. | O | O | Bölüm verisi yansıma kullanmayan kendi ayrıştırıcımızla okunuyor (Görev 03); başka yerde Newtonsoft gerekirse `link.xml`; cihazda IL2CPP derlemesiyle test |
| R-16 | **Renk uzayı.** URP projeleri Linear renk uzayında; `#rrggbb` sRGB'dir. Instancing dizilerine (`SetVectorArray`) ham değer verilirse renkler soluk/yanlış görünür; ışık/gölge benzer renkleri birbirine yaklaştırabilir (oyun renk ayırt etmeye dayanıyor). | O | Y | `ColorUtility.TryParseHtmlString` + gerektiğinde `.linear`; unlit/az gölgeli küp materyali; renk körlüğü simülasyonuyla kontrol |
| R-17 | **3D okunabilirlik.** 46×46 tarlada küpler telefonda küçük; kamyonlar küplerin arkasında kalabilir; ortografik kamerada derinlik algısı zayıflar. | O | Y | **Ortografik kamera seçildi** (uzak satırlar küçülmez); eğik açı + gölge/AO ile derinlik; kamyonlar küplerden yüksekte okunur boyutta; küp boyu ≥ ~7 dp hedefi; gri kutu aşamasında erken kullanıcı testi |
| R-18 | **Ekran oranları ve güvenli alan.** TS sabit 420×900 tasarım; telefonlar 16:9 – 21:9, çentik/Dynamic Island. | Y | O | `CameraRig` + Canvas Scaler + `Screen.safeArea`; cihaz matrisi |
| R-19 | **Giriş.** Canvas'ta "son çizilen buton kazanır"; Unity'de UI ile 3D dokunuşlar çakışabilir, çoklu dokunuş. | O | D | Input System + EventSystem; UI üstündeyken 3D girişini yok say; kasa dokunma alanı ≥ 48 dp |
| R-20 | **"Your image" platform farkları** *(v1 dışı; özellik eklendiğinde geçerli)*. Tarayıcının `drawImage` küçültmesi Unity'de birebir yok → farklı palet/bölüm; galeri erişimi yerel eklenti ve izin metni (iOS `NSPhotoLibraryUsageDescription`) gerektirir; büyük resimlerde bellek. | Y | D | Saf `ImageQuantizer` birebir test edilir (G-10); küçültme toleranslı test; yerel eklenti seçimi ayrı iş kalemi; **ilk sürümden çıkarıldı** |
| R-21 | **Haptik.** `navigator.vibrate(8)` karşılığı yok; Android `Handheld.Vibrate` uzun ve kaba. | O | D | Küçük yerel eklenti ya da hazır paket; ayarlardan kapatılabilir |
| R-22 | **Gizli davranışların kaybı.** Kural gibi görünmeyen ama oyunu etkileyen ayrıntılar: son kasanın `min`'den küçük olabilmesi (B-22), +1 Slot kuralları (B-30), uçuşun hızdan bağımsızlığı (B-32), panelin kamyonlar dönünce açılması (B-36), dolu slotta sallanma (B-38). | O | O | §1.2 katalog + her madde için test |
| R-23 | **Kaynak projenin kayması.** TS geliştirilmeye devam ederse port eski davranışı kopyalar. | O | O | Kaynak commit `559dfd9` sabitlendi; değişiklikler golden yeniden üretimiyle fark olarak görülür |
| R-24 | **Fontlar ve lisans.** TS sistem fontlarını (SF Pro Rounded, Nunito…) kullanıyor; SF Pro Android'e gömülemez. | O | D | OFL lisanslı yuvarlak font (ör. Nunito) + TextMeshPro; Türkçe karakter kapsamı |
| R-25 | **Sürüm ve mağaza gereksinimleri.** Unity 6.3 LTS paket sürümleri, Android hedef API seviyesi (Google Play'in güncel şartı), ARM64 + IL2CPP zorunluluğu, iOS asgari sürümü. | O | O | `manifest.json`'da sabit sürümler; CI'da iki platform derlemesi; mağaza şartları yayın öncesi yeniden kontrol |
| R-27 | **İki mesafe ölçütünün "düzeltilmesi".** `wayIn` yolu (derinlik, yol) sözlük sırasıyla seçer, `pickTarget` küpleri toplamla karşılaştırır (I-08). Tutarsız görünür; portlarken birleştirmek hedef sırasını değiştirir. | O | Y | Kodda açıklama + `TargetingTests` + G-08 izleri |
| R-28 | **Kare içi çağrı sırası.** `tick` → rampa → sürüş/`collect` sırası değişirse (ör. sürüşün önce çalışması) küpler bir adım erken claim edilir, oyun akışı değişir (I-18). | O | Y | `GameSession.Advance` sırası sabit; G-11 tarayıcı simülasyonu eşdeğerlik testi |
| R-29 | **Hız düğmesi oyunu değiştirir** ([TS_BASELINE F-1](TS_BASELINE.md#f-1--hız-düğmesi-kural-çağrılarının-sırasını-değiştirir-tasarım-bulgusu-etkisi-var)). Tarayıcıda kare süresi hızla çarpılıp kasa uçuşu gerçek zamanda kaldığı için 2x/3x farklı claim sırası üretir (ölçümde ilk 3–88 kamyonda ayrışma). `main.ts` zamanlaması birebir kopyalanırsa port da cihaza ve hıza bağlı olur. | Y | O | Sabit adımlı simülasyon; 2x/3x = kare başına 2/3 sabit adım → oyun kare hızından ve cihazdan bağımsız olur (hızlar arası fark kalır: oyuncu ve uçuş gerçek zamanlı, B-32). Golden eşitlik yalnızca kurallar ve üretim için aranır; G-11, `Tools/ts-reference/tests/appModel.ts` ile kurulur |
| R-26 | **Paket kimliği.** Ad `CubeHaul3D` olarak kesinleşti; uygulama paket kimliği (`com.<şirket>.cubehaul3d`) mağazaya çıktıktan sonra değiştirilemez. | D | O | Paket kimliği ilk Unity commit'inden önce kesinleşmeli (§9) |

---

## 8. Önerilen iş sırası

Her aşama bir öncekinin testleri yeşilken başlar.

| Aşama | İçerik | Bitti sayılır |
|---|---|---|
| **0 — Bu belge** | Analiz, mimari, test planı, JSON şeması, riskler | Bu belgenin onayı |
| **1 — Dışa aktarma aracı** ✔ | `Tools/ts-reference` `export:golden`: 10 bölüm JSON'u + golden fixture'lar (TS'ye dokunmadan) — Görev 04 | JSON'lar [LEVEL_FORMAT](LEVEL_FORMAT.md) doğrulamasından geçer; `check:golden` temiz |
| **2 — Unity iskeleti** | Unity 6.3 LTS URP projesi, asmdef'ler, Test Framework, CI'da EditMode koşusu | Boş test paketi CI'da yeşil |
| **3 — Core portu** | `Board`, `Game` + §5.1 kural testleri | 17 tahta/oyun testi yeşil, G-08 yeşil |
| **4 — Levels portu** | RNG, üretici, çözücü, quantize | G-01…G-07, G-10 yeşil; §1.3 tablosu birebir |
| **5 — Data** | DTO, eşleyici, doğrulayıcı, `LevelRepository` | §5.4 yeşil (tr-TR dahil) |
| **6 — Simulation** | `FieldLayout`, `RoadMap`, `DispatchScheduler`, `TruckFleet`, `GameSession` | 13 rota testi, G-09, G-11, zamanlama testleri yeşil |
| **7 — 3D gri kutu** | Basit küp/kamyon/tepsi görünümleri, giriş, ortografik kamera ve açı ayarı, paneller | PlayMode akış testleri yeşil; cihazda oynanabilir |
| **8 — Görsel cila** | Modeller, materyaller, efektler, UI, haptik | Cihaz matrisi performans bütçesi |
| *v1 sonrası* — "Your image" | Galeri eklentisi, küçültme, arka plan üretimi, `LevelBuildService` | Android/iOS'ta uçtan uca |

---

## 9. Açık sorular

Yanıtlananlar: proje adı (CubeHaul3D), kamera (ortografik), "Your image" (v1'de yok) — bkz. [Alınan kararlar](#alınan-kararlar).

1. **Paket kimliği:** `com.<şirket>.cubehaul3d` için şirket/geliştirici öneki ne olacak?
2. **Kamera açısı:** Eğik (~55°) mı, tam tepeden mi? Öneri: gri kutu aşamasında ikisini cihazda deneyip seçmek.
3. **İlerleme kaydı:** Web sürümünde yok (B-37). Mobilde açılış bölümü/kilit sistemi istenecek mi? (İstenirse ayrı kural seti olarak eklenir, mevcut davranışın üzerine.)
4. **UI teknolojisi:** uGUI + TextMeshPro (olgun, yaygın) mi, UI Toolkit mi? Öneri: uGUI.
5. **Sabit adım hızı:** 60 Hz öneriliyor; 30 fps'e kilitlenen düşük segment cihazlarda da aynı adımla çalışır.
6. **TS projesinin geleceği:** Aşama 4'ten sonra kaynak doğruluk C#'a mı geçecek, yoksa TS bir süre "referans uygulama" olarak mı kalacak?
