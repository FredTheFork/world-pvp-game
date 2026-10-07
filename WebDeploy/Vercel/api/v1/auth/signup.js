const { endpoint } = require('../../_lib/handler');
const { authFlow } = require('../../_lib/auth-flow');
module.exports = endpoint((req, res) => authFlow(req, res, 'signup'));
