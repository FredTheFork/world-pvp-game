const { endpoint } = require('../_lib/handler');
const { allowMethods, sendJson } = require('../_lib/http');

module.exports = endpoint(async (req, res) => {
  if (!allowMethods(req, res, ['GET'])) return;
  // Liveness only: does not reveal environment configuration, credentials, account IDs, or DB details.
  sendJson(res, 200, { status: 'ok', service: 'world-pvp-control-plane' });
});
