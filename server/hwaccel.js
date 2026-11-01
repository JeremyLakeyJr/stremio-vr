import { spawn } from 'node:child_process';

export const CANDIDATE_ENCODERS = [
  {
    name: 'nvenc',
    encoder: 'h264_nvenc',
    initArgs: ['-hwaccel', 'cuda'],
    encodeArgs: (bitrate) => ['-c:v', 'h264_nvenc', '-preset', 'p4', '-b:v', bitrate, '-maxrate', bitrate, '-bufsize', '24M', '-pix_fmt', 'yuv420p'],
    scaleFilter: (maxH) => `scale=-2:'min(${maxH},ih)'`
  },
  {
    name: 'qsv',
    encoder: 'h264_qsv',
    initArgs: ['-hwaccel', 'qsv'],
    encodeArgs: (bitrate) => ['-c:v', 'h264_qsv', '-preset', 'faster', '-b:v', bitrate, '-maxrate', bitrate, '-bufsize', '24M'],
    scaleFilter: (maxH) => `scale=-2:'min(${maxH},ih)'`
  },
  {
    name: 'vaapi',
    encoder: 'h264_vaapi',
    initArgs: ['-vaapi_device', process.env.VAAPI_DEVICE || '/dev/dri/renderD128'],
    encodeArgs: (bitrate) => ['-c:v', 'h264_vaapi', '-b:v', bitrate, '-maxrate', bitrate, '-bufsize', '24M'],
    scaleFilter: (maxH) => `format=nv12,hwupload,scale_vaapi=w=-2:h='min(${maxH},ih)'`
  },
  {
    name: 'videotoolbox',
    encoder: 'h264_videotoolbox',
    initArgs: ['-hwaccel', 'videotoolbox'],
    encodeArgs: (bitrate) => ['-c:v', 'h264_videotoolbox', '-b:v', bitrate, '-maxrate', bitrate],
    scaleFilter: (maxH) => `scale=-2:'min(${maxH},ih)'`
  },
  {
    name: 'amf',
    encoder: 'h264_amf',
    initArgs: [],
    encodeArgs: (bitrate) => ['-c:v', 'h264_amf', '-quality', 'balanced', '-b:v', bitrate, '-maxrate', bitrate],
    scaleFilter: (maxH) => `scale=-2:'min(${maxH},ih)'`
  }
];

export function testEncoder(ffmpegBin, candidate) {
  return new Promise((resolve) => {
    let proc;
    try {
      const args = [
        '-hide_banner',
        '-loglevel', 'error',
        ...candidate.initArgs,
        '-f', 'lavfi', '-i', 'color=c=black:s=256x256:d=0.1',
        '-frames:v', '1',
        '-c:v', candidate.encoder,
        '-f', 'null', '-'
      ];
      proc = spawn(ffmpegBin, args, { stdio: 'ignore' });
    } catch {
      return resolve(false);
    }
    const timer = setTimeout(() => {
      try { proc.kill('SIGKILL'); } catch {}
      resolve(false);
    }, 2500);
    proc.on('close', (code) => {
      clearTimeout(timer);
      resolve(code === 0);
    });
    proc.on('error', () => {
      clearTimeout(timer);
      resolve(false);
    });
  });
}

export async function detectMediaCapabilities(ffmpegBin = process.env.FFMPEG_BINARY || 'ffmpeg') {
  for (const candidate of CANDIDATE_ENCODERS) {
    if (await testEncoder(ffmpegBin, candidate)) {
      console.log(`[cinema] Hardware acceleration active: ${candidate.encoder} — bypassing 720p/30fps limits.`);
      return {
        hwAccel: true,
        backend: candidate,
        maxHeight: Number(process.env.MEDIA_MAX_HEIGHT || 2160), // Unlock up to 4K
        maxFps: Number(process.env.MEDIA_MAX_FPS || 60), // Unlock 60fps
        videoBitrate: process.env.MEDIA_VIDEO_BITRATE || '15M',
        maxBytes: Number(process.env.MEDIA_MAX_BYTES || 25769803776), // 24 GiB cache
        maxJobs: Number(process.env.MEDIA_MAX_JOBS || 4)
      };
    }
  }
  return {
    hwAccel: false,
    backend: null,
    maxHeight: Number(process.env.MEDIA_MAX_HEIGHT || 720),
    maxFps: Number(process.env.MEDIA_MAX_FPS || 30),
    videoBitrate: process.env.MEDIA_VIDEO_BITRATE || '3500k',
    maxBytes: Number(process.env.MEDIA_MAX_BYTES || 8589934592), // 8 GiB default
    maxJobs: Number(process.env.MEDIA_MAX_JOBS || 2)
  };
}

export const ALLOWED_UPSTREAM_HEADERS = new Set([
  'authorization',
  'x-emby-token',
  'x-emby-authorization',
  'x-mediastorm-key',
  'cookie',
  'user-agent'
]);

export function sanitizeUpstreamHeaders(upstreamHeaders = {}) {
  const sanitized = {};
  if (!upstreamHeaders || typeof upstreamHeaders !== 'object') return sanitized;
  for (const [key, value] of Object.entries(upstreamHeaders)) {
    const lowerKey = key.toLowerCase();
    if (!ALLOWED_UPSTREAM_HEADERS.has(lowerKey)) continue;
    const cleanVal = String(value).replace(/[\r\n]/g, '').trim();
    if (cleanVal) sanitized[lowerKey] = cleanVal;
  }
  return sanitized;
}

export function formatUpstreamHeaders(upstreamHeaders = {}) {
  const sanitized = sanitizeUpstreamHeaders(upstreamHeaders);
  const lines = Object.entries(sanitized).map(([k, v]) => `${k}: ${v}\r\n`);
  return lines.length > 0 ? ['-headers', lines.join('')] : [];
}

export function buildTranscodeArgs({
  input,
  startTimeSeconds = 0,
  capabilities = {},
  sourceCodec = null,
  audioTrackIndex = null,
  upstreamHeaders = {},
  outputDir,
  threads = 2
}) {
  const headerArgs = formatUpstreamHeaders(upstreamHeaders);
  const seekArgs = startTimeSeconds > 0 ? ['-ss', String(startTimeSeconds)] : [];
  const canDirectCopy = sourceCodec === 'h264' && process.env.MEDIA_DISABLE_REMUX !== 'true';

  let videoArgs;
  let hwInitArgs = [];

  if (canDirectCopy) {
    videoArgs = ['-c:v', 'copy'];
  } else if (capabilities.hwAccel && capabilities.backend) {
    hwInitArgs = capabilities.backend.initArgs || [];
    videoArgs = [
      '-vf', capabilities.backend.scaleFilter(capabilities.maxHeight || 1080),
      '-r', String(capabilities.maxFps || 60),
      ...capabilities.backend.encodeArgs(capabilities.videoBitrate || '15M')
    ];
  } else {
    // CPU Fallback (720p / 30fps default cap)
    const maxHeight = capabilities.maxHeight || 720;
    const maxFps = capabilities.maxFps || 30;
    const bitrate = capabilities.videoBitrate || '3500k';
    videoArgs = [
      '-vf', `scale=w='min(1280,iw)':h='min(${maxHeight},ih)':force_original_aspect_ratio=decrease:force_divisible_by=2`,
      '-r', String(maxFps),
      '-c:v', 'libx264',
      '-preset', 'veryfast',
      '-profile:v', 'main',
      '-pix_fmt', 'yuv420p',
      '-crf', '21',
      '-maxrate', bitrate,
      '-bufsize', '10M',
      '-g', '60',
      '-keyint_min', '60',
      '-sc_threshold', '0'
    ];
  }

  const audioMap = (audioTrackIndex !== null && audioTrackIndex !== undefined)
    ? `0:a:${audioTrackIndex}?`
    : '0:a:0?';

  return [
    '-nostdin',
    '-hide_banner',
    '-loglevel', 'error',
    '-threads', String(threads),
    '-protocol_whitelist', 'http,tcp',
    '-format_whitelist', 'mov,matroska,webm,hls,mpegts,aac,mp3',
    ...hwInitArgs,
    ...headerArgs,
    ...seekArgs,
    '-i', input,
    '-map', '0:v:0',
    '-map', audioMap,
    '-sn',
    '-dn',
    ...videoArgs,
    '-c:a', 'aac',
    '-ac', '2',
    '-ar', '48000',
    '-b:a', '192k',
    '-f', 'hls',
    '-hls_time', '2',
    '-hls_list_size', '0',
    '-hls_playlist_type', 'event',
    '-hls_flags', 'independent_segments+temp_file',
    '-hls_segment_filename', `${outputDir}/segment%06d.ts`,
    `${outputDir}/index.m3u8`
  ];
}
