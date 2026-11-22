# Golden Fixture'lar ve Bölüm Dışa Aktarma (Görev 04)

cube-hunt TypeScript kaynağından (`559dfd9`, değiştirilmeden) üretilen ve C# portunun **birebir** tutturması gereken
referans dosyalar. Hepsi tek bir komutla yeniden üretilir; aynı kaynak her zaman bayt bayt aynı dosyaları verir.

## 1. Komutlar

```bash
cd Tools/ts-reference
```

```bash
npm ci
```

```bash
npm run export:golden
```

```bash
npm run check:golden
```

| Komut | Ne yapar |
|---|---|
| `npm run export:golden` | Kaynağı sabit commit'ten çeker, tüm dosyaları üretir. Değişmeyen dosyalara dokunmaz, artık üretilmeyen eski dosyaları siler. Tekrar tekrar çalıştırılabilir |
| `npm run check:golden` | Hiçbir şey yazmaz; eksik, farklı ya da eski bir dosya varsa listeler ve 1 ile çıkar (CI için) |

Üretim yaklaşık 7 saniye sürer. Tüm rastgelelik `mulberry32` tohumlarından gelir; saat, `Math.random` ya da makineye
bağlı hiçbir girdi kullanılmaz.

## 2. Dosyalar

| Yol | İçerik | Plan kimliği |
|---|---|---|
| `Assets/_CubeHaul/Levels/Content/level-NN-<id>.level.json` | 10 yerleşik bölüm, [LevelDefinition v1](LEVEL_FORMAT.md) olarak: resim matrisi (`cells`), palet, `buildLevel`'in seçtiği kolonlar (`tray.columns`), üretim ayarları ve zorluk | G-02, G-06 |
| `Assets/_CubeHaul/Tests/Fixtures/Golden/rng.json` | `mulberry32` — 6 tohum × ilk 256 değer, uint32 olarak | G-01 |
| `…/Golden/peel-layers.json` | Her bölümün kendi kenar türüyle `peelLayers` sonucu | G-03 |
| `…/Golden/generate.json` | Her bölüm için `generateLevel`, tohum {1, 77} × karıştırma {0, 1.5}: kasa sayısı, kolon uzunlukları, öndeki kasalar, kolon özeti | G-05 |
| `…/Golden/build.json` | `buildLevel`'in tam izi: denenen her aday (tohum, kolon özeti, açgözlü çözücü sonucu, 24 rastgele oyunun tek tek sonucu, kazanma oranı), seçilen aday | G-06, G-07 |
| `…/Golden/scenarios/*.json` | 10 senaryo: her işlemden sonra oyunun tam durumu | G-08 |
| `…/Golden/manifest.json` | Diğer tüm dosyaların boyutu ve SHA-256'sı | — |

Bölüm kimlikleri ve sıra: `strawberry`, `mushroom`, `sunset`, `ringed-planet`, `cat`, `cactus`, `rocket`,
`rubber-duck`, `ice-cream`, `watermelon` (dosya adındaki `NN` = oyundaki sıra). Bölümlerin değerleri
[TS_BASELINE §3.1](TS_BASELINE.md#31-yerleşik-bölümler-golden) ile aynıdır.

Her golden dosyası şu başlığı taşır:

```json
{
  "format": "cubehaul.golden.<tür>",
  "version": 1,
  "source": { "repo": "https://github.com/yunusemreerinc/cube-hunt", "commit": "559dfd9…" },
  "description": "…"
}
```

## 3. Senaryolar

| Senaryo | Bölüm | Kenar | İşlemler | Son durum | Neyi sabitler |
|---|---|---|---|---|---|
| `ring-step` | 3×3 halka | all | tap, step | won | En basit tam oyun |
| `claim-blocks-until-collect` | 1×3 sütun | bottom | tap, tick, collect | won | Claim edilen küp toplanana kadar arkası kapalı; yolda kamyon varken tıkanma yok |
| `stuck-addslot-won` | 1×2 iki renk | bottom | tap, tick, addSlot, step | won | Tıkanma, tıkalıyken etkisiz `tap`/`tick`, +1 Slot sonrası devam |
| `sealed-gap-opensup` | 1×4, kapalı boşluk | bottom | tap, tick, collect | won | `slotBlocked` / `opensUp` |
| `ready-mask-middle-out` | 5×2 | bottom | tap, tick(ready), collect | won | Bekletilen slotlar, satırın ortadan dışa alınması |
| `all-edges-top-row` | 3×3 | all | tap, step | won | Üst satırın dışarıdan, köşelerden alınması |
| `sides-edges-wall` | 4×3 | sides | tap, step | won | Yanlardan giriş, alt duvar |
| `strawberry-step` | 1. bölüm (29×29) | all | tap, step | won (167 adım) | Gerçek bir bölüm, anında toplama |
| `mushroom-random-delayed` | 2. bölüm (36×36) | bottom | tap, tick(ready), collect | playing (517 adım, işlem sınırı) | Rastgele sırayla toplanan kamyonlar, bekletilen slotlar |
| `cat-random-delayed` | 5. bölüm (43×43) | bottom | tap, tick(ready), collect | stuck (522 adım) | Rastgele oyunda gerçek bir bölümün tıkanması |

Hamleler dosya üretilirken basit bir oyuncu kuralıyla ya da tohumlu rastgele seçilir, ama dosyaya yalnızca
**kaydedilen hamleler** yazılır. Tekrar oynatmak için o kural gerekmez.

### 3.1 Senaryo dosyası

```json
{
  "format": "cubehaul.golden.scenario", "version": 1, "source": {…}, "description": "…",
  "name": "ring-step",
  "detail": true,
  "level": { …LevelDefinition… },
  "steps": [
    { "op": "start", "state": {…} },
    { "op": "tap", "col": 0, "result": 0, "state": {…} },
    { "op": "tick", "ready": [true, false], "result": [[0, 7, 0]], "state": {…} },
    { "op": "step", "result": [[0, 7, 0], [1, 4, 1]], "state": {…} },
    { "op": "collect", "cell": 7, "state": {…} },
    { "op": "addSlot", "state": {…} }
  ]
}
```

- `level` (satır içi) ya da `levelFile` (depo köküne göre bölüm dosyası yolu).
- `tick.ready`: slot başına hazır mı; `null` = hepsi hazır (`tick()`).
- `result`: `tap` için slot indeksi (ya da −1); `tick`/`step` için `[slot, cell, color]` dispatch listesi, sırasıyla.
- `state`: işlemden **sonraki** durum.

### 3.2 Durum (`state`)

| Alan | Anlam |
|---|---|
| `status`, `remaining`, `pending`, `trayCount` | `Game.status`, `Board.remaining`, `Board.pending`, `Game.trayCount()` |
| `columns` | Kolon başına kasa kimlikleri, önden arkaya |
| `slots` | Slot başına `[id, color, count]` ya da `null` |
| `claimed` | Claim edilmiş, henüz toplanmamış hücreler (artan) |
| `emptied` | Bir önceki durumdan bu yana boşalan hücreler (artan) |
| `reachableCount`, `pickTarget` | Renk başına ulaşılabilir küp sayısı ve `Board.pickTarget(color)` |
| `reachable` | Renk başına ulaşılabilir hücreler (artan); yalnızca `detail: true` senaryolarda |
| `reachableHash` | `fnv1a` — her rengin artan hücre listesi, ardından `-1`, renk sırasıyla |
| `slotCanEat`, `slotBlocked`, `isStuck` | `Game.slotCanEat(s)`, `Game.slotBlocked(s)`, `Game.isStuck()` |
| `boardHash` | `fnv1a` — önce her hücrenin rengi (`-1` boş), sonra her hücre için `isOpen` (0/1), sonra `isClaimed` (0/1) |

**`fnv1a`**: 32 bit FNV-1a; her tamsayı 4 bayt little-endian olarak beslenir; sonuç 8 küçük onaltılık hane.

```text
h = 2166136261
for each int v: for k in 0..3: h = (h XOR ((v >>> 8k) AND 0xFF)) * 16777619  (mod 2^32)
```

C# karşılığı `Tools/dotnet/CubeHaul.Tests/GoldenFixtureTests.cs › Fnv1a`; iki dilin aynı sonucu verdiği test edilir.

**`columnsSha256`** (generate/build): `JSON.stringify(columns)` metninin SHA-256'sı — anahtarlar `id, color, count`
sırasında, boşluksuz, örn. `[[{"id":2,"color":0,"count":10},…],…]`.

## 4. Testler

| Takım | Ne sınar |
|---|---|
| `Tools/ts-reference › tests/golden.test.ts` (28) | Diskteki dosyalar dışa aktarıcının çıktısıyla bayt bayt aynı; eski dosya yok; ikinci üretim aynı; manifest doğru; 10 bölüm geçerli ve resmi, paleti, tepsisi TS'tekiyle aynı; `rng.json` = `mulberry32`; `build.json`'da seçilen aday bölüm dosyasının tepsisi ve oyun sonuçlarının ortalaması kazanma oranı; **her senaryo yalnızca kaydedilen hamlelerden yeni bir `Game` üzerinde birebir aynı durumları veriyor** ve her adımda invariantlar sağlanıyor |
| `Tools/dotnet/CubeHaul.Tests › GoldenFixtureTests` (25) | Manifest SHA-256'ları ve boyutları tutuyor (satır sonu değişirse yakalar), 10 bölüm C# okuyucusuyla hatasız okunuyor, tüm dosyalar ayrıştırılıyor, senaryolar iyi biçimli, `fnv1a` tanımı iki dilde aynı |

`.gitattributes` JSON dosyalarını LF satır sonuyla sabitler. Windows'ta git'in satır sonlarını CRLF'e çevirmesi
SHA-256'ları bozacağı için bu gereklidir.

## 5. C# eşdeğerlik testinde kullanım (sonraki görevler)

1. `rng.json` → `Mulberry32` (uint32 eşitliği).
2. `peel-layers.json`, `generate.json`, `build.json` → `PeelLayers`, `LevelGenerator.Generate`, `LevelGenerator.Build`,
   `GreedySolver`, `RandomPlayer`. Sorun çıkarsa `build.json`'daki aday izi farkın hangi adayda başladığını gösterir.
3. `scenarios/*.json` → C# `Game` üzerinde hamleleri tekrar oynat, her adımda `state`'in her alanını karşılaştır.
   `boardHash` ve `reachableHash` aynı `fnv1a` tanımıyla hesaplanır.
   **Görev 07:** tahta düzeyindeki alanlar (`remaining`, `pending`, `claimed`, `emptied`, `reachableCount`, `pickTarget`,
   `reachable`, `reachableHash`, `boardHash`, `slotCanEat`, `slotBlocked`) ve her dispatch'in hedefi C# `Board` ile
   karşılaştırılıyor (`Assets/_CubeHaul/Tests/EditMode/BoardGoldenTests.cs`); `peel-layers.json` de öyle.
4. Bölüm dosyaları Unity'de `TextAsset` olarak oyunun gerçek içeriğidir.

Kaynak cube-hunt'ta bir kural değişirse dışa aktarıcı yeniden çalıştırılır; fark, `check:golden` ve testlerde görünür.
