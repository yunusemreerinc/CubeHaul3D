# Cube Hunt TypeScript — Referans Test Raporu (Görev 02)

| | |
|---|---|
| **Kaynak** | [yunusemreerinc/cube-hunt](https://github.com/yunusemreerinc/cube-hunt) @ `559dfd90977b1e15367bd7de25aec9fbae4b52f6` — **değiştirilmedi** |
| **Testlerin yeri** | Bu depoda [`Tools/ts-reference/`](../Tools/ts-reference) |
| **Ortam** | Node 22.23.4, Vitest 5.1.0, TypeScript 5.9.3, Linux x64 |
| **Tarih** | 15.11.2026 |
| **Sonuç** | **192 / 192 test geçti** (kaynağın 43 testi + 149 yeni test), 8 dosya, ~16 s. `tsc --noEmit` temiz. |
| **Kural değişikliği** | Yok. Kaynak koda tek satır dokunulmadı; dışarıdan içe aktarılarak test edildi. |

Bölüm, davranış (B-xx) ve invariant (I-xx) numaraları [`PORTING_PLAN.md`](PORTING_PLAN.md) ile aynıdır.

---

## 1. Kurulum ve çalıştırma

Kaynak projeyi değiştirmeden korumak için yeni testler TS deposuna eklenmedi. `Tools/ts-reference` paketi kaynağı
`source.json`'da sabitlenen commit'ten `vendor/cube-hunt/` klasörüne (git dışı) çeker ve testler oradan içe aktarır.
Betik, bu kopyada yerel bir değişiklik bulursa çalışmayı durdurur. Kaynağın kendi testleri de aynı koşuda, kendi
yerlerinden ve değiştirilmeden çalıştırılır.

```bash
cd Tools/ts-reference
```

```bash
npm ci
```

```bash
npm test
```

| Komut | Ne yapar |
|---|---|
| `npm test` | Kaynağı çeker/doğrular, 192 testin hepsini çalıştırır |
| `npm run test:source` | Yalnızca kaynağın kendi 43 testi |
| `npm run test:new` | Yalnızca yeni 149 test |
| `npm run typecheck` | `tsc --noEmit` (yeni testler + içe aktardıkları kaynak dosyalar) |

| Dosya | Görev |
|---|---|
| `source.json` | Kaynak depo ve sabit commit |
| `scripts/fetch-source.mjs` | Sabit commit'i `vendor/`'a çeker; commit ve temizlik kontrolü |
| `tests/helpers.ts` | Küçük resim/bölüm kurucuları, `violations()` invariant denetleyicisi, `freshDepths()` |
| `tests/appModel.ts` | `main.ts` zamanlamasının başsız modeli (hız testleri için, §4) |

---

## 2. Sonuçlar

### 2.1 Dosya bazında

| Dosya | Test | Geçti | Kaldı | Süre |
|---|--:|--:|--:|--:|
| `vendor/cube-hunt/tests/core.test.ts` (kaynak) | 30 | 30 | 0 | 3.3 s |
| `vendor/cube-hunt/tests/roads.test.ts` (kaynak) | 13 | 13 | 0 | 3.8 s |
| `tests/game.edge.test.ts` | 24 | 24 | 0 | < 0.1 s |
| `tests/board.edge.test.ts` | 25 | 25 | 0 | < 0.1 s |
| `tests/solver.test.ts` | 8 | 8 | 0 | 0.7 s |
| `tests/generator.test.ts` | 61 | 61 | 0 | 3.0 s |
| `tests/timing.test.ts` | 21 | 21 | 0 | 4.6 s |
| `tests/invariants.test.ts` | 10 | 10 | 0 | 10.6 s |
| **Toplam** | **192** | **192** | **0** | ~16 s (paralel) |

Kaynağın 43 testi, Görev 01'deki taban çizgisiyle aynı sonucu verdi: hepsi geçti. Kaynak kopyada koşu sonrası
`git status` temiz (0 değişiklik).

### 2.2 İstenen başlıklar ve kapsayan testler

| Başlık | Testler | Beklenen ve gözlenen sonuç |
|---|---|---|
| **Dolu slot** | `game.edge › full slots` (3) | Tüm slotlar doluyken `tap` −1 döner, durum hiç değişmez; son kamyonunu gönderen kasanın slotu aynı tick'te boşalır; sonraki dokunuş **en küçük indeksli** boş slota gider; kolondan yalnızca öndeki kasa çıkar |
| **Tıklanamayan kolon** | `game.edge › columns that cannot be tapped` (8) | Boş kolon, var olmayan indeks (−1, 2, 99, 0.5, NaN) ve oyun bitmişken dokunuş −1 döner ve durumu değiştirmez |
| **Bekleyen / claimed küp** | `board.edge › claimed (pending) cubes` (5) | `claim` küpü ulaşılabilirlikten çıkarır ama tahtada bırakır (`pending` 1, `remaining` değişmez); claimed küp bir daha seçilmez; çift claim, ulaşılamaz/boş hücre claim'i ve çift `eat` istisna atar |
| **Küp alınmadan arkadakinin açılmaması** | `board.edge › nothing opens behind a cube…` (4) | Claimed küpün arkası ve arkasındaki kapalı boşluk `collect`'e kadar kapalı kalır; `opensUp` yalnızca yolda kamyon varken ve yalnızca bir küp derinliğe bakar; oyunda ikinci tick kamyon dönene kadar boş döner, slot "bloke" sayılmaz |
| **Stuck / won** | `game.edge › won` (4), `› stuck` (4) | `won` son küp **toplandığında** (gönderildiğinde değil); kazanınca tepsi ve slotlar boş; `won` kalıcı; `stuck` için boş slot olmamalı ve yolda kamyon olmamalı; `stuck` bir `collect` ile de oluşabilir |
| **bottom / sides / all farkları** | `board.edge › bottom / sides / all edges` (15) | Köşelerin açık kenarları, başlangıçta ulaşılabilir küpler, soyma katmanları, aynı kasanın `bottom`'da tıkanıp `sides`/`all`'da oynanabilmesi, temizlenme sırası (§3.3) |
| **Hızdan bağımsız mantık** | `timing` (21) | Kurallar saat içermez: boş tick'ler hiçbir şeyi değiştirmez, her hızda oynanan oyun yalnızca çağrı sırasından birebir yeniden üretilir. **Ancak** hız düğmesi `main.ts`'de çağrı sırasını değiştirir → **Bulgu F-1** |
| **Booster sonrası devam** | `game.edge › play after the +1 Slot booster` (4) | `stuck → addSlot → playing`, yeni slot sona eklenir, bölüm kazanılabilir; tıkanmış oyunda tepside her zaman kasa vardır (+1 Slot her zaman bir hamle verir); çekirdekte slot sınırı yoktur (bir kez / en fazla 6 kuralı `main.ts`'de) |
| **Tekrarlanabilir seed** | `generator` (61), `solver › randomWinRate` (3) | RNG golden değerleri, tohumun 2³² modülü, aynı tohum = aynı tepsi (`shuffle` ve kenar türünden bağımsız), `buildLevel` ve `winRate` tekrarlanabilir, 10 yerleşik bölümün golden özeti (§3.1) |
| Ek: invariantlar | `invariants` (10), `board.edge › I-09` (1) | 10 bölüm × 2 oyun × 2 500 rastgele işlem; her işlemden sonra §4'teki koşullar ve canlılık; her 100 işlemde artımlı mesafeler = sıfırdan hesap |

---

## 3. Kayıt altına alınan beklenen sonuçlar

C# portu bu değerleri birebir üretmelidir.

### 3.1 Yerleşik bölümler (golden)

`buildLevel(spec.picture, spec)` sonucu. `sha256` = `JSON.stringify(level.columns)` özetinin ilk 16 hanesi.

| # | Ad | Boyut | Küp | Kasa | Kolonlar | winRate | Greedy tick / tap | sha256 (16) |
|--:|---|---|--:|--:|---|---|---|---|
| 1 | Strawberry | 29×29 | 527 | 45 | 9/9/9/9/9 | 24/24 | 114 / 45 | `5c52ecd9bcdaa7e8` |
| 2 | Mushroom | 36×36 | 1296 | 61 | 12/12/12/13/12 | 20/24 | 311 / 61 | `fcdd58e556b5cf46` |
| 3 | Sunset | 36×36 | 1296 | 49 | 10/10/10/9/10 | 13/24 | 329 / 49 | `c7687aa439cb8477` |
| 4 | Ringed Planet | 46×46 | 2116 | 66 | 13/13/13/14/13 | 12/24 | 492 / 66 | `1f92abe91f2a3cd1` |
| 5 | Cat | 43×43 | 1849 | 64 | 13/12/13/13/13 | 10/24 | 409 / 64 | `c956ceb7fdfef8ed` |
| 6 | Cactus | 40×43 | 1720 | 72 | 15/14/14/14/15 | 19/24 | 357 / 72 | `8f4a47b37b1b2eb5` |
| 7 | Rocket | 39×46 | 1794 | 65 | 13/13/13/13/13 | 17/24 | 567 / 65 | `6ea12d3b67acf4cd` |
| 8 | Rubber Duck | 43×43 | 1849 | 60 | 12/12/12/12/12 | 17/24 | 453 / 60 | `df6975f47b8b9d65` |
| 9 | Ice Cream | 36×46 | 1656 | 62 | 13/12/12/13/12 | 12/24 | 385 / 62 | `b3ff881236c7c61a` |
| 10 | Watermelon | 46×35 | 1610 | 62 | 12/13/13/12/12 | 8/24 | 360 / 62 | `328b3036a808916e` |

Hepsinde `slotCount = 5` ve açgözlü çözücü kazanır.

### 3.2 `mulberry32` (uint32 olarak, yani `değer × 2³²`)

| Tohum | 1 | 2 | 3 | 4 | 5 |
|--:|--:|--:|--:|--:|--:|
| 0 | 1144304738 | 1416247 | 958946056 | 627933444 | 2007157716 |
| 1 | 2693262067 | 11749833 | 2265367787 | 4213581821 | 4159151403 |
| 11 | 2197249643 | 2276102492 | 2611849345 | 2534707746 | 3654058086 |
| 1234 | 314799534 | 3021131492 | 3877737075 | 4168477787 | 175938938 |
| 2147483647 | 1842962257 | 546041740 | 1654754255 | 1702490205 | 513796057 |

Ayrıca: çıktılar `[0, 1)` aralığında ve 2⁻³²'nin tam katı (10⁵ değer); tohum 2³² modülünde alınır
(`-1 ≡ 2³²−1`, `2³²+5 ≡ 5`).

### 3.3 Kenar türleri

| Ölçüm (düz 3×3 resim) | `bottom` | `sides` | `all` |
|---|---|---|---|
| Sol alt köşenin açık kenarları | bottom | bottom, left | bottom, left |
| Sol üst köşenin açık kenarları | — | left | left, top |
| Üst ortanın açık kenarları | — | — | top |
| Başlangıçta ulaşılabilir hücreler | 6, 7, 8 | 0, 2, 3, 5, 6, 7, 8 | merkez (4) hariç hepsi |
| Soyma katmanları (indeks 0–8) | 2 2 2 / 1 1 1 / 0 0 0 | 0 1 0 / 0 1 0 / 0 0 0 | 0 0 0 / 0 1 0 / 0 0 0 |

Düz 5×3 resim, her kamyon küpünü anında alırsa temizlenme sırası:

| Kip | Sıra |
|---|---|
| `bottom`, `sides` | 12 11 13 10 14 · 7 6 8 5 9 · **2 1 3 0 4** |
| `all` | 12 11 13 10 14 · 7 6 8 5 9 · **0 4 1 3 2** |

`all`'da üst satır köşelerden içeri temizlenir: üst kenardaki her küp yoldan doğrudan girilebildiği için (0 hücre
içeride) kenar yolu kullanılır; köşeler yan yoldan (5), diğerleri üst yoldan (7, 7, 8) alınır. Bu bir hata değil,
I-08'in (giriş yolunu "en az hücre içeride" seçme) doğal sonucudur, ama 3D'de görünür bir davranış olduğu için
kaydedildi. I-08'i doğrudan ayırt eden senaryo da teste bağlandı: `sides` kipinde bir yan küp, alttaki boş hücreden
toplam 3 ile ulaşılabilirken yandan girişi (toplam 4) seçtiği için iç küpten sonra alınır.

Bir duvarın etkisi (alt satır renk 1, üstü renk 0, tek slot, renk 0 kasası): ulaşılabilir renk-0 küpü
`bottom` 0 → **tıkandı**, `sides` 4 → oynanıyor, `all` 5 → oynanıyor.

### 3.4 Çözücü

| Senaryo | `solveGreedy` sonucu |
|---|---|
| Düz 3×1, tek kasa (3), 1 slot | `{ solved: true, ticks: 3, taps: 1 }` |
| Renk 1 renk 0'ın üstünde, kolonlar `[1]`, `[0]`, 1 slot | `{ solved: true, ticks: 2, taps: 2 }` (önce yiyebilen kasa) |
| Aynısı tek kolonda `[1, 0]`, 1 slot | `{ solved: false, ticks: 1, taps: 1 }` (zorunlu dokunuş, tıkanma) |
| Düz 1×5, tek kasa (5); `maxTicks = 2` | varsayılan `{ true, 5, 1 }`; sınırlı `{ false, 2, 1 }` |

`randomWinRate(level, n, seed)` = tohumları `seed, seed+101, seed+202, …` olan `n` oyunun ortalaması (her oyun 0 ya
da 1); kazanılamayan bölümde 0, kaybedilemeyen bölümde 1. Kazanılamayan bir resimde `buildLevel` uyarı yazar
(`Could not verify level "…" as solvable`), `shuffle = 0` tepsisine döner ve `winRate = 0` bildirir.

### 3.5 Zamanlama (`main.ts` modeli)

| Ölçüm | Değer |
|---|---|
| Dokunuştan ilk kamyona | `1/3.8 s` gerçek zaman (kasa uçuşu; hızdan bağımsız) |
| Aynı slottan ardışık kamyonlar | `0.15 s / hız` gerçek zaman |
| Her hızda | gönderilen her kamyon küpünü tam bir kez toplar, tüm küpler toplanır, invariantlar her 30 karede bir sağlanır |

---

## 4. Bulgular

Yerleşik 10 bölümün kurallarını bozan bir hata **bulunmadı**. Aşağıdakiler kaynakta olduğu gibi bırakıldı; testler
mevcut davranışı "FINDING" adıyla sabitler. C# portunda her biri için bilinçli karar verilmelidir.

### F-1 — Hız düğmesi kural çağrılarının sırasını değiştirir (tasarım bulgusu, etkisi var)

`Game`/`Board` hız bilmez; ama `main.ts` bir karenin süresini hızla çarpar (3x'te 1/60 s'lik kare = 1/20 s simülasyon)
ve kasa uçuşu ile oyuncunun dokunuşları gerçek zamanda kalır. Bu yüzden 2x ve 3x, 1x'in hızlandırılmış bir tekrarı
değildir: kamyonlar farklı sırayla gönderilir. Uçuşu da simülasyon zamanına almak etkiyi kaldırmaz (kare
taneselliği yeterlidir).

Model oyuncusuyla (her karede en fazla bir dokunuş, önce yiyebilen kasa, tıkanınca +1 Slot) ölçüm, 60 fps:

| Bölüm | 1x | 2x | 3x | 1x/2x ilk fark (claim no) | 1x/3x ilk fark |
|---|---|---|---|--:|--:|
| Strawberry | kazandı, 527 küp, 33.8 s | kazandı, 527, 34.7 s sim / 17.4 s | kazandı, 527, 38.5 s sim / 12.8 s | 13 | 13 |
| Mushroom | kazandı, 1296, 93.1 s | kazandı, 1296, 102.7 / 51.3 s | kazandı, 1296, 90.6 / 30.2 s | 58 | 60 |
| Sunset | kazandı (+1 Slot), 1296, 127.8 s | kazandı (+1 Slot), 128.1 / 64.0 s | kazandı (+1 Slot), 128.9 / 43.0 s | 51 | 3 |
| Ringed Planet | kazandı, 2116, 153.4 s | kazandı, 133.6 / 66.8 s | kazandı, 133.4 / 44.5 s | 88 | 80 |
| Cat | kazandı, 1849, 112.8 s | kazandı, 121.2 / 60.6 s | kazandı, 118.8 / 39.6 s | 78 | 72 |
| Cactus | kazandı, 1720, 105.3 s | kazandı, 107.0 / 53.5 s | kazandı, 105.6 / 35.2 s | 42 | 3 |
| Rocket | **tıkandı** (+1 Slot), **1183** küp, 86.7 s | **tıkandı** (+1 Slot), **1155**, 103.7 / 51.8 s | **tıkandı** (+1 Slot), **1183**, 92.4 / 30.8 s | 74 | 64 |
| Rubber Duck | kazandı, 1849, 106.2 s | kazandı, 108.4 / 54.2 s | kazandı, 108.5 / 36.2 s | 78 | 72 |
| Ice Cream | kazandı, 1656, 113.7 s | kazandı, 117.3 / 58.6 s | kazandı, 116.3 / 38.8 s | 61 | 59 |
| Watermelon | kazandı, 1610, 131.6 s | kazandı, 130.8 / 65.4 s | kazandı, 134.2 / 44.7 s | 82 | 76 |

Sonuç: bu oyuncuyla her bölümün sonucu (kazandı/tıkandı, +1 Slot kullanımı) üç hızda aynı, ama oyunun kendisi
ilk 3–88 kamyon içinde ayrışıyor. Rocket'ta tıkanmadan önce temizlenen küp sayısı 2x'te farklı. (Rocket'ın tıkanması
bu basit oyuncuya özgüdür; açgözlü çözücü Rocket'ı kazanır.) Simülasyon süresi de hıza göre ±%13'e kadar değişiyor.

Not: `appModel.ts`, `main.ts`'nin kurala dokunan kısmının satır satır aynası olarak yazıldı (DOM'a bağlı olduğu için
`main.ts` Node'da yüklenemiyor); oyuncu davranışı modelindir. Sayılar bu modele aittir; tarayıcıda kare süresi de
değişken olduğundan aynı oyun tarayıcıda bile tekrarlanmaz.

**Port için anlamı:** "Birebir koruma" hedefi kuralların (`Game`/`Board`) ve bölüm üretiminin kendisi için
geçerlidir; `main.ts`'nin kare zamanlaması zaten tarayıcıda bile tekrarlanabilir değildir ve bire bir kopyalanması
hedeflenmemeli. Plan §3.4'teki sabit adımlı simülasyon, 2x/3x'i kare başına 2/3 sabit adımla yürütürse oyunu kare
hızından ve cihazdan bağımsız yapar: aynı (simülasyon zamanlı) dokunuş dizisi her cihazda aynı oyunu verir. Farklı
hızlar yine farklı oyunlar üretir, çünkü oyuncu ve kasa uçuşu (B-32) gerçek zamanda kalır; bu, hız düğmesinin
doğasıdır ve kabul edilebilir. Karar plana R-29 olarak eklendi.

### F-2 — Küpsüz bir bölüm `won` değil `playing` başlar (gizli hata, etkisi yok)

`Game` kurucusu `updateStatus` çağırmaz; boş resimli bir bölüm ilk `tick`'e kadar `playing` kalır, ilk tick'te
`won` olur. Yerleşik bölümlerin hiçbiri boş değil; tamamen saydam bir "Your image" resmi bunu tetikleyebilirdi
(v1'de yok). Test: `game.edge › FINDING: a level with no cubes…`.

### F-3 — `Game.collect` kamyon gönderilmemiş bir küpü de kabul eder (gevşek API, etkisi yok)

`Board.eat` "claimed **ya da** ulaşılabilir" hücre kabul eder (`step()` buna dayanır) ve `Game.collect` doğrudan onu
çağırır. Gönderilmemiş bir küp toplanırsa tepsi bunu ödemez ve renk toplamları (I-01) bozulur. Görünüm yalnızca
gönderilmiş hücreleri topladığı için oyunda olmaz. Test: `board.edge › FINDING: Game.collect also accepts…`.
C#'ta `Collect` için claimed şartı eklenip eklenmeyeceğine karar verilmeli (eklenirse `Step` kendi yolunu kullanmalı).

### F-4 — `minBlock` / `maxBlock` elle verilince sınırlar korunmaz (gizli hata, etkisi yok)

`splitCount` `lo..hi` aralığından çeker, `hi = min(max, kalan − min)`. `maxBlock` hesaplanan `bigMin`'den küçük
verilirse (Mushroom'da 41 > 30) büyük kasa aralığı ters döner ve 31–40'lık kasalar çıkar; büyük bir `minBlock`'ta
`kalan − min < min` olabilir ve `minBlock`'tan küçük kasalar çıkar (ör. `minBlock: 40` ile 13). Toplamlar yine
doğrudur. Yerleşik bölümlerin hiçbiri bu seçenekleri kullanmaz; varsayılan boyutlarla 10 resim × 200 tohum ×
`bigChance 0.5` = **86 426 kasada sınır dışı kasa yok**. Test: `generator › FINDING: minBlock / maxBlock…`.
C# portu davranışı aynen kopyalamalı (golden eşitliği için), düzeltme ayrı bir karar olarak ele alınmalı.

### Doğrulanan (hata olmayan) dikkat noktaları

- Bir rengin `min`'den küçük kasası yalnızca o rengin toplam küpü `min`'den azsa olur (ör. Cactus'ta çiçeğin 4
  küplük sarı ortası → 4'lük tek kasa). Görev 01'de "son kasa küçük olabilir" diye not edilen durum budur.
- Aynı tohumla kasaların kendisi `shuffle`'dan bağımsızdır; `shuffle` yalnızca sırayı değiştirir (RNG çağrı sayısı
  gürültü 0 olsa da aynı kalır). C# portunda gürültü çağrısı `shuffle = 0` iken atlanmamalıdır.
- Tıkanmış bir oyunda tepside her zaman kasa vardır; oynanan bir oyunda yolda kamyon yokken her zaman yiyebilen bir
  slot ya da dokunulabilir bir kasa vardır (yaklaşık 50 000 rastgele işlemde ihlal yok).
- Artımlı derinlik/yol hesabı (I-09) her durumda sıfırdan hesapla aynı.

---

## 5. Port için yapılacaklar

1. §3'teki tüm değerler C# EditMode testlerine golden olarak taşınır (PORTING_PLAN §5.2).
2. `tests/helpers.ts › violations()` → C# `InvariantChecker`; `invariants.test.ts` → `InvariantFuzzTests`.
3. F-1 için sabit adım + hızda alt adım kararı (R-29); F-2, F-3, F-4 için "aynen koru / düzelt" kararı. Öneri:
   golden eşitliği bozmamak için ilk portta aynen korunup ayrı bir değişiklikte düzeltilmesi.
