namespace S16.PantryPal.Contracts;

public class ConfirmDetectionsRequest
{
    public DetectionConfirmation[] Confirmations { get; set; } = Array.Empty<DetectionConfirmation>();
}

public class DetectionConfirmation
{
    public string DetectionId { get; set; } = string.Empty;
    public string? SelectedCandidateId { get; set; }
    public string? UserInput { get; set; }
}
