const { ApiError } = require('./errors');

function requireGameServer(req) {
  const expected = process.env.WORLD_PVP_SERVER_TOKEN;
  if (!expected || expected.length < 32) {
    throw new ApiError(503, 'SERVER_AUTH_NOT_CONFIGURED', 'Trusted game-server authentication is not configured.');
  }
  const header = req.headers && (req.headers.authorization || req.headers.Authorization);
  const supplied = typeof header === 'string' && header.startsWith('Bearer ')
    ? header.slice(7).trim()
    : '';
  const crypto = require('node:crypto');
  const a = Buffer.from(supplied, 'utf8');
  const b = Buffer.from(expected, 'utf8');
  if (a.length !== b.length || !crypto.timingSafeEqual(a, b)) {
    throw new ApiError(401, 'SERVER_AUTH_REQUIRED', 'A trusted game server is required.');
  }
}

module.exports = { requireGameServer };
