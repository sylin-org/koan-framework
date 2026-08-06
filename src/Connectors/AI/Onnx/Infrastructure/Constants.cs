namespace Koan.AI.Connector.Onnx.Infrastructure;

internal static class Constants
{
    public const string Section = "Koan:Ai:Onnx";

    internal static class Adapter
    {
        public const string Type = "onnx";
        public const string DisplayName = "ONNX";
    }

    internal static class Source
    {
        public const string Member = "onnx::inproc";
        public const string ConnectionString = "inproc://onnx";
        public const string Policy = "Fallback";
        public const string Origin = "in-process";
        public const int Priority = 50;
    }

    internal static class LogActions
    {
        public const string Activation = "onnx.activation";
    }
}
