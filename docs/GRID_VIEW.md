# 3D Küp Alanı ve Havuzlama (Görev 12)

Bölümün resmi (`LevelDefinition.picture.cells`) 3D küplerle çiziliyor. Her renkli hücreye paletteki renginde bir küp
konuyor, `-1` (EMPTY) hücreler boş kalıyor. Görünüm oyunu yalnızca hücre indeksiyle izliyor. Fizik ve çarpışma
hiçbir kararda kullanılmıyor.

## 1. Parçalar

| Katman | Tür | Görev |
|---|---|---|
| Gameplay (motordan bağımsız) | `CubeField` | Hücre başına renk ve durum (`Empty`, `Present`, `Claimed`); tasarım konumu (`DesignCenter`) ve tersi (`CellAt`); `Sync(Board)` ile oyunu izler |
| Gameplay | `PaletteColor` | `"#rrggbb"` → 8 bit kanallar |
| Presentation | `GridPresenter` | `CubeField`'i küplere çevirir: konum, ölçek, renk başına materyal; her karede tahtayı izler |
| Presentation | `CubePool` | Küp GameObject'lerini yeniden kullanır |
| Presentation | `CubeMesh` | 24 köşeli, düz gölgeli birim küp |
| Presentation | `GridPresenter.TakeCube` / `ReturnCube` | Görev 15: kamyon varınca küpü yükleme animasyonu için ızgaradan alır, kasaya inince havuza geri verir. Oyun döngüsü `GameSessionDriver`, [SESSION.md](SESSION.md) |
| Editor | `GameSceneBuilder` | `Art/Materials/Cube.mat`'i (URP Lit, GPU instancing açık) oluşturur, sahneye `World/Grid`'i ekler |

## 2. Hücre ↔ küp eşlemesi

- **İndeks:** Küp `i`, tahtadaki hücre `i`'dir: satır `i / genişlik`, kolon `i % genişlik`. Satır 0 en üstte (rampaya en
  uzak), kolon 0 en solda. Küp nesnesinin adı `Cube i`.
- **Konum:** Küpün merkezi cube-hunt'ın hücre merkezi (`main.ts cellCenter` = `DesignLayout.CellCenter`), dünyaya
  `WorldMapper.FromDesign` ile geçirilir. Küpler tahtanın üstünde (`BoardTop` = 2 tasarım birimi) durur. Kenarı hücrenin
  %90'ı; aradaki %10 boşluk ızgarayı gösterir.
- **Renk:** `picture.palette[cells[i]]`. Her palet rengi için bir materyal kopyası var; aynı renkteki küpler aynı
  materyali paylaşır.

## 3. Oyunu izleme

`GridPresenter.Bind(board)`'dan sonra her `LateUpdate`'te `CubeField.Sync(board)` çalışır:

| Tahtada | Küp |
|---|---|
| Hücre dolu, claim edilmemiş | Görünür (`Present`) |
| Hücre claim edilmiş (kamyon yolda) | **Görünür kalır** (`Claimed`): zamanlama sözleşmesi, küp ancak toplanınca gider |
| Hücre boşaldı (`Collect` / `Step`) | Havuza döner, görünmez olur (`Empty`); bir daha gelmez |

`Sync` bellek ayırmaz ve yalnızca durumu değişen hücreleri bildirir.

## 4. Çizim ve performans

- **Fizik yok:** Küplerde collider yok; seçim ve kararlar hücre indeksiyle yapılıyor.
- **Havuzlama:** Küpler `CubePool`'dan alınıp geri veriliyor. Bölüm değişince yeni nesne yalnızca önceki en büyük
  bölümü aşan kadar üretiliyor (örnek: watermelon 1610 → cactus 1720 için 110 yeni küp, sonra çilek için hiç).
- **Toplu çizim:** Tüm küpler tek bir mesh'i, aynı renktekiler tek bir materyali paylaşıyor. URP'nin SRP Batcher'ı
  böyle bir sahneyi renk başına birkaç çizim grubuyla çizer. Materyallerde GPU instancing de açık.
- **Sahneye kaydedilmez:** Küpler, mesh ve materyal kopyaları `HideFlags.DontSave`. Sahne dosyasında yalnızca
  `GridPresenter` bileşeni ve ayarları var. Editor'de de (`[ExecuteAlways]`) seçili bölüm görünüyor.
- **Ölçüm:** En büyük bölümde 2116 küp var. Cihazda ölçülmedi. Gerekirse tek mesh'e birleştirme ya da
  `Graphics.RenderMeshInstanced` sonraki bir performans görevinde değerlendirilir; `CubeField` buna hazır.

## 5. Doğrulama

| Test | Nerede | Ne sınar |
|---|---|---|
| `CubeFieldTests` (20) | EditMode + `dotnet test` | Çilekte 527 küp, renkleri ve palet aynı. 10 bölümün her hücresinde indeks, renk, konum ve ters eşleme (`CellAt`) tahta ve cube-hunt ile aynı. Claim edilen küp kalıyor, toplanan gidiyor. 5 eşdeğerlik senaryosu (TS'te kaydedilmiş oyunlar) adım adım oynatılınca görünen küpler tahtadaki küpler, claim edilenler tahtadaki claim'ler, giden küpler de TS'in boşalttığı hücreler |
| `GameAllocationTests.FollowingTheBoardWithTheCubeFieldAllocatesNothing` | `dotnet test` | Oyunu izlemek bellek ayırmıyor |
| `GridPresenterTests` (4) | **PlayMode** | Çilek 1080×1920'de: her renkli hücrede kendi renginde küp, boş hücrede küp yok; küpün üst yüzü ekranda hücre merkezinde (0,5 px). Çizilen görüntüde her hücrenin rengi doğru. Claim edilen küp kalıyor, toplanan havuza dönüyor. Havuz bölümler arasında küpleri yeniden kullanıyor. Collider yok |

## 6. Elle doğrulama

1. **CubeHaul ▸ Setup ▸ Create Game Scene** → Rebuild. Sahneye `World ▸ Grid` eklenir ve `Art/Materials/Cube.mat`
   oluşur. Tahtada çilek düz doku yerine 3D küplerle görünmeli.
2. Play: aynı görüntü.
3. Oyunu izlemesini görmek için `World ▸ Grid`'i seç. Inspector'da **Grid Demo Driver › Auto Play**'i işaretle ve Play'e
   bas. Kamyonların alacağı küpler bir süre yerinde kalır, sonra kaybolur. Bu ayarı commit etmeden kapat.
4. Commit: `Assets/_CubeHaul/Scenes/GameScene.unity`, `Assets/_CubeHaul/Art/Materials/Cube.mat` (`.meta` dosyasıyla).
