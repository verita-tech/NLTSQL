/**
 * Cube runtime configuration.
 *
 * Security model
 * --------------
 * Cube is never reachable from the browser. Two kinds of client reach it,
 * and each one has to arrive carrying a security context, because
 * `queryRewrite` refuses to run without one:
 *
 *   REST API  The Blazor backend mints a short-lived JWT (signed with
 *             CUBEJS_API_SECRET) that carries the caller's tenant and the
 *             sites they may see. Cube verifies the signature and puts the
 *             claims into the security context.
 *
 *   SQL API   Metabase connects over the Postgres wire protocol. There is
 *             no JWT on that path — the context comes from `checkSqlAuth`
 *             below. Without it Cube hands `queryRewrite` an empty context
 *             and every Metabase query fails.
 *
 * `queryRewrite` is the enforcement point for both: it appends a mandatory
 * filter after the incoming query is parsed, so a client cannot remove it.
 *
 * Known limit of the SQL API path
 * -------------------------------
 * Metabase authenticates as a single, static database user, so the whole
 * Metabase path resolves to a single tenant — the one named by
 * CUBEJS_SQL_TENANT_ID. That is fine for a prototype with one customer's
 * data, and wrong for shared operation. Making it real means one SQL user
 * per tenant plus `canSwitchSqlUser`; the filter logic below does not
 * change, only where the identity comes from.
 */

/** Views that clients are allowed to query, mapped to their site member. */
const SITE_MEMBER_BY_VIEW = {
  fertigung: 'fertigung.site_name',
  stillstaende: 'stillstaende.site_name',
};

/** Collects the view prefixes referenced anywhere in a query. */
function referencedViews(query) {
  const members = [
    ...(query.measures || []),
    ...(query.dimensions || []),
    ...(query.timeDimensions || []).map((td) => td.dimension),
    ...(query.filters || []).map((f) => f.member || f.dimension),
  ].filter(Boolean);

  return [...new Set(members.map((m) => String(m).split('.')[0]))];
}

/** Parses a comma-separated site list; empty means "all sites". */
function parseSites(value) {
  if (!value) {
    return [];
  }

  return String(value)
    .split(',')
    .map((s) => s.trim())
    .filter(Boolean);
}

module.exports = {
  /**
   * Compiled schema and pre-aggregations are cached per app id, so the
   * id has to vary with anything that changes query results.
   */
  contextToAppId: ({ securityContext }) =>
    `nltsql_${securityContext?.tenant_id ?? 'anonymous'}`,

  contextToOrchestratorId: ({ securityContext }) =>
    `nltsql_${securityContext?.tenant_id ?? 'anonymous'}`,

  /**
   * Authenticates a SQL API session and gives it a security context.
   *
   * Returning the expected password is how Cube's contract works: Cube
   * compares it against what the client sent. Returning a context here is
   * the whole reason this function exists — the default implementation
   * authenticates the user but leaves the context empty, which is exactly
   * the state `queryRewrite` rejects.
   */
  checkSqlAuth: (req, user, password) => {
    const expectedUser = process.env.CUBEJS_SQL_USER;
    const expectedPassword = process.env.CUBEJS_SQL_PASSWORD;

    if (!expectedUser || !expectedPassword) {
      throw new Error('Access denied: SQL API credentials are not configured.');
    }

    if (user !== expectedUser || password !== expectedPassword) {
      throw new Error('Access denied: unknown SQL user.');
    }

    return {
      password: expectedPassword,
      securityContext: {
        tenant_id: process.env.CUBEJS_SQL_TENANT_ID || 'demo',
        allowed_sites: parseSites(process.env.CUBEJS_SQL_ALLOWED_SITES),
      },
    };
  },

  queryRewrite: (query, { securityContext }) => {
    if (!securityContext || !securityContext.tenant_id) {
      throw new Error('Access denied: token carries no tenant_id.');
    }

    // `allowed_sites` absent means "all sites" (used by admin tokens).
    const allowedSites = securityContext.allowed_sites;
    if (!Array.isArray(allowedSites) || allowedSites.length === 0) {
      return query;
    }

    query.filters = query.filters || [];

    for (const view of referencedViews(query)) {
      const siteMember = SITE_MEMBER_BY_VIEW[view];
      if (!siteMember) {
        throw new Error(`Access denied: view "${view}" is not exposed.`);
      }

      query.filters.push({
        member: siteMember,
        operator: 'equals',
        values: allowedSites,
      });
    }

    return query;
  },
};
