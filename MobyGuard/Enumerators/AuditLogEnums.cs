namespace MobyGuard.Enumerators;

public class AuditLogEnums
{
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
        Retention_Policy_Executed = 10
    }

    public enum SearchBy
    {
        None = 0,
        Username = 1,
        Repository = 2,
        Actions = 3,
        Description = 4,
        ClientIp = 5,
        Timestamp = 6
    }
}
