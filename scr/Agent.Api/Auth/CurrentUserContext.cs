namespace Agent.Api.Auth;

public sealed class CurrentUserContext
{
    public Guid Id { get; private set; }
    public string Issuer { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public string? DisplayName { get; private set; }
    public string? Email { get; private set; }
    public bool IsResolved => Id != Guid.Empty;

    public void Set(Guid id, string issuer, string subject, string? displayName, string? email)
    {
        if (id == Guid.Empty) throw new ArgumentException("User ID cannot be empty.", nameof(id));
        Id = id;
        Issuer = issuer;
        Subject = subject;
        DisplayName = displayName;
        Email = email;
    }
}

