using Depot.Models;

namespace DepotManager;

public static class UpdateRecoveryRules
{
    public static bool CanRestorePreviousExecutable(DatabaseSchemaCompatibility? current, DatabaseSchemaCompatibility? previous) =>
        RollbackCompatibility.IsCompatible(current, previous);
}
