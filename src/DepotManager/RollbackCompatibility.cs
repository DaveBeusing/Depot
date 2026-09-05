namespace DepotManager;

public static class RollbackCompatibility
{
    public static bool IsCompatible(int currentSchemaVersion, int supportedSchemaVersion) => currentSchemaVersion == supportedSchemaVersion;
}
