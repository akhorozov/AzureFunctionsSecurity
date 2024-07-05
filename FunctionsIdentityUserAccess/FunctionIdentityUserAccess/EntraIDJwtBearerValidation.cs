﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace FunctionIdentityUserAccess;

public class EntraIDJwtBearerValidation
{
    private IConfiguration _configuration;
    private ILogger _log;
    private const string scopeType = @"http://schemas.microsoft.com/identity/claims/scope";
    private ConfigurationManager<OpenIdConnectConfiguration> _configurationManager;
    private ClaimsPrincipal _claimsPrincipal;

    private string _wellKnownEndpoint = string.Empty;
    private string? _tenantId = string.Empty;
    private string? _audience = string.Empty;
    private string? _instance = string.Empty;
    private string _requiredScope = "access_as_user";

    public EntraIDJwtBearerValidation(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        _configuration = configuration;
        _log = loggerFactory.CreateLogger<EntraIDJwtBearerValidation>();

        _tenantId = _configuration["AzureAd:TenantId"];
        _audience = _configuration["AzureAd:ClientId"];
        _instance = _configuration["AzureAd:Instance"];

        if(_tenantId == null || _audience == null || _instance == null)
        {
            throw new ArgumentException("missing API configuration");
        }

        _wellKnownEndpoint = $"{_instance}{_tenantId}/v2.0/.well-known/openid-configuration";
    }

    public async Task<ClaimsPrincipal?> ValidateTokenAsync(string authorizationHeader)
    {
        if (string.IsNullOrEmpty(authorizationHeader))
        {
            return null;
        }

        if (!authorizationHeader.Contains("Bearer"))
        {
            return null;
        }

        var accessToken = authorizationHeader.Substring("Bearer ".Length);

        var oidcWellknownEndpoints = await GetOIDCWellknownConfiguration();

        var tokenValidator = new JwtSecurityTokenHandler();

        var validationParameters = new TokenValidationParameters
        {
            RequireSignedTokens = true,
            ValidAudience = _audience,
            ValidateAudience = true,
            ValidateIssuer = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            IssuerSigningKeys = oidcWellknownEndpoints.SigningKeys,
            ValidIssuer = oidcWellknownEndpoints.Issuer
        };

        try
        {
            SecurityToken securityToken;
            _claimsPrincipal = tokenValidator.ValidateToken(accessToken, validationParameters, out securityToken);

            if (IsScopeValid(_requiredScope))
            {
                return _claimsPrincipal;
            }

            return null;
        }
        catch (Exception ex)
        {
            _log.LogError(ex.ToString());
        }
        return null;
    }

    public string GetPreferredUserName()
    {
        string preferredUsername = string.Empty;
        var preferred_username = _claimsPrincipal.Claims.FirstOrDefault(t => t.Type == "preferred_username");
        if (preferred_username != null)
        {
            preferredUsername = preferred_username.Value;
        }

        return preferredUsername;
    }

    private async Task<OpenIdConnectConfiguration> GetOIDCWellknownConfiguration()
    {
        _log.LogDebug("Get OIDC well known endpoints {_wellKnownEndpoint}", _wellKnownEndpoint);
        _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
             _wellKnownEndpoint, new OpenIdConnectConfigurationRetriever());

        return await _configurationManager.GetConfigurationAsync();
    }

    private bool IsScopeValid(string scopeName)
    {
        if (_claimsPrincipal == null)
        {
            _log.LogWarning("Scope invalid {scopeName}", scopeName);
            return false;
        }

        var scopeClaim = _claimsPrincipal.HasClaim(x => x.Type == scopeType)
            ? _claimsPrincipal.Claims.First(x => x.Type == scopeType).Value
            : string.Empty;

        if (string.IsNullOrEmpty(scopeClaim))
        {
            _log.LogWarning("Scope invalid {scopeName}", scopeName);
            return false;
        }

        if (!scopeClaim.Equals(scopeName, StringComparison.OrdinalIgnoreCase))
        {
            _log.LogWarning("Scope invalid {scopeName}", scopeName);
            return false;
        }

        _log.LogDebug("Scope valid {scopeName}", scopeName);
        return true;
    }
}
