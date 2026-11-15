# LevelDefinition — Bölüm Formatı (şema v1)

CubeHaul3D bölümlerinin JSON formatı. Aynı dosyayı TypeScript araçları (dışa aktarma, test) ve Unity'deki C# oyunu
okur; ikisi de aynı kurallarla doğrular ve aynı hataları aynı yerlerde bildirir.

| | |
|---|---|
| **Şema sürümü** | `1` |
| **JSON Schema** | [`LevelFormat/level-definition.v1.schema.json`](../LevelFormat/level-definition.v1.schema.json) (draft 2020-12, yapısal) |
| **Örnekler** | [`LevelFormat/examples/`](../LevelFormat/examples) — tam, statik ve yalnızca üretimli birer bölüm |
| **Ortak test verisi** | [`LevelFormat/fixtures/`](../LevelFormat/fixtures) — 58 geçersiz + 12 geçerli dosya, beklenen sonuçlarıyla |
| **TypeScript okuyucu** | [`Tools/level-format/src/validate.ts`](../Tools/level-format/src/validate.ts) |
| **C# okuyucu** | [`Assets/_CubeHaul/Levels/Scripts/`](../Assets/_CubeHaul/Levels/Scripts) — `CubeHaul.Levels`, Unity'ye bağımlı değil |

Bu belge [`PORTING_PLAN.md`](PORTING_PLAN.md) §6'daki ilk taslağın yerini alır (karakter satırları ve
`JsonUtility` uyumlu kolonlar yerine: düz `cells` dizisi ve iki boyutlu `columns`).

---

## 1. Tasarım kararları

1. **Unity'den bağımsız.** Format hiçbir Unity türüne (`Color`, `Vector2`, `ScriptableObject`) dayanmaz. C# okuyucu
   `noEngineReferences: true` olan `CubeHaul.Levels` derlemesinde, bağımlılıksız kendi JSON ayrıştırıcısıyla çalışır;
   aynı kaynak Unity dışında `netstandard2.1` + C# 9 ile derlenip test edilir. Unity'nin `JsonUtility`'si iki
   boyutlu dizileri (`columns`) okuyamadığı ve `null`/eksik alan ayrımı yapamadığı için kullanılmaz; Newtonsoft gibi
   bir paket de gerekmez.
2. **Resim düz dizi.** `picture.cells` satır sırasıyla `width * height` tamsayıdır: `-1` boş (EMPTY), `0..n-1`
   palet indeksi. Bu, cube-hunt'taki `Picture.cells` (`Int16Array`) ile bire birdir; dönüşüm gerekmez, renk indeksleri
   korunur. Satır 0 en üst satırdır (rampadan en uzak); hücre `(x, y)` = `cells[y * width + x]`.
3. **Statik tepsi ile üretim ayarları ayrı.**
   - `tray` — oynanacak **kesin** tepsi. Varsa oyun bunu olduğu gibi kullanır, hiçbir şey üretmez.
   - `generation` — tepsiyi üretmenin tarifi (cube-hunt `BuildOptions`: tohum, karıştırma, hedef zorluk…).
   - En az biri zorunludur. İkisi birden varsa `tray` esastır; `generation` onun nasıl üretildiğini belgeler ve
     yeniden üretim/doğrulama içindir.
   - `difficultyMetadata` statik tepsinin ölçülmüş zorluğudur; bu yüzden `tray` olmadan bulunamaz.
4. **Kurallar paylaşılır, mesajlar değil.** Her sorun bir **kod** (`TRAY_TOTAL_MISMATCH` gibi) ve bir **JSON Pointer**
   yolu (`/tray/columns/1/0/color`) taşır. İki dilde kod ve yol aynıdır; insan için yazılmış mesaj farklı olabilir.
5. **Sürümlü ve katı.** Bilinmeyen `schemaVersion` reddedilir; bilinmeyen alanlar (yazım hatalarını yakalamak için)
   hatadır. Araçlara ait serbest veri yalnızca `extensions` nesnesine yazılır.

---

## 2. Alanlar

### 2.1 Kök

| Alan | Tür | Zorunlu | Açıklama |
|---|---|:-:|---|
| `$schema` | string | – | Editör desteği için şema yolu; okuyucular yok sayar |
| `schemaVersion` | tamsayı | ✔ | `1` |
| `levelId` | string | ✔ | `^[a-z0-9][a-z0-9_-]{0,63}$` — kalıcı kimlik (kayıtlar, analitik) |
| `themeId` | string | ✔ | Aynı desen — görsel tema/ortam seti; v1'de tüm bölümler `"default"` |
| `name` | string | ✔ | Görünen ad, 1–64 karakter, boşluktan ibaret olamaz |
| `picture` | nesne | ✔ | §2.2 |
| `edges` | string | ✔ | `"bottom"` \| `"sides"` \| `"all"` — kamyonların girebildiği kenarlar (B-02) |
| `slotCount` | tamsayı | ✔ | Başlangıç slot sayısı, 1–6 (+1 Slot güçlendirmesi en fazla 6'ya çıkarır) |
| `tray` | nesne | * | Statik tepsi, §2.3 |
| `generation` | nesne | * | Üretim ayarları, §2.4 |
| `difficultyMetadata` | nesne | – | Statik tepsinin zorluğu, §2.5 (yalnızca `tray` varsa) |
| `extensions` | nesne | – | Araçlara ait serbest veri; oyun okumaz |

\* `tray` ve `generation`'dan en az biri.

### 2.2 `picture`

| Alan | Tür | Açıklama |
|---|---|---|
| `width`, `height` | tamsayı | 1–64 |
| `palette` | string[] | 1–32 renk, `#rrggbb` (sRGB; büyük/küçük harf serbest, yazıcı küçük harf kullanır). İndeks = renk kimliği |
| `cells` | tamsayı[] | Tam olarak `width * height` eleman; `-1` (EMPTY) ya da `0..palette.length-1` |

### 2.3 `tray` — statik tepsi

| Alan | Tür | Açıklama |
|---|---|---|
| `columns` | `Crate[][]` | 1–10 kolon. `columns[c][0]` kolonun **önündeki** (dokunulabilir) kasa. Kolon boş olabilir ama tepsinin tamamı boş olamaz |
| `Crate.id` | tamsayı | 0–999 999, tepside benzersiz |
| `Crate.color` | tamsayı | Palet indeksi |
| `Crate.count` | tamsayı | 1–9 999 kamyon; her biri bir küp taşır |

Her renk için kasa sayılarının toplamı o rengin küp sayısına **eşit** olmalıdır (I-01, B-26).

### 2.4 `generation` — üretim ayarları

| Alan | Tür | Zorunlu | Varsayılan | Açıklama |
|---|---|:-:|---|---|
| `seed` | tamsayı | ✔ | | 0 – 2 147 483 647 (`mulberry32`) |
| `shuffle` | sayı | ✔ | | 0–100; 0 = açılma sırası, büyüdükçe karışık |
| `targetWinRate` | sayı | – | 1 | 0–1, rastgele oyuncu hedef kazanma oranı |
| `columns` | tamsayı | – | 5 | 1–10; `tray` varsa onun kolon sayısına eşit olmalı |
| `bigChance` | sayı | – | 0.15 | 0–1, büyük kasa olasılığı |
| `minBlock`, `maxBlock` | tamsayı | – | resimden türetilir | 1–9 999. Kullanılırsa uyarı: sınırlar her zaman korunmaz (TS_BASELINE F-4) |
| `candidates` | tamsayı | – | 16 | 1–1 000, `buildLevel` aday sayısı |

Slot sayısı ve kenarlar üretimde de kökteki `slotCount` ve `edges`'ten alınır; tekrar yazılmaz.

### 2.5 `difficultyMetadata`

| Alan | Tür | Zorunlu | Açıklama |
|---|---|:-:|---|
| `winRate` | sayı | ✔ | 0–1, statik tepsinin rastgele oyuncu kazanma oranı |
| `label` | string | ✔ | `Easy` \| `Medium` \| `Hard` \| `Expert`; `winRate`'ten türetilenle **aynı** olmalı (≥0.9 / ≥0.65 / ≥0.45 / altı, B-25) |
| `randomGames` | tamsayı | – | Oranın ölçüldüğü oyun sayısı (cube-hunt: 24) |
| `greedy` | `{solved, ticks, taps}` | – | Açgözlü çözücü sonucu |

---

## 3. Örnekler

İki dilin de okuduğu, eksiksiz ve geçerli en küçük bölüm ([`ring.static.level.json`](../LevelFormat/examples/ring.static.level.json)):

```json
{
  "$schema": "../level-definition.v1.schema.json",
  "schemaVersion": 1,
  "levelId": "ring",
  "themeId": "default",
  "name": "Ring",
  "picture": {
    "width": 3,
    "height": 3,
    "palette": ["#ff0000", "#0000ff"],
    "cells": [
       0, 0, 0,
       0, 1, 0,
       0, 0, 0
    ]
  },
  "edges": "all",
  "slotCount": 5,
  "tray": {
    "columns": [
      [
        { "id": 0, "color": 0, "count": 8 }
      ],
      [
        { "id": 1, "color": 1, "count": 1 }
      ]
    ]
  }
}
```

Statik tepsi, üretim ayarları ve zorluk bilgisi olan tam bir bölüm: [`strawberry.level.json`](../LevelFormat/examples/strawberry.level.json)
— cube-hunt'ın 1. bölümü, TS üreticisinin gerçek çıktısıyla (29×29, 527 küp, 45 kasa). Sonu:

```json
  "generation": {
    "seed": 11,
    "shuffle": 0.2,
    "targetWinRate": 1,
    "columns": 5,
    "bigChance": 0.15,
    "candidates": 16
  },
  "difficultyMetadata": {
    "winRate": 1,
    "label": "Easy",
    "randomGames": 24,
    "greedy": {"solved": true, "ticks": 114, "taps": 45}
  }
}
```

Yalnızca üretim ayarı olan bölüm: [`heart.generated.level.json`](../LevelFormat/examples/heart.generated.level.json)
(`tray` yok; tepsi tohumdan üretilir).

Dosyaların biçimi kanoniktir: anahtarlar şema sırasında, resim satır satır, her kasa bir satırda. Bu yalnızca
okunabilirlik ve diff içindir; okuyucular boşluk ve sıra bağımsızdır. Yazıcı:
[`Tools/level-format/src/format.ts`](../Tools/level-format/src/format.ts).

---

## 4. Okuma sırası ve iki dilde aynı davranış

`read(text)` → BOM varsa at → JSON ayrıştır → doğrula → hata yoksa model. Okuyucu hiçbir zaman istisna atmaz; her
sorun `errors` ya da `warnings` listesine girer. Model yalnızca hata yoksa döner.

İki dilin aynı sonucu vermesi için sabitlenen ayrıntılar:

| Konu | Kural |
|---|---|
| Sayılar | IEEE double (JavaScript gibi). C#'ta `InvariantCulture` ile ayrıştırılır |
| Tamsayı | Kesirsiz, sonlu sayı: `1`, `1.0`, `5e0` tamsayıdır (`Number.isInteger`) |
| Çok büyük sayı | `1e400` → sonsuz (JS ile aynı; eski Mono'nun `OverflowException`'ı yakalanır) → tamsayı değildir |
| Tekrarlanan anahtar | İlk yerinde kalır, **son** değeri alır (`JSON.parse` gibi). Kaçınılmalı |
| Sözdizimi | Yalnızca RFC 8259: yorum, sondaki virgül, tek tırnak, `NaN`/`Infinity` hata |
| BOM | Baştaki UTF-8 BOM yok sayılır |
| Düzenli ifadeler | C#'ta `\z` (JS'teki `$` ile aynı); `"ring\n"` iki dilde de geçersiz |
| Kültür | Hiçbir yerde `ToLower`/`ToUpper`/kültüre bağlı biçim yok; `"SIDES"` reddedilir, küçültülmez (R-14). tr-TR, de-DE, ar-SA altında test edildi |
| Derinlik | C# ayrıştırıcı 128 seviyeden derini reddeder (yığın taşması yerine hata) |

---

## 5. Doğrulama kuralları

### 5.1 Sıra

Kurallar her iki dilde **bu sırayla** işletilir; bir bölümün yapısı bozuksa ona dayanan anlam kuralları atlanır
(böylece tek bir hata zincirleme sahte hatalar üretmez ve iki dil aynı listeyi verir).

1. **Kök nesne** değilse `WRONG_TYPE ""` → dur.
2. **Sürüm:** `schemaVersion` yoksa `MISSING_FIELD`, tamsayı değilse `WRONG_TYPE`, `1` değilse
   `UNSUPPORTED_SCHEMA_VERSION` → dur (bilinmeyen bir sürümün geri kalanı yorumlanmaz).
3. **Kök alanlar:** bilinmeyen → `UNKNOWN_FIELD`, eksik zorunlu → `MISSING_FIELD`; sonra her alanın türü, deseni ve
   aralığı.
4. **Bölüm yapıları:** `picture`, `tray`, `generation`, `difficultyMetadata` — aynı tür/aralık kontrolleri.
5. **S1 — resim** (yalnızca `picture` yapısal olarak sağlamsa): `CELLS_LENGTH` → `CELL_COLOR` → `EMPTY_PICTURE`;
   ardından uyarılar `UNUSED_PALETTE_COLOR`, `DUPLICATE_PALETTE_COLOR`.
6. **S2 — tepsi** (yalnızca `tray` yapısal olarak sağlamsa): `TRAY_EMPTY` → `DUPLICATE_CRATE_ID` → (palet sağlamsa)
   `CRATE_COLOR` → (resim S1'i geçtiyse ve kasa renkleri geçerliyse) renk başına `TRAY_TOTAL_MISMATCH`.
7. **S3** — `tray` ve `generation` sağlamsa: `COLUMN_COUNT_MISMATCH`.
8. **S4** — `difficultyMetadata` var ama `tray` yoksa: `DIFFICULTY_WITHOUT_TRAY`. (Etiket–oran uyuşmazlığı
   `DIFFICULTY_LABEL_MISMATCH` bölüm yapısı kontrolünde verilir.)
9. **S5** — `tray` de `generation` da yoksa: `NO_TRAY_OR_GENERATION ""`.

### 5.2 Kodlar

| Kod | Tür | Yol | Ne zaman | Örnek test verisi |
|---|---|---|---|---|
| `JSON_SYNTAX` | hata | `""` | Geçerli JSON değil | `syntax-*.json` (6) |
| `WRONG_TYPE` | hata | değer | Yanlış JSON türü, `null`, tamsayı yerine kesirli/sonsuz sayı | `slot-count-fraction`, `theme-id-null`, `tray-flat-columns` |
| `MISSING_FIELD` | hata | eksik alan | Zorunlu alan yok | `missing-required-fields`, `crate-missing-count` |
| `UNKNOWN_FIELD` | hata | alan | Şemada olmayan alan | `unknown-root-field`, `crate-unknown-field` |
| `UNSUPPORTED_SCHEMA_VERSION` | hata | `/schemaVersion` | Sürüm 1 değil | `schema-version-2`, `schema-version-0` |
| `INVALID_VALUE` | hata | değer | Desen/enum dışı: kimlik, renk, `edges`, `label`, boş ad | `level-id-pattern`, `edges-uppercase`, `palette-bad-color` |
| `OUT_OF_RANGE` | hata | değer/dizi | Sayı ya da dizi uzunluğu sınır dışı | `slot-count-too-big`, `tray-no-columns`, `generation-seed-too-big` |
| `CELLS_LENGTH` | hata | `/picture/cells` | `cells.length ≠ width * height` | `cells-length` |
| `CELL_COLOR` | hata | `/picture/cells/i` | Hücre `-1` değil ve paletin dışında | `cell-color-outside-palette`, `cell-below-empty` |
| `EMPTY_PICTURE` | hata | `/picture/cells` | Hiç küp yok (TS_BASELINE F-2'yi formatta engeller) | `picture-empty` |
| `TRAY_EMPTY` | hata | `/tray/columns` | Tepside hiç kasa yok | `tray-empty` |
| `DUPLICATE_CRATE_ID` | hata | `…/id` | Aynı kasa kimliği ikinci kez | `crate-duplicate-id` |
| `CRATE_COLOR` | hata | `…/color` | Kasa rengi paletin dışında | `crate-color-outside-palette` |
| `TRAY_TOTAL_MISMATCH` | hata | `/picture/palette/k` | `k` renginin kamyon toplamı ≠ küp sayısı | `tray-total-mismatch`, `tray-color-missing` |
| `COLUMN_COUNT_MISMATCH` | hata | `/tray/columns` | Tepsi kolonu ≠ `generation.columns` (varsayılan 5) | `column-count-mismatch` |
| `NO_TRAY_OR_GENERATION` | hata | `""` | İkisi de yok | `no-tray-or-generation` |
| `DIFFICULTY_WITHOUT_TRAY` | hata | `/difficultyMetadata` | Zorluk var, statik tepsi yok | `difficulty-without-tray` |
| `DIFFICULTY_LABEL_MISMATCH` | hata | `/difficultyMetadata/label` | Etiket orandan türetilenle aynı değil | `difficulty-label-mismatch` |
| `UNUSED_PALETTE_COLOR` | uyarı | `/picture/palette/k` | Hiçbir hücrenin kullanmadığı renk | `valid/unused-palette-color` |
| `DUPLICATE_PALETTE_COLOR` | uyarı | `/picture/palette/k` | Aynı renk iki kez (ayırt edilemez kasalar) | `valid/duplicate-palette-color` |
| `BLOCK_SIZE_OVERRIDE` | uyarı | `/generation/minBlock\|maxBlock` | Elle kasa boyutu (F-4) | `valid/block-size-override` |

JSON Schema yapısal kuralların tamamını içerir (`anyOf` ile S5, `dependentRequired` ile S4); alanlar arası kuralları
(uzunluk, renk indeksi, toplamlar, etiket, kolon sayısı) yalnızca doğrulayıcılar uygular. Hangi geçersiz dosyanın
şemaya da takıldığı `fixtures/invalid/expected.json`'daki `schemaRejects` alanında yazılıdır ve test edilir.

---

## 6. Sürümleme

- `schemaVersion` tek bir tamsayıdır. Okuyucu yalnızca bildiği sürümü kabul eder; daha yeni bir dosya açık bir
  `UNSUPPORTED_SCHEMA_VERSION` hatası verir, yarım yorumlanmaz.
- **Sürüm artırmayı gerektiren** değişiklikler: alanın anlamını, türünü, birimini ya da zorunluluğunu değiştirmek;
  alan kaldırmak; yeni zorunlu alan; yeni bir `edges` değeri gibi eski okuyucunun yanlış oynayacağı her şey.
- **Sürüm artırmadan** yapılabilir: yalnızca `extensions` içine veri eklemek. Bilinmeyen alanlar hata olduğundan,
  yeni bir isteğe bağlı alan da sürüm artırır; bu bilinçli bir seçimdir (eski oyun, anlamadığı bir kuralı sessizce
  yok saymasın).
- Yeni sürümde okuyucular `vN → vN+1` göçünü (migration) kod olarak taşır ve eski dosyaları yüklerken uygular;
  depodaki dosyalar araçla toplu güncellenir. Her sürümün kendi şema dosyası olur (`level-definition.vN.schema.json`).

---

## 7. Kullanım

**TypeScript** ([`Tools/level-format`](../Tools/level-format)):

```ts
import { readLevelDefinition } from "./src/validate";
const result = readLevelDefinition(text); // { ok, level?, errors, warnings }
```

```bash
cd Tools/level-format && npm ci && npm run validate -- ../../LevelFormat/examples/*.json
```

**C#** ([`Assets/_CubeHaul/Levels/Scripts`](../Assets/_CubeHaul/Levels/Scripts), namespace `CubeHaul.Levels`):

```csharp
LevelReadResult result = LevelDefinitionReader.Read(text);
if (!result.Ok) foreach (LevelIssue e in result.Errors) Debug.LogError(e); // e.Code, e.Path, e.Message
LevelDefinition level = result.Level; // Picture.Cells, Tray?.Columns, Generation, Difficulty
```

Unity'de dosyalar `TextAsset` olarak gelir (`textAsset.text`); bölüm kataloğu ve Unity'ye yükleme sonraki görevin
konusudur. `Assets/` altında henüz Unity projesi yok; `.meta` dosyaları proje oluşturulunca Unity tarafından üretilir.

**Testler:**

```bash
cd Tools/level-format && npm test
```

```bash
cd Tools/ts-reference && npm test
```

```bash
cd Tools/dotnet/CubeHaul.Tests && dotnet test
```

| Takım | Test | İçerik |
|---|--:|---|
| `Tools/level-format` | 152 | Ortak test verileri (58 geçersiz, 12 geçerli, 3 örnek), JSON Schema uyumu (Ajv), kanonik yazıcı, etiket eşikleri |
| `Tools/ts-reference › levelFormat` | 15 | Örnekler TS kaynağının çıktısıyla aynı; 10 yerleşik bölüm JSON'a dönüşüp doğrulanıyor ve JSON'dan kurulan bölüm orijinaliyle **aynı oyunu** oynuyor; yalnızca üretimli örnekten tepsi üretilip geçerli statik bölüm oluyor |
| `Tools/dotnet/CubeHaul.Tests` (bölüm formatı kısmı) | 122 | Aynı ortak test verileri ve aynı beklentiler, JSON dilbilgisi, model alanları, kültür bağımsızlığı. Kaynak `netstandard2.1` + C# 9 ile ayrıca derlenir |

---

## 8. Sınırlar özeti

| Değer | Sınır | Neden |
|---|---|---|
| `width`, `height` | 1–64 | Yerleşik en büyük 46; mobilde okunabilirlik (R-17) |
| `palette` | 1–32 renk | Yerleşik en fazla 7, özel resim 6 |
| `slotCount` | 1–6 | `MAX_SLOTS` (B-30) |
| `tray.columns` | 1–10 | Varsayılan 5 |
| `Crate.count` | 1–9 999 | |
| `Crate.id` | 0–999 999 | |
| `generation.seed` | 0–2 147 483 647 | C# `int` ve JS'te güvenli; `mulberry32` tohumu |
| `name` | 1–64 karakter | |
