# Geçici 3D Kamyon ve Hareketi (Görev 14)

Low-poly bir kamyon modeli, renklendirme, havuz ve cube-hunt'taki gibi bir hareket. Kamyon planlanan yoldan
([ROADS.md](ROADS.md)) küpüne gidiyor, küpü yüklüyor, dönüyor ve rampadan tünele iniyor. Ticari ya da dış asset yok:
model Unity'nin yerleşik küp ve silindir mesh'lerinden kuruluyor ve sonradan değiştirilebilir.

## 1. Model

`TruckModelBuilder.Build()`, Editor'de **CubeHaul ▸ Setup ▸ Create Truck Prefab** → `Art/Prefabs/Truck.prefab`
(materyaller `Art/Materials/Truck*.mat`).

| Parça | Hiyerarşi | Renk |
|---|---|---|
| Gövde (şasi) | `Body/Chassis` | Kasanın renginin koyu tonu |
| Kabin | `Cabin/Cab`, `Cabin/Hood`, `Windshield`, `Headlight L/R` | Kasanın rengi; cam ve farlar sabit |
| Yük kasası | `Bed/Floor`, `Side L/R`, `Tailgate` | Kasanın rengi; zemin koyu ton |
| Yük | `Bed/Cargo` (7 birimlik küp) | Kasanın rengi; yalnızca yüklüyken görünür |
| Dört teker | `Wheels/Wheel FL, FR, RL, RR` | Koyu lastik; kamyon ilerledikçe döner |

- **Ölçü:** Bir model birimi bir tasarım birimi. cube-hunt'ın kamyonu gibi 21,4 uzun, 11,4 geniş. Model +Z'ye bakıyor, orijini zeminde. Ekranda `carScale` (hücre boyu / 13, 0,5–1 arası) ile küçülüyor.
- **Fizik:** Collider yok.
- **Modeli değiştirmek:**
  1. Yeni modelin köküne `TruckVisual` ekle.
  2. **Paint**, **Trim**, **Cargo** ve **Wheels** alanlarını doldur.
  3. Bunu `World ▸ Trucks › Prefab`'a ata.

  Oyun kodu modelin içine bakmıyor.

## 2. Renk ve havuz

- **Renk:** Kamyon, gönderildiği kasanın rengine boyanıyor (cube-hunt'taki gibi). Her renk için tek bir boya ve tek bir koyu ton materyali var; aynı renkteki kamyonlar aynı materyali paylaşıyor.
- **Havuz (`TruckPool`):** Kamyon gönderilince havuzdan alınıyor, tünele inince geri veriliyor. En çok aynı anda yolda olan kadar kamyon üretiliyor.

## 3. Hareket (`Gameplay/Trucks/TruckMotion`, motordan bağımsız)

`main.ts`'teki araç güncellemesinin portu:

| Davranış | Değer |
|---|---|
| Hız | 200 tasarım birimi/sn (`CAR_SPEED`), oyun hızıyla çarpılır |
| Yol | Rotanın ara noktaları arasında düz; `Out` (tünel → yol → giriş → park), `Home` (aynı yoldan geri → tünel) |
| Dönüş | Burun bir sonraki noktaya en fazla **14 rad/sn** döner (yüklerken küpe doğru 10 rad/sn); köşelerde ani dönüş yok |
| Şerit | Yolda ve rampa önünde gidiş yönünün sağına 5,8 · ölçek kayar; tarlanın içinde ortadan gider (`laneShare`) |
| Varış | Park noktasına gelince `Arrived`: küp o an toplanır (`main.ts pickUp`) |
| Yükleme | 0,42 sn (`LOAD_TIME`); küp yüklemenin %78'inde kasaya iner (`LAND_AT`) |
| Tünel | Çıkarken ilk 28 birimde büyüyerek ve yükselerek görünür olur, inerken küçülerek kaybolur (`RAMP_FADE`) |

Hareket, rotanın kırık çizgisini birebir izliyor ve yumuşaklığı burnun dönüşünden geliyor. Köşeleri yuvarlamak varış
zamanlarını, dolayısıyla hangi küplerin ne zaman açılacağını değiştirirdi (R-07).

## 4. Sahnede

- **Bileşen:** `World ▸ Trucks` (`TruckFleetView`). `Send(route, cube, cellSize, ring, color)` bir kamyon gönderir. Her karede (`AutoStep`) ya da `Step(saniye)` ile ilerler. `Arrived` olayı oyunun `Collect`'ini çağırmak içindir.
- **Otomatik oynatıcı:** `World ▸ Grid › Grid Demo Driver › Auto Play` artık kamyonlarla oynuyor. Her claim bir kamyon gönderiyor, küp kamyon varınca toplanıyor.

## 5. Testler

| Test | Nerede | Ne sınar |
|---|---|---|
| `TruckMotionTests` (12) | EditMode + `dotnet test` | Kamyon rotasının uzunluğu / hız sürede varıyor, 0,42 sn yüklüyor, aynı yoldan dönüp tünel noktasında bitiyor. Her karede rotanın üstünde ve dönüşü 14 rad/sn'yi geçmiyor (40 rotada). Yüklerken küpe dönüyor. Yolda sağ şeritte, tarlada ortada. Tünelde görünmüyor. Küp %78'de kasada. `turnToward` kısa yönden dönüyor. Rocket bölümü baştan sona: küp başına bir kamyon, hepsi eve dönüyor |
| `GameAllocationTests.MovingATruckAllocatesNothing` | `dotnet test` | Kamyon hareketi bellek ayırmıyor |
| `TruckTests` (4) | **PlayMode** | Modelde gövde, kabin, kasa, yük ve 4 teker var, collider yok, ölçüler doğru. Sahnede bir kamyon kasanın rengine boyanıyor, rotasını izliyor (model konumu ve yönü hareketle aynı), varınca küp toplanıyor, rampadan tünele iniyor ve havuza dönüyor. Üç tur gönderimde aynı kamyonlar kullanılıyor. Çilek kamyonlarla baştan sona oynanıyor |

## 6. Elle doğrulama

1. **CubeHaul ▸ Setup ▸ Create Game Scene** → Rebuild. `Art/Prefabs/Truck.prefab` ve `Truck*.mat` oluşur, sahneye `World ▸ Trucks` eklenir.
2. `World ▸ Grid` › **Auto Play**'i işaretle, Play. Kamyonlar rampadan çıkıp yolu izliyor, küplere gidiyor; küpler kamyon varınca kayboluyor, kamyonlar küp kasada geri dönüp rampaya iniyor.
3. Commit etmeden Auto Play'i kapat. Commit: sahne, `Truck.prefab` ve `Truck*.mat` (hepsi `.meta` dosyalarıyla).
