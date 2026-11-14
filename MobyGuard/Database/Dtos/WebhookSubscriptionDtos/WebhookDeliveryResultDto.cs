namespace MobyGuard.Database.Dtos.WebhookSubscriptionDtos;

public class WebhookDeliveryResultDto
{
    public int SubscriptionId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public string ResponseBody { get; set; } = string.Empty;
}
