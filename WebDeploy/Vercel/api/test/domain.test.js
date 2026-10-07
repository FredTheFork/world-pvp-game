const test = require('node:test');
const assert = require('node:assert/strict');
const {
  canTransition,
  createInviteCode,
  createOpaqueToken,
  normalizeEmail,
  sha256Hex,
  validateAvatarUrl,
  validateMatchDraft,
  validatePassword,
  validateUsername
} = require('../_lib/domain');

 test('account input validators normalize email and enforce username/password limits', () => {
  assert.equal(normalizeEmail('  Player+1@Example.com '), 'player+1@example.com');
  assert.equal(normalizeEmail('invalid'), '');
  assert.equal(validateUsername('Player_01'), 'Player_01');
  assert.throws(() => validateUsername('x'), { code: 'INVALID_USERNAME' });
  assert.equal(validatePassword('long-enough-pass'), 'long-enough-pass');
  assert.throws(() => validatePassword('short'), { code: 'INVALID_PASSWORD' });
});

test('avatar URLs require HTTPS and valid match drafts use bounded WGS84/radius/player values', () => {
  assert.equal(validateAvatarUrl('https://cdn.example.com/avatar.webp'), 'https://cdn.example.com/avatar.webp');
  assert.throws(() => validateAvatarUrl('http://127.0.0.1/private'), { code: 'INVALID_AVATAR_URL' });
  assert.deepEqual(validateMatchDraft({
    centre: { latitude: 51.5, longitude: -0.1, altitudeMeters: 0 },
    radiusMeters: 500,
    maximumPlayers: 8
  }), { latitude: 51.5, longitude: -0.1, altitude: 0, radiusMeters: 500, maximumPlayers: 8 });
  assert.throws(() => validateMatchDraft({ centre: { latitude: 91, longitude: 0, altitudeMeters: 0 }, radiusMeters: 1, maximumPlayers: 2 }), { code: 'INVALID_LOCATION' });
  assert.throws(() => validateMatchDraft({ centre: { latitude: 51, longitude: 0, altitudeMeters: 0 }, radiusMeters: 2001, maximumPlayers: 2 }), { code: 'INVALID_RADIUS' });
});

test('match invites and session credentials are opaque, high entropy, and hashed before storage', () => {
  const invite = createInviteCode();
  const ticket = createOpaqueToken();
  assert.match(invite, /^[A-Z2-9]{25,27}$/);
  assert.match(ticket, /^[A-Za-z0-9_-]{40,50}$/);
  assert.match(sha256Hex(invite), /^[0-9a-f]{64}$/);
  assert.notEqual(sha256Hex(invite), invite);
});

test('match lifecycle transitions are strictly forward and skip-free', () => {
  assert.equal(canTransition('CREATING', 'LOBBY'), true);
  assert.equal(canTransition('LOBBY', 'LOADING'), true);
  assert.equal(canTransition('LOADING', 'COUNTDOWN'), true);
  assert.equal(canTransition('COUNTDOWN', 'LIVE'), true);
  assert.equal(canTransition('LIVE', 'FINISHED'), true);
  assert.equal(canTransition('FINISHED', 'CLOSED'), true);
  assert.equal(canTransition('CREATING', 'LIVE'), false);
  assert.equal(canTransition('LIVE', 'LOBBY'), false);
  assert.equal(canTransition('CLOSED', 'LOBBY'), false);
});
