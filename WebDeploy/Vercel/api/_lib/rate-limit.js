const crypto = require('node:crypto');
const { ApiError } = require('./errors');
const { clientIp } = require('./http');
const { rpc } = require('./supabase');

function digestSubject(value) {
  const secret = process.env.RATE_LIMIT_HMAC_SECRET;
  if (!secret || secret.length < 32) {
    throw new ApiError(503, 'BACKEND_NOT_CONFIGURED', 'The backend rate-limit secret is missing.');
  }
  return crypto.createHmac('sha256', secret).update(String(value)).digest('hex');
}

async function consume(subject, bucket, limit, windowSeconds) {
  let data;
  try {
    data = await rpc('consume_rate_limit', {
      p_subject_hash: digestSubject(subject),
      p_bucket: bucket,
      p_limit: limit,
      p_window_seconds: windowSeconds
    });
  } catch (error) {
    // Fail closed for abuse-sensitive writes if persistent rate limiting is unavailable.
    if (error instanceof ApiError) throw error;
    throw new ApiError(503, 'RATE_LIMIT_UNAVAILABLE', 'Request protection is temporarily unavailable.');
  }
  const row = Array.isArray(data) ? data[0] : data;
  if (!row || typeof row.allowed !== 'boolean') {
    throw new ApiError(503, 'RATE_LIMIT_UNAVAILABLE', 'Request protection is temporarily unavailable.');
  }
  if (!row.allowed) {
    const error = new ApiError(429, 'RATE_LIMITED', 'Too many requests. Wait briefly and try again.');
    error.retryAfterSeconds = Number.isFinite(row.retry_after_seconds)
      ? Math.max(1, row.retry_after_seconds)
      : windowSeconds;
    throw error;
  }
}

async function enforceRateLimit(req, options = {}) {
  const bucket = options.bucket || 'api';
  const limit = options.limit || 60;
  const windowSeconds = options.windowSeconds || 60;
  const ip = clientIp(req);
  await consume('ip:' + ip, bucket + '_ip', options.ipLimit || limit, options.ipWindowSeconds || windowSeconds);
  if (options.userId) {
    await consume('user:' + options.userId, bucket + '_user', options.userLimit || limit, options.userWindowSeconds || windowSeconds);
  }
  if (options.identity) {
    await consume('identity:' + options.identity, bucket + '_identity', options.identityLimit || limit, options.identityWindowSeconds || windowSeconds);
  }
}

module.exports = { digestSubject, enforceRateLimit };
