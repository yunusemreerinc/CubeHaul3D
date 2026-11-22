# C# Çekirdek Kabul Testi: TypeScript/C# Eşdeğerliği (Görev 10)

10 bölümün hepsi golden fixture'lar üzerinden Unity EditMode testine bağlı. 24 eşdeğerlik (parity) senaryosu
TypeScript oyununda kaydedildi ve C# kurallarında adım adım tekrar oynatılıyor.

**Sonuç:** 24 senaryo, 8 936 adım, **fark yok**. Kritik davranış farkı kalmadı (§6). Testler tekrarlanabilir: her
senaryo ve her golden dosya sabit tohumlardan üretiliyor ve iki dilde de aynı sonuçla yeniden oynatılabiliyor (§5).

## 1. Çalıştırma

| Nerede | Nasıl | Ne görürsünüz |
|---|---|---|
| Unity | **Window ▸ General ▸ Test Runner › EditMode** › `CubeHaul.Tests.EditMode.Parity.ParityTests` › Run | Senaryo başına bir satır (`ScenarioMatchesTypeScriptStepByStep`), bölüm başına bir satır (`LevelPassesTheGoldenAcceptance`), rapor ve kapsam testleri |
| Unity dışında | `cd Tools/dotnet/CubeHaul.Tests` → `dotnet test --filter FullyQualifiedName~Parity` | Aynı testler |
| Rapor dosyası | `Logs/cubehaul-parity-report.txt` (proje kökünde; `Logs/` git'e girmez) | `TheReportIsWrittenAndShowsNoDifferences` her koşuda yazar |
| TypeScript | `cd Tools/ts-reference` → `npm run check:golden`, `npx vitest run tests/golden.test.ts` | Dosyalar güncel; her senaryo TS'te de kayıtlı çağrılardan aynı satırları veriyor |
| Yeniden üretim | `cd Tools/ts-reference` → `npm run export:golden` | Kaynak değişmediyse hiçbir dosya değişmez |

## 2. Senaryolar

`Assets/_CubeHaul/Tests/Fixtures/Golden/parity/*.json`, üretici: `Tools/ts-reference/scripts/golden/parity.ts`.

| Senaryo | Bölüm | Tepsi | Kenar / slot | Girdi politikası (seed) | Adım | Sonuç |
|---|---|---|---|---|---|---|
| p01 | strawberry | dosya | all / 5 | greedy-step, sonra oyun bitince girdi | 164 | won |
| p02 | strawberry | generate seed 3, shuffle 1,5 | bottom / 3 | delayed 0,7 (11) | 351 | playing |
| p03 | strawberry | dosya | all / 1 | random-step (13) | 300 | playing |
| p04 | mushroom | dosya | bottom / 5 | fifo, 3 tick gecikme | 416 | playing |
| p05 | mushroom | dosya | bottom / 5 | abuse (8) | 404 | playing |
| p06 | mushroom | dosya | **all** / 5 | fifo, gecikmesiz | 402 | playing |
| p07 | sunset | dosya | bottom / 5 | random-step (21) | 138 | stuck |
| p08 | sunset | build seed 99, hedef 0,5 | **sides** / 5 | delayed 0,6 (4) | 417 | playing |
| p09 | sunset | dosya | bottom / 6 | jam (77), 8 slota kadar | 410 | playing |
| p10 | ringed-planet | dosya | bottom / 5 | greedy-step | 581 | won |
| p11 | ringed-planet | generate seed 1234, shuffle 0 | bottom / 7 | fifo, 1 tick gecikme | 407 | playing |
| p12 | cat | dosya | bottom / 5 | jam (3), 7 slota kadar | 175 | stuck |
| p13 | cat | generate seed 2³¹−1, shuffle 0,5 | **all** / 5 | random-step (9) | 350 | playing |
| p14 | cat | dosya | bottom / 5 | reverse-collect | 404 | playing |
| p15 | cactus | dosya | bottom / 5 | delayed 0,7 (31) | 411 | playing |
| p16 | cactus | dosya | bottom / 2 | greedy-step | 404 | playing |
| p17 | rocket | dosya | bottom / 5 | random-step (1) | 354 | playing |
| p18 | rocket | generate seed 0, shuffle 100 | bottom / 5 | fifo, 5 tick gecikme | 403 | playing |
| p19 | rubber-duck | dosya | bottom / 5 | abuse (2) | 421 | playing |
| p20 | rubber-duck | build seed 7, hedef 0,3 | bottom / 5 | greedy-step | 549 | won |
| p21 | ice-cream | dosya | **sides** / 5 | delayed 0,7 (500) | 370 | playing |
| p22 | ice-cream | dosya | bottom / 5 | delayed 0,2 (6): slotlar çoğunlukla bekletilir | 369 | playing |
| p23 | watermelon | dosya | bottom / 5 | greedy-step | 427 | won |
| p24 | watermelon | generate seed 42, shuffle 2 | bottom / 1 | jam (42), 6 slota kadar | 309 | playing |

"playing" ile biten senaryolar işlem sınırında kesilir; amaç bitirmek değil, o ana kadar her adımın aynı olmasıdır.

| Politika | Ne yapar | Neyi zorlar |
|---|---|---|
| `greedy-step` | Hemen yiyebilecek en iyi kasayı seçer, `step` (kamyonlar hemen toplar); tıkanınca +1 Slot | Kazanma, uzun oyun |
| `random-step` | cube-hunt'ın dikkatsiz oyuncusu: rastgele dokunuş, `step` | Tıkanma, +1 Slot |
| `delayed p` | Rastgele dokunuş, slotlar `p` olasılıkla hazır, kamyonlar rastgele sırayla toplar | Bekletilen slotlar, sırasız toplama, claim/collect zamanlaması |
| `fifo lag` | Kamyonlar gönderildikleri sırayla `lag` tick sonra toplar | Yolda kamyon varken durum, gecikmeli açılma |
| `reverse-collect` | Her tick'in kamyonları son gönderilen önce toplar | Sırasız toplama |
| `abuse` | Rastgele oyuna geçersiz girdi karıştırır: olmayan ve boş kolonlar, dolu slotlara dokunuş, tümü bekletilen tick, fazladan +1 Slot | Yok sayılması gereken girdiler |
| `jam` | Yiyemeyecek kasaları bilerek seçer, tıkanınca +1 Slot | Tıkanma, `slotBlocked`, çok slot |
| sonrası | Bazı senaryolar oyun bitince `tick`, `step`, `tap(0)`, `tap(-1)` dener | Kazanılmış oyun değişmez |

## 3. Dosya biçimi

Başlık: `levelFile`, `edges`, `slotCount`, `tray` (`{source: "file"}`, `{source: "generate", seed, shuffle}` ya da
`{source: "build", seed, shuffle, targetWinRate?, candidates?}`; tepsi bölümün generation ayarlarıyla yeniden
üretilir), `traySha256`, `policy`, `seed`, `outcome`, `stepCount`.

`steps`: satır başına bir çağrı. İlk satır başlangıç durumunun tamamıdır. Sonraki satırlarda çağrı alanları her zaman
yazılır, durum alanlarından yalnızca bir önceki satırdan **değişenler** yazılır (yazılmayan alan = önceki değer).

| Alan | Anlam |
|---|---|
| `i`, `op` | Adım no; `start`, `tap`, `tick`, `step`, `collect`, `addSlot` |
| `col` → `result` | `tap(col)` ve döndürdüğü slot (−1 = yok sayıldı) |
| `ready` → `sent` | `tick`'te slot başına hazır mı (`null` = hepsi); `tick`/`step`'in gönderdiği kamyonlar `[slot, cell, color]` (**dispatched**) |
| `cell` | `collect(cell)` |
| `status`, `remaining`, `pending`, `tray` | Oyun durumu, kalan küp, yoldaki kamyon, tepside kalan kasa |
| `columns`, `fronts` | Kolon uzunlukları, öndeki kasanın kimliği (−1 boş) |
| `slots` | Slot başına `[id, color, count]` ya da `null` (**slot sayısı** = uzunluk) |
| `emptied` | Bu çağrıda boşalan hücreler (**collected**) |
| `canEat`, `blocked`, `stuck` | `slotCanEat`, `slotBlocked`, `isStuck` |
| `reachable`, `pick` | Renk başına ulaşılabilir küp sayısı ve `pickTarget` (**erişilebilirlik**) |
| `reachHash`, `boardHash` | Ulaşılabilir hücrelerin ve tüm tahtanın (renk, açık, claim) FNV-1a özeti |

## 4. Rapor

Her çağrıdan sonra C# kuralları aynı alanları hesaplar ve sırayla karşılaştırır: önce çağrının sonucu (`result`,
`sent`), sonra §3'teki durum alanları. İlk farklı adımda durur, çünkü sonraki adımlar o farktan etkilenir. Fark tepside
de olabilir (yeniden üretilen tepsi farklıysa adım −1, kurulum) ya da C# bir istisna atabilir; ikisi de fark olarak
raporlanır.

Örnek (bir beklenen değer bilerek bozulduğunda; değerler temsilîdir):

```text
CubeHaul TypeScript/C# parity: 24 scenarios, 8936 steps, 1 with differences

scenario                                     tray      edges/slots policy            steps  outcome  result
p05-mushroom-abuse                           file      bottom/5    abuse               404  playing  DIFFERS at step 12
…

p05-mushroom-abuse (p05-mushroom-abuse.json): first difference at step 12 of 404
  call:      collect(cell 1283)
  after:     step 11: tick(ready [1,0,1,1,1]) (12 steps matched)
  field:     pick
  expected:  [1,2,3]   (TypeScript)
  actual:    [1260,-1,1287,-1,-1]   (C#)
  also different: boardHash
```

Rapor biçimi de test ediliyor: beklenen bir değer, bir çağrının sonucu, tepsi ya da toplanan hücre bilerek bozulunca
fark tam o adımda, doğru alanla ve iki değerle raporlanıyor (`ParityTests › AWrongValue…`, `AWrongCallResult…`,
`AnotherTray…`, `AnException…`).

## 5. Kapsam ve tekrarlanabilirlik

`ParityTests › TheScenariosCoverWhatTheTaskAsksFor` kapsamı dosyalardan ölçer ve eksik varsa adıyla düşer:

| Ölçüt | Gereken | Bugün |
|---|---|---|
| Senaryo | ≥ 20 | 24 |
| Bölüm başına senaryo | ≥ 2 | 2–3 |
| Farklı politika seed'i / tepsi seed'i | ≥ 15 / ≥ 6 | ✔ |
| Tepsi kaynağı | dosya, generate, build | ✔ |
| Kenar | bottom, sides, all | ✔ |
| Slot sayısı | 1'den 7'ye | 1–7 |
| Kazanma / tıkanma | ≥ 3 / ≥ 2 | 4 / 2, ayrıca tıkanma → +1 Slot → devam |
| Yok sayılan dokunuş | ≥ 100 | ✔ |
| Bekletilen slotlu tick / tümü bekletilen | ≥ 100 / ≥ 10 | ✔ |
| Gönderen çağrı / toplanan küp | ≥ 1000 / ≥ 2000 | ✔ |
| Sırasız toplama, yolda kamyon varken bekleyen slot, kazanıldıktan sonra girdi | var | ✔ |

Tekrarlanabilirlik:

- Tüm rastgelelik `mulberry32` tohumlarından gelir; dışa aktarıcı iki kez çalışınca dosyalar bayt bayt aynıdır
  (`check:golden`, `tests/golden.test.ts`).
- TS testi her senaryoyu kayıtlı çağrılardan yeni bir oyunda tekrar oynatır, her adımda invariantları denetler ve
  satırların dosyayla aynı olduğunu doğrular.
- C# testi her senaryoyu iki kez ve ters sırayla oynatır; her oyunun iz özeti aynı kalır
  (`ReplaysAreRepeatableAndIndependentOfOrder`).

## 6. Kalan farklar

Kural ve üretim çıktısında TypeScript ile C# arasında fark yok. Bilinen farkların hiçbiri kritik değil:

| Kimlik | Fark | Kritik mi | Durum |
|---|---|---|---|
| F-2, F-3, F-4 | TS'teki gizli hatalar | Hayır | C#'ta **aynen** korunuyor (karar 15.11.2026); testlerle sabit |
| D-1 … D-6 | Üretici API farkları ([LEVEL_GENERATION §4](LEVEL_GENERATION.md#4-belgelenmiş-farklar)) | Hayır | Geçerli girdilerde sonuç aynı |
| `ReachableCells` sırası | C# artan sırada, TS ekleme sırasında döner | Hayır | Karşılaştırmalar sıralı listeyle; `pickTarget` sıradan bağımsız (I-07) |
| F-1 | Hız düğmesi `main.ts`'de kural çağrılarının sırasını değiştirir | Çekirdekte değil | Görünüm/zamanlama katmanının konusu (R-29); çekirdek yalnızca çağrı sırasını görür ve aynı sırada aynı sonucu verir |
