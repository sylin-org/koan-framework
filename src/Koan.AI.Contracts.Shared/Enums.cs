namespace Koan.AI.Contracts.Shared;

/// <summary>
/// Hardware accelerator type. Platform-agnostic — the public API
/// never exposes vendor-specific names; runtimes resolve internally.
/// </summary>
public enum Accelerator
{
    /// <summary>CPU only — no GPU acceleration.</summary>
    None,

    /// <summary>Use the best available accelerator. Framework selects.</summary>
    Any,

    /// <summary>NVIDIA CUDA.</summary>
    CUDA,

    /// <summary>AMD ROCm.</summary>
    ROCm,

    /// <summary>Apple Silicon Metal.</summary>
    Metal,

    /// <summary>Windows universal GPU (AMD, Intel, NVIDIA via DirectX).</summary>
    DirectML,

    /// <summary>Intel oneAPI.</summary>
    OneAPI
}

/// <summary>
/// Model serialization format.
/// </summary>
public enum ModelFormat
{
    SafeTensors,
    GGUF,
    ONNX,
    PyTorch,
    CoreML,
    OpenVINO
}

/// <summary>
/// Quantization level applied to a model.
/// </summary>
public enum Quantization
{
    None,
    Q4_0,
    Q4_K_M,
    Q5_0,
    Q5_K_M,
    Q8_0,
    GPTQ_4bit,
    GPTQ_8bit,
    AWQ_4bit,
    FP8,
    INT4,
    INT8
}

/// <summary>
/// Job lifecycle status. Shared across Training, Model.Convert, and Eval contexts.
/// </summary>
public enum JobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Where compute resources are located.
/// </summary>
public enum ComputeLocation
{
    Local,
    Network,
    Cloud
}

/// <summary>
/// Model capabilities (what a model can do).
/// </summary>
public enum ModelCapability
{
    Chat,
    Embed,
    Vision,
    Ocr,
    Transcription,
    CodeGeneration,
    ImageGeneration,
    Reranking
}

/// <summary>
/// Where a model originated from.
/// </summary>
public enum ModelOrigin
{
    HuggingFace,
    Ollama,
    OnnxZoo,
    Local,
    Custom,
    Training
}
