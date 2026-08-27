/**
 * Checks the access-control logic in cube.js without a running Cube.
 *
 * `checkSqlAuth` and `queryRewrite` are the two functions that decide what
 * a caller may see, and both are plain functions of their inputs — so the
 * part worth guarding is testable on its own. What this does not prove is
 * that Cube calls them, or calls them with the arguments assumed here;
 * that needs a running instance.
 *
 * Run with:  node infra/cube/cube.test.js
 */

const assert = require('node:assert/strict');
const { test } = require('node:test');

/** Every variable cube.js reads, so each test starts from a clean slate. */
const CUBE_VARS = [
  'CUBEJS_SQL_USER',
  'CUBEJS_SQL_PASSWORD',
  'CUBEJS_SQL_TENANT_ID',
  'CUBEJS_SQL_ALLOWED_SITES',
];

/**
 * Loads cube.js with a specific environment.
 *
 * The environment stays set after this returns, because `checkSqlAuth`
 * reads `process.env` when it is called rather than when the module is
 * loaded — which is what lets an operator change the tenant without
 * rebuilding an image.
 */
function load(env) {
  for (const name of CUBE_VARS) {
    delete process.env[name];
  }
  Object.assign(process.env, env);

  delete require.cache[require.resolve('./cube.js')];
  return require('./cube.js');
}

const CREDENTIALS = {
  CUBEJS_SQL_USER: 'metabase',
  CUBEJS_SQL_PASSWORD: 'secret',
};

test('checkSqlAuth rejects a wrong password', () => {
  const config = load(CREDENTIALS);

  assert.throws(
    () => config.checkSqlAuth({}, 'metabase', 'wrong'),
    /unknown SQL user/,
  );
});

test('checkSqlAuth rejects an unknown user', () => {
  const config = load(CREDENTIALS);

  assert.throws(
    () => config.checkSqlAuth({}, 'someone-else', 'secret'),
    /unknown SQL user/,
  );
});

test('checkSqlAuth refuses to run without configured credentials', () => {
  const config = load({ CUBEJS_SQL_USER: '', CUBEJS_SQL_PASSWORD: '' });

  assert.throws(
    () => config.checkSqlAuth({}, 'metabase', 'secret'),
    /not configured/,
  );
});

test('checkSqlAuth yields a tenant, which is what queryRewrite requires', () => {
  const config = load(CREDENTIALS);
  const { password, securityContext } = config.checkSqlAuth({}, 'metabase', 'secret');

  assert.equal(password, 'secret');
  assert.equal(securityContext.tenant_id, 'demo');
  assert.deepEqual(securityContext.allowed_sites, []);

  // The regression this whole function exists to prevent.
  assert.doesNotThrow(() =>
    config.queryRewrite({ measures: ['fertigung.oee'] }, { securityContext }));
});

test('checkSqlAuth carries the configured tenant and sites', () => {
  const config = load({
    ...CREDENTIALS,
    CUBEJS_SQL_TENANT_ID: 'kunde-a',
    CUBEJS_SQL_ALLOWED_SITES: 'Werk Nord, Werk Süd',
  });

  const { securityContext } = config.checkSqlAuth({}, 'metabase', 'secret');

  assert.equal(securityContext.tenant_id, 'kunde-a');
  assert.deepEqual(securityContext.allowed_sites, ['Werk Nord', 'Werk Süd']);
});

test('queryRewrite refuses a context without a tenant', () => {
  const config = load(CREDENTIALS);

  assert.throws(
    () => config.queryRewrite({ measures: ['fertigung.oee'] }, { securityContext: {} }),
    /no tenant_id/,
  );
});

test('queryRewrite appends the site filter per referenced view', () => {
  const config = load(CREDENTIALS);

  const query = config.queryRewrite(
    { measures: ['fertigung.oee'], dimensions: ['fertigung.machine_name'] },
    { securityContext: { tenant_id: 'kunde-a', allowed_sites: ['Werk Nord'] } },
  );

  assert.deepEqual(query.filters, [
    { member: 'fertigung.site_name', operator: 'equals', values: ['Werk Nord'] },
  ]);
});

test('queryRewrite leaves an unrestricted context alone', () => {
  const config = load(CREDENTIALS);

  const query = config.queryRewrite(
    { measures: ['fertigung.oee'] },
    { securityContext: { tenant_id: 'kunde-a', allowed_sites: [] } },
  );

  assert.equal(query.filters, undefined);
});

test('queryRewrite rejects a view that is not exposed', () => {
  const config = load(CREDENTIALS);

  assert.throws(
    () => config.queryRewrite(
      { measures: ['production_runs.count'] },
      { securityContext: { tenant_id: 'kunde-a', allowed_sites: ['Werk Nord'] } },
    ),
    /is not exposed/,
  );
});

test('contextToAppId partitions the cache per tenant', () => {
  const config = load(CREDENTIALS);

  assert.equal(config.contextToAppId({ securityContext: { tenant_id: 'a' } }), 'nltsql_a');
  assert.notEqual(
    config.contextToAppId({ securityContext: { tenant_id: 'a' } }),
    config.contextToAppId({ securityContext: { tenant_id: 'b' } }),
  );
});
