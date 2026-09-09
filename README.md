# SmallConvert

A small, no-nonsense file converter. Drag files in, pick a target format, done.
Runs on **Windows, macOS and Linux**.

## Supported conversions

| Input | → JPG | → PNG | → PDF | → Word (DOCX) |
|---|:---:|:---:|:---:|:---:|
| PNG, JPG, WebP, BMP, GIF, TIFF | ✓ | ✓ | ✓ | ✓ |
| SVG | ✓ | ✓ | ✓ | ✓ |
| PDF | – | – | – | ✓ * |
| Word (DOC/DOCX) | – | – | ✓ * | – |
| Markdown (MD) | – | – | ✓ * | ✓ * |

\* Needs a document engine: **Microsoft Word** (Windows) or **LibreOffice** (any platform).
SmallConvert uses whichever it finds; image conversions work without either.

## Download

Grab the build for your system from the [Releases](../../releases) page:

| File | Platform | Notes |
|---|---|---|
| `SmallConvert-*-windows-x64.exe` | Windows 10/11 | Everything bundled, just run it |
| `SmallConvert-*-windows-x64-dotnet.exe` | Windows 10/11 | Small download; needs the free [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (Windows offers to install it on first start) |
| `SmallConvert-*-macos-arm64` | macOS, Apple Silicon | See macOS note below |
| `SmallConvert-*-macos-x64` | macOS, Intel | See macOS note below |
| `SmallConvert-*-linux-x64` | Linux | Run `chmod +x SmallConvert-*` first |

**Windows:** SmartScreen may warn about an unknown publisher. Click "More info" → "Run anyway".

**macOS:** the download is a plain executable, not an `.app`. Open Terminal, run
`chmod +x SmallConvert-*-macos-*` and then `xattr -d com.apple.quarantine SmallConvert-*-macos-*`
(removes the "unidentified developer" block), then start it from Terminal or by double-click.

## Usage

1. Choose the target format.
2. Drag one or more files onto the drop area.

Converted files are saved next to the originals. Existing files are never overwritten – a `(1)` suffix is added instead. Transparent areas become white when converting to JPG.

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```
dotnet run          # run during development, on any platform
./publish.sh        # macOS/Linux: build all release files into dist/
publish.cmd         # Windows:     same
```

## Libraries used

- [Avalonia UI](https://avaloniaui.net/) – cross-platform desktop UI
- [Magick.NET](https://github.com/dlemstra/Magick.NET) – image decoding/encoding
- [Svg.Skia](https://github.com/wieslawsoltes/Svg.Skia) – SVG rendering
- [Markdig](https://github.com/xoofx/markdig) – Markdown parsing
- [Open XML SDK](https://github.com/dotnet/Open-XML-SDK) – Word document generation
