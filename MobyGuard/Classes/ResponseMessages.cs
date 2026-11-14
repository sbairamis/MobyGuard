namespace MobyGuard.Classes;

public class ResponseMessages
{
    public const string InternalServerError = "_internalServerError";

    public const string TooManyRequests = "_tooManyRequests";

    public const string InvalidCredentials = "_invalidCredentials";

    public const string InvalidRefreshToken = "_invalidRefreshToken";

    public const string UserIsInactive = "_userIsInactive";

    public const string ServiceAccountCannotLogin = "_serviceAccountCannotLogin";


    public const string UserNotFound = "_userNotFound";

    public const string UserExists = "_userAlreadyExists";

    public const string CannotDeleteYourself = "_cannotDeleteYourself";

    public const string CannotChangeOwnTypeOrStatus = "_cannotChangeOwnTypeOrStatus";

    public const string CannotModifyRootAdmin = "_cannotModifyRootAdmin";

    public const string InvalidCurrentPassword = "_invalidCurrentPassword";

    public const string PasswordPolicyViolation = "_passwordPolicyViolation";

    public const string PasswordBlacklisted = "_passwordBlacklisted";


    public const string UserTypeNotFound = "_userTypeNotFound";

    public const string UserTypeExists = "_userTypeAlreadyExists";

    public const string CannotDeleteSystemUserType = "_cannotDeleteSystemUserType";

    public const string UserTypeInUse = "_userTypeInUse";


    public const string RepositoryPermissionNotFound = "_repositoryPermissionNotFound";

    public const string RepositoryPermissionExists = "_repositoryPermissionAlreadyExists";

    public const string InvalidRepositoryPattern = "_invalidRepositoryPattern";

    public const string NoPermissionActionsSelected = "_noPermissionActionsSelected";


    public const string SetupAlreadyCompleted = "_setupAlreadyCompleted";

    public const string RegistryRepositoryAccessDenied = "_registryRepositoryAccessDenied";

    public const string RegistryManifestNotFound = "_registryManifestNotFound";

    public const string RegistryUpstreamError = "_registryUpstreamError";

    public const string RetentionPolicyNotFound = "_retentionPolicyNotFound";

    public const string RetentionPolicyExists = "_retentionPolicyAlreadyExists";

    public const string InvalidRetentionPolicyRule = "_invalidRetentionPolicyRule";

    public const string WebhookSubscriptionNotFound = "_webhookSubscriptionNotFound";

    public const string WebhookSubscriptionExists = "_webhookSubscriptionAlreadyExists";

    public const string InvalidWebhookSubscription = "_invalidWebhookSubscription";
}
