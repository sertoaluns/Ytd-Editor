# YTD Optimizer & Editor (YTD Editor)

**GTA5 & FiveM Clothing Texture & Patch Optimization Studio**  
*Native C# / Windows Forms High-Performance Desktop Application*

[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8%20(x64)-blue.svg)](https://dotnet.microsoft.com/)
[![Language](https://img.shields.io/badge/Language-C%23%20WinForms-brightgreen.svg)]()
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-lightgrey.svg)]()
[![License](https://img.shields.io/badge/License-MIT-orange.svg)](LICENSE)
[![Languages](https://img.shields.io/badge/Languages-EN%20%7C%20DE%20%7C%20TR-purple.svg)]()

---

<p align="center">
  <b>🌐 Trilingual Interface: English (EN) | Deutsch (DE) | Türkçe (TR)</b><br>
  <i>Switch languages dynamically in real-time from the top menu: <code>🌐 Language / Sprache / Dil</code></i>
</p>

---

## 📖 Languages / Sprachen / Diller

- [English](#-english)
- [Deutsch](#-deutsch)
- [Türkçe](#-türkçe)

---

## 🇬🇧 English

### Overview
**YTD Optimizer & Editor** is a specialized, high-performance desktop application designed for GTA5 and FiveM modders, server developers, and clothing artists. It simplifies the tedious task of removing unwanted badges, logos, patches, and text from clothing textures (`.ytd` files), creating transparent cutouts, fine-tuning texture alignments across multi-letter variants (`000_a`, `000_b`, `000_c`), and generating new clothing slots with one click.

### Key Features
1. **Vector Shape & Precision Selection Tools**:
   - **Rectangle / Square Tool (`R`)**: Drag to create sharp rectangular or square mask areas. Hold `Shift` to constrain proportions. Right-click drag to erase from mask.
   - **Circle / Ellipse Tool (`C`)**: Drag to create clean circular or oval mask areas. Hold `Shift` for a perfect circle.
   - **Triangle Tool (`T`)**: Drag to create sharp triangle cutout masks for angled patches and chevron insignia.
   - **Zero-Feather / Zero-Gradient Mode**: Strict nearest-neighbor rasterization (`Dilate = 0`, `Feather = 0`) prevents blurred borders and fuzzy gradient transitions.
   - **Brush (`B`) & Eraser (`E`)**: High-performance, zero-latency GDI+ dirty-rectangle drawing.
   - **Move & Fine-Tune Tool (`M`)**: Drag or use arrow keys (`Shift` for 10px steps) to adjust mask placement per variant.

2. **Transparent Cutout Mode (Alpha = 0)**:
   - Eliminates black box artifacts by setting the alpha channel to complete transparency (`Alpha = 0`) across selected areas, allowing full transparency in game.

3. **Lossless 4x4 DXT5 (BC3_UNORM) Block Preservation**:
   - Only encodes modified 4x4 pixel blocks inside the masked region.
   - Untouched blocks are preserved bit-for-bit from original DDS compressed stream, preventing compression degradation or quality loss.

4. **Multi-Variant Slot Grouping**:
   - Automatically detects and organizes slot families (`jbib_000_a`, `jbib_000_b`, etc.) into easy-to-navigate tabs.
   - Apply masks uniformly or offset individual variants with independent UV fine-tuning.

5. **Advanced Inpainting & Fill Modes**:
   - **Telea & Navier-Stokes**: Fast mathematical inpainting for seamless fabric pattern blending.
   - **Canvas Cloning**: Sample source texture coordinates and stamp onto masked areas.
   - **Surrounding Solid Average**: Matches background fabric color.
   - **Donor Variant Copy**: Clones clean areas from an alternate clean variant (`000_a` -> `000_b`).

6. **Mask Template Manager**:
   - Save frequently used masks (e.g., standard police shoulder patches, breast pockets, badges) to JSON with embedded preview thumbnails.
   - Import, export, duplicate, and batch apply templates across hundreds of YTD files in seconds.

7. **Save as New Slot**:
   - Automatically duplicates the active variant into the next available slot letter (e.g., `d`), with optional HSB (Hue, Saturation, Brightness) color shifting.

8. **Built-in Self-Diagnostics**:
   - One-click health check verifies CodeWalker integration, DXT5 block patching, alpha transparency, and format safety.

### Requirements & Setup
- **OS**: Windows 10 / Windows 11 (64-bit)
- **Runtime**: .NET Framework 4.8 or later
- **Dependencies**:
  - `CodeWalker.Core.dll` (point to your CodeWalker directory via *Settings*)
  - `texconv.exe` (DirectXTex) or `CompressonatorCLI.exe`

### Building from Source
Open `YTDEditor.csproj` in Visual Studio 2022 or build via Developer PowerShell:
```powershell
MSBuild.exe "YTDEditor.csproj" /p:Configuration=Release /p:Platform=x64 /v:minimal
```

---

## 🇩🇪 Deutsch

### Übersicht
**YTD Optimizer & Editor** ist eine spezialisierte Desktop-Anwendung für GTA5- und FiveM-Modder, Server-Entwickler und 3D-Künstler. Sie automatisiert das Entfernen von Abzeichen, Wappen, Logos und Texten von Kleidungs-Texturen (`.ytd`-Dateien), ermöglicht transparente Ausschnitte, präzise Varianten-Ausrichtungen (`000_a`, `000_b`, `000_c`) und das Erstellen neuer Bekleidungs-Slots mit einem Klick.

### Hauptfunktionen
1. **Vektorformen & Präzisionswerkzeuge**:
   - **Rechteck- / Quadrat-Werkzeug (`R`)**: Scharfe rechteckige Bereiche ziehen. `Shift` gedrückt halten für gleichseitige Quadrate. Rechtsklick zum Löschen aus der Maske.
   - **Kreis- / Ellipsen-Werkzeug (`C`)**: Perfekt für runde Abzeichen und Patches.
   - **Dreiecks-Werkzeug (`T`)**: Ideal für gewinkelte Ärmel-Patches und Chevrons.
   - **Scharfkantige Pixelränder**: Keine unerwünschten Weichzeichner oder Farbverläufe (`Dilate = 0`, `Feather = 0`).
   - **Pinsel (`B`) & Radierer (`E`)**: Extrem flüssiges Zeichnen ohne Mausverzögerung.
   - **Verschieben & Feinabstimmung (`M`)**: Maske pixelgenau mit Pfeiltasten oder Maus verschieben.

2. **Transparenter Ausschnitts-Modus (Alpha = 0)**:
   - Maskierte Bereiche werden vollständig transparent gesetzt (`Alpha = 0`) – verhindert schwarze Kanten oder fehlerhafte Farbblöcke im Spiel.

3. **Verlustfreie 4x4 DXT5 (BC3_UNORM) Block-Erhaltung**:
   - Nur veränderte 4x4-Pixel-Blöcke innerhalb der Maske werden neu komprimiert.
   - Unberührte Texturbereiche behalten ihre originalen komprimierten Bytes bei, wodurch Qualitätsverluste vermieden werden.

4. **Slot-Gruppierung & Variantenverwaltung**:
   - Erkennt zusammenhängende Kleidungs-Dateien (`000_a`, `000_b`, `000_c`) automatisch.
   - Masken können für die gesamte Familie oder mit individuellen Offsets pro Variante angewendet werden.

5. **Leistungsstarke Füllmethoden**:
   - **Inpainting (Telea & Navier-Stokes)**: Rekonstruiert Stofftexturen nahtlos.
   - **Klonen**: Texturstellen direkt auf der Leinwand als Quelle auswählen.
   - **Durchschnittsfarbe**: Füllt den Bereich mit der umgebenden Stofffarbe.
   - **Donor-Variante**: Kopiert saubere Texturpartien aus einer anderen Farbvariante.

6. **Maskenvorlagen-Manager**:
   - Speichern von Standardmasken (z.B. Polizei-Ärmelabzeichen, Brustabzeichen) als JSON mit Vorschaubild.
   - Import, Export und Stapelanwendung auf ganze Ordner mit hunderten von Dateien.

7. **Als neuen Slot speichern**:
   - Erstellt automatisch eine neue Variante mit dem nächsten freien Buchstaben (z.B. `d`) inklusive Farbton-, Sättigungs- und Helligkeitsanpassung (HSB).

8. **Integrierter Selbsttest**:
   - Prüft CodeWalker-Kompatibilität, DXT5-Encoding, Alpha-Kanäle und Block-Erhaltung.

---

## 🇹🇷 Türkçe

### Genel Bakış
**YTD Optimizer & Editor**, GTA5 ve FiveM sunucu geliştiricileri, kıyafet mod yapımcıları ve 3D tasarımcılar için geliştirilmiş yerel ve yüksek performanslı bir Windows masaüstü uygulamasıdır. Kıyafet dokularındaki (`.ytd`) istenmeyen polis armalarını, logoları, rütbe şeritlerini ve yazıları temizleme, şeffaf dekupe yapma, harfli varyantlar (`000_a`, `000_b`, `000_c`) arasında hizalama yapma ve tek tıkla yeni slot oluşturma süreçlerini kolaylaştırır.

### Öne Çıkan Özellikler
1. **Vektörel Şekil ve Seçim Araçları**:
   - **Kare / Dikdörtgen Aracı (`R`)**: Sürükleyerek keskin kare veya dikdörtgen temizleme alanı belirler. `Shift` basılıyken tam kare çizer. Sağ tık sürükle ile maskeden siler.
   - **Daire / Elips Aracı (`C`)**: Kol armaları ve yuvarlak logolar için keskin dairesel maske oluşturur.
   - **Üçgen Aracı (`T`)**: Açılı rütbeler ve üçgen logolar için vektörel keskin üçgen maske çizer.
   - **Sıfır Gradyant / Keskin Piksel Modu**: `Dilate = 0` ve `Feather = 0` ile pikseller etrafında istenmeyen geçiş/gradyant efekti oluşturmadan sadece hedeflenen alanı temizler.
   - **Fırça (`B`) ve Silgi (`E`)**: Sıfır fare gecikmesiyle (`LockBits` dirty-rectangle) akıcı çizim sağlar.
   - **Maske Taşıma ve Hizalama (`M`)**: Ok tuşları (`Shift` ile 10px) veya fareyle maskeyi varyantlar arasında kaydırır.

2. **Şeffaflaştır / Transparan Dekupe Modu (Alfa = 0)**:
   - Maskelenen alanı tamamen şeffaf (`Alfa = 0`) yaparak oyun içerisinde siyah kare veya leke kalmadan kusursuz şeffaflık sağlar.

3. **Kayıpsız 4x4 DXT5 (BC3_UNORM) Blok Koruma**:
   - Yalnızca maske içerisindeki 4x4 piksel bloklarını yeniden kodlar.
   - Maske dışındaki dokunulmayan tüm bloklar orijinal sıkıştırılmış baytlarıyla birebir korunur; görüntüde jenerasyon kaybı ve bozulma yaşanmaz.

4. **Slot Ailesi Gruplama ve Varyant Yönetimi**:
   - Klasördeki `000_a`, `000_b`, `000_c` gibi tüm varyantları otomatik eşleştirip sekmelere ayırır.
   - Normal (`_n`) ve Specular (`_s`) haritalarına da eşzamanlı maskeleme uygulayabilir.

5. **Gelişmiş Doldurma Yöntemleri**:
   - **Telea & Navier-Stokes**: Kumaş dokusunu matematiksel olarak kusursuz tamamlar.
   - **Tuvalden Klonlama**: Tuval üzerinden kaynak nokta seçerek doku kopyalama.
   - **Çevre Ortalaması Düz Renk**: Arka plan kumaş rengini otomatik hesaplar.
   - **Donor Kopyalama**: Logosu olmayan temiz bir varyanttan logolu varyanta doku aktarır.

6. **Maske Şablon Yöneticisi**:
   - Sık kullanılan armaları küçük önizleme resmiyle JSON formatında şablona kaydeder.
   - Şablonları dışa/içe aktarabilir, tek tıkla klasördeki yüzlerce YTD dosyasına toplu uygulayabilirsiniz.

7. **Yeni Slot Olarak Kaydet**:
   - Kaynak varyantı sıradaki boş harfe (örn. `d`) kopyalar; isteğe bağlı HSB (Renk, Doygunluk, Parlaklık) ayarıyla yeni renk varyantları üretir.

8. **Kendi Kendini Kontrol Et (Self-Diagnostics)**:
   - Tek tıkla CodeWalker, DXT5 encoder, alfa kanalı ve 4x4 blok korumasını otomatik doğrular.

---

## 🛠️ Build & Installation

### Requirements
- **Windows 10 / 11 (64-bit)**
- **.NET Framework 4.8+**
- Visual Studio 2022 (Community or higher) with .NET Desktop Development workload

### Compilation
```powershell
MSBuild.exe "YTDEditor.csproj" /p:Configuration=Release /p:Platform=x64 /v:minimal
```
The compiled binary will be generated at `bin\x64\Release\YTD_Editor.exe`.

---

## 📜 License
This project is licensed under the [CC0 1.0 Universal (Public Domain Dedication)](LICENSE) license.
