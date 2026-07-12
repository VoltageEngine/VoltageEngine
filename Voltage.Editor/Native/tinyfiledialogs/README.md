# tinyfiledialogs (vendored native)

Native OS file/folder dialogs for the Voltage Editor, via
[tinyfiledialogs](https://sourceforge.net/projects/tinyfiledialogs/) v3.21.3 (Zlib license — see the
banner at the top of `tinyfiledialogs.c`).

The editor P/Invokes this from `Voltage.Editor.FilePickers.NativeFileDialogs`. When the native library
is missing for the current platform, the editor **falls back to the built-in ImGui folder/file picker**,
so a missing native never breaks anything — you just don't get the OS-native dialog.

## Layout

```
tinyfiledialogs.c / .h        vendored upstream source (do not edit)
runtimes/<rid>/native/        prebuilt shared libraries, copied to the editor output by the .csproj
build-native.sh               builds the shared lib for the current macOS/Linux host
build-native.cmd              builds the DLL on Windows (needs MSVC `cl` or `clang`)
```

`runtimes/osx-x64` and `runtimes/osx-arm64` ship a prebuilt **universal** dylib. To produce the
Windows (`win-x64/native/tinyfiledialogs.dll`) and Linux (`linux-x64/native/libtinyfiledialogs.so`)
binaries, run the matching build script **on that OS** (they cannot be reliably cross-compiled) and
commit the result. Until then, those platforms use the ImGui fallback.

## Rebuilding

- **macOS / Linux:** `./build-native.sh`
- **Windows:** `build-native.cmd` from a Developer Command Prompt (or with `clang` on PATH)
