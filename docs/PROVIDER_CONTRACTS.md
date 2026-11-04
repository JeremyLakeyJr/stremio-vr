# Provider response contracts

This document records the JSON shapes consumed by the Unity adapters. It is
an implementation contract, not a claim that every deployment exposes these
routes. Live service validation remains a release checklist item.

All collection responses must be valid JSON. Missing, null, malformed, or
non-success responses are treated as an empty result by the current adapters,
except authentication, which returns `false`.

## Shared output model

Adapters map provider responses to:

- `MediaCatalog`: `ProviderId`, `CatalogId`, `Name`, and `Type`
  (`movie` or `series`).
- `MediaItem`: `ProviderId`, `ItemId`, `Title`, `PosterUrl`, `Type`,
  `RuntimeTicks`, and `ResumePositionTicks`.
- `ResolvedStream`: `ProviderId`, `ItemId`, `Title`, `StreamUrl`,
  `SourceCodec`, optional `AudioTrackIndex`, optional subtitle URL/tracks,
  upstream headers, and `RequiresServerProxy`.

## Stremio add-ons

The configured URL is normalized to `/manifest.json`.

### Manifest

```json
{
  "name": "Addon name",
  "catalogs": [
    { "id": "catalog-id", "type": "movie", "name": "Movies" }
  ]
}
```

The adapter requests `/catalog/{type}/{id}.json`, or the
`/skip={offset}.json` form for pagination. The response is:

```json
{
  "metas": [
    { "id": "tt123", "type": "movie", "name": "Title", "poster": "https://..." }
  ]
}
```

Streams are requested from `/stream/{movie|series}/{id}.json`:

```json
{
  "streams": [
    { "name": "1080p", "title": "Direct", "url": "https://..." }
  ]
}
```

Entries without a `url` are ignored. Standard add-ons do not provide continue
watching data to this adapter.

## Jellyfin and Remux-compatible servers

Authentication is `POST /Users/AuthenticateByName` with:

```json
{ "Username": "user", "Pw": "password" }
```

The response must include `AccessToken` and `User.Id`. Catalogs are read from
`GET /Users/{userId}/Views`:

```json
{ "Items": [{ "Id": "library", "Name": "Movies", "CollectionType": "movies" }] }
```

Items and resume items use an `Items` array with `Id`, `Name`, `Type`,
`RunTimeTicks`, and optional `UserData.PlaybackPositionTicks`. Stream
resolution uses `POST /Items/{itemId}/PlaybackInfo?UserId={userId}` and expects
`MediaSources[]` with `Id`, `Name`, `Path`, `IsRemote`, and `MediaStreams[]`.
Video and subtitle stream entries use `Type`, `Codec`, `Index`, `Language`,
and optional `DisplayTitle`.

## MediaStorm

Requests use the `X-MediaStorm-Key` header. Authentication probes
`GET /api/v1/health`, with `/api/v1/catalogs` as fallback.

Catalogs:

```json
{ "Catalogs": [{ "Id": "movies", "Name": "Movies", "Type": "movie" }] }
```

Catalog, continue-watching, and search results use:

```json
{
  "Items": [
    {
      "Id": "item-1",
      "Title": "Title",
      "PosterUrl": "https://...",
      "Type": "movie",
      "RuntimeTicks": 36000000000,
      "ResumePositionTicks": 120000000
    }
  ]
}
```

Streams use:

```json
{
  "Streams": [
    {
      "Title": "Direct",
      "Url": "https://...",
      "Codec": "h264",
      "Quality": "1080p",
      "Debrid": "service",
      "Size": "4 GB",
      "SubtitleUrl": "https://...",
      "AudioTrackIndex": 0
    }
  ]
}
```

Progress is posted to `/api/v1/user/progress` with `ItemId`,
`PositionTicks`, and `IsPaused`.

## Silo

Token-authenticated requests use the configured authorization token. Login
uses `POST /api/auth/login` with `Username` and `Password`, returning:

```json
{ "Token": "token-value" }
```

Libraries use `{ "Libraries": [{ "Id": "library", "Name": "Movies", "Type": "movie" }] }`.
Items use the same `Items` shape as MediaStorm. Streams use the same fields
as MediaStorm except `Bitrate` replaces `Debrid` and `Size`.

Silo progress is posted to `/api/progress` with `ItemId`, `PositionTicks`,
and `IsPaused`. Continue watching is read from `/api/continue-watching`;
search is read from `/api/search?query=...&limit=32`.

## Mellon / Nuvio sync

`GET /api/addons` must return:

```json
{ "addons": ["https://addon.example/manifest.json"] }
```

Only non-empty URLs ending in `manifest.json` are registered. Invalid entries
are ignored and an unavailable endpoint produces an empty list.

## Contract test requirements

Fixture tests should cover one valid response, an empty array, malformed JSON,
HTTP authentication failure, HTTP 5xx, timeout/offline behavior, and fields
missing from otherwise valid JSON. Live-provider credentials must never be
committed to fixtures or CI.
