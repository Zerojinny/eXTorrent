# eXTorrent
.torrent to .exe

[한국어](README.ko.md) | **English**

eXTorrent packages a `.torrent` file into a **single self-extracting `.exe`**. Whoever runs the generated file does not need a torrent client: the exe unpacks `aria2c`, picks a download profile that matches the PC's network speed, downloads the torrent contents, and cleans up after itself.

## How it works

```
 .torrent file ──► eXTorrent (AutoIt GUI / CLI) ──► <torrent name>.exe
                                                        │  (run on the target PC)
                                                        ▼
                      extracts to %temp% ─► start.bat ─► aria2c ─► files in SAVEPATH
```

The generated exe is built as `7zS.sfx` + `config.txt` + `files.7z`. The 7z archive contains:

| File | Role |
|---|---|
| `<name>.torrent` | The torrent to download |
| `tfile.txt` | Name of the torrent file (read by `start.bat`) |
| `aria2c.exe` | Download engine (aria2) |
| `aria2.conf.100` | aria2 profile for 10/100 Mbps links and unknown speeds |
| `aria2.conf.1G` | aria2 profile for 1 Gbps or faster links |
| `getlinkspeed.bat` | Detects the active NIC link speed |
| `start.bat` | Main script run after extraction |

## Behavior of the generated exe (`start.bat`)

1. Requests **administrator rights** if not already elevated (needed for the firewall rule).
2. Reads `tfile.txt`, verifies the torrent exists, and creates `SAVEPATH` (default `C:\Temp`).
3. Runs `getlinkspeed.bat` to detect link speed (WMIC first, inline PowerShell as fallback; only connected physical adapters are considered, and the fastest one is used).
4. Chooses the profile: **Gbps → `aria2.conf.1G`**, anything else (100/10 Mbps or detection failure) → **`aria2.conf.100`**.
5. Adds a temporary inbound/outbound Windows Firewall rule (`eXTorrent_Temp`) for `aria2c.exe`.
6. Runs `aria2c.exe -T <torrent> --dir=<SAVEPATH> --conf-path=<profile>`.
7. Removes the firewall rule (also on failure).
8. On success: if `RunFirst` is empty, opens Explorer with the downloaded item selected; otherwise calls the script set in `RunFirst` as post-processing.

User-editable variables at the top of `start.bat`:

```bat
set "SAVEPATH=C:\Temp"
set "RunFirst="
set "FWRULE=eXTorrent_Temp"
```

### aria2 profiles

| Setting | `aria2.conf.100` | `aria2.conf.1G` |
|---|---|---|
| `bt-max-peers` | 30 | 100 |
| `max-upload-limit` | 8M | 0 (unlimited) |
| `disk-cache` | (default) | 128M |
| `bt-tracker-interval` | 60 | 30 |

Common to both: `file-allocation=none`, `seed-time=0` (no seeding after completion), `enable-dht=false`, `enable-peer-exchange=false`, `bt-enable-lpd=false`, `max-overall-download-limit=0` (unlimited).

## Using the builder (`eXTorrent.au3`)

### Required folder layout

```
eXTorrent.exe (compiled from eXTorrent_EN.au3)
default.ini
config.ini            (created when you save settings)
bin\
 ├─ 7z.exe
 ├─ 7zS.sfx
 ├─ aria2c.exe
 ├─ aria2.conf.100
 ├─ aria2.conf.1G
 ├─ getlinkspeed.bat
 └─ start.bat            (English version, shared by all builds)
```

`eXTorrent_EN.au3` is the English-language source of the builder (all GUI text, log messages and dialogs are in English). The `bin\start.bat` it packages is also the English version, so the generated exe prints English messages as well.

### GUI mode

Run `eXTorrent.exe`, or drag a `.torrent` file onto it.

1. Select the target `.torrent` file (drag & drop supported).
2. Choose the folder where the generated exe will be saved.
3. Edit `bin\start.bat` directly if needed, then press **Save start.bat**. When you close the window with unsaved changes, you are asked whether to save them (Yes / No / Cancel).
4. Edit the SFX configuration (`config.txt`).
5. Press **Start Creating Executable**. The output is `<torrent name>.exe` in the chosen folder, and progress is shown in the work log.

Extra buttons: **Load External INI** (load the config text and save path from another INI file) and **Save Current Settings to INI** (save the current config text and save path to `config.ini`). The **Select File** and **Select Path** buttons open the file and folder pickers.

### Command-line mode

```
eXTorrent.exe "C:\path\file.torrent" "D:\output" [/q]
```

* Builds without showing a window; exit code `0` = success, `1` = failure.
* Uses `bin\start.bat`, and the config from `config.ini` (falls back to `default.ini`).
* `/q` suppresses the result message box.

## Configuration files

`default.ini` / `config.ini`:

```ini
[config]
content=;!@Install@!UTF-8!\nTitle="eXTorrent"\nInstallPath="%temp%"\nRunProgram="start.bat"\n;!@InstallEnd@!
[savepath]
path=C:\Users\OWNER\Sync\eXTorrent3
```

* `[config] content` is the 7-Zip SFX configuration (`\n` stands for a line break). It extracts to `%temp%` and runs `start.bat`.
* `[savepath] path` is the default output folder of the builder. **Change `OWNER` to your own path.**

## Notes and limitations

* Windows only. The generated exe must be allowed to run elevated (UAC prompt appears).
* In `start.bat`, the "select in Explorer" step derives the item name from `tfile.txt` up to the first `.`; torrent names containing extra dots may not be selected correctly.
* Torrent names with special batch characters (`&`, `%`, `^`, etc.) may need care.
* Only use this for content you have the right to download and distribute.

## License

This project bundles third-party binaries:

* `7z.dll`: GNU LGPL (some code under the unRAR license restriction, BSD 3-clause and BSD 2-clause)
* `7z.exe`, `7zS.sfx`: GNU LGPL
* `aria2c.exe`: GNU GPL

See `license.txt` for details.
