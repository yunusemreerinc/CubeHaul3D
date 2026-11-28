# Oyun Akışı ve Bitiş Ekranları (Görev 17)

Bölümler arka arkaya oynanıyor. Oyunda başlangıç ekranı, kazanma ve tıkanma panelleri, yeniden başlatma, sonraki ve
önceki bölüm, Retry ve +1 Slot var. Oyunun ne zaman bittiğine kurallar (`GameEngine` durumu) karar veriyor. Panelin ne
zaman açılacağına ise `GameFlow` karar veriyor: oyuncunun izlediği her şey bitince.

## 1. Ekranlar

| Ekran | Ne zaman | İçerik (main.ts `drawOverlay` / `panel`) |
|---|---|---|
| **Start** | Oyun açılınca (`GameSessionDriver › Show Start Screen`) | Bölüm başlığı ("1/10 · Strawberry"), kısa açıklama, **Play**. Bölüm görünür ama hiçbir şey hareket etmez. cube-hunt'ta yok; görev istedi |
| **Playing** | Oyun sürerken, ayrıca son kamyonlar eve dönerken | Panel yok |
| **Won** | Kazanıldı ve her şey yerine oturdu | "Picture complete!" / "Every cube was hauled away." / **Next level** (son bölümde **Back to level 1**) |
| **Stuck** | Tıkandı ve her şey yerine oturdu | "Out of moves" / "Every slot is waiting for a color that can't be reached." / **+1 Slot** (hakkı varsa) ve **Retry** |

- **Arka plan:** Paneller açıkken oyunun üstünü karartan katman bütün dokunuşları alır; tepsi oynanamaz (main.ts: tam ekran boş düğme).
- **Üst çubuk:**
  - **R** bölümü baştan başlatır.
  - **‹ ›** önceki ve sonraki bölüme geçer. Sondan sonra ilk, ilkten önce son bölüm gelir (B-37).
  - Başlık ("2/10 · Mushroom") ve zorluk çipi her bölümde güncellenir.

## 2. Görsel bekleyen durum: panel ne zaman açılır

Kurallar oyunu son küp toplanınca kazanılmış, hamle kalmayınca tıkanmış sayar. Bu anda ekranda hâlâ hareket olabilir:

- Eve dönen kamyonlar (son küpü toplayan kamyon yükler, yolu geri sürer ve tünele iner).
- Rampa kuyruğunda bekleyen kamyonlar.
- Slotuna uçmakta olan bir kasa (tıkanma, uçan kasa daha konmadan ilan edilebilir).

`GameFlow.VisualPending = kamyonlar (yolda + kuyrukta) + uçan kasalar`. Panel ancak bu sıfır olduktan sonra açılır,
üstüne main.ts'deki gibi 0,4 sn beklenir (`overlayDelay`). Sayaç yeniden hareket olursa ya da +1 Slot oyunu çözerse
sıfırlanır. Kazanınca bu an, main.ts'in `overlayDelay`'iyle aynı kareye denk gelir (test edilir).

## 3. +1 Slot

`GameSessionDriver.AddSlot` → `GameSession.AddSlot` → `GameEngine.AddSlot`:

- **Hak:** Bölüm başına bir kez, en fazla 6 slot, kazanınca kapalı (main.ts `canSlot`, B-30).
- **Tıkanmış oyunda:** Bir slot ekleyince durum yeniden Playing olur ve panel bir sonraki karede kapanır.
- **Ücret yok:** Bu aşamada ücretsiz. Reklam ya da satın alma bağımlılığı yok. İleride bir kapı eklenecekse yeri `GameSessionDriver.AddSlot`'u çağıran düğmeler (alt çubuk ve Stuck paneli).
- **Görünüm:** Alt çubuktaki **+1 Slot** harcanınca gri olur (main.ts'deki mat arduvaz rengi) ve basılmaz. Başlangıç ekranında da basılmaz.
- **Retry ve sonraki bölüm** yeni bir oyun başlatır; booster hakkı geri gelir.

## 4. Parçalar

| Katman | Sınıf | Görev |
|---|---|---|
| Gameplay | `GameFlow` | Ekran durumu (Start, Playing, Won, Stuck), görsel bekleyen durum, 0,4 sn bekleme, bölüm sırası (`NextIndex`, `PreviousIndex`, `IsLast`). Motordan bağımsız |
| Gameplay | `GameSession.CanAddSlot` | Booster kuralı (kazanınca kapalı eklendi); `ShowsEndPanel` main.ts gibi ≥ 0,4 sn |
| Presentation | `GameSessionDriver` | `LoadLevel(i, showStart)`, `NextLevel`, `PreviousLevel`, `Restart`, `Retry`, `StartPlaying`, `AddSlot`. Başlangıç ekranında oturumu ilerletmez. Her karede `GameFlow.Update`. `LevelTitle`, `IsLastLevel`, `UseLevels` (testler) |
| UI | `GameFlowView` | `Canvas ▸ Overlay`: karartma katmanı ve üç panel. Sürücünün ekranını gösterir. Stuck panelinde düğmeler main.ts gibi yerleşir (iki düğme 130 genişlikte, tek düğme 200) |
| UI | `SessionControls` | Üst çubuk (R, ‹, ›, başlık, zorluk çipi, hız) ve alt çubuk (+1 Slot; harcanınca gri) |

## 5. Testler

| Test | Nerede | Ne sınar |
|---|---|---|
| `GameFlowTests` (6) | EditMode + `dotnet test` | **Won paneli:** Kazanma anında kamyonlar hâlâ yoldayken panel açılmıyor. Son kamyon indikten 0,4 sn sonra açılıyor, main.ts'in `overlayDelay`'iyle aynı karede. **Tıkanma:** Panel uçan kasa konduktan sonra açılıyor; +1 Slot oyunu çözüyor ve panel kapanıyor. **Booster:** Bölüm başına bir kez; kazanınca ya da 6 slotta yok; Retry ile geri geliyor. **Start:** Ekran Play'e kadar bekliyor. **Art arda:** İlk 3 bölüm kazanılıyor; sondan sonra ilk bölüm |
| `GameFlowSceneTests` (3) | **PlayMode** | **Kabul:** sahnede ilk 3 bölüm art arda oynanıyor. Start → Play, sonra her bölümde "Picture complete!", bütün kamyonlar evdeyken açılıyor; Next level ile 2/10 ve 3/10, sonunda 4/10'a geçiliyor. Tıkanan bölüm: "Out of moves", +1 Slot ve Retry görünüyor. Retry bölümü sıfırlıyor; +1 Slot paneli kapatıp bölümü kazandırıyor ve gri oluyor. Üst çubuk bölümler arasında dönüyor, R bölümü baştan başlatıyor |

## 6. Elle doğrulama

1. **CubeHaul ▸ Setup ▸ Create Game Scene** → Rebuild. Sahneye `UI ▸ Overlay` eklenir.
2. Play. Başlangıç ekranı açılır: "1/10 · Strawberry" ve **Play**.
3. Bölümü oyna. Son küp toplanınca kamyonlar eve döner, sonra "Picture complete!" açılır. **Next level** → 2/10.
4. Tıkanmak için yanlış renkleri seç. Kamyonlar dönünce "Out of moves" açılır:
   - **+1 Slot** paneli kapatır ve alt çubuktaki düğme gri olur.
   - **Retry** bölümü baştan başlatır.
5. Üst çubuktaki **‹ ›** ve **R**'yi dene; hız seçimi bölüm değişince korunur.
6. Commit: sahne.
