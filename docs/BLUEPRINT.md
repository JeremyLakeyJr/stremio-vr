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
