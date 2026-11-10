namespace MobyGuard.Interfaces;

public interface IModel
{
    uint Id { get; set; }
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}
