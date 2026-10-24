using Depot.Models;

namespace DepotManager;

public static class RollbackCompatibility
{
    public static bool IsCompatible(DatabaseSchemaCompatibility? current, DatabaseSchemaCompatibility? supported)
    {
        if (current is null || supported is null) return false;
        try
        {
            RollbackMetadataService.RequireObservation(current);
            RollbackMetadataService.RequireObservation(supported);
            return current.HasSameState(supported);
        }
        catch (InvalidDataException) { return false; }
    }
}
