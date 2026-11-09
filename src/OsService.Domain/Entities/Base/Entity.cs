namespace OsService.Domain.Entities.Base
{
    public abstract class Entity<TId>
    {
        public TId Id { get; protected set; } = default!;
    }
}
