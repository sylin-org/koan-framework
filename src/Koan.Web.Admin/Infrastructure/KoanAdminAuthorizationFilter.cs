using System.Net;
using Koan.Admin.Options;
using Koan.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Koan.Web.Admin.Infrastructure;

internal sealed class KoanAdminAuthorizationFilter : IAsyncAuthorizationFilter
{
    private readonly IAuthorizationService _authorizationService;
    private readonly IOptionsMonitor<KoanAdminOptions> _options;
    private readonly IKoanAdminFeatureManager _features;

    public KoanAdminAuthorizationFilter(
        IAuthorizationService authorizationService,
        IOptionsMonitor<KoanAdminOptions> options,
        IKoanAdminFeatureManager features)
    {
        _authorizationService = authorizationService;
        _options = options;
        _features = features;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var snapshot = _features.Current;
        if (!snapshot.Enabled || !snapshot.WebEnabled)
        {
            context.Result = new NotFoundResult();
            return;
        }

        var options = _options.CurrentValue;
        if (!IsNetworkAllowed(context.HttpContext.Connection.RemoteIpAddress, options.Authorization.AllowedNetworks))
        {
            context.Result = new ForbidResult();
            return;
        }

        var policy = options.Authorization.Policy;
        if (!string.IsNullOrWhiteSpace(policy))
        {
            var result = await _authorizationService.AuthorizeAsync(context.HttpContext.User, null, policy).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                context.Result = new ForbidResult();
                return;
            }
        }
    }

    private static bool IsNetworkAllowed(IPAddress? remote, string[] allowed)
    {
        if (allowed is null || allowed.Length == 0)
        {
            return true;
        }

        if (remote is null)
        {
            return false;
        }

        foreach (var entry in allowed)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var trimmed = entry.Trim();
            if (!trimmed.Contains('/', StringComparison.Ordinal))
            {
                if (string.Equals(remote.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                continue;
            }

            if (TryParseCidr(trimmed, out var networkAddress, out var maskBits))
            {
                if (IsInSubnet(remote, networkAddress, maskBits))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryParseCidr(string cidr, out IPAddress network, out int maskBits)
    {
        network = IPAddress.None;
        maskBits = 0;
        var parts = cidr.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        if (!IPAddress.TryParse(parts[0], out network))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out maskBits))
        {
            return false;
        }

        return network.AddressFamily switch
        {
            System.Net.Sockets.AddressFamily.InterNetwork => maskBits is >= 0 and <= 32,
            System.Net.Sockets.AddressFamily.InterNetworkV6 => maskBits is >= 0 and <= 128,
            _ => false
        };
    }

    private static bool IsInSubnet(IPAddress address, IPAddress network, int prefixLength)
    {
        if (address.AddressFamily != network.AddressFamily)
        {
            return false;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var addressBytes = address.GetAddressBytes();
            var networkBytes = network.GetAddressBytes();
            var mask = PrefixToMask(prefixLength, addressBytes.Length);
            for (int i = 0; i < addressBytes.Length; i++)
            {
                if ((addressBytes[i] & mask[i]) != (networkBytes[i] & mask[i]))
                {
                    return false;
                }
            }
            return true;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var addressBytes = address.GetAddressBytes();
            var networkBytes = network.GetAddressBytes();
            var mask = PrefixToMask(prefixLength, addressBytes.Length);
            for (int i = 0; i < addressBytes.Length; i++)
            {
                if ((addressBytes[i] & mask[i]) != (networkBytes[i] & mask[i]))
                {
                    return false;
                }
            }
            return true;
        }

        return false;
    }

    private static byte[] PrefixToMask(int prefixLength, int length)
    {
        var mask = new byte[length];
        int fullBytes = prefixLength / 8;
        int remainingBits = prefixLength % 8;

        for (int i = 0; i < fullBytes; i++)
        {
            mask[i] = 0xFF;
        }

        if (remainingBits > 0 && fullBytes < mask.Length)
        {
            mask[fullBytes] = (byte)(0xFF << (8 - remainingBits));
        }

        return mask;
    }
}
