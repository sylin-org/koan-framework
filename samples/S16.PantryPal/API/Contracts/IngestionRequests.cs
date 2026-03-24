namespace S16.PantryPal.Contracts;

public class ConfirmDetectionsRequest
{
    public DetectionConfirmation[] Confirmations { get; set; } = [];
}

public class DetectionConfirmation
{
    public string DetectionId { get; set; } = "";
    public string? SelectedCandidateId { get; set; }
    public string? UserInput { get; set; }
}
