# Dokunmatik Tepsi ve Slotlar (Görev 16)

cube-hunt'taki tepsi ve slotlar Unity UI (uGUI) ile çiziliyor:

- Kolonlar ve yığınları: öndeki kasa tam boy, arkadakiler küçük ve soluk.
- İlk boş slota uçan renkli kasalar.
- Sayaçlar, ilerleme çubukları ve "blocked" durumu.

Fare ve dokunma, Input System üzerinden aynı yoldan geçiyor ve aynı hamleyi üretiyor. Arayüz oyun mantığını
tekrarlamıyor: neyin nerede olduğunu `GameState`'ten okuyor, hamleyi `GameSession.Tap` → `GameEngine.Tap` ile yapıyor.

> **Kolon sayısı:** Görev metni "dört kolonluk tray" diyor. Mevcut oyunda (cube-hunt @ 559dfd9) ve 10 bölüm dosyasının
> hepsinde tepsi **5 kolon**. Arayüz kolon sayısını bölümün verisinden alıyor: 4 kolonlu bir bölüm aynı kodla çalışır,
> kolonlar ortalanır.

## 1. Parçalar

| Katman | Sınıf | Görev |
|---|---|---|
| Gameplay | `HudModel` | main.ts'nin çizim durumu, motordan bağımsız. Her kasanın dokunulduğu andaki sayısı (ilerleme çubuğu için), uçan kasanın kalktığı yer, kolonların kayması (`columnTargetX`), sıraların yükselmesi, basma çukuru, dolu slotta titreme, iniş, sayı zıplaması, boşalan kasanın ayrılması. Hepsi gerçek zamanda. Kural kararı yok |
| Gameplay | `HudLayout` | Tasarım noktası → ekran. Tepsi ve slotlar main.ts'de tahta ve rampayla aynı 420×900 sayfada; kamera zemini nerede gösteriyorsa UI da orada (eğik bakış yüzünden yatay ve düşey ölçek ayrı) |
| Gameplay | `PaletteColor.Shade`, `Luminance` | draw.ts `shade`, `luminance` |
| UI | `PlayAreaView` | `Canvas ▸ Play Area`: raf, şeritler, kasa yığınları, slot yuvası, slotlar, uçan kasalar, dokunma hedefleri. Her karede `HudModel`'i çizer |
| UI | `CrateWidget` | Bir kasa (draw.ts `pod` + `countLabel`): koyu taban, renkli yüz, parlaklık bandı, ince kenar, sayı (koyu renklerde beyaz yazı ve koyu kontur, açık renklerde koyu yazı). Slottakilerde ilerleme çubuğu ve "beklemede" çizgileri |
| UI | `ColumnTapTarget` | Her kolonun ön kasası üstünde görünmez dokunma alanı (main.ts her kare aynı kutuyu düğme yapar). `OnPointerDown` (main.ts gibi basınca) → `PlayAreaView.TapColumn` → `GameSessionDriver.Tap` |
| UI | `UiShapes` | Kodla üretilen yuvarlak köşeli dikdörtgen, çerçeve ve çizgili doku (9-slice). Görsel asset yok |
| Presentation | `GameSessionDriver.Tap(col)` | `GameSession.Tap` → `GameEngine.Tap` |

3D `LayoutPreview` artık slot ve tepsi yer tutucularını çizmiyor (`ShowDockAndTray = false`); tahta ve rampa
duruyor.

## 2. Ne nereden geliyor

| Gösterilen | Kaynak |
|---|---|
| Tepside hangi kasa, rengi, sayısı | `GameState.CrateAt(col, row)` |
| Slottaki kasa, rengi, sayısı | `GameState.SlotAt(i)` |
| "blocked" (çizgiler, soluk sayı, rozet) | `GameState.SlotBlocked(i)` (`opensUp` dahil) |
| İlerleme çubuğu | `Count` / dokunulduğundaki `Count` |
| Slota uçan kasa | `DispatchScheduler.IsFlying` / `FlightOf` (1/3,8 s gerçek zaman) |
| Kasa ayrılıyor | `GameSession.CrateEmptied` |
| Dolu slot uyarısı | `GameSession.TapRefused` |

`HudModelTests`, bütün bir oyun boyunca (Strawberry kazanılıyor, Rocket blocked kasalarla tıkanıyor) her karede her
slotun ve tepsi kasasının bu kaynaklarla aynı olduğunu denetliyor.

## 3. Dokunma ve fare

- **Tek yol:** `EventSystem` + `InputSystemUIInputModule`. Fare tıklaması, kalem ve parmak dokunuşu aynı `PointerDown` olayı olur. `ColumnTapTarget` yalnızca birincil düğmeyi kabul eder (fare sağ/orta tıklaması hamle değildir).
- **Yalnızca ön sıradaki kasa dokunulabilir.** Hedef, main.ts `blockRect(c, 0)` kutusu: kolon kayarken onunla gider, boş kolonda kapanır. Arka sıralara ve kolon aralarına dokunmak hiçbir şey yapmaz.
- **Boş slot yoksa** dokunuş kurallara ulaşmadan reddedilir (`GameSession.Tap` → `TapRefused`) ve oyun durumu değişmez. Uyarı:
  - Slotlardaki kasalar 0,35 s sallanır (main.ts: `sin(shake·60)·6`).
  - Dokunulan kasa yerinde çöker.
  - Slot yuvasının kenarı kırmızıya dönüp söner (görev isteği; main.ts'de yok).

## 4. Ekranlar

`HudLayout` slotları ve tepsiyi kameranın zemini gösterdiği yere koyuyor. Yuva rampanın altında, raf yuvanın altında
kalıyor. `HudModelTests`, `ScreenLayoutTests`'in 14 ekranında (16:9'dan 22,5:9'a, çentik / ada / alt çubuk, tabletler)
5 ve 6 slotla şunları denetliyor:

- Her slot, "blocked" rozeti ve tepsi kutusu oyun bölgesinde ve güvenli alanın içinde; hiçbiri üst ya da alt çubuğun altında değil.
- Slotlar birbirine binmiyor.
- Her ön kasa en az 44×40 canvas birimi (telefonda ~44 pt parmak ucu).

PlayMode'da `GameSceneLayoutTests` 7 ekran boyunda gerçek kamera ve canvas'la çizip aynısını denetliyor.

## 5. Testler

| Test | Nerede | Ne sınar |
|---|---|---|
| `HudModelTests` (22) | EditMode + `dotnet test` | Dokunulan kasa ilk boş slota uçuyor (16 kare = 0,26 s) ve kendi kolonundan kalkıyor. Sayaç ve ilerleme kuralların sayısı. Her karede her slot ve tepsi kasası `GameState` ile aynı (2 bölüm). Dolu slotta 15 dokunuş reddediliyor ve durum (kolonlar, slotlar, tahta, `pending`) birebir aynı kalıyor; titreme ve çökme görünüyor, sonra sönüyor. Bir kolon bitince diğerleri ortalanıyor; boş kolon yerinde, dokunma hedefi kapalı, raf genişliği sabit. Sıralar yükseliyor (soluk → tam). Boşalan kasa 0,5 s'de ayrılıyor, yeni kasaya yer açıyor. 14 ekranda yerleşim |
| `TrayInputTests` (3) | **PlayMode** | **Kabul:** gerçek Input System, sanal fare ve sanal dokunmatik ekran. Aynı 7 dokunuş (5 kasa slotlara, 2'si dolu slota) fareyle ve dokunmayla aynı hamleleri ve her adımdan sonra aynı oyun durumunu üretiyor (olayların `MouseOrPen` / `Touch`'tan geldiği de denetleniyor). Dolu slotta 10 dokunuş (fare + dokunma) hiçbir şeyi değiştirmiyor, kırmızı uyarı yanıp sönüyor. Arka sıraya ve kolon arasına dokunmak hiçbir şey yapmıyor; ön kasaya dokunmak onu ilk slota gönderiyor |
| `GameSceneLayoutTests.TheTrayAndTheSlotsStayBetweenTheBars` (7) | **PlayMode** | 7 ekran boyunda (render texture) dokunma hedefleri ön kasaların üstünde, en az 44 birim; yuva, hedefler ve kasalar oyun bölgesinde ve güvenli alanda, çubukların altında değil |

## 6. Elle doğrulama

1. **CubeHaul ▸ Setup ▸ Create Game Scene** → Rebuild. `UI ▸ Play Area` eklenir; Auto Play varsayılan olarak kapalı.
2. Play. Tepside 5 kolon görünür: öndeki kasalar tam boy, arkadakiler küçük ve soluk.
3. Ön kasaya tıkla ya da dokun:
   - Kasa kavis çizerek ilk boş slota uçar.
   - Arkadaki kasalar bir sıra yükselir.
   - Slottaki kasa sayısını geri sayar, ilerleme çubuğu kısalır.
   - Rengi ulaşılamıyorsa kasa çizgili olur ve altında "blocked" rozeti çıkar.
4. Beş slot doluyken bir kasaya dokun: slottaki kasalar sallanır, yuvanın kenarı kırmızı yanıp söner, hiçbir şey taşınmaz.
5. Game penceresinde Simulator ya da farklı çözünürlüklerle (ör. 1080×2400, iPhone 13) tepsinin ve slotların çubuklara değmediğini kontrol et.
6. Commit: sahne.
