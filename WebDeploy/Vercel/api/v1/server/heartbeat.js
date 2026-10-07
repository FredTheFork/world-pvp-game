const { endpoint } = require('../../_lib/handler');
const { ApiError } = require('../../_lib/errors');
const { allowMethods, readJson, sendJson } = require('../../_lib/http');
const { requireGameServer } = require('../../_lib/server-auth');
const { rest } = require('../../_lib/supabase');
const { getMatchId } = require('../../_lib/domain');

module.exports = endpoint(async (req, res) => {
  if (!allowMethods(req, res, ['POST'])) return;
  requireGameServer(req);
  const body = await readJson(req, 4096);
  const matchId = getMatchId({ query: { id: body.matchId } });
  const instanceId = String(body.serverInstanceId || '');
  if (!/^[A-Za-z0-9_-]{1,128}$/.test(instanceId)) {
    throw new ApiError(400, 'INVALID_SERVER_INSTANCE', 'The game-server instance ID is invalid.');
  }
  const update = {
    server_instance_id: instanceId,
    server_heartbeat_at: new Date().toISOString(),
    updated_at: new Date().toISOString()
  };
  if (body.networkEndpoint !== undefined && body.networkEndpoint !== null) {
    const endpoint = body.networkEndpoint;
    if (!endpoint || typeof endpoint !== 'object' ||
        typeof endpoint.host !== 'string' || endpoint.host.length < 1 || endpoint.host.length > 255 ||
        !Number.isInteger(endpoint.port) || endpoint.port < 1 || endpoint.port > 65535 ||
        !['udp', 'wss', 'tcp'].includes(String(endpoint.transport || '').toLowerCase())) {
      throw new ApiError(400, 'INVALID_NETWORK_ENDPOINT', 'The game-server network endpoint is invalid.');
    }
    update.network_endpoint = {
      host: endpoint.host,
      port: endpoint.port,
      transport: String(endpoint.transport).toLowerCase()
    };
  }
  const rows = await rest(
    'matches?id=eq.' + encodeURIComponent(matchId) + '&state=not.in.(FINISHED,CLOSED)',
    { method: 'PATCH', body: update, prefer: 'return=representation' }
  );
  if (!Array.isArray(rows) || rows.length !== 1) {
    throw new ApiError(404, 'MATCH_NOT_ACTIVE', 'The match is not available for a server heartbeat.');
  }
  sendJson(res, 200, { matchId, state: rows[0].state, serverHeartbeatAt: rows[0].server_heartbeat_at });
});
