# Oyun Döngüsü: Motor + Kamyonlar (Görev 15)

Kural motoru (`GameEngine`) ile kamyon animasyonu, cube-hunt `src/main.ts`'nin `App.update` sırasıyla
birleştiriliyor. Bir küpe tek bir kamyon gidiyor. Küp, kamyonu varana kadar tahtada kalıyor ve arkasındaki küpleri
kapatmaya devam ediyor. Kamyon varınca küp tahtadan kalkıp kasaya uçuyor ve `GameEngine.Collect` tam o anda çağrılıyor.
Hız 1x / 2x / 3x seçilebiliyor. Aynı hamleler aynı kare süresiyle cube-hunt'la kare kare aynı oyunu veriyor (golden
`sessions/*.json`).

## 1. Parçalar

| Katman | Sınıf | Görev |
|---|---|---|
| Gameplay | `GameSession` | Bir bölüm oyunu. `Advance(dt)` main.ts `update`'in bir karesi (§2). `Tap`, `AddSlot` (bölüm başına bir kez, en fazla 6), `SetSpeed` / `CycleSpeed`. Olaylar: `CrateTapped`, `TapRefused`, `CrateLanded`, `TruckSent`, `TruckLaunched`, `CubeCollected`, `TruckLoaded`, `TruckReturned`, `CrateEmptied`, `SlotAdded`, `SpeedChanged` |
| Gameplay | `DispatchScheduler` | Slot bekleme süreleri (`POP_INTERVAL` = 0,15 s simülasyon zamanı) ve slotuna uçan kasalar (1/3,8 s **gerçek** zaman). `Ready`, `GameEngine.Tick`'e verilen önbellekli temsilci |
| Gameplay | `TruckFleet` | `sendTruck`: en ucuz rotalı rampa şeridi, şerit kuyruğu. Şerit aralığı `30 · carScale / 200` s. Yoldaki kamyonlar; varışta `Collect` |
| Gameplay | `AutoPlayer` | cube-hunt referans politikası (`Tools/ts-reference/tests/appModel.ts` `play`). Tepsi dokunuş almaya başlayana kadar oyuncunun yerini tutar |
| Gameplay | `FixedStepClock`, `SessionLoop` | Cihaz kare süresi → tam 1/60 s adımlar (en fazla 0,05 s/kare, main.ts gibi). `Alpha` iki adım arası çizim için |
| Gameplay | `CubeLoad` | main.ts `drawLoading` eğrisi: gevşeme ve yükselme (`LIFT_END` = 0,22), kavis, kasaya iniş (`LAND_AT` = 0,78) |
| Presentation | `GameSessionDriver` | Sahnedeki döngü (`Game ▸ Game Loop`). Kütüphaneden bölümü açar, her kare `SessionLoop.Frame`, sonra `TruckFleetView.Render(alpha)` |
| Presentation | `TruckFleetView` | Oturumun kamyonları (havuzdan). Varışta küpü `GridPresenter.TakeCube` ile alır ve kasaya uçurur. `LAND_AT`'ta küp havuza döner, kamyonun kendi yük küpü görünür |
| UI | `SessionControls` | Hız düğmesi (1x → 2x → 3x → 1x, etiket "Nx"), Restart, +1 Slot (kullanılınca pasif) |

Gameplay sınıfları motordan bağımsız. EditMode'da ve `dotnet test` ile test ediliyor.

## 2. Bir karenin sırası (main.ts `update`)

1. `sdt = dt · hız`. Her slotun bekleme süresi `sdt` kadar azalır.
2. Oyun `Playing` ise `GameEngine.Tick(hazır slotlar)`. Bekleme süresi bitmiş ve kasası inmiş her slot, ulaşılabilir bir
   küpünü **claim** eder (`count−−`). Her claim için bir kamyon planlanır ve şeridinin kuyruğuna girer. Slot 0,15 s
   bekler. Kasanın son kamyonuysa slot boşalır (`CrateEmptied`).
3. Her rampa şeridinin süresi `sdt` kadar azalır. Süresi dolan şerit, kuyruğundaki ilk kamyonu yola çıkarır.
4. Yoldaki kamyonlar `sdt` kadar ilerler. Küpüne varan kamyon için **o anda** `GameEngine.Collect(cell)` çağrılır:
   küp tahtadan kalkar, arkası açılır. Kamyon 0,42 s yükler, aynı yoldan döner ve tünele iner.
5. Tünele inen kamyonlar filodan çıkar. Uçan kasalar gerçek zamanla (`dt`) ilerler ve iner.
6. Oyun bitmiş ve kamyon kalmamışsa bitiş paneli sayacı (`OverlayDelay`) işler. Panel 0,4 s sonra açılır.

Bu sıranın sonuçları:

- **Bir küp tek kamyona verilir.** Claim edilen küp `reachable` listesinden çıkar; `PickTarget` onu bir daha seçmez.
- **Arkadaki küpler erken açılmaz.** Claim ile collect arasında küp tahtada durur ve yolu kapatır. Bir karede açılan
  küp, en erken bir sonraki karenin `tick`'inde claim edilir (I-18).
- **Hız:** sürüş, yükleme, slot bekleme süresi ve şerit aralığı `sdt` ile ölçeklenir. Kasa uçuşu ölçeklenmez
  (B-31, B-32). 2x tam iki, 3x tam üç kat hızlı akar; kasa uçuşu her hızda 0,26 s'dir.

## 3. Sabit adım ve kare hızı

cube-hunt her tarayıcı karesinde bir `update` çalıştırır; bu, 60 Hz ekranda 1/60 s'dir. Burada oturum, cihazın kare
hızından bağımsız olarak **her zaman tam 1/60 s'lik adımlarla** ilerler (`FixedStepClock`). Böylece 30, 60, 90, 120
ya da 144 Hz cihazlarda ve düzensiz karelerde oyun aynıdır: aynı hamleler, aynı kamyonlar, aynı adımda aynı toplamalar
(test edilir). Ekran adımlardan hızlıysa kamyonlar son iki adımın arasında çizilir (`TruckMotion.Interpolate`).

## 4. Yükleme animasyonu (3D)

Kamyon varınca küpün GameObject'i ızgaradan alınır (`TakeCube`). Yükleme boyunca (`LoadProgress` 0 → `LAND_AT`):

1. **0–0,22:** Küp sallanarak (yaw) gevşer ve yerinden `cs·0,3` yükselir.
2. **0,22–0,78:** Kasaya doğru yumuşak geçişle (smoothstep) kavis çizer. En yüksek noktası `cs·1,1` kadar daha yüksektir.
   Bu sırada kasa küpünün boyuna küçülür ve kısa yoldan dönerek kamyonla hizalanır.
3. **0,78'den sonra** küp ızgaranın havuzuna döner ve kamyonun `Bed/Cargo` küpü görünür.

Hedef, kamyon modelinin `Cargo` transform'u. Model değişirse animasyon yeni kasaya kendiliğinden uyar.

## 5. Golden oturumlar (G-11)

`Tools/ts-reference`: `npm run export:golden` → `Assets/_CubeHaul/Tests/Fixtures/Golden/sessions/*.json` (18 koşu):

- 10 bölümün hepsi 1x / 60 fps.
- Çilek ayrıca 2x ve 3x hızda, 30, 120 ve 144 fps'te.
- Ringed Planet 3x, Rocket 2x, Watermelon 2x.

Her koşuda her tap, claim (slot, hücre), collect ve +1 Slot olduğu kareyle birlikte kayıtlı. Seçili karelerde
yoldaki her kamyonun konumu (bit düzeyinde) ve rampa kuyrukları da kayıtlı. Kaynak, `appModel.ts`: main.ts'nin
zamanlamasını satır satır izleyen başsız model. Rocket, referans politikayla tıkanıyor (+1 Slot'a rağmen). Bu, politikanın
sonucu; aynı sonucu C# da veriyor.

`SessionGoldenTests` aynı bölümü aynı politika ve kare süresiyle oynatıyor. Her olayın aynı karede ve sırada olmasını,
kamyon konumlarının aynı double'lar olmasını ve sonuç, kare sayısı, simülasyon süresi (bit düzeyinde), kamyon sayısı
ile en çok aynı anda yolda olan kamyon sayısının aynı olmasını istiyor. İlk farkta, çevresindeki olaylarla birlikte
durur. Kasıtlı bozmalar (slot süresi 0,1, şerit aralığında `carScale` yok, kasa uçuşu simülasyon zamanında)
yakalanıyor.

**JSON sayıları:** Unity'nin Mono'su .NET Framework'ün `double.Parse`'ını kullanır. Bu, 16–17 basamaklı sayılarda
son bitte yanılabilir (ör. 435.27586206896723 → 435.27586206896717). Golden kamyon konumları bu yüzden Unity'de bir bit
farklı okunuyordu. `JsonNumber.Parse` en yakın double'ı (eşitlikte çift olanı) her çalışma zamanında aynı veriyor:
kısa sayılarda Clinger'ın kesin hızlı yolu, diğerlerinde BigInteger ile kesin bölme. `JsonParser` ve `JsonWriter`
bunu kullanıyor. `JsonNumberTests` (EditMode, Unity'de de koşar) Node'un bitleriyle karşılaştırıyor;
`JsonNumberFuzzTests` (`dotnet test`) .NET 8'in doğru `double.Parse`'ına karşı 660 bin girdiyi deniyor (tam yarı yol
noktaları dahil).

**`Math.hypot`:** JavaScript'in (V8) `Math.hypot`'u, `Math.Sqrt(x² + y²)`'den rastgele girdilerin ~%40'ında son bitte
farklı. `JsMath.Hypot` V8'in algoritmasının portu; kamyon hareketi ve rota planlayıcı bunu kullanıyor. Bugünkü rotalar
eksenlere paralel olduğu için iki yöntem aynı sonucu veriyor; çapraz bir bacak eklenirse eşdeğerlik bozulmaz.

## 6. Testler

| Test | Nerede | Ne sınar |
|---|---|---|
| `SessionGoldenTests` (19) | EditMode + `dotnet test` | 18 golden koşu cube-hunt ile kare kare aynı (§5) |
| `SessionRuleTests` (10) | EditMode + `dotnet test` | İlk bölüm 1x / 2x / 3x baştan sona kazanılıyor. Küp başına bir kamyon, hiçbir küp iki kamyona verilmiyor, her kamyon kendi küpünü topluyor ve eve dönüyor. Küp yalnızca kamyonu park yerindeyken kalkıyor. Her claim'de, claim edilmiş küpler dahil bütün küpler dururken sıfırdan kurulan bir tahtada hedef ulaşılabilir; arkadaki küp erken açılmıyor. 1×3 sütunda sıradaki kamyon öndeki küp toplanmadan çıkmıyor. Slot bekleme süresi ≥ 0,15 s, şerit aralığı ve şerit sırası (FIFO). Kasa uçuşu her hızda 0,26 s gerçek zaman. Aynı oyun 30–144 Hz ve düzensiz karelerde aynı. Tap reddi, +1 Slot bir kez, hız döngüsü |
| `TruckLoadTests` (27) | EditMode + `dotnet test` | `JsMath.Hypot` Node'un değerleriyle aynı (Sqrt'un farklı olduğu girdiler dahil). Yükleme eğrisinin evreleri ve sürekliliği. Kısa yoldan hizalanma. İki adım arası çizim |
| `GameAllocationTests.ASessionFrame…` (2) | `dotnet test` | Kamyon gönderilmeyen karelerde bütün döngü (sürüş, yükleme, rampa, uçuş, otomatik oyuncu) bellek ayırmıyor. Kamyon başına < 4 KB (hareket + rota) |
| `GameLoopTests` (3) | **PlayMode** | **Kabul:** sahnede ilk bölüm baştan sona oynanıyor (hız düğmesiyle 1x → 2x → 3x). Claim edilen küp kamyonu varana kadar tahtada; her küp hücresinden kasaya uçuyor, kasaya inince havuza dönüyor ve yük küpü görünüyor. Sonunda bütün küpler ve kamyonlar havuzda, kamyonlar yeniden kullanılmış. Hız düğmesi etiketi, Restart her şeyi geri koyuyor, +1 Slot bir kez |

## 7. Elle doğrulama

1. **CubeHaul ▸ Setup ▸ Create Game Scene** → Rebuild. Sahneye `Game ▸ Game Loop` (`GameSessionDriver`) eklenir; eski
   `Grid Demo Driver` kalkar.
2. Play. İlk bölüm kendiliğinden oynanır (`Auto Play` varsayılan olarak açık, tepsi henüz dokunuş almıyor). Kamyonlar
   rampadan sırayla çıkar ve küplerine gider. Küpler kamyon varınca kasaya uçar. Kamyonlar yükle döner. Son kamyon
   inince Console'da "Strawberry: won after … s" yazar.
3. Sağ üstteki **1x** düğmesi 2x ve 3x'e geçirir. **R** baştan başlatır. **+1 Slot** bir kez çalışır.
4. Commit: sahne (`.meta` ile).
