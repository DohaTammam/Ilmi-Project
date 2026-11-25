namespace Ilmi.SharedKernel.Domain;

/// <summary>
/// Entities that carry the project's audit columns. An infrastructure interceptor maintains them:
/// <see cref="CreatedAt"/>/<see cref="CreatedBy"/> on insert, <see cref="UpdatedOn"/>/<see cref="UpdatedBy"/> on every modification.
/// </summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    Guid? CreatedBy { get; set; }
    DateTime UpdatedOn { get; set; }
    Guid? UpdatedBy { get; set; }
}

/// <summary>Marks append-only records (history and audit logs). Infrastructure rejects any update or delete of these entities.</summary>
public interface IAppendOnly;
