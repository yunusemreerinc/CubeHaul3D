# İlk Sahne, Kamera ve Ekran Yerleşimi (Görev 11)

Oyun sahnesi `Assets/_CubeHaul/Scenes/GameScene.unity`. Sahneyi Editor betiği kurar. Yerleşim kuralları motordan
bağımsız C# kodundadır ve 14 ekran boyutunda test edilir. Unity bileşenleri yalnızca bu hesabı uygular.

## 1. Kompozisyon

cube-hunt'ın 420 × 900 tasarım ekranı (`src/main.ts`) yukarıdan aşağıya aynen korunur:

| Bölge | Tasarım y | Unity'de | Nerede |
|---|---|---|---|
| Üst çubuk: yeniden başlat, önceki, başlık + zorluk çipi, sonraki, hız | 0–64 | UI (Canvas) | `GameSceneComposer`, `Top Bar` |
| Tahta (küp alanı, yol) | 64–452 | 3D dünya | `DesignLayout.Board`, `Field` |
| Rampa (tünel) | 478–514 | 3D dünya | `DesignLayout.Tunnel` |
| Slotlar (en fazla 6) | 540–606 | 3D dünya | `DesignLayout.SlotRect` |
| Tepsi (3 sıra görünür) | 636–808 | 3D dünya | `DesignLayout.BlockRect` |
| Alt çubuk: "+1 Slot" | 826–900 | UI (Canvas) | `Bottom Bar` |

"Your image" v1'de olmadığından "+1 Slot" alt çubukta ortalanır. Tasarım düzlemi dünyaya `WorldMapper` ile yatırılır:
`(x, y) → (x · 0,025; 0; −y · 0,025)` dünya birimi. Böylece 420 birimlik genişlik 10,5 dünya birimi olur.

## 2. Kamera

- **Ortografik**, X ekseninde **60°** eğik (90° = tam tepeden; `GameCameraRig.Pitch`, 30–90° ayarlanabilir), **dönüş
  (yaw) yok**. Izgara portre ekrana dik kalır ve hücreler kare görünür. Eğim yalnızca derinlik hissi verir.
- Yer düzlemi dikeyde `sin(60°) ≈ 0,87` kadar kısalır. Kasa ve kamyon gibi yerden yükselen şeyler için içeriğin üstüne
  `40 · cos(60°)` tasarım birimi pay bırakılır.
- `orthographicSize` ve kamera konumu her ekran için `ScreenLayout` ile hesaplanır. Dünya içeriği (tahtanın üstünden
  tepsinin altına, kenarlarda 6 birim pay) üst ve alt çubuk arasındaki bölgeye **sığacak en büyük boyda** ve ortalanarak
  yerleşir. Ekran boyutu ya da Safe Area değişince yeniden hesaplanır.
- 16:9'da içerik yüksekliğe göre sığar, yanlarda boşluk kalır. Uzun ekranlarda (19,5:9 ve üstü) genişliğe göre sığar,
  fazla yükseklik içeriğin üstüne ve altına eşit bölünür.

## 3. UI

| Ayar | Değer |
|---|---|
| Canvas | Screen Space - Overlay |
| CanvasScaler | Scale With Screen Size, referans **420 × 900**, Screen Match Mode **Expand** (420 × 900'lük referans her zaman ekrana sığar) |
| Safe Area | `Safe Area` nesnesi (`SafeAreaFitter`) `Screen.safeArea`'ya gerilir. Çentik, ada, yuvarlak köşe ve home indicator dışarıda kalır |
| Üst çubuk | Safe Area'nın tepesine yapışık, 64 birim. Sol düğmeler sol kenara, sağ düğmeler sağ kenara, başlık ortaya göre konumlanır. Geniş ekranda aralar açılır, hiçbiri üst üste binmez |
| Alt çubuk | Safe Area'nın dibine yapışık, 74 birim |
| EventSystem | Input System UI Input Module (proje yalnızca yeni Input System kullanıyor) |
| Yazı | Yer tutucu olarak yerleşik `LegacyRuntime.ttf`; gerçek UI görsel görevlerinde değişecek |

## 4. Sahneyi oluşturma

1. Unity projeyi açtığında `GameScene.unity` yoksa sahne **bir kez otomatik** oluşturulur (Console: "CubeHaul: … creating it").
   Kaydedilmemiş değişiklikli bir "Untitled" sahne açıksa otomatik oluşturma yapılmaz; menüyü kullanın.
2. Elle: **CubeHaul ▸ Setup ▸ Create Game Scene** (varsa üzerine yazmadan önce sorar), **Open Game Scene**.
3. Betik ayrıca `Assets/_CubeHaul/Levels/LevelLibrary.asset`'i (10 bölüm dosyası, oyun sırasıyla) oluşturur. Sahneyi
   Build Settings'te ilk sıraya koyar.
4. Oluşan dosyaları commit edin: `Assets/_CubeHaul/Scenes/` (+ `.meta`), `Assets/_CubeHaul/Levels/LevelLibrary.asset`
   (+ `.meta`), `ProjectSettings/EditorBuildSettings.asset`.

Sahne hiyerarşisi:

```text
Game
├─ Main Camera        Camera (Solid Color #f6efe4), AudioListener, GameCameraRig
├─ Directional Light
├─ World
│  ├─ Layout Preview  LayoutPreview: tahta ve rampa (yer tutucu kutular; slotları ve tepsiyi Görev 16'dan beri UI çizer)
│  ├─ Grid            GridPresenter: bölümün 3D küpleri (Görev 12, GRID_VIEW.md)
│  ├─ Trucks          TruckFleetView: havuzlanmış kamyonlar (Görev 14, TRUCKS.md)
│  └─ Road Debug      RoadDebugGizmos: kamyon rotaları gizmo olarak (Görev 13, ROADS.md)
├─ Game Loop          GameSessionDriver: oyun döngüsü, motor + kamyonlar (Görev 15, SESSION.md)
├─ UI                 Canvas, CanvasScaler, GraphicRaycaster, SessionControls (üst ve alt çubuk)
│  ├─ Play Area       PlayAreaView: dokunmatik tepsi ve slotlar (Görev 16, TRAY_UI.md)
│  ├─ Safe Area       SafeAreaFitter
│  │  ├─ Top Bar      Restart, Previous, Next, Speed, Title, Difficulty
│  │  └─ Bottom Bar   Add Slot
│  └─ Overlay         GameFlowView: başlangıç, kazanma ve tıkanma panelleri (Görev 17, GAME_FLOW.md)
└─ EventSystem        EventSystem, InputSystemUIInputModule
```

`LayoutPreview` geçici yer tutucudur. Geometrisini çalışırken üretir ve sahneye kaydetmez (`HideFlags.DontSave`).
Gerçek tahta, kasa ve kamyon görselleri geldiğinde kaldırılacak.

## 5. Doğrulama

**Otomatik testler:**

| Test | Nerede | Ne sınar |
|---|---|---|
| `ScreenLayoutTests` (53) | EditMode + `dotnet test` | 14 portre ekranında (720×1280 … 1440×3200, çentikli iPhone'lar, 20:9 / 21:9 / 22,5:9, iki tablet) taşma yok. Çubuklar Safe Area'da, içerik çubukların arasında. Sıra cube-hunt'taki gibi (çubuk > tahta > rampa > slotlar > tepsi > çubuk) ve ortalı. İçerik bölgeyi bir boyutta dolduruyor. 46×46'lık bir hücre en az 10 px. `DesignLayout`'un main.ts formülleriyle aynı olduğu da sınanıyor |
| `GameSceneLayoutTests` (22) | **PlayMode** | Sahne, Editor betiğiyle aynı kodla kurulup her ekran boyunda bir RenderTexture'a çiziliyor. Gerçek Unity kamerası ve Canvas'ıyla sınananlar: yer tutucu kutuların hepsi dünya bölgesinde; çubuklar ve düğmeler Safe Area'da ve kendi çubuklarında; düğmeler üst üste binmiyor; `CanvasScaler` ölçeği hesaplananla aynı; Unity kamerasının izdüşümü `ScreenLayout`'unkiyle 0,5 px içinde; ekran değişince yerleşim yenileniyor |

Ekranda 46×46'lık (en sık) bir hücrenin boyu:

| Ekran | Genişlik × derinlik |
|---|---|
| 16:9 720×1280 | 10,9 × 9,4 px |
| 16:9 1080×1920 | 16,3 × 14,2 px |
| 19,5:9 iPhone 13 1170×2532 | 18,9 × 16,4 px |
| 20:9 1080×2400 | 17,4 × 15,1 px |
| 4:3 iPad 1536×2048 | 17,4 × 15,1 px |

**Elle (Play Mode):**

1. **CubeHaul ▸ Setup ▸ Open Game Scene**, sonra Play.
2. Game penceresinin çözünürlük menüsünden sırayla **1080×1920 (16:9 Portrait)**, **1080×2400** ve **1170×2532** ekleyip
   seçin. Tahta üstte, slotlar ortada, tepsi altta görünmeli. Hiçbir şey çubukların altına girmemeli ya da ekrandan
   taşmamalı.
3. Çentik ve home indicator için Game penceresini **Simulator**'e çevirip bir iPhone seçin; üst ve alt çubuk Safe
   Area'nın içinde kalmalı.

## 6. Sınırlar

- Bu ortamda Unity Editor yok. Saf yerleşim kodu `dotnet test` ile koşturuldu. Unity bileşenleri (`GameCameraRig`,
  `LayoutPreview`, `SafeAreaFitter`, `GameSceneComposer`) Unity 2021.3 referans assembly'lerine karşı derleme
  denetiminden geçti; uGUI ve Input System tipleri bu denetimde taklit edildi. Editor betiği ve PlayMode testleri
  derlenemedi. Kesin doğrulama §5'teki Unity adımlarıdır.
- Yatay (landscape) yönlendirme hedef değil; proje portreye kilitli.
