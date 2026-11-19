using System;
using Vintagestory.API.Server;

namespace Vintagestory.ServerMods;

public class VSEssentialsModServerConfig
{
    /// <summary>
    /// Whether to enable runtime integration for Tool Suite modules.
    /// </summary>
    public bool EnableToolSuiteIntegration = false;

    /// <summary>
    /// Whether to enable hot reloading for assets that support it.
    /// </summary>
    public bool EnableAssetHotReloading = false;

    private static readonly string ConfigFileName = "VSEssentialsModServerConfig.json";

    public static VSEssentialsModServerConfig LoadOrCreate(ICoreServerAPI api)
    {
        VSEssentialsModServerConfig serverConfig;
        try
        {
            serverConfig = api.LoadModConfig<VSEssentialsModServerConfig>(ConfigFileName);
            if (serverConfig == null)
            {
                serverConfig = new VSEssentialsModServerConfig();
                api.StoreModConfig(serverConfig, ConfigFileName);
            }
        }
        catch (Exception)
        {
            api.Logger.Error("Failed to load mod config - falling back to defaults");
            serverConfig = new VSEssentialsModServerConfig();
        }

        return serverConfig;
    }
}
