namespace DepotManager;

public static class UpdateRecoveryRules
{
    public static bool CanRestorePreviousExecutable(int? currentSchemaVersion, int? previousExecutableSchemaVersion) =>
        currentSchemaVersion is > 0 &&
        previousExecutableSchemaVersion is > 0 &&
        RollbackCompatibility.IsCompatible(currentSchemaVersion.Value, previousExecutableSchemaVersion.Value);
}
