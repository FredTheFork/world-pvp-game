const { ApiError } = require('./errors');
const { bearerToken } = require('./http');
const { verifyAccessToken } = require('./supabase');
const { requireGameServer } = require('./server-auth');

async function requireUser(req) {
  const accessToken = bearerToken(req);
  if (!accessToken) throw new ApiError(401, 'AUTH_REQUIRED', 'Sign in to continue.');
  let user;
  try {
    user = await verifyAccessToken(accessToken);
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) throw error;
    throw new ApiError(401, 'INVALID_SESSION', 'Your session has expired. Sign in again.');
  }
  return { id: user.id, email: user.email || '', accessToken };
}

module.exports = { requireGameServer, requireUser };
