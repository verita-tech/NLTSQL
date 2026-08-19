/**
 * Cube runtime configuration.
 *
 * Security model
 * --------------
 * Cube is never reachable from the browser. The Blazor backend mints a
 * short-lived JWT (signed with CUBEJS_API_SECRET) that carries the
 * caller's tenant and the sites they may see. Cube verifies the
 * signature and `queryRewrite` then appends a mandatory filter to every
 * single query — including the ones Metabase sends through the SQL API.
 *
 * The rewrite is the enforcement point: a client cannot remove the
 * filter, because it is applied after the incoming query is parsed.
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

module.exports = {
  /**
   * Compiled schema and pre-aggregations are cached per app id, so the
   * id has to vary with anything that changes query results.
   */
  contextToAppId: ({ securityContext }) =>
    `nltsql_${securityContext?.tenant_id ?? 'anonymous'}`,

  contextToOrchestratorId: ({ securityContext }) =>
    `nltsql_${securityContext?.tenant_id ?? 'anonymous'}`,

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
