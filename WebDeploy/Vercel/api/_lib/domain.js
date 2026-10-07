const crypto = require('node:crypto');
const { ApiError } = require('./errors');

const MATCH_STATES = Object.freeze(['CREATING', 'LOBBY', 'LOADING', 'COUNTDOWN', 'LIVE', 'FINISHED', 'CLOSED']);
const NEXT_STATES = Object.freeze({
  CREATING: ['LOBBY', 'CLOSED'],
  LOBBY: ['LOADING', 'CLOSED'],
  LOADING: ['COUNTDOWN', 'CLOSED'],
  COUNTDOWN: ['LIVE', 'CLOSED'],
  LIVE: ['FINISHED', 'CLOSED'],
  FINISHED: ['CLOSED'],
  CLOSED: []
});
const INVITE_ALPHABET = '23456789ABCDEFGHJKLMNPQRSTUVWXYZ';

function normalizeEmail(value) {
  if (typeof value !== 'string') return '';
  const email = value.trim().toLowerCase();
  if (email.length < 3 || email.length > 254 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) return '';
  return email;
}

function validateUsername(value) {
  if (typeof value !== 'string' || !/^[A-Za-z0-9_]{3,24}$/.test(value)) {
    throw new ApiError(400, 'INVALID_USERNAME', 'Username must be 3–24 letters, numbers, or underscores.');
  }
  return value;
}

function validatePassword(value) {
  if (typeof value !== 'string' || value.length < 10 || value.length > 128) {
    throw new ApiError(400, 'INVALID_PASSWORD', 'Password must be between 10 and 128 characters.');
  }
  return value;
}

function validateMatchDraft(body) {
  const centre = body && body.centre;
  const latitude = Number(centre && centre.latitude);
  const longitude = Number(centre && centre.longitude);
  const altitude = Number(centre && centre.altitudeMeters);
  const radiusMeters = Number(body && body.radiusMeters);
  const maximumPlayers = Number(body && body.maximumPlayers);
  if (!Number.isFinite(latitude) || latitude < -90 || latitude > 90 ||
      !Number.isFinite(longitude) || longitude < -180 || longitude > 180 ||
      !Number.isFinite(altitude) || altitude < -12000 || altitude > 100000) {
    throw new ApiError(400, 'INVALID_LOCATION', 'Enter a valid WGS84 centre and ellipsoid altitude.');
  }
  if (!Number.isFinite(radiusMeters) || radiusMeters <= 0 || radiusMeters > 2000) {
    throw new ApiError(400, 'INVALID_RADIUS', 'Match radius must be greater than zero and no more than 2,000 metres.');
  }
  if (!Number.isInteger(maximumPlayers) || maximumPlayers < 2 || maximumPlayers > 32) {
    throw new ApiError(400, 'INVALID_PLAYER_LIMIT', 'Maximum players must be between 2 and 32.');
  }
  return { latitude, longitude, altitude, radiusMeters, maximumPlayers };
}

function validateAvatarUrl(value) {
  if (value === null || value === '') return null;
  if (typeof value !== 'string' || value.length > 512) {
    throw new ApiError(400, 'INVALID_AVATAR_URL', 'Avatar URL must be an HTTPS URL no longer than 512 characters.');
  }
  let parsed;
  try { parsed = new URL(value); } catch (_) {
    throw new ApiError(400, 'INVALID_AVATAR_URL', 'Avatar URL must be a valid HTTPS URL.');
  }
  if (parsed.protocol !== 'https:' || parsed.username || parsed.password ||
      parsed.hostname === 'localhost' || parsed.hostname.endsWith('.local')) {
    throw new ApiError(400, 'INVALID_AVATAR_URL', 'Avatar URL must be a public HTTPS URL.');
  }
  return parsed.toString();
}

function createInviteCode(byteCount = 16) {
  const bytes = crypto.randomBytes(byteCount);
  let bits = 0;
  let value = 0;
  let result = '';
  for (const byte of bytes) {
    value = (value << 8) | byte;
    bits += 8;
    while (bits >= 5) {
      result += INVITE_ALPHABET[(value >>> (bits - 5)) & 31];
      bits -= 5;
    }
  }
  if (bits > 0) result += INVITE_ALPHABET[(value << (5 - bits)) & 31];
  return result;
}

function sha256Hex(value) {
  return crypto.createHash('sha256').update(String(value), 'utf8').digest('hex');
}

function createOpaqueToken(byteCount = 32) {
  return crypto.randomBytes(byteCount).toString('base64url');
}

function canTransition(from, to) {
  return MATCH_STATES.includes(from) && NEXT_STATES[from].includes(to);
}

function getMatchId(req) {
  const value = req.query && (req.query.id || req.query.matchId);
  const id = Array.isArray(value) ? value[0] : value;
  if (typeof id !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(id)) {
    throw new ApiError(400, 'INVALID_MATCH_ID', 'A valid match ID is required.');
  }
  return id;
}

module.exports = {
  MATCH_STATES,
  NEXT_STATES,
  canTransition,
  createInviteCode,
  createOpaqueToken,
  getMatchId,
  normalizeEmail,
  sha256Hex,
  validateAvatarUrl,
  validateMatchDraft,
  validatePassword,
  validateUsername
};
