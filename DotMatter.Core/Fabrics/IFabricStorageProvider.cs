namespace DotMatter.Core.Fabrics;

/// <summary>IFabricStorageProvider interface.</summary>
public interface IFabricStorageProvider
{
    /// <summary>DoesFabricExist.</summary>
    bool DoesFabricExist(string fabricName);

    /// <summary>LoadFabricAsync.</summary>
    Task<Fabric> LoadFabricAsync(string fabricName);

    /// <summary>SaveFabricAsync.</summary>
    Task SaveFabricAsync(Fabric fabric);
}
