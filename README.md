# BR-SteamPress / The Boxroom Press

BR-SteamPress turns recent public Steam news and ordinary RSS/Atom feeds into a
dated, readable magazine in BOXROOM. It discovers installed Steam games (or
uses an explicit AppID list), downloads configured feeds, renders
newspaper-style JPEG pages, packages them as a CBZ issue, and asks
BR-BookSystem to refresh its catalogue.

Each issue now receives a BOXROOM-styled magazine cover using the game's
burgundy, gold, and off-white palette. The date deterministically selects one
of six mascot poses and a matching editorial layout, so re-publishing the same
day remains stable while different issues vary naturally. The bundled Lilita
One and Yanone Kaffeesatz faces match BOXROOM's display and interface text.

The in-room publication is branded **The Boxroom Press**. The DLL keeps the
`BR_SteamPress` identity so upgrading from the original Steam-only build does
not leave a duplicate mod installed.

## Requirements

- MelonLoader 0.7+
- BR-MediaAPI 1.1.2+
- ModsPanel 2.6.2+
- BR-BookSystem 2.1.0+

## Use

1. Configure **BR-BookSystem > Book Folder Location**.
2. Install `BR_SteamPress.dll` in `BOXROOM/Mods`.
3. Start BOXROOM. SteamPress publishes today's issue in the background.
4. Open BR-BookSystem's Book Box after the catalogue refreshes.

If today's generated issue already exists, startup uses it without contacting
Steam again. **Publish Now** deliberately refreshes and replaces today's issue.
Startup publication waits until BOXROOM has loaded a scene and BR-BookSystem's
saved Book Folder Location is available; the early Melon initialization phase
does not produce a false "folder not configured" failure.

Open **Mods > Mod Settings > BR-SteamPress** to select explicit Steam AppIDs,
add semicolon-separated RSS/Atom feed URLs, change story/lookback limits,
disable Steam news or startup publishing, or press **Publish Now**. With no
AppIDs configured, SteamPress uses installed games from Steam's local
`appmanifest_*.acf` files.

Each issue receives a stable date-based `BookID`, `Type: Magazine`, a cover JPG,
and a CBZ file. Existing issues are never deleted automatically. Re-publishing
the current date only replaces a folder containing SteamPress's ownership
marker, so an unrelated user folder is never overwritten.

## Build

Copy `Directory.Build.user.props.example` to `Directory.Build.user.props`, edit
the BOXROOM path, then run:

```powershell
dotnet build -c Release -p:DeployToGame=false
```

To deploy the built DLL into a closed local BOXROOM installation, build with
`-p:DeployToGame=true`.

## Privacy and network behavior

SteamPress reads local Steam app manifests only to identify installed AppIDs and
display names. It sends those AppIDs to Steam's public ISteamNews endpoint and
requests only the RSS/Atom URLs entered in its settings. It does not read
account credentials, browser cookies, Steam login tokens, or the owned-games
API. XML DTDs are prohibited, article markup is stripped, and no remote script
or HTML is run.
