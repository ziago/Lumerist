# Lumerist bridge protocol, version 4

Contract between the Lumerist app (the companion, on macOS or Windows) and
the Lightroom Classic plug-in. Transport: loopback only (`127.0.0.1`); the
plug-in listens on **50550** (`LrSocket` "receive", app → plug-in) and
**50551** ("send", plug-in → app). One UTF-8 JSON object per line,
`\n`-terminated. Encoders use sorted keys. Unknown keys are ignored by
decoders.

A peer that receives another version rejects the message (the plug-in answers
with an `ack` whose `accepted` is false and whose message says "protocol 4
required"), so stale builds fail visibly.

Version 4 is version 3 plus the bridge token (below) and Lightroom's
interface language in the `context` event and result. Nothing else changed.

## The bridge token

`127.0.0.1` is shared by every account on the computer, and a web page can
post to it too. So every request carries `"token"`: 64 lowercase
hexadecimal characters (32 random bytes) that only the user's own account
can read.

- **Where.** macOS: `~/Library/Application Support/Lumerist/bridge-token`
  (0600). Windows: `%APPDATA%\Lumerist\bridge-token`, next to
  `plugin-link.json`. The file holds the token alone (surrounding whitespace
  is ignored).
- **Who writes it.** The app, at start, when the file is missing or does not
  hold a valid token: from the system's secure random generator, created
  private and renamed into place, so the plug-in never reads half of it.
  The same token is then used at every start (`BridgeToken.swift`,
  `BridgeToken.cs`).
- **Who reads it.** The plug-in (`BridgeToken.lua`), on its worker task. It
  reads the file again, at most once a second, after a request carried a
  token it did not know, so a token created after Lightroom started is
  picked up.
- **What the plug-in refuses.** It checks, in this order, that the line is a
  JSON object, that `version` is 4 and that `token` is the one in the file.
  When any of them fails it runs nothing, answers with a rejected `ack`
  (without an `id` for a wrong token, so the app never takes it for one of
  its own answers), and disconnects the sender: what that sender sends until
  the request socket reconnects is ignored. A web page's request therefore
  fails at its first line, `POST / HTTP/1.1`.
- **Not covered.** A process of the same user can read the file, and could
  already do anything that user can. The response port (50551) is not
  authenticated: whoever connects to it first receives the acks, the module
  name and the preset names (never photos or keystrokes), and a program that
  holds either port can keep the app from connecting. A refused sender is
  disconnected and the plug-in listens again about 10 s later (the same
  restart as when the app quits); while the app is connected, other
  connections wait and are not served. The token is never logged.

Parameter ids come from `Shared/parameters.json` (generated into
`Parameters.lua`, `ParameterCatalogData.swift` and
`ParameterCatalogData.g.cs`). A request may only name a parameter of the
stated scope that exists in the catalog, and `|delta|` must not exceed its
`maxDelta`. Both sides validate.

## Requests (app → plug-in)

Every request has `"version": 4`, the bridge token `"token"`, an integer
`"id"` (assigned by the client, increasing; echoed in the matching
`ack`/`result`), and `"kind"`.

| kind | fields | effect |
| --- | --- | --- |
| `ping` | – | diagnostic, no catalog change |
| `adjust` | `scope` (`develop`/`library`), `parameter`, `delta` | develop: change the active photo through `LrDevelopController` (Develop module required), clamped to `getRange`; hue (`wraps`) parameters wrap around the range; `choice` (`enumerated`) parameters step through options (`delta` sign = direction); `lumerist.cropZoom` moves the four crop edges symmetrically by `delta` percent keeping the aspect ratio. library: relative change applied to every target photo (`catalog:getTargetPhotos()`) inside one `withWriteAccessDo` (Library module required) |
| `reset` | `scope`, `parameter` | develop: `LrDevelopController.resetToDefault(parameter)` (`resetCrop()` for `lumerist.cropZoom`); library: rejected |
| `gesture` | `phase` (`begin`/`end`), `parameter` | develop: `startTracking(parameter)` / `stopTracking()` so one Dial Key gesture becomes one history step; optional, best effort |
| `command` | `name`, optional `argument` (string) | an allow-listed Lightroom action, see below |
| `query` | `name` (`context`, `presets`, `catalog`) | answered with a `result` |

### Command names (allow-list)

`undo`, `redo`, `autoTone`, `autoWhiteBalance`, `resetAll`,
`nextPhoto`, `previousPhoto`, `flagPick`, `flagReject`, `flagRemove`,
`rating` (argument `"0"`…`"5"`), `colorLabel` (argument `red`, `yellow`,
`green`, `blue`, `purple`, `none`), `switchModule` (argument
`library`/`develop`/`map`/`book`/`slideshow`/`print`/`web`),
`selectTool` (argument `loupe`/`crop`/`dust`/`redeye`/`masking`/`upright`),
`revealPanel` (argument = parameter id), `zoomToggle`, `showClipping`,
`createMask` / `addToMask` / `subtractFromMask` / `intersectWithMask`
(argument `"type/subtype"`, e.g. `aiSelection/subject`, `aiSelection/sky`,
`brush`, `gradient`, `radialGradient`, `rangeMask/luminance`),
`invertMask`, `toggleMaskOverlay`, `applyPreset` (argument = preset UUID;
develop: active photo; library: every target photo), `copySettings`,
`pasteSettings`.

A command the current module or SDK cannot perform is answered with
`accepted: false` and a short reason; the companion may then fall back to
sending Lightroom's own keyboard shortcut.

## Messages (plug-in → app)

| kind | fields |
| --- | --- |
| `ack` | `id`, `accepted`, `message` (≤ 1024 UTF-8 bytes), optional `parameter`, `scope`, `value`, `minimum`, `maximum`, `unit` (`EV`, `K`, `deg`, `px`, `%`, `pt`, `choice`) |
| `event` | `name: "context"`, `module` (Lightroom module name), `tool` (`getSelectedTool()`, may be empty), optional `language` (below). Pushed after connect and whenever module or tool changes (polled about every 0.15 s) |
| `result` | `id`, `name`, then for `context`: `module`, `tool`, optional `language`; for `presets`: `items: [{ "id": uuid, "name": …, "group": … }]` and `truncated: true` when the list was cut to fit the line; for `catalog`: `unsupported: [parameter ids]` (develop parameters whose `getRange` fails in this Lightroom) |

Numbers are finite; non-finite values are omitted. A single line may be up to
256 KB (preset lists); the companion accepts lines up to 1 MB.

`language` is Lightroom's interface language, which decides its keyboard
shortcuts: `LrLocalization.currentLanguage()` as Lightroom gives it ("en",
"it", "zh_cn"…). It is left out when the call fails or returns no text. It
cannot change while Lightroom runs (a new language needs a restart, and the
restart reconnects), so every push carries the same value. The apps write it
one way, lowercase with `_` before a region ("zh_CN" and "zh-cn" become
"zh_cn"), and treat anything that is not 2–3 letters plus an optional region
of 2–8 letters or digits, or that is not text, as unknown (nil) without
rejecting the line. Which shortcuts a code means is up to the app.

Merged adjustments: the plug-in sums consecutive `adjust` requests for
the same scope + parameter before applying them, and answers with one `ack`
carrying the `id` of the last merged request.

## Plug-in notes (implementation)

- Requests over 64 KB are rejected; malformed JSON gets an `ack` without `id`
  and disconnects the sender (see the bridge token above).
- Text a peer sent (an unknown parameter, a bad argument) reaches the
  plug-in's log shortened to 160 bytes, with control characters replaced, so
  a request can neither flood nor forge log lines.
- The `catalog` result also carries `skipped`: mask (`local_*`) parameters
  that could not be checked because no mask was selected. Run the query with
  a mask selected in the Masking panel to check them too.
- Mask parameters (`local_*`) act on the selected mask only: outside the
  Masking panel the ack says "open the Masking panel to adjust a mask"; with
  no mask selected (or when Lightroom reports no value) it says "select a
  mask first". They never fall back to the global slider. A gesture that
  began on a `local_*` parameter ends with `stopTracking(true)`.
- `choice` parameters (PostCropVignetteStyle) move one option per request in
  the sign of `delta`; the ack message names the option.
- The context event is checked every 0.15 s on the worker task, so a change
  reaches the companion within about 0.25 s; it is pushed right after either
  socket connects.
- Library adjustments use the process-2012 keys when the photo has them
  (`Exposure2012`, …), `Temperature`/`Tint` for raw files (Kelvin, 50 K per
  point) and `IncrementalTemperature`/`IncrementalTint` otherwise; one
  `withWriteAccessDo` per request batch.
