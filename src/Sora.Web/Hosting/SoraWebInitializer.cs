using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Core;

namespace Sora.Web;

// Self-hook into Sora.AddSoraDataCore() discovery
// legacy initializer removed in favor of standardized auto-registrar
