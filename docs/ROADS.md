# Yol Algoritması ve Dünya Koordinatı Dönüşümü (Görev 13)

cube-hunt'ın yol planlayıcısı (`src/view/roads.ts › RoadMap`) ve `main.ts`'teki rampa, tünel ve şerit yerleşimi C#'a
taşındı: `Assets/_CubeHaul/Gameplay/Roads/` (UnityEngine'siz). Rotalar TypeScript ile **aynı double'lar**; her test
hücresinde rota yalnızca açık kenardan giriyor ve açık alandan geçiyor.

## 1. Kamyonun yolu

```text
tünel (şerit k, y = 503.2) → şeridin yolla buluştuğu nokta (halkanın alt kenarı)
  → yol halkasında kısa yönden (köşeler) → bir hücrenin AÇIK kenarı karşısındaki yol noktası
  → sınırdaki hücreye dik giriş → yalnızca boşalmış, açık hücreler (4 komşu adım)
  → küpün yanındaki hücrede ya da kenardaki küp için alanın hemen dışında park
yükleme → aynı yoldan geri (park noktası hariç) → şeridinden tünele
```

| Kural | Nerede |
|---|---|
| Giriş yalnızca açık kenardan: `bottom` → alt; `sides` → alt, sol, sağ; `all` → dört kenar | `Board.OpenSides`, planner |
| Alan içinde yalnızca açık (boşalmış ve kenara bağlı) hücreler; claim edilmiş küp de duvar | BFS, `Board.IsOpen` |
| Şerit: üç rampa şeridinden rotası en ucuz olan (eşitlikte ilki) | `RoadPathPlanner.PlanTruck` (`main.ts sendTruck`) |
| Maliyet: yolda gidilen + yoldan ilk hücreye + adım başına bir hücre | `RouteFrom` dönüşü |
| Dönüş: rota tersten (park noktası hariç), sonra şeridin tünel noktası | `TruckRoute.Home` |

## 2. Kod

| TypeScript | C# | Not |
|---|---|---|
| `Pt` | `PointD` | Tasarım birimi (x sağa, y aşağı) |
| `Ring` | `RoadRing` | Dört kenar ayrı tutulur (`X0, Y0, X1, Y1`), böylece her koordinat TS ile aynı double |
| `RoadMap` (`center`, `beside`, `roadPoint`, `roadDistance`, `roadPath`, `route`, `routeFrom`) | `RoadPathPlanner` (aynı adlar, PascalCase) | Tamponlar yeniden kullanılır; çıktı listesi çağıranın |
| `main.ts` ring, `entry`, `TUNNEL`, `RAMP_LANES`, `sendTruck`, dönüş yolu | `RoadLayout` (`Geometry`, `LaneEntry`, `TunnelStart`), `RoadPathPlanner.PlanTruck` → `TruckRoute` | |
| — | `BoardCoordinateMapper` | Hücre ↔ tasarım noktası ↔ dünya (XZ düzlemi) |

**Aynılık için dikkat edilenler:**
- **Arama sırası:** Kenarlar `bottom, left, right, top` sırasında, komşular `sol, sağ, üst, alt` sırasında taranıyor ve eşit maliyette ilk bulunan tutuluyor. Bunlar `Board.openSides` ve `neighbors` ile aynı; eşitlikler aynı çözülüyor.
- **`Math.hypot`:** Rotada hep bir bileşeni 0 olan vektörlerde çağrılıyor; sonuç tam olarak `|d|`.
- **Köşe sıralaması:** Kararlı sıralama ile yapılıyor.
- **Halka:** `RectD` ile değil kendi dört kenarıyla tutuluyor. `x0 + (x1 − x0)` son bitte `x1`'den farklı olabilir.

**TS'ten aynen alınan bir ayrıntı:** Şerit yolla tam giriş noktasında buluşuyorsa rota o noktayı iki kez listeler
(önce giriş, sonra yol noktası). Hücreleri kenarın iki katından (44 birim) büyük olan küçük tahtalarda, kenardaki küpün
park noktası yolun ötesinde kalır. 10 bölümün hiçbirinde hücre 10,8 birimi geçmez; bu yalnızca test tahtalarında olur.

## 3. Dünya koordinatları

`BoardCoordinateMapper`: `x = tasarımX · u`, `z = −tasarımY · u`, `y = yükseklik · u` (u = 0,025). Rampa tarafı
(büyük tasarım y) kameraya en yakın, negatif z. Ters dönüşüm `FromWorld`, `WorldToCell` ve `CellAt` alan dışında −1
verir (yol, rampa, tünel).

## 4. Testler

| Test | Ne sınar |
|---|---|
| `RoadGoldenTests` (57) | `roads.json`: 56 tahta durumu (10 bölüm taze, 6 ve 18 tur soyulmuş, claim edilmiş küplerle; iki bölüm diğer kenar türleriyle; 12 elle yapılmış küçük tahta), 1235 hedef. Her şeridin maliyeti, seçilen şerit ve rotanın her noktası TS ile aynı |
| `RoadRuleTests` | `roads.test.ts`'in 3 testi; 10 bölümde bütün oyun boyunca gönderilen her kamyon (küp başına bir rota); 10 bölüm × 3 kenar türü × 5 durumda (taze, 5/15/40 tur, claim'li) ve `roads.json`'un her durumunda **her ulaşılabilir hücre** için tüm kurallar (toplam ~17 700 rota); koordinat dönüşümü iki yönde; yol ve rampanın tahtadaki yeri; rota bellek ayırmıyor |
| `RoadDebugGizmosTests` (2, PlayMode) | Oyun sahnesinde çilekte her ulaşılabilir küp için rota. Kamyon küpün bir hücre yanına park ediyor (dünya koordinatında, gerçek küp nesnesine göre). Alan içindeki her yol noktası açık bir hücrenin üstünde. Alt kenarlı bölümde giriş hep alttan |

Her rota için denetlenen kurallar (`RoadRuleTests.Check`):
1. Kamyon tünelde kendi şeridinden çıkıyor ve yola şeridin buluşma noktasından giriyor.
2. Giriş kenarı tahtanın açık kenarlarından biri ve giriş hücresi o kenarda; giriş hücresi açık ya da küpün kendisi.
3. Yol noktasına kadar her nokta halkanın üstünde, eksen boyunca.
4. Sonrasında her nokta açık bir hücrenin merkezi; ilki giriş hücresi; ardışıklar komşu.
5. Park noktası küpe tam bir hücre uzaklıkta (kenardaki küpte alanın dışında, küpe bakan).
6. Maliyet = yol + giriş + adım başına bir hücre.
7. Dönüş yolu rotanın tersi, sonra tünel.

**Kapalı kenar testleri:** Kapalı kenardan girmenin gerçekten kısa olduğu iki tahta var: `top-corridor-bottom` ve
`top-corridor-sides` (9×3, sol yukarı ve üstten koridor). Planlayıcıya "üst kenar hep açık" ya da "sağ kenar hep açık"
hatası sokulunca hem golden hem kural testleri düşüyor. Açık olmayan hücreden geçme hatası 94 testi, eşitlikte son
seçeneği alma hatası 14 testi düşürüyor.

## 5. Gizmo ile hata ayıklama

Sahnede `Game ▸ World ▸ Road Debug` (`RoadDebugGizmos`). Scene görünümünde (ya da Game görünümünde Gizmos açıkken)
şunları çizer:
- **sarı:** yol halkası
- **camgöbeği:** üç şerit ve tünel noktaları
- **gri:** açık hücreler
- **yeşil:** her rota ve ara noktaları
- **kırmızı:** park noktası
- **mor:** hedef küp
- **mavi:** dönüş yolu

| Ayar | Anlamı |
|---|---|
| Target Cell | −1: her ulaşılabilir hücre; bir indeks: yalnızca o hücre |
| Level Index, Edges, Peel Rounds | Oyun çalışmıyorken gösterilen tahta: bölüm, kenar türü (Level = bölümün kendi kenarı), önceden soyulan tur |
| Show Home, Show Open Cells, Height | Dönüş yolu, açık hücreler, çizgilerin tahtadan yüksekliği |

Oyun çalışırken (`GridPresenter`'a bağlı bir tahta varken) rotalar oyunun tahtasından ve her yeniden çizimde
hesaplanır.
