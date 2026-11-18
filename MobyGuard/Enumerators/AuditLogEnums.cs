namespace MobyGuard.Enumerators;

public class AuditLogEnums
{
    // Values are append-only: never renumber or remove, the name is persisted in AuditLogs.Description.
    public enum Description
    {
        User_Created = 1,
        Token_Issued = 2,
        Permission_Created = 3,
        Permission_Updated = 4,
        Permission_Deleted = 5,
        Manifest_Deleted = 6,
        Manifest_Pushed = 7,
        Manifest_Pulled = 8,
        Garbage_Collection_Acknowledged = 9,
        Retention_Policy_Executed = 10,
        User_Updated = 11,
        User_Deleted = 12,
        Setup_Completed = 13,
        Password_Changed = 14,
        Password_Reset = 15,
        Login_Succeeded = 16,
        Webhook_Created = 17,
        Webhook_Updated = 18,
        Webhook_Deleted = 19,
        Webhook_Tested = 20,
        Retention_Policy_Created = 21,
        Retention_Policy_Updated = 22,
        Retention_Policy_Deleted = 23
    }

    public enum SearchBy
    {
        None = 0,
        Username = 1,
        Repository = 2,
        Actions = 3,
        Description = 4,
        ClientIp = 5,
        Timestamp = 6,
        Target = 7,
        Details = 8,
        UserId = 9
    }
}
