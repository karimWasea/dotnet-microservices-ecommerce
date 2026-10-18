namespace UserService.Domain.Users;

public enum RefreshOutcome
{
    Rotated = 1,
    Invalid = 2,
    Expired = 3,
    ReuseDetected = 4
}
