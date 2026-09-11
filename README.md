# SmallConvert

SmallConvert is small, no-nonsense file converter run locally for your convenience. Drag files in, pick a target format, and you're done.
Runs on **Windows, macOS and Linux**.

## Supported conversions

| Input | → JPG | → PNG | → PDF | → Word (DOCX) |
|---|:---:|:---:|:---:|:---:|
| PNG, JPG, WebP, BMP, GIF, TIFF | ✓ | ✓ | ✓ | ✓ |
| SVG | ✓ | ✓ | ✓ | ✓ |
| PDF | – | – | – | ✓ * |
| Word (DOC/DOCX) | – | – | ✓ * | – |
| Markdown (MD) | – | – | ✓ * | ✓ * |

\* Note that these need a document engine: **Microsoft Word** (Windows) or **LibreOffice** (other).
SmallConvert will use whichever it finds; image conversions will work without either.

## Download

Grab the build for your system from the [Releases](../../releases) page:

| File | Platform | Notes |
|---|---|---|
| `SmallConvert-*-windows-x64.exe` | Windows 10/11 | Everything bundled, just run it |
| `SmallConvert-*-windows-x64-dotnet.exe` | Windows 10/11 | Small download; needs the free [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (Windows offers to install it on first start) |
| `SmallConvert-*-macos-arm64` | macOS, Apple Silicon | Zipped file, see macOS note below |
| `SmallConvert-*-macos-x64` | macOS, Intel | Zipped file, macOS note below |
| `SmallConvert-*-linux-x64` | Linux | Unzip, then run `chmod +x SmallConvert-*` |

**Windows:** SmartScreen may warn about an unknown publisher. Click "More info" → "Run anyway".

**macOS:** the download is a plain executable, not an `.app`. Unzip it, open Terminal in that folder and run
`chmod +x SmallConvert` and then `xattr -dr com.apple.quarantine .`
(removes the "unidentified developer" block for the executable and its libraries), then start it with `./SmallConvert` or by double-click.

The macOS and Linux archives contain the `SmallConvert` executable(the app itself) plus a few native library files (`.dylib` / `.so`). You'll need to keep those together in the same file for the program to run.

## Usage

1. First, choose your target format. (DOCX, PDF, PNG and JPG) 
2. Drag one or more files onto the drop area. The results will appear on the same folder as the original.



## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```
dotnet run          # run during development, on any platform
./publish.sh        # macOS/Linux: build all release files into dist/
publish.cmd         # Windows:     same
```

## Libraries used

- [Avalonia UI](https://avaloniaui.net/) – UI
- [Magick.NET](https://github.com/dlemstra/Magick.NET) – image decoding/encoding
- [Svg.Skia](https://github.com/wieslawsoltes/Svg.Skia) – SVG rendering
- [Markdig](https://github.com/xoofx/markdig) – Markdown parsing
- [Open XML SDK](https://github.com/dotnet/Open-XML-SDK) – Word document generation
