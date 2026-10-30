# stremio-vr design blueprint

Multiplayer VR cinema framework for Meta Quest. Built on Stremio core for media stream routing and add-on support.

Layers:
- Presentation (VR UI / avatar presence)
- Network (WebSocket signaling, room sync)
- Sync (stream state, co-watch timing)
- Render (OpenXR / Quest SDK output)

Character Customization:
- Avatar builder: head/body/skin tone, hair, outfit presets
- Accessories: glasses, hats, VR headset overlay
- Room-based visibility: only users in same session see customized avatars
- Data: local profile JSON saved per user, synced via room state on join

Implementation direction (2026-10-27):
- Native Quest APK required; Unity 6 with OpenXR selected.
- Node/WebSocket room server with authoritative host playback and reconnect grace.
- Initial native source implements video, room UI, avatar profiles/poses and PCM voice.
- Stremio integration uses the actual Rust core through the backend for stream routing and manifest validation; an external streaming service handles torrents/proxy headers.
- Custom R6 character meshes replace primitive avatars; customization includes a live preview. The menu follows the supplied charcoal panel/navigation reference.
- See ACCEPTANCE.md for goal-by-goal implementation and device-validation status.
