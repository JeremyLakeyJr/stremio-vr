import http from 'node:http';
import https from 'node:https';
import { getConfig } from './config.js';

/**
 * Formats a notification payload depending on target service (Discord, Slack, or Generic/Apprise).
 */
export function formatNotificationPayload({ event, title, roomId, inviteUrl, clientCount } = {}, targetUrlStr = '') {
  const isDiscord = targetUrlStr.includes('discord.com');
  const isSlack = targetUrlStr.includes('hooks.slack.com');

  const eventTitles = {
    playback: `🎬 Now Playing: ${title || 'Movie'}`,
    playback_stop: `⏹️ Playback Ended in Room [${roomId || ''}]`,
    room_created: `🍿 New Movie Night Room [${roomId || ''}] is open!`,
    participant_joined: `👋 A friend joined Room [${roomId || ''}]`
  };

  const header = eventTitles[event] || `🍿 Movie Night Event: ${event}`;

  if (isDiscord) {
    const isPlayback = event === 'playback';
    const color = isPlayback ? 0x9667ff : (event === 'room_created' ? 0x78e8b4 : 0x4a90e2);

    return JSON.stringify({
      username: 'Movie Night VR',
      embeds: [{
        title: header,
        description: isPlayback
          ? `Watch **${title}** together in immersive VR cinema!`
          : `Room **[${roomId || 'N/A'}]** is ready for VR cinema party.`,
        color,
        fields: [
          ...(roomId ? [{ name: 'Room Code', value: `\`${roomId}\``, inline: true }] : []),
          ...(typeof clientCount === 'number' ? [{ name: 'Viewers', value: `${clientCount}/8`, inline: true }] : []),
          ...(inviteUrl ? [{ name: 'Join / Invite Link', value: inviteUrl, inline: false }] : [])
        ],
        footer: { text: 'Movie Night VR Cinema • Meta Quest & Desktop' },
        timestamp: new Date().toISOString()
      }]
    });
  }

  if (isSlack) {
    return JSON.stringify({
      text: header,
      blocks: [
        {
          type: 'section',
          text: {
            type: 'mrkdwn',
            text: `*${header}*\n${inviteUrl ? `<${inviteUrl}|Click here to Join>` : ''}`
          }
        }
      ]
    });
  }

  // Generic / Apprise / Home Assistant JSON format
  return JSON.stringify({
    title: header,
    body: inviteUrl ? `Room ${roomId || ''} is live at ${inviteUrl}` : `Room ${roomId || ''} updated`,
    type: 'info',
    roomId: roomId || undefined,
    event,
    inviteUrl: inviteUrl || undefined
  });
}

/**
 * Dispatch room and playback notifications to Discord, Slack, Telegram, or Apprise webhooks.
 * Fails silently so webhook network issues never impact room performance.
 */
export async function sendNotification({ event, title, roomId, inviteUrl, clientCount }) {
  const cfg = typeof getConfig === 'function' ? getConfig() : null;
  const webhookUrl = cfg?.webhookUrl ||
                     process.env.DISCORD_WEBHOOK_URL ||
                     process.env.SLACK_WEBHOOK_URL ||
                     process.env.APPRISE_URL ||
                     process.env.NOTIFICATION_WEBHOOK_URL;
  if (!webhookUrl) return;

  try {
    const url = new URL(webhookUrl);
    const payload = formatNotificationPayload({ event, title, roomId, inviteUrl, clientCount }, webhookUrl);

    return new Promise((resolve) => {
      const req = (url.protocol === 'https:' ? https : http).request(url, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Content-Length': Buffer.byteLength(payload)
        },
        timeout: 4000
      }, (res) => {
        res.resume();
        resolve(res.statusCode);
      });

      req.on('error', () => resolve(null));
      req.on('timeout', () => {
        req.destroy();
        resolve(null);
      });

      req.write(payload);
      req.end();
    });
  } catch {
    return null;
  }
}
