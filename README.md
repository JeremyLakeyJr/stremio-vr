# stremio-vr — Multiplayer VR Cinema on Meta Quest

Open-source multiplayer VR extension of Stremio for Meta Quest headsets.

## Authors / Credits
- Creator: Jeremy Lakey (Owasso HS, class of '27)
- Based on Stremio (Stremio LLC) — media streaming framework
- Meta Quest / OpenXR SDK — VR multiplayer backbone
- Community contributors welcome

## License
MIT — see LICENSE

## Structure
- src/ — Unity / Unreal VR project source (Quest SDK hooks)
- server/ — multiplayer signaling + sync server (Node/WebSocket)
- docs/ — design blueprint and sourcing guide

## Design Goals
Multiplayer co-watching in VR: shared Stremio streams with voice, avatar presence, and room-based sessions.
