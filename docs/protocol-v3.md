# Lumerist bridge protocol, version 3

Contract between the macOS companion and the Lightroom Classic plug-in.
Transport: loopback only (`127.0.0.1`); the plug-in
listens on **50550** (`LrSocket` "receive", app → plug-in) and **50551**
("send", plug-in → app). One UTF-8 JSON object per line, `\n`-terminated.
Encoders use sorted keys. Unknown keys are ignored by decoders.

A peer that receives another version rejects the message (the plug-in answers
with an `ack` whose `accepted` is false and whose message says "protocol 3
required"), so stale builds fail visibly.

Parameter ids come from `Shared/parameters.json` (generated into
`Parameters.lua` and `ParameterCatalogData.swift`). A request may only name a
parameter of the stated scope that exists in the catalog, and `|delta|` must
not exceed its `maxDelta`. Both sides validate.

## Requests (app → plug-in)

Every request has `"version": 3`, an integer `"id"` (assigned by the client,
increasing; echoed in the matching `ack`/`result`), and `"kind"`.

| kind | fields | effect |
| --- | --- | --- |
| `ping` | – | diagnostic, no catalog change |
| `adjust` | `scope` (`develop`/`library`), `parameter`, `delta` | develop: change the active photo through `LrDevelopController` (Develop module required), clamped to `getRange`; hue (`wraps`) parameters wrap around the range; `choice` (`enumerated`) parameters step through options (`delta` sign = direction); `lumerist.cropZoom` moves the four crop edges symmetrically by `delta` percent keeping the aspect ratio. library: relative change applied to every target photo (`catalog:getTargetPhotos()`) inside one `withWriteAccessDo` (Library module required) |
| `reset` | `scope`, `parameter` | develop: `LrDevelopController.resetToDefault(parameter)`; library: rejected |
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
| `event` | `name: "context"`, `module` (Lightroom module name), `tool` (`getSelectedTool()`, may be empty). Pushed after connect and whenever either changes (polled about every 0.15 s) |
| `result` | `id`, `name`, then for `context`: `module`, `tool`; for `presets`: `items: [{ "id": uuid, "name": …, "group": … }]`; for `catalog`: `unsupported: [parameter ids]` (develop parameters whose `getRange` fails in this Lightroom) |

Numbers are finite; non-finite values are omitted. A single line may be up to
256 KB (preset lists); the companion accepts lines up to 1 MB.

Merged adjustments: the plug-in still sums consecutive `adjust` requests for
the same scope + parameter before applying them, and answers with one `ack`
carrying the `id` of the last merged request.

## Plug-in notes (implementation, 0.9.0)

- Requests over 64 KB are rejected; malformed JSON gets an `ack` without `id`.
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
