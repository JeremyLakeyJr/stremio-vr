/** Shared wire contract for the Node server and native Unity client.
 * The previous joinRoom placeholder was removed: joining is asynchronous over /ws.
 * Runtime implementation: unity/Assets/Scripts/RoomClient.cs.
 */
export interface AvatarProfile {
  name: string;
  skin: string;
  color: string;
  head: 'round' | 'square';
  body: 'regular' | 'wide';
  hair: 'none' | 'short' | 'tall';
  outfit: 'casual' | 'formal' | 'sport';
  accessory: 'none' | 'glasses' | 'hat' | 'headset';
}
export interface PlaybackState {
  url: string;
  title: string;
  position: number;
  paused: boolean;
  updatedAt: number;
  revision: number;
}
export interface RoomSession {
  type: 'state';
  id: string;
  host: string;
  world: 'cinema' | 'home';
  players: { id: string; profile: AvatarProfile; seat: number; seatedSeat: number; pose?: Pose; connected: boolean; voice: boolean }[];
  playback: PlaybackState;
  serverTime: number;
}
export interface TrackedPose { position: [number, number, number]; rotation: [number, number, number] }
export type Pose = { head: TrackedPose; left?: TrackedPose; right?: TrackedPose };
export type ClientMessage =
  | { type: 'create'; profile?: Partial<AvatarProfile> }
  | { type: 'join'; roomId: string; token?: string; profile?: Partial<AvatarProfile> }
  | { type: 'seat'; seatIndex: number }
  | { type: 'world'; world: 'cinema' | 'home' }
  | { type: 'leave' }
  | { type: 'ping'; sentAt: number }
  | { type: 'profile'; profile: Partial<AvatarProfile> }
  | { type: 'playback'; url?: string; title?: string; paused?: boolean; position?: number }
  | { type: 'pose'; pose: Pose }
  | { type: 'voice'; enabled: boolean }
  | { type: 'audio'; pcm: string }
  | { type: 'signal'; target: string; data: unknown };
export type ServerMessage = RoomSession
  | { type: 'joined'; roomId: string; playerId: string; token: string }
  | { type: 'left' }
  | { type: 'pong'; sentAt: number; serverTime: number }
  | { type: 'error'; message: string }
  | { type: 'pose'; playerId: string; pose: Pose }
  | { type: 'audio'; playerId: string; pcm: string }
  | { type: 'signal'; from: string; data: unknown };
