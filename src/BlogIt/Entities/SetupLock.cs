namespace BlogIt.Shared.Entities;

/// <summary>
/// Sentinel row that guarantees first-run setup completes at most once, even under two
/// concurrent POST /setup/initialize requests. Always inserted with the fixed <see cref="Id"/>
/// of 1 alongside the new admin user in the same SaveChanges call; a second concurrent request's
/// insert fails the primary key constraint instead of silently creating a second admin account.
/// </summary>
public class SetupLock
{
    public int Id { get; set; } = 1;
}
