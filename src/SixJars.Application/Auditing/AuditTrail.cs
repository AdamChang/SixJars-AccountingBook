using SixJars.Application.Common;

namespace SixJars.Application.Auditing;

internal sealed class AuditTrail(ISixJarsDbContext db, ICurrentUser user, TimeProvider clock) : IAuditTrail
{
    public void Record<T>(Guid bookId, AuditAction action, string entityType, Guid entityId, T? before, T? after) =>
        db.AuditEntries.Add(new AuditEntry
        {
            BookId = bookId,
            At = clock.GetUtcNow(),
            ActorSubject = user.Subject,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Before = before is null ? null : AuditSnapshots.Serialize(before),
            After = after is null ? null : AuditSnapshots.Serialize(after),
        });
}
