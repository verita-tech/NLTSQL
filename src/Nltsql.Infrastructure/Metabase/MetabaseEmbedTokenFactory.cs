using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Nltsql.Infrastructure.Configuration;

namespace Nltsql.Infrastructure.Metabase;

/// <summary>
/// Signs Metabase static-embed tokens.
/// </summary>
/// <remarks>
/// Static embedding is the right fit here: the iframe URL is generated
/// server-side and signed with the embedding secret, so the browser
/// never holds a Metabase session and cannot widen the question it is
/// allowed to see.
/// </remarks>
public sealed class MetabaseEmbedTokenFactory(IOptions<MetabaseOptions> options, TimeProvider timeProvider)
{
    private readonly MetabaseOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new();

    public string CreateQuestionToken(int cardId)
    {
        if (string.IsNullOrWhiteSpace(_options.EmbeddingSecretKey))
        {
            throw new InvalidOperationException(
                "Metabase:EmbeddingSecretKey ist nicht gesetzt; ein Embed-Token kann nicht signiert werden.");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.EmbeddingSecretKey));
        var now = timeProvider.GetUtcNow().UtcDateTime;

        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object>
            {
                ["resource"] = new Dictionary<string, object> { ["question"] = cardId },

                // Locked parameters would go here. The prototype embeds
                // the whole question, because the tenant filter is already
                // enforced by Cube on the connection Metabase reads through.
                ["params"] = new Dictionary<string, object>(),
            },
            Expires = now.Add(_options.EmbedTokenLifetime),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>Builds the iframe URL for a question, chrome switched off.</summary>
    /// <remarks>
    /// The frame is made transparent with <c>background=false</c> rather
    /// than the older <c>theme=transparent</c>. Both are still accepted,
    /// but background and colour scheme are separate switches now, and
    /// leaving <c>theme</c> unset is what lets the embedded question follow
    /// the surrounding page instead of pinning itself to one look.
    /// </remarks>
    public string CreateQuestionEmbedUrl(int cardId)
    {
        var token = CreateQuestionToken(cardId);

        return $"{_options.EffectivePublicUrl.TrimEnd('/')}/embed/question/{token}" +
               "#background=false&bordered=false&titled=false";
    }
}
