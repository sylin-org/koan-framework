# Authentication Guide

**Document Type**: Reference Documentation (REF)  
**Target Audience**: Developers, Security Engineers, AI Agents  
**Last Updated**: 2025-01-10  
**Framework Version**: v0.2.18+

---

## 🔐 Sora Framework Authentication Guide

This document provides comprehensive coverage of Sora's authentication capabilities, including OAuth 2.1, OIDC, SAML, and multi-provider authentication patterns.

---

## 🌟 Authentication Overview

Sora provides **enterprise-grade authentication** with support for multiple protocols and providers through a unified, developer-friendly API.

### Key Features

- **Multi-Protocol Support**: OAuth 2.1, OpenID Connect (OIDC), SAML 2.0
- **Provider Ecosystem**: Google, Microsoft, Discord, generic OIDC, SAML, and development TestProvider
- **Account Linking**: Users can link multiple identity providers to a single account
- **Security-First**: PKCE, state/nonce validation, secure defaults, rate limiting
- **Production Ready**: Secret management, production gating, audit trails
- **Zero Config Development**: TestProvider for local development with no external dependencies

### Architecture Philosophy

- **Composition over Configuration**: Providers supply intelligent defaults, you provide minimal credentials
- **Controllers Only**: All endpoints are controller-based for consistency and testability
- **Secure by Default**: All security best practices enabled out-of-the-box
- **Provider Independence**: Unified API regardless of underlying identity provider

---

## 🚀 Quick Start Examples

### 1. **Google OAuth (Minimal Setup)**

```json
{
  "Sora": {
    "Web": {
      "Auth": {
        "Providers": {
          "google": {
            "ClientId": "${GOOGLE_CLIENT_ID}",
            "ClientSecret": "${GOOGLE_CLIENT_SECRET}"
          }
        }
      }
    }
  }
}
```

**What you get automatically:**
- ✅ OIDC protocol configuration
- ✅ Google's discovery endpoints
- ✅ Default scopes (`openid`, `email`, `profile`)
- ✅ Google branding and icon
- ✅ PKCE and security headers
- ✅ Available at `/auth/google/challenge`

### 2. **Microsoft Entra ID**

```json
{
  "Sora": {
    "Web": {
      "Auth": {
        "Providers": {
          "microsoft": {
            "ClientId": "${MS_CLIENT_ID}",
            "ClientSecret": "${MS_CLIENT_SECRET}"
          }
        }
      }
    }
  }
}
```

**Advanced Microsoft setup:**
```json
{
  "Sora": {
    "Web": {
      "Auth": {
        "Providers": {
          "microsoft": {
            "ClientId": "${MS_CLIENT_ID}",
            "ClientSecret": "${MS_CLIENT_SECRET}",
            "Authority": "https://login.microsoftonline.com/{tenant-id}",
            "Scopes": ["openid", "profile", "email", "User.Read"]
          }
        }
      }
    }
  }
}
```

### 3. **Multi-Provider Setup**

```json
{
  "Sora": {
    "Web": {
      "Auth": {
        "ReturnUrl": {
          "DefaultPath": "/dashboard",
          "AllowList": ["/admin", "/profile"]
        },
        "RateLimit": {
          "ChallengesPerMinutePerIp": 10,
          "CallbackFailuresPer10MinPerIp": 5
        },
        "Providers": {
          "google": {
            "ClientId": "${GOOGLE_CLIENT_ID}",
            "ClientSecret": "${GOOGLE_CLIENT_SECRET}"
          },
          "microsoft": {
            "ClientId": "${MS_CLIENT_ID}",
            "ClientSecret": "${MS_CLIENT_SECRET}"
          },
          "discord": {
            "ClientId": "${DISCORD_CLIENT_ID}",
            "ClientSecret": "${DISCORD_CLIENT_SECRET}",
            "Scopes": ["identify", "email", "guilds"]
          },
          "corporate-saml": {
            "Type": "saml",
            "EntityId": "https://myapp.com/auth/corporate-saml/saml/metadata",
            "IdpMetadataUrl": "https://sso.mycompany.com/metadata",
            "DisplayName": "Corporate SSO",
            "AllowIdpInitiated": false
          }
        }
      }
    }
  }
}
```

---

## 🛡️ Built-in Security Features

### OAuth 2.1 & OIDC Security

- **PKCE (Proof Key for Code Exchange)**: Automatic protection against authorization code interception
- **State Parameter**: CSRF protection with cryptographically secure state tokens
- **Nonce Validation**: Replay attack prevention for ID tokens
- **Secure Cookies**: `HttpOnly`, `Secure`, `SameSite` attributes
- **Redirect URI Validation**: Strict callback path enforcement
- **Open Redirect Prevention**: Configurable return URL allowlists

### SAML 2.0 Security

- **Signature Validation**: XML signature verification for assertions
- **Issuer Validation**: Strict issuer checking
- **Clock Skew Protection**: Configurable time tolerance for assertions
- **Replay Protection**: Assertion ID caching to prevent replay attacks
- **IdP-Initiated Protection**: Disabled by default, explicit opt-in required

### Production Security

- **Provider Gating**: Dynamic providers disabled in production by default
- **Secret Management**: Integration with Azure Key Vault, AWS Secrets Manager, etc.
- **Rate Limiting**: Per-IP limits on authentication attempts
- **Audit Logging**: All authentication events logged for compliance
- **Token Security**: Tokens not persisted by default; encrypted when enabled

---

## 🔑 Authentication Endpoints

### Well-Known Discovery Endpoints

```http
GET /.well-known/auth/providers
```

**Response:**
```json
[
  {
    "id": "google",
    "name": "Google",
    "protocol": "oidc",
    "enabled": true,
    "icon": "/icons/google.svg",
    "challengeUrl": "/auth/google/challenge",
    "scopes": ["openid", "email", "profile"]
  },
  {
    "id": "corporate-saml",
    "name": "Corporate SSO", 
    "protocol": "saml",
    "enabled": true,
    "icon": "/icons/saml.svg",
    "metadataUrl": "/auth/corporate-saml/saml/metadata"
  }
]
```

### OAuth/OIDC Endpoints

```http
# Start authentication
GET /auth/{provider}/challenge?return={path}&prompt={hint}

# Handle callback
GET /auth/{provider}/callback?code={code}&state={state}

# Logout
GET /auth/logout?return={path}
POST /auth/logout
```

### SAML Endpoints

```http
# Service Provider metadata
GET /auth/{provider}/saml/metadata

# Assertion Consumer Service
POST /auth/{provider}/saml/acs
```

### User Profile Endpoints

```http
# Current user profile
GET /me

# User's linked identity providers
GET /me/connections

# Available providers for linking
GET /me/connections/providers

# Link new provider
POST /me/connections/{provider}/link

# Unlink provider
DELETE /me/connections/{provider}/{keyHash}
```

---

## 🔗 Account Linking & Identity Management

### Account Linking Flow

Users can link multiple identity providers to a single account:

```csharp
// User starts authenticated with Google
// They can link Microsoft account
POST /me/connections/microsoft/link
→ Redirects to Microsoft for consent
→ Returns to callback, associates Microsoft identity
→ User now has both Google and Microsoft linked
```

### Identity Storage

```csharp
public class ExternalIdentity : Entity<ExternalIdentity>
{
    public string UserId { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ProviderKeyHash { get; set; } = ""; // SHA-256 of provider ID
    public string DisplayName { get; set; } = "";
    public string ClaimsJson { get; set; } = "";
    public DateTimeOffset FirstLinked { get; set; }
    public DateTimeOffset LastUsed { get; set; }
}
```

### Security Policies

- **Minimum Identity Requirement**: Users must maintain at least one linked identity
- **Admin Override**: Administrators can force unlink with audit trail
- **Re-consent on Link**: Optional forced re-consent when linking new providers
- **Email Correlation**: No automatic email-based account merging (security by design)

---

## 🛠️ Development & Testing

### TestProvider (Development Only)

Sora includes a built-in TestProvider for development that requires no external configuration:

```json
{
  "Sora": {
    "Web": {
      "Auth": {
        "Providers": {
          "testprovider": {
            "Type": "oidc",
            "DisplayName": "Test Login"
          }
        }
      }
    }
  }
}
```

**Features:**
- ✅ No external dependencies
- ✅ Configurable test users
- ✅ Simulates real OAuth flow
- ✅ PKCE support for testing
- ✅ Remembered user cookies for convenience
- ✅ Integrates with logout for clean testing

**TestProvider Endpoints:**
```http
GET /.testoauth/authorize  # Authorization endpoint
POST /.testoauth/token     # Token exchange
GET /.testoauth/userinfo   # User information
```

### Configuration for Testing

```json
{
  "Sora": {
    "Web": {
      "Auth": {
        "TestProvider": {
          "AllowedRedirectUris": [
            "http://localhost:5000/auth/testprovider/callback",
            "/auth/testprovider/callback"
          ]
        }
      }
    }
  }
}
```

---

## 🏢 Enterprise Features

### SAML 2.0 Integration

Complete SAML 2.0 support for enterprise identity providers:

```json
{
  "Sora": {
    "Web": {
      "Auth": {
        "Providers": {
          "corp-sso": {
            "Type": "saml",
            "EntityId": "https://myapp.com/auth/corp-sso/saml/metadata",
            "IdpMetadataUrl": "https://sso.company.com/metadata",
            "SigningCertRef": "kv:auth/corp-sso/signing-cert",
            "DecryptionCertRef": "kv:auth/corp-sso/decryption-cert",
            "AllowIdpInitiated": false,
            "ClockSkewSeconds": 120,
            "DisplayName": "Corporate SSO",
            "Icon": "/icons/company-logo.svg"
          }
        }
      }
    }
  }
}
```

### Generic OIDC Provider

Support for any OIDC-compliant provider:

```json
{
  "Sora": {
    "Web": {
      "Auth": {
        "Providers": {
          "custom-oidc": {
            "Type": "oidc",
            "Authority": "https://auth.mycompany.com",
            "ClientId": "${CUSTOM_CLIENT_ID}",
            "ClientSecret": "${CUSTOM_CLIENT_SECRET}",
            "Scopes": ["openid", "profile", "email", "custom_scope"],
            "DisplayName": "Company Login",
            "Icon": "/icons/custom.svg"
          }
        }
      }
    }
  }
}
```

### Secret Management

Integration with enterprise secret stores:

```json
{
  "Sora": {
    "Web": {
      "Auth": {
        "Providers": {
          "google": {
            "ClientId": "${GOOGLE_CLIENT_ID}",
            "ClientSecretRef": "kv:auth/google/client-secret"
          },
          "microsoft": {
            "ClientId": "${MS_CLIENT_ID}",
            "ClientSecretRef": "secrets:auth/microsoft/client-secret"
          }
        }
      }
    }
  }
}
```

**Supported Secret Stores:**
- Azure Key Vault (`kv:`)
- AWS Secrets Manager (`secrets:`)
- Google Cloud Secret Manager (`gcp:`)
- HashiCorp Vault (`vault:`)

---

## 💻 Programming API

### Authentication in Controllers

```csharp
[Route("api/[controller]")]
[Authorize] // Require authentication
public class ProfileController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetProfile()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var profile = await User.ByIdAsync(userId);
        return Ok(profile);
    }
    
    [HttpGet("connections")]
    public async Task<IActionResult> GetConnections()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var connections = await ExternalIdentity.Query()
            .Where(i => i.UserId == userId)
            .ToArrayAsync();
        return Ok(connections);
    }
}
```

### Custom Authentication Logic

```csharp
public class AuthenticationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    
    public async Task<AuthResult> AuthenticateWithCustomProvider(AuthRequest request)
    {
        // Custom authentication logic
        var httpClient = _httpClientFactory.CreateClient();
        
        // Exchange authorization code for tokens
        var tokenResponse = await ExchangeCodeForTokens(request.Code, httpClient);
        
        // Get user information
        var userInfo = await GetUserInfo(tokenResponse.AccessToken, httpClient);
        
        // Create or update user
        var user = await FindOrCreateUser(userInfo);
        
        return new AuthResult
        {
            IsSuccess = true,
            User = user,
            AccessToken = tokenResponse.AccessToken
        };
    }
}
```

### Identity Provider Extensibility

```csharp
public class CustomOAuthProvider : IAuthProviderContributor
{
    public string ProviderId => "custom-oauth";
    
    public ProviderDefaults GetDefaults()
    {
        return new ProviderDefaults
        {
            Type = "oauth2",
            DisplayName = "Custom OAuth",
            Icon = "/icons/custom.svg",
            AuthorizationEndpoint = "https://api.custom.com/oauth/authorize",
            TokenEndpoint = "https://api.custom.com/oauth/token",
            UserInfoEndpoint = "https://api.custom.com/oauth/userinfo",
            Scopes = ["read_profile", "read_email"]
        };
    }
    
    public async Task<UserClaims> GetUserClaimsAsync(TokenResponse tokenResponse)
    {
        // Custom user claims mapping
        var userInfo = await GetUserInfoFromCustomApi(tokenResponse.AccessToken);
        
        return new UserClaims
        {
            Subject = userInfo.Id,
            Name = userInfo.DisplayName,
            Email = userInfo.Email,
            Picture = userInfo.AvatarUrl
        };
    }
}
```

---

## 🎨 Frontend Integration

### React/TypeScript Example

```typescript
interface AuthProvider {
  id: string;
  name: string;
  protocol: string;
  enabled: boolean;
  icon?: string;
  challengeUrl?: string;
  metadataUrl?: string;
  scopes?: string[];
}

interface UserProfile {
  id: string;
  displayName: string;
  pictureUrl?: string;
  connections: UserConnection[];
}

interface UserConnection {
  provider: string;
  displayName: string;
  keyHash: string;
}

class AuthService {
  async getProviders(): Promise<AuthProvider[]> {
    const response = await fetch('/.well-known/auth/providers');
    return response.json();
  }
  
  async getCurrentUser(): Promise<UserProfile | null> {
    try {
      const response = await fetch('/me');
      if (response.status === 401) return null;
      return response.json();
    } catch {
      return null;
    }
  }
  
  startLogin(providerId: string, returnUrl: string = '/dashboard'): void {
    window.location.href = `/auth/${providerId}/challenge?return=${encodeURIComponent(returnUrl)}`;
  }
  
  async linkProvider(providerId: string): Promise<void> {
    const response = await fetch(`/me/connections/${providerId}/link`, {
      method: 'POST'
    });
    const result = await response.json();
    window.location.href = result.challengeUrl;
  }
  
  async unlinkProvider(providerId: string, keyHash: string): Promise<void> {
    await fetch(`/me/connections/${providerId}/${keyHash}`, {
      method: 'DELETE'
    });
  }
  
  logout(returnUrl: string = '/'): void {
    window.location.href = `/auth/logout?return=${encodeURIComponent(returnUrl)}`;
  }
}

// React component example
const LoginPage: React.FC = () => {
  const [providers, setProviders] = useState<AuthProvider[]>([]);
  const authService = new AuthService();
  
  useEffect(() => {
    authService.getProviders().then(setProviders);
  }, []);
  
  return (
    <div className="login-page">
      <h1>Sign In</h1>
      <div className="providers">
        {providers.map(provider => (
          <button
            key={provider.id}
            className={`provider-button provider-${provider.id}`}
            onClick={() => authService.startLogin(provider.id)}
          >
            {provider.icon && <img src={provider.icon} alt={provider.name} />}
            Sign in with {provider.name}
          </button>
        ))}
      </div>
    </div>
  );
};
```

### Vue.js Example

```vue
<template>
  <div class="auth-component">
    <div v-if="!user" class="login-section">
      <h2>Sign In</h2>
      <div class="provider-buttons">
        <button 
          v-for="provider in providers" 
          :key="provider.id"
          @click="signIn(provider.id)"
          :class="`btn btn-${provider.id}`"
        >
          <img v-if="provider.icon" :src="provider.icon" :alt="provider.name" />
          {{ provider.name }}
        </button>
      </div>
    </div>
    
    <div v-else class="user-section">
      <h2>Welcome, {{ user.displayName }}!</h2>
      
      <div class="connected-accounts">
        <h3>Connected Accounts</h3>
        <div v-for="connection in user.connections" :key="connection.keyHash" class="connection">
          <span>{{ connection.displayName }}</span>
          <button 
            v-if="user.connections.length > 1"
            @click="unlinkProvider(connection.provider, connection.keyHash)"
            class="btn btn-danger btn-sm"
          >
            Unlink
          </button>
        </div>
      </div>
      
      <div class="available-providers">
        <h3>Link Additional Account</h3>
        <button 
          v-for="provider in availableProviders" 
          :key="provider.id"
          @click="linkProvider(provider.id)"
          class="btn btn-outline"
        >
          Link {{ provider.name }}
        </button>
      </div>
      
      <button @click="signOut" class="btn btn-outline">Sign Out</button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'

const providers = ref<AuthProvider[]>([])
const user = ref<UserProfile | null>(null)

const availableProviders = computed(() => {
  if (!user.value) return []
  const connectedProviders = user.value.connections.map(c => c.provider)
  return providers.value.filter(p => !connectedProviders.includes(p.id))
})

const authService = new AuthService()

onMounted(async () => {
  providers.value = await authService.getProviders()
  user.value = await authService.getCurrentUser()
})

const signIn = (providerId: string) => {
  authService.startLogin(providerId)
}

const linkProvider = async (providerId: string) => {
  await authService.linkProvider(providerId)
}

const unlinkProvider = async (providerId: string, keyHash: string) => {
  await authService.unlinkProvider(providerId, keyHash)
  // Refresh user data
  user.value = await authService.getCurrentUser()
}

const signOut = () => {
  authService.logout()
}
</script>
```

---

## 🏗️ Advanced Configuration

### Production Hardening

```json
{
  "Sora": {
    "Web": {
      "Auth": {
        "AllowDynamicProvidersInProduction": false,
        "ReturnUrl": {
          "DefaultPath": "/",
          "AllowList": [
            "https://myapp.com",
            "https://admin.myapp.com/dashboard",
            "/dashboard",
            "/profile"
          ]
        },
        "RateLimit": {
          "ChallengesPerMinutePerIp": 5,
          "CallbackFailuresPer10MinPerIp": 3
        },
        "Tokens": {
          "PersistTokens": false,
          "EncryptionKey": "${TOKEN_ENCRYPTION_KEY}"
        },
        "ReConsent": {
          "ForceOnLink": true
        }
      }
    }
  }
}
```

### Load Balancer Integration

For applications behind load balancers:

```json
{
  "Sora": {
    "Web": {
      "Auth": {
        "TrustForwardedHeaders": true,
        "ForwardedHeadersOptions": {
          "ForwardedHeaders": "XForwardedFor,XForwardedProto,XForwardedHost",
          "KnownProxies": ["10.0.0.100", "10.0.0.101"]
        }
      }
    }
  }
}
```

### High Availability Setup

```json
{
  "Sora": {
    "Web": {
      "Auth": {
        "SessionStore": {
          "Provider": "Redis",
          "ConnectionString": "${REDIS_CONNECTION_STRING}",
          "KeyPrefix": "sora:auth:",
          "DefaultExpiration": "01:00:00"
        },
        "DistributedCache": {
          "Provider": "Redis", 
          "ConnectionString": "${REDIS_CONNECTION_STRING}"
        }
      }
    }
  }
}
```

---

## 📊 Monitoring & Observability

### Built-in Metrics

Sora automatically tracks authentication metrics:

```csharp
// Authentication metrics
auth_challenges_total{provider, status}
auth_callbacks_total{provider, status}  
auth_failures_total{provider, reason}
auth_session_duration_seconds{provider}
auth_rate_limit_hits_total{provider, type}
```

### Structured Logging

```csharp
// Sample log entries
{
  "timestamp": "2025-01-10T10:30:00Z",
  "level": "Information",
  "message": "Authentication challenge initiated",
  "providerId": "google",
  "userId": null,
  "ipAddress": "192.168.1.100",
  "userAgent": "Mozilla/5.0...",
  "correlationId": "abc123"
}

{
  "timestamp": "2025-01-10T10:30:15Z", 
  "level": "Information",
  "message": "Authentication successful",
  "providerId": "google",
  "userId": "user_123",
  "newUser": false,
  "duration": "00:00:15",
  "correlationId": "abc123"
}
```

### Health Checks

```csharp
public class AuthenticationHealthCheck : IHealthContributor
{
    public string Name => "authentication";
    public bool IsCritical => false; // Usually not critical for app function
    
    public async Task<HealthReport> CheckAsync(CancellationToken ct)
    {
        var enabledProviders = await GetEnabledProvidersAsync();
        var healthyProviders = 0;
        var issues = new List<string>();
        
        foreach (var provider in enabledProviders)
        {
            try
            {
                await ValidateProviderConfiguration(provider);
                healthyProviders++;
            }
            catch (Exception ex)
            {
                issues.Add($"{provider.Id}: {ex.Message}");
            }
        }
        
        if (healthyProviders == enabledProviders.Count)
        {
            return HealthReport.Healthy($"All {healthyProviders} auth providers healthy");
        }
        else if (healthyProviders > 0)
        {
            return HealthReport.Degraded($"{healthyProviders}/{enabledProviders.Count} providers healthy. Issues: {string.Join(", ", issues)}");
        }
        else
        {
            return HealthReport.Unhealthy($"No auth providers healthy. Issues: {string.Join(", ", issues)}");
        }
    }
}
```

---

## 🚨 Troubleshooting

### Common Issues

#### 1. **Provider Not Found (404)**
```
Problem: GET /auth/unknown/challenge returns 404
Solution: Check provider is configured and enabled in appsettings.json
```

#### 2. **Invalid State Parameter (400)**
```
Problem: Callback fails with "Invalid state parameter"
Solution: Check cookie settings, ensure secure cookies in production
```

#### 3. **Redirect URI Mismatch**
```
Problem: OAuth provider returns "redirect_uri_mismatch" error
Solution: Verify callback URL matches exactly in provider configuration
```

#### 4. **SAML Signature Validation Failed**
```
Problem: SAML assertion signature validation fails
Solution: Check signing certificate and clock skew settings
```

### Debug Logging

Enable detailed authentication logging:

```json
{
  "Logging": {
    "LogLevel": {
      "Sora.Web.Auth": "Debug",
      "Microsoft.AspNetCore.Authentication": "Debug"
    }
  }
}
```

### Development vs Production

| Feature | Development | Production |
|---------|-------------|------------|
| TestProvider | ✅ Available | ❌ Disabled |
| Dynamic Providers | ✅ Enabled | ❌ Disabled by default |
| HTTPS Required | ❌ Optional | ✅ Required |
| Secure Cookies | ❌ Optional | ✅ Required |
| Rate Limiting | ⚠️ Relaxed | ✅ Strict |

---

## 📚 Best Practices

### Security Best Practices

1. **Always use HTTPS in production**
2. **Enable rate limiting** to prevent brute force attacks
3. **Use secret references** instead of hardcoded secrets
4. **Implement proper redirect URL validation**
5. **Monitor authentication failures** for security events
6. **Regularly rotate client secrets**
7. **Use least-privilege scopes** for OAuth providers
8. **Enable audit logging** for compliance requirements

### Development Best Practices

1. **Use TestProvider for local development**
2. **Test with multiple providers** early in development
3. **Implement proper error handling** for auth failures  
4. **Design for account linking** from the beginning
5. **Use environment-specific configuration**
6. **Test logout and session expiration scenarios**

### Integration Best Practices

1. **Design authentication-first** - consider auth requirements early
2. **Use claims-based authorization** for flexible permissions
3. **Cache provider metadata** to reduce external calls
4. **Handle provider outages gracefully**
5. **Implement proper session management**
6. **Consider mobile and SPA scenarios**

---

## 🎯 Summary

Sora's authentication system provides:

1. **🚀 Developer Experience**: Zero-config development, intelligent defaults, comprehensive documentation
2. **🔒 Enterprise Security**: OAuth 2.1, OIDC, SAML support with security best practices
3. **🔗 Account Linking**: Seamless multi-provider account management
4. **🏢 Production Ready**: Secret management, monitoring, rate limiting, audit trails
5. **🎨 Frontend Friendly**: RESTful APIs perfect for modern web and mobile applications

**Start simple with a single provider, then scale to enterprise multi-provider authentication without changing your application code.**

---

**Next Steps**: 
- See [Getting Started](../getting-started.md) for basic authentication setup
- See [Usage Patterns](../architecture/patterns.md) for authorization patterns
- See [Advanced Topics](../advanced-topics.md) for custom authentication providers