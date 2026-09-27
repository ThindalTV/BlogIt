namespace BlogIt.Shared.Entities;

/// <summary>
/// An entity that carries an optimistic-concurrency token, bumped by
/// <c>BlogItDbContext.SaveChanges</c> on every update.
/// </summary>
/// <remarks>
/// Public, like the entity types themselves, because a host supplying its own database provider
/// registers against <c>BlogItDbContext</c> and may need to see the model it is mapping.
/// </remarks>
/// <remarks>
/// <para>
/// Deliberately an application-generated <see cref="Guid"/> rather than a SQL Server
/// <c>rowversion</c>. BlogIt ships an in-memory provider alongside the SQL Server ones, and
/// <c>rowversion</c> is store-generated — it would silently never populate there, so the whole
/// mechanism would be untestable and would behave differently per provider. An ordinary token
/// is just as safe: EF Core puts its original value in the <c>WHERE</c> clause of the update, so
/// a second writer still matches zero rows and still fails.
/// </para>
/// <para>
/// The token alone is not the protection. The API also compares the value the client was given
/// when it loaded the record, which is what catches the case this exists for — two people editing
/// the same post in separate sessions, minutes apart. EF's own check would only ever compare
/// against the value loaded moments earlier in the same request.
/// </para>
/// </remarks>
public interface IConcurrencyStamped
{
    Guid ConcurrencyStamp { get; set; }
}
