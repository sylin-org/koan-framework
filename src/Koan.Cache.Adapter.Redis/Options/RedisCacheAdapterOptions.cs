using System;
using System.ComponentModel.DataAnnotations;

namespace Koan.Cache.Adapter.Redis.Options;

public sealed class RedisCacheAdapterOptions
{
    [Required]
    public string Configuration { get; set; } = "localhost:6379";

    public string? InstanceName { get; set; }

    public string KeyPrefix { get; set; } = "cache:";

    public string TagPrefix { get; set; } = "cache:tag:";

    public string ChannelName { get; set; } = "koan-cache";

    public int Database { get; set; } = -1;

    [Range(0, int.MaxValue)]
    public int TagIndexCapacity { get; set; } = 8192;

    public bool EnableStaleWhileRevalidate { get; set; } = true;

    public bool EnablePubSubInvalidation { get; set; } = true;

    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(2);
}
