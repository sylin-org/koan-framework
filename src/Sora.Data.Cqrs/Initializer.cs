using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Data.Abstractions;
using Microsoft.Extensions.Hosting;

namespace Sora.Data.Cqrs;

// legacy initializer removed in favor of standardized auto-registrar
