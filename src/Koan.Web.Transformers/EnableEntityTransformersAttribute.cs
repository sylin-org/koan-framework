namespace Koan.Web.Transformers;

// Marker attribute to enable transformers on a controller
[AttributeUsage(AttributeTargets.Class)]
public sealed class EnableEntityTransformersAttribute : Attribute { }