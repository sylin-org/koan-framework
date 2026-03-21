namespace Koan.AI.Training;

/// <summary>
/// Training method for fine-tuning models.
/// </summary>
public enum TrainMethod
{
    LoRA,
    QLoRA,
    Full,
    SentenceTransformer,
    Contrastive,
    Adapter
}

/// <summary>
/// Alignment method for preference-based training.
/// </summary>
public enum AlignMethod
{
    DPO,
    RLHF,
    KTO,
    ORPO
}
